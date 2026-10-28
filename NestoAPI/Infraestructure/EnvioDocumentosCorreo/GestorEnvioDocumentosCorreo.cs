using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.EnvioDocumentosCorreo
{
    /// <summary>
    /// NestoAPI#724 (sugerencia 831, María José): mandar al cliente el PDF de un pedido o de un mandato SEPA sin
    /// descargarlo. Nesto corre en el escritorio remoto y Outlook web no puede adjuntar un fichero de allí: el
    /// correo sale desde la API, con el remitente de la empresa y copia al usuario que lo manda.
    /// </summary>
    public class GestorEnvioDocumentosCorreo
    {
        internal const string MOTIVO_SIN_DESTINATARIOS =
            "El cliente no tiene ninguna persona de contacto con correo. Escriba el correo al que quiere mandarlo.";
        internal const string MOTIVO_NO_ENVIADO =
            "El correo no se ha podido enviar (el servidor de correo no responde). Vuelva a intentarlo en un rato.";

        private readonly IProveedorDocumentosCorreo proveedor;
        private readonly IServicioCorreoElectronico servicioCorreo;

        public GestorEnvioDocumentosCorreo(IProveedorDocumentosCorreo proveedor, IServicioCorreoElectronico servicioCorreo)
        {
            this.proveedor = proveedor;
            this.servicioCorreo = servicioCorreo;
        }

        /// <exception cref="NestoBusinessException">404 si el pedido no existe; 400 si no hay a quién mandarlo o los correos
        /// están mal escritos; 502 si el servidor de correo no lo acepta.</exception>
        public async Task<ResultadoEnvioDocumentoCorreoDTO> EnviarPedido(string empresa, int numero, EnvioDocumentoCorreoDTO envio, string usuario)
        {
            DocumentoParaCorreo documento = await proveedor.PdfPedido(empresa, numero, usuario).ConfigureAwait(false);
            if (documento == null)
            {
                throw new NestoBusinessException($"No existe el pedido {numero}.") { StatusCode = HttpStatusCode.NotFound };
            }
            string asuntoDefecto = $"Pedido nº {numero}";
            string textoDefecto = $"Adjunto le enviamos el pedido nº {numero}.";
            return await Enviar(empresa, documento, envio, usuario, asuntoDefecto, textoDefecto, "Pedidos",
                $"Pedido {numero}", $"Pedido {numero} enviado por correo a").ConfigureAwait(false);
        }

        public async Task<ResultadoEnvioDocumentoCorreoDTO> EnviarMandato(string empresa, string cliente, string contacto, EnvioMandatoCorreoDTO envio, string usuario)
        {
            DocumentoParaCorreo documento = await proveedor.PdfMandato(empresa, cliente, contacto, envio?.Ccc, envio?.Motor).ConfigureAwait(false);
            if (documento == null)
            {
                throw new NestoBusinessException($"No existe el cliente {cliente}/{contacto} o no tiene esa cuenta bancaria.")
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }
            const string asuntoDefecto = "Orden de domiciliación SEPA";
            const string textoDefecto = "Adjunto le enviamos la orden de domiciliación de adeudo directo SEPA. " +
                "Le rogamos que nos la devuelva firmada.";
            return await Enviar(empresa, documento, envio, usuario, asuntoDefecto, textoDefecto, "Clientes",
                $"Cliente {cliente}/{contacto}", $"Mandato SEPA del cliente {cliente}/{contacto} enviado por correo a").ConfigureAwait(false);
        }

        private async Task<ResultadoEnvioDocumentoCorreoDTO> Enviar(string empresa, DocumentoParaCorreo documento, EnvioDocumentoCorreoDTO envio,
            string usuario, string asuntoDefecto, string textoDefecto, string tabla, string referencia, string descripcion)
        {
            List<string> destinatarios = await ResolverDestinatarios(empresa, documento, envio?.Destinatarios).ConfigureAwait(false);
            string correoUsuario = proveedor.CorreoUsuario(usuario);

            bool enviado;
            using (MailMessage correo = ConstruirCorreo(documento, destinatarios, correoUsuario,
                string.IsNullOrWhiteSpace(envio?.Asunto) ? asuntoDefecto : envio.Asunto.Trim(),
                string.IsNullOrWhiteSpace(envio?.Texto) ? textoDefecto : envio.Texto))
            {
                enviado = servicioCorreo.EnviarCorreoSMTP(correo);
            }
            if (!enviado)
            {
                throw new NestoBusinessException(MOTIVO_NO_ENVIADO) { StatusCode = HttpStatusCode.BadGateway };
            }

            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            string texto = $"{descripcion} {string.Join(", ", destinatarios)}";
            try
            {
                await proveedor.RegistrarEnvio(tabla, referencia, $"{texto} (por {quien})", quien).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // El correo ya ha salido: que no falle la petición por no poder dejar constancia
                ElmahHelper.Log(new Exception($"[NestoAPI#724] No se pudo registrar en Modificaciones: {texto}. {ex.Message}", ex), quien);
            }

            return new ResultadoEnvioDocumentoCorreoDTO
            {
                Destinatarios = destinatarios,
                Adjunto = documento.NombreFichero,
                Mensaje = $"Enviado a {string.Join(", ", destinatarios)}."
            };
        }

        private async Task<List<string>> ResolverDestinatarios(string empresa, DocumentoParaCorreo documento, IEnumerable<string> pedidos)
        {
            bool vienenDestinatarios = (pedidos ?? Enumerable.Empty<string>()).Any(d => !string.IsNullOrWhiteSpace(d));
            if (vienenDestinatarios)
            {
                // Lanza NestoBusinessException (400) diciendo cuál está mal escrito
                return GestorFacturas.NormalizarCorreos(pedidos);
            }
            List<string> contactos = await proveedor.CorreosContacto(empresa, documento.Cliente, documento.Contacto).ConfigureAwait(false);
            if (contactos == null || !contactos.Any())
            {
                throw new NestoBusinessException(MOTIVO_SIN_DESTINATARIOS);
            }
            return contactos;
        }

        /// <summary>Los correos de la ficha bien escritos, sin espacios ni repetidos (uno mal escrito no tumba el envío).</summary>
        internal static List<string> CorreosValidosSinRepetir(IEnumerable<string> correos)
        {
            var resultado = new List<string>();
            foreach (string correo in (correos ?? Enumerable.Empty<string>())
                .SelectMany(c => (c ?? string.Empty).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(c => c.Trim())
                .Where(c => c.Length > 0))
            {
                try
                {
                    if (!resultado.Contains(correo, StringComparer.OrdinalIgnoreCase) && GestorFacturas.NormalizarCorreos(new[] { correo }).Any())
                    {
                        resultado.Add(correo);
                    }
                }
                catch (NestoBusinessException)
                {
                    // mal escrito: se salta
                }
            }
            return resultado;
        }

        internal static MailMessage ConstruirCorreo(DocumentoParaCorreo documento, List<string> destinatarios, string correoUsuario,
            string asunto, string texto)
        {
            var correo = new MailMessage
            {
                From = documento.Remitente ?? new MailAddress(Constantes.Correos.CORREO_ADMON),
                Subject = asunto,
                IsBodyHtml = true,
                Body = CuerpoHtml(texto, documento.Firma)
            };
            foreach (string destinatario in destinatarios)
            {
                correo.To.Add(new MailAddress(destinatario));
            }
            if (!string.IsNullOrWhiteSpace(correoUsuario))
            {
                try
                {
                    correo.CC.Add(new MailAddress(correoUsuario.Trim()));
                }
                catch (FormatException)
                {
                    // El CorreoDefecto del usuario está mal escrito: sale sin copia
                }
            }
            correo.Attachments.Add(new Attachment(new MemoryStream(documento.Pdf ?? new byte[0]), documento.NombreFichero, "application/pdf"));
            return correo;
        }

        /// <summary>El texto del usuario, a salvo (sin HTML suyo) y con sus saltos de línea; debajo, la firma del departamento.</summary>
        internal static string CuerpoHtml(string texto, string firma)
        {
            var s = new StringBuilder();
            // Solo lo que rompe el HTML: WebUtility.HtmlEncode de .NET Framework también convierte «º» y las tildes en entidades
            string seguro = (texto ?? string.Empty)
                .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")
                .Replace("\r\n", "\n").Replace("\n", "<br/>");
            _ = s.AppendLine($"<p>{seguro}</p>");
            if (!string.IsNullOrWhiteSpace(firma))
            {
                _ = s.AppendLine("<br/>");
                _ = s.AppendLine(firma);
            }
            return s.ToString();
        }
    }
}
