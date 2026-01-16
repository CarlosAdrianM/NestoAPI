using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Videos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Agencias
{
    public class GestorEnviosAgencia
    {
        public async Task EnviarCorreoEntregaAgencia(EnviosAgencia envio)
        {
            if (envio == null || string.IsNullOrWhiteSpace(envio.Email))
            {
                return;
            }

            if (envio.Cliente == Constantes.ClientesEspeciales.TIENDA_ONLINE || envio.Cliente == Constantes.ClientesEspeciales.AMAZON)
            {
                return;
            }

            GestorFacturas gestorFacturas = new GestorFacturas();
            FacturaLookup factura = new FacturaLookup { Empresa = envio.Empresa, Factura = envio.Pedido.ToString() };
            List<FacturaLookup> lista = new List<FacturaLookup>
            {
                factura
            };
            List<Factura> facturas = gestorFacturas.LeerFacturas(lista);
            var facturaPdf = gestorFacturas.FacturasEnPDF(facturas);
            Attachment attachment = new Attachment(new MemoryStream(await facturaPdf.ReadAsByteArrayAsync()), envio.Pedido.ToString() + ".pdf");

            MailMessage mail = new MailMessage();
            try
            {
                ServicioFacturas servicio = new ServicioFacturas();
                CabPedidoVta cabEspejo = servicio.CargarCabPedido(Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO, (int)envio.Pedido);
                CabPedidoVta cabPedido = servicio.CargarCabPedido(envio.Empresa, (int)envio.Pedido);
                if (cabEspejo != null || cabPedido.IVA == null)
                {
                    return;
                }
                ISerieFactura serieFactura = GestorFacturas.LeerSerie(cabPedido.Serie);
                mail.From = serieFactura.CorreoDesdeLogistica;
                mail.To.Add(new MailAddress(envio.Email));
                mail.Bcc.Add(new MailAddress("carlosadrian@nuevavision.es"));
                mail.Subject = string.Format("Pedido entregado a la agencia ({0}/{1})", envio.Cliente.Trim(), envio.Pedido.ToString());
            }
            catch
            {
                mail.To.Add(new MailAddress(Constantes.Correos.LOGISTICA));
                mail.Subject = String.Format("[ERROR: {0}] Pedido entregado a la agencia ({1}/{2})", envio.Email, envio.Cliente.Trim(), envio.Pedido.ToString());
            }

            mail.Body = (await GenerarCorreoHTML(envio)).ToString();
            mail.IsBodyHtml = true;
            mail.Attachments.Add(attachment);
            SmtpClient client = CrearClienteSMTP();
            client.Send(mail);
            mail.Dispose();
        }

        private static SmtpClient CrearClienteSMTP()
        {
            SmtpClient client = new SmtpClient();
            client.Port = 587;
            client.EnableSsl = true;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = false;
            string contrasenna = ConfigurationManager.AppSettings["office365password"];
            client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
            client.Host = "smtp.office365.com";
            client.TargetName = "STARTTLS/smtp.office365.com"; // Añadir esta línea para especificar el nombre del objetivo para STARTTLS
            // Configurar TLS 1.2 explícitamente
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            return client;
        }

        private async Task<StringBuilder> GenerarCorreoHTML(EnviosAgencia envio)
        {
            ServicioEnviosAgencia servicio = new ServicioEnviosAgencia();
            string nombreAgencia = servicio.LeerAgencia(envio.Agencia).Nombre;
            EnvioAgenciaDTO envioDTO = new EnvioAgenciaDTO(envio);
            envioDTO.AgenciaNombre = nombreAgencia;
            StringBuilder s = new StringBuilder();

            s.AppendLine("<h3>¡Hola!</h3>");
            s.AppendLine("<p>Le comunicamos que ya hemos enviado su pedido. Debido a que este pedido ya se encuentra en poder de la agencia de transportes, a partir de este momento no se puede realizar ninguna modificación en él. ");
            s.AppendLine("El pedido ya está en camino y por lo tanto no se puede modificar.</p>");
            s.AppendLine("<p>¡IMPORTANTE! Si a la entrega de la mercancía encuentra algún daño en la caja, es <strong>importante que lo indique en el albarán o PDA del transportista</strong>. En caso contrario, la agencia no admitirá reclamaciones posteriores.</p>");
            s.AppendLine("<p>También es muy importante, de cara a una posible reclamación posterior, <strong>comprobar que el nº de bultos que pone en la PDA o albarán coincide con el nº de bultos efectivamente recibido</strong>.</p>");
            s.AppendLine("<p></p>");
            s.AppendLine("<p>La propia agencia le enviará un correo electrónico a esta misma dirección con el enlace al seguimiento de la expedición, para que pueda saber en cada momento por donde va el envío.</p>");
            s.AppendLine("<p>No obstante, le adelantamos que <b>la agencia responsable de la entrega es "+ nombreAgencia +" y el número de envío es <a href=\""+envioDTO.EnlaceSeguimiento+"\">" +envio.CodigoBarras+"</a> </b>");
            s.AppendLine("(es posible que el enlace tarde un rato en estar operativo).</p>");
            s.AppendLine("<p>Adjunto encontrará un PDF con el pedido completo, en el que hemos marcado <span style=\"color: red;\">en rojo las líneas pendientes de enviar y facturar</span>, que se le enviarán tan ");
            s.AppendLine("pronto como tengamos stock y <span style=\"color: green;\">en verde las que enviamos en esta expedición</span>.</p>");
            s.AppendLine("<p><strong>El archivo adjunto NO es una factura electrónica.</strong> Las facturas electrónicas se envían a partir de las 21h del día en que se emiten.</p>");
            s.AppendLine("<p></p>");
            s.AppendLine("<p></p>");
            s.AppendLine("<p></p>");
            s.AppendLine("<a href=\"https://bit.ly/3skuKxx\"> <img src=\"http://productosdeesteticaypeluqueriaprofesional.com/Repositorio/Firma.jpg\" style=\"max-width:100%;\" /></a>");

            // Pie promocional de la app con videoprotocolo
            string piePromoApp = await GenerarPiePromocionAppAsync(envio.Cliente);
            s.AppendLine(piePromoApp);

            return s;
        }

        /// <summary>
        /// Genera el pie promocional para la descarga de la app, mostrando el último videoprotocolo.
        /// Diseñado con tablas HTML y estilos inline para máxima compatibilidad con clientes de correo.
        /// </summary>
        /// <param name="cliente">Cliente para futura personalización del videoprotocolo mostrado</param>
        internal async Task<string> GenerarPiePromocionAppAsync(string cliente = null)
        {
            IServicioVideos servicioVideos = new ServicioVideos();
            var videoprotocolo = await servicioVideos.ObtenerVideoprotocoloParaCorreo(cliente);

            return GenerarPiePromocionApp(videoprotocolo);
        }

        /// <summary>
        /// Genera el HTML del pie promocional. Método separado para facilitar testing.
        /// </summary>
        internal static string GenerarPiePromocionApp(VideoLookupModel videoprotocolo)
        {
            const string urlGooglePlay = "https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas";

            var s = new StringBuilder();

            // Separador
            s.AppendLine("<br/>");
            s.AppendLine("<hr style=\"border: none; border-top: 1px solid #ddd; margin: 20px 0;\" />");

            // Contenedor principal - tabla para compatibilidad con email
            s.AppendLine("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" width=\"100%\" style=\"max-width: 600px;\">");
            s.AppendLine("<tr><td style=\"padding: 20px; background-color: #f8f9fa; border-radius: 8px;\">");

            // Título principal
            s.AppendLine("<p style=\"font-size: 18px; color: #333; margin: 0 0 15px 0; font-family: Arial, sans-serif;\">");
            s.AppendLine("<strong>🎬 Descubre nuestros videoprotocolos exclusivos</strong>");
            s.AppendLine("</p>");

            // Subtítulo
            s.AppendLine("<p style=\"font-size: 14px; color: #666; margin: 0 0 15px 0; font-family: Arial, sans-serif;\">");
            s.AppendLine("Tutoriales paso a paso de tratamientos en cabina, fichas técnicas de productos y mucho más. ");
            s.AppendLine("<strong>¡Todo gratis en nuestra app!</strong>");
            s.AppendLine("</p>");

            // Si hay un videoprotocolo, mostrarlo
            if (videoprotocolo != null && !string.IsNullOrEmpty(videoprotocolo.VideoId))
            {
                // Tabla para el videoprotocolo destacado
                s.AppendLine("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" width=\"100%\" style=\"margin: 15px 0;\">");
                s.AppendLine("<tr>");

                // Columna de la imagen (miniatura de YouTube)
                s.AppendLine("<td style=\"width: 160px; vertical-align: top; padding-right: 15px;\">");
                // Usamos mqdefault (320x180) que es buen tamaño para email
                string urlMiniatura = $"https://img.youtube.com/vi/{videoprotocolo.VideoId}/mqdefault.jpg";
                s.AppendLine($"<img src=\"{urlMiniatura}\" alt=\"{EscapeHtml(videoprotocolo.Titulo)}\" ");
                s.AppendLine("style=\"width: 160px; height: 90px; border-radius: 4px; display: block;\" />");
                s.AppendLine("</td>");

                // Columna del texto
                s.AppendLine("<td style=\"vertical-align: top;\">");
                s.AppendLine("<p style=\"font-size: 13px; color: #007bff; margin: 0 0 5px 0; font-family: Arial, sans-serif;\">");
                s.AppendLine("<strong>ÚLTIMO PROTOCOLO</strong>");
                s.AppendLine("</p>");
                s.AppendLine($"<p style=\"font-size: 14px; color: #333; margin: 0 0 8px 0; font-family: Arial, sans-serif;\">");
                s.AppendLine($"<strong>{EscapeHtml(TruncateText(videoprotocolo.Titulo, 60))}</strong>");
                s.AppendLine("</p>");

                // Descripción truncada
                if (!string.IsNullOrEmpty(videoprotocolo.Descripcion))
                {
                    s.AppendLine($"<p style=\"font-size: 12px; color: #666; margin: 0; font-family: Arial, sans-serif;\">");
                    s.AppendLine(EscapeHtml(TruncateText(videoprotocolo.Descripcion, 100)));
                    s.AppendLine("</p>");
                }

                s.AppendLine("</td>");
                s.AppendLine("</tr>");
                s.AppendLine("</table>");
            }

            // Sección de descarga de la app
            s.AppendLine("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" width=\"100%\" style=\"margin-top: 15px;\">");
            s.AppendLine("<tr>");

            // Botón/Badge de Google Play
            s.AppendLine("<td style=\"vertical-align: middle;\">");
            s.AppendLine($"<a href=\"{urlGooglePlay}\" target=\"_blank\" style=\"text-decoration: none;\">");
            s.AppendLine("<img src=\"https://upload.wikimedia.org/wikipedia/commons/7/78/Google_Play_Store_badge_EN.svg\" ");
            s.AppendLine("alt=\"Descargar en Google Play\" style=\"width: 135px; height: auto; display: inline-block;\" />");
            s.AppendLine("</a>");
            s.AppendLine("</td>");

            // Texto junto al botón
            s.AppendLine("<td style=\"vertical-align: middle; padding-left: 15px;\">");
            s.AppendLine("<p style=\"font-size: 13px; color: #333; margin: 0; font-family: Arial, sans-serif;\">");
            s.AppendLine("<strong>Descarga la app</strong> y accede a todos los contenidos.");
            s.AppendLine("</p>");
            s.AppendLine("</td>");

            s.AppendLine("</tr>");
            s.AppendLine("</table>");

            // Enlace de texto como fallback (Outlook bloquea imágenes)
            s.AppendLine($"<p style=\"font-size: 11px; color: #999; margin: 15px 0 0 0; font-family: Arial, sans-serif;\">");
            s.AppendLine($"¿No ve la imagen? Descargue la app aquí: <a href=\"{urlGooglePlay}\" style=\"color: #007bff;\">{urlGooglePlay}</a>");
            s.AppendLine("</p>");

            // Cerrar contenedor principal
            s.AppendLine("</td></tr>");
            s.AppendLine("</table>");

            return s.ToString();
        }

        /// <summary>
        /// Escapa caracteres HTML para evitar XSS y problemas de renderizado.
        /// </summary>
        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return System.Web.HttpUtility.HtmlEncode(text);
        }

        /// <summary>
        /// Trunca texto a una longitud máxima, añadiendo "..." si es necesario.
        /// </summary>
        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength - 3) + "...";
        }
        
        public static decimal ImporteReembolso(CabPedidoVta pedidoSeleccionado)
        {
            // Miramos la deuda que tenga en su extracto. 
            // Esa deuda la tiene que pagar independientemente de la forma de pago
            decimal importeDeuda = 0;

            // Miramos los casos en los que no hay contra reembolso
            if (pedidoSeleccionado == null) 
            {
                return importeDeuda;
            }
            if (pedidoSeleccionado.CCC != null)
            {
                return importeDeuda;
            }
            if (pedidoSeleccionado.Periodo_Facturacion == "FDM")
            {
                return importeDeuda;
            }
            if (pedidoSeleccionado.Forma_Pago == "CNF" ||
                pedidoSeleccionado.Forma_Pago == "TRN" ||
                pedidoSeleccionado.Forma_Pago == "CHC" ||
                pedidoSeleccionado.Forma_Pago == "TAR")
            {
                return importeDeuda;
            }

            if (pedidoSeleccionado.NotaEntrega)
            {
                return importeDeuda;
            }

            if (pedidoSeleccionado.PlazosPago != null && pedidoSeleccionado.PlazosPago.Trim() == "PRE")
            {
                return importeDeuda;
            }

            if (pedidoSeleccionado.MantenerJunto) {

                List<LinPedidoVta> lineasSinFacturar;
                lineasSinFacturar = pedidoSeleccionado.LinPedidoVtas.Where(l => l.Estado == Constantes.EstadosLineaVenta.PENDIENTE).ToList();
            if (lineasSinFacturar.Any()) {
                    return importeDeuda;
            }
        }

            // Para el resto de los casos ponemos el importe correcto
            List<LinPedidoVta> lineas;
            lineas = pedidoSeleccionado.LinPedidoVtas.Where(l => l.Picking != 0 && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO).ToList();
            if (lineas == null || !lineas.Any()) {
                return importeDeuda;
            }

            //Double importeFinal = Math.Round((Aggregate l In lineas Select l.Total Into Sum()) + importeDeuda, 2, MidpointRounding.AwayFromZero);
            decimal importeFinal = Math.Round(lineas.Sum(l => l.Total) + importeDeuda, 2, MidpointRounding.AwayFromZero);

            // Evitamos los reembolsos negativos
            if (importeFinal < 0) {
                importeFinal = 0;
            }


            return importeFinal;
        }
    }
}