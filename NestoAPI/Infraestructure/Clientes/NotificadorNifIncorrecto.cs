using NestoAPI.Models;
using System;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#327: correo al VENDEDOR del cliente (con copia a administración) cuando el
    /// NIF de una ficha resulta incorrecto contra el censo de la AEAT, para que soliciten el
    /// NIF correcto al cliente. Siempre menciona el periodo de gracia: hasta el 01/12/2026
    /// se factura igualmente con avisos; a partir de esa fecha la factura quedará bloqueada
    /// y el pedido retenido.
    /// </summary>
    public class NotificadorNifIncorrecto
    {
        private readonly NVEntities db;
        private readonly IServicioCorreoElectronico servicioCorreo;

        public NotificadorNifIncorrecto(NVEntities db, IServicioCorreoElectronico servicioCorreo = null)
        {
            this.db = db;
            this.servicioCorreo = servicioCorreo ?? new ServicioCorreoElectronico();
        }

        /// <param name="contexto">De dónde viene el aviso: "el pedido 922123" o "la factura NV2612489".</param>
        /// <param name="esFactura">True si el aviso salta al facturar (el texto cambia: ya no
        /// queda margen hasta facturar, el documento acaba de emitirse).</param>
        /// <param name="usuario">Quien procesó el documento (con o sin dominio). Si el vendedor
        /// es el general o no tiene correo, el aviso va a su CorreoDefecto de ParametrosUsuario.</param>
        public async Task Enviar(string empresa, string cliente, string contexto, bool esFactura,
            string nif, string nombre, string resultadoAeat, string usuario = null)
        {
            Cliente ficha = await LeerFichaPrincipalOContacto(empresa, cliente).ConfigureAwait(false);
            Vendedor vendedor = ficha == null ? null : db.Vendedores
                .FirstOrDefault(v => v.Empresa == empresa && v.Número == ficha.Vendedor);

            // Destinatario (Carlos 22/07): al vendedor si es uno REAL con correo; con el vendedor
            // general (NV) o sin correo, al USUARIO que metió el documento (su CorreoDefecto);
            // y si tampoco tiene correo, solo a administración (sin duplicarla en CC).
            bool vendedorGeneral = ficha?.Vendedor?.Trim() == Constantes.Vendedores.VENDEDOR_GENERAL;
            string correoVendedor = vendedor?.Mail?.Trim();
            string correoUsuario = LeerCorreoUsuario(empresa, usuario);
            string destinatario = !vendedorGeneral && !string.IsNullOrWhiteSpace(correoVendedor)
                ? correoVendedor
                : correoUsuario;

            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es"),
                Subject = $"NIF incorrecto del cliente {cliente?.Trim()} - {nombre?.Trim()}",
                IsBodyHtml = true,
                Body =
                    $"<p>El NIF <b>{nif?.Trim()}</b> del cliente <b>{cliente?.Trim()} - {nombre?.Trim()}</b> " +
                    $"no está registrado en el censo de la AEAT (resultado: {resultadoAeat ?? "NO IDENTIFICADO"}). " +
                    $"Se ha detectado al procesar {contexto}.</p>" +
                    (esFactura
                        ? "<p><b>La factura se ha emitido igualmente</b>, pero a partir del <b>01/12/2026</b> " +
                          "(entrada de Verifactu) una factura así <b>no podrá emitirse</b> y el pedido quedará retenido.</p>"
                        : "<p>Hay tiempo de corregirlo <b>hasta que el pedido se facture</b>. A partir del " +
                          "<b>01/12/2026</b> (entrada de Verifactu), si al facturar el NIF sigue incorrecto, " +
                          "<b>no se podrá crear la factura</b> y el pedido quedará retenido. Hasta entonces se " +
                          "factura igualmente, con avisos como este.</p>") +
                    "<p><b>Por favor, solicitad al cliente el NIF correcto</b> (tal como figura en su DNI/CIF) " +
                    "y corregidlo en la ficha. Al corregirlo se revalida automáticamente y desaparecen los avisos.</p>"
            };
            if (!string.IsNullOrWhiteSpace(destinatario))
            {
                mail.To.Add(new MailAddress(destinatario.ToLower()));
                mail.CC.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                // Carlos 24/07: el USUARIO que metió el documento va SIEMPRE en copia (tiene al
                // cliente delante o al teléfono y puede corregir el NIF al momento), aunque el
                // aviso vaya dirigido al vendedor del cliente. Se omite si ya es el destinatario
                // (vendedor general o él mismo es el vendedor) o si coincide con administración.
                if (!string.IsNullOrWhiteSpace(correoUsuario)
                    && !string.Equals(correoUsuario, destinatario, System.StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(correoUsuario, Constantes.Correos.CORREO_ADMON, System.StringComparison.OrdinalIgnoreCase))
                {
                    mail.CC.Add(new MailAddress(correoUsuario.ToLower()));
                }
            }
            else
            {
                mail.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
            }

            _ = servicioCorreo.EnviarCorreoSMTP(mail);
        }

        // El correo del usuario que procesó el documento: parámetro CorreoDefecto de
        // ParametrosUsuario (mismo mecanismo que GestorPresupuestos), quitando el dominio
        // ("NUEVAVISION\Laura" → "Laura"). Null si no lo tiene.
        private string LeerCorreoUsuario(string empresa, string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }
            string usuarioSinDominio = usuario.Substring(usuario.IndexOf("\\") + 1).Trim();
            ParametroUsuario parametro = db.ParametrosUsuario.FirstOrDefault(p =>
                p.Empresa == empresa && p.Usuario == usuarioSinDominio
                && p.Clave == Parametros.Claves.CorreoDefecto);
            return string.IsNullOrWhiteSpace(parametro?.Valor) ? null : parametro.Valor.Trim();
        }

        // El vendedor sale del contacto principal (o del primero que haya): el aviso es por
        // CLIENTE, aunque el NIF viva por contacto.
        private async Task<Cliente> LeerFichaPrincipalOContacto(string empresa, string cliente)
        {
            var fichas = await System.Data.Entity.QueryableExtensions.ToListAsync(
                db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente))
                .ConfigureAwait(false);
            return fichas.FirstOrDefault(c => c.ClientePrincipal) ?? fichas.FirstOrDefault();
        }
    }
}
