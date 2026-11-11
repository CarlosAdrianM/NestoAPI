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
        /// <param name="clientePermitido">
        /// Para los vendedores de NestoApp: (empresa, cliente del pedido) → si puede. Si no, 403 con
        /// <see cref="Seguridad.ClientesDelVendedor.MOTIVO_SOLO_SUS_CLIENTES"/> sin mandar nada. Null (empleados) = sin comprobar.
        /// </param>
        /// <param name="vendedorDelToken">Sugerencia 1167: el vendedor del token de NestoApp (null en Nesto: se lee de sus parámetros).</param>
        public async Task<ResultadoEnvioDocumentoCorreoDTO> EnviarPedido(string empresa, int numero, EnvioDocumentoCorreoDTO envio, string usuario,
            System.Func<string, string, bool> clientePermitido = null, string vendedorDelToken = null)
        {
            DocumentoParaCorreo documento = await proveedor.PdfPedido(empresa, numero, usuario).ConfigureAwait(false);
            if (documento == null)
            {
                throw new NestoBusinessException($"No existe el pedido {numero}.") { StatusCode = HttpStatusCode.NotFound };
            }
            if (clientePermitido != null && !clientePermitido(empresa, documento.Cliente?.Trim()))
            {
                throw new NestoBusinessException(Seguridad.ClientesDelVendedor.MOTIVO_SOLO_SUS_CLIENTES) { StatusCode = HttpStatusCode.Forbidden };
            }
            string asuntoDefecto = $"Pedido nº {numero}";
            string textoDefecto = $"Adjunto le enviamos el pedido nº {numero}.";
            return await Enviar(empresa, documento, envio, usuario, asuntoDefecto, textoDefecto, "Pedidos",
                $"Pedido {numero}", $"Pedido {numero} enviado por correo a", vendedorDelToken).ConfigureAwait(false);
        }

        public async Task<ResultadoEnvioDocumentoCorreoDTO> EnviarMandato(string empresa, string cliente, string contacto, EnvioMandatoCorreoDTO envio, string usuario, string vendedorDelToken = null)
        {
            DocumentoParaCorreo documento = await proveedor.PdfMandato(empresa, cliente, contacto, envio?.Ccc).ConfigureAwait(false);
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
                $"Cliente {cliente}/{contacto}", $"Mandato SEPA del cliente {cliente}/{contacto} enviado por correo a", vendedorDelToken).ConfigureAwait(false);
        }

        private async Task<ResultadoEnvioDocumentoCorreoDTO> Enviar(string empresa, DocumentoParaCorreo documento, EnvioDocumentoCorreoDTO envio,
            string usuario, string asuntoDefecto, string textoDefecto, string tabla, string referencia, string descripcion,
            string vendedorDelToken = null)
        {
            List<string> destinatarios = await ResolverDestinatarios(empresa, documento, envio?.Destinatarios).ConfigureAwait(false);
            // Sugerencia 1167: a nombre de quien lo manda (sin sus datos, como antes)
            RemitenteUsuario remitente = proveedor.DatosRemitenteUsuario(usuario, vendedorDelToken);
            string correoUsuario = !string.IsNullOrWhiteSpace(remitente?.Correo) ? remitente.Correo.Trim() : proveedor.CorreoUsuario(usuario);

            bool enviado;
            using (MailMessage correo = ConstruirCorreo(documento, destinatarios, correoUsuario,
                string.IsNullOrWhiteSpace(envio?.Asunto) ? asuntoDefecto : envio.Asunto.Trim(),
                string.IsNullOrWhiteSpace(envio?.Texto) ? textoDefecto : envio.Texto, remitente?.Nombre))
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

        /// <param name="nombreUsuario">Sugerencia 1167: el nombre de quien lo manda (null: como antes, a nombre de la empresa).</param>
        internal static MailMessage ConstruirCorreo(DocumentoParaCorreo documento, List<string> destinatarios, string correoUsuario,
            string asunto, string texto, string nombreUsuario = null)
        {
            MailAddress empresa = documento.Remitente ?? new MailAddress(Constantes.Correos.CORREO_ADMON);
            bool aSuNombre = !string.IsNullOrWhiteSpace(nombreUsuario);
            var correo = new MailMessage
            {
                From = aSuNombre ? DeParteDe(empresa, nombreUsuario) : empresa,
                Subject = asunto,
                IsBodyHtml = true,
                Body = CuerpoHtml(texto, aSuNombre ? FirmaPersonal(nombreUsuario, correoUsuario, documento.Firma) : documento.Firma)
            };
            if (aSuNombre)
            {
                ResponderA(correo, correoUsuario);
            }
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

        /// <summary>Sugerencia 1167: lo que se ve como remitente de un correo mandado a mano por esta persona.</summary>
        internal const string SUFIJO_NOMBRE_REMITENTE = " - Nueva Visión";

        /// <summary>
        /// Sugerencia 1167: la MISMA dirección técnica (el SMTP de Office 365 autenticado rechazaría otra) con el nombre visible
        /// «{Nombre} - Nueva Visión».
        /// </summary>
        internal static MailAddress DeParteDe(MailAddress empresa, string nombre)
            => new MailAddress((empresa ?? new MailAddress(Constantes.Correos.CORREO_ADMON)).Address, nombre.Trim() + SUFIJO_NOMBRE_REMITENTE);

        /// <summary>Sugerencia 1167: las respuestas van al correo de quien lo manda (si está bien escrito).</summary>
        internal static void ResponderA(MailMessage correo, string correoUsuario)
        {
            if (correo == null || string.IsNullOrWhiteSpace(correoUsuario))
            {
                return;
            }
            try
            {
                correo.ReplyToList.Add(new MailAddress(correoUsuario.Trim()));
            }
            catch (FormatException)
            {
                // Mal escrito: las respuestas van al buzón de la empresa, como antes
            }
        }

        /// <summary>
        /// Sugerencia 1167: la firma de quien lo manda (su nombre y su correo) con el teléfono general de la firma de la serie,
        /// sin el departamento.
        /// </summary>
        internal static string FirmaPersonal(string nombre, string correoUsuario, string firmaSerie)
        {
            var lineas = new List<string> { Seguro(nombre?.Trim()) };
            System.Text.RegularExpressions.Match telefono = System.Text.RegularExpressions.Regex.Match(firmaSerie ?? string.Empty, @"Tel\.\s*[0-9 ]+[0-9]");
            if (telefono.Success)
            {
                lineas.Add(telefono.Value.Trim());
            }
            if (!string.IsNullOrWhiteSpace(correoUsuario))
            {
                lineas.Add(Seguro(correoUsuario.Trim()));
            }
            return $"<p>{string.Join("<br/>", lineas)}</p>";
        }

        private static string Seguro(string texto)
            => (texto ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

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
