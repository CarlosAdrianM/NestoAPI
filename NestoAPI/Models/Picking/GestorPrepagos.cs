using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Models.Picking
{
    public class GestorPrepagos
    {
        /// <param name="lector">NestoAPI#665 (Sugerencia 584): de dónde salen los comerciales de cada pedido.
        /// Null = el correo va, como antes, a quien grabó cada pedido.</param>
        public static void EnviarCorreo(List<PedidoPicking> pedidosRetenidos, ILectorComercialesPedido lector = null)
        {
            if (pedidosRetenidos == null || pedidosRetenidos.Count == 0)
            {
                return;
            }

            MailMessage mail = ComponerCorreo(pedidosRetenidos, p => LeerComerciales(lector, p));

            SmtpClient client = new SmtpClient();
            client.Port = 587;
            client.EnableSsl = true;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = false;
            string contrasenna = ConfigurationManager.AppSettings["office365password"];
            client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
            client.Host = "smtp.office365.com";
            // si da error al conectarse al servidor, vuelve a intentarlo 2s después.
            try
            {
                client.Send(mail);
            }
            catch
            {
                Task.Delay(2000);
                client.Send(mail);
            }
        }

        /// <summary>
        /// NestoAPI#665 (Sugerencia 584, María José): el correo va a los COMERCIALES de cada pedido según sus
        /// líneas (estética y/o peluquería), no a quien lo grabó. Si un comercial no tiene correo válido, va a
        /// quien grabó el pedido (lo de siempre). Sigue siendo UN correo para todos los retenidos de la pasada,
        /// con la unión de destinatarios en «Para» y copia a administración.
        /// </summary>
        internal static MailMessage ComponerCorreo(List<PedidoPicking> pedidosRetenidos, Func<PedidoPicking, ComercialesPedido> leerComerciales)
        {
            var comercialesPorPedido = new Dictionary<PedidoPicking, ComercialesPedido>();
            var destinatarios = new List<string>();
            foreach (PedidoPicking pedido in pedidosRetenidos)
            {
                ComercialesPedido comerciales = leerComerciales?.Invoke(pedido);
                comercialesPorPedido[pedido] = comerciales;
                List<string> correos = comerciales?.Correos() ?? new List<string>();
                if (!correos.Any())
                {
                    string correoUsuario = pedido.CorreoUsuarioPedido?.Trim();
                    if (ComercialesPedido.EsCorreoValido(correoUsuario))
                    {
                        correos.Add(correoUsuario.ToLowerInvariant());
                    }
                }
                foreach (string correo in correos.Where(c => !destinatarios.Contains(c, StringComparer.OrdinalIgnoreCase)))
                {
                    destinatarios.Add(correo);
                }
            }

            MailMessage mail = new MailMessage();
            mail.From = new MailAddress("nesto@nuevavision.es");
            if (destinatarios.Any())
            {
                destinatarios.ForEach(d => mail.To.Add(new MailAddress(d)));
                mail.CC.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
            }
            else
            {
                // Ni comerciales ni usuario con correo: que al menos lo vea administración
                mail.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
            }
            mail.Subject = "Pedidos retenidos por prepago inexistente";
            mail.Body = GenerarTablaHTML(pedidosRetenidos, comercialesPorPedido).ToString();
            mail.IsBodyHtml = true;
            return mail;
        }

        // Nunca lanza: si no se pueden leer los comerciales, el correo va a quien grabó el pedido (lo de antes).
        private static ComercialesPedido LeerComerciales(ILectorComercialesPedido lector, PedidoPicking pedido)
        {
            if (lector == null || pedido == null)
            {
                return null;
            }
            try
            {
                return lector.Leer(pedido.Empresa, pedido.Id);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Retenidos #665] No se pudieron leer los comerciales del pedido {pedido.Empresa?.Trim()}/{pedido.Id}: {ex.Message}", ex));
                return null;
            }
        }

        private static StringBuilder GenerarTablaHTML(List<PedidoPicking> pedidosRetenidos, Dictionary<PedidoPicking, ComercialesPedido> comercialesPorPedido)
        {
            StringBuilder s = new StringBuilder();

            s.AppendLine("<h1>Pedidos Retenidos</h1>");
            s.AppendLine("<table border=\"1\">");
            s.AppendLine("<thead align = \"right\">");
            s.Append("<tr><th>Empresa</th>");
            s.Append("<th>Pedido</th>");
            s.Append("<th>Total</th>");
            s.Append("<th>Fecha Entrega</th>");
            s.Append("<th>Usuario</th>");
            s.Append("<th>Comercial</th>");
            s.AppendLine("</thead>");
            s.AppendLine("<tbody align = \"right\">");
            foreach (PedidoPicking pedido in pedidosRetenidos)
            {

                s.Append("<tr>");

                s.Append("<td>" + pedido.Empresa + "</td>");
                s.Append("<td>" + pedido.Id.ToString() + "</td>");
                s.Append("<td style=\"text-align:right\">" + pedido.ImporteTotalConIVA.ToString("C") + "</td>");
                if (pedido.Lineas != null && pedido.Lineas.Any())
                {
                    s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Min(l => l.FechaEntrega).ToString("dd/MM/yyyy") + "</td>");
                }
                else
                {
                    s.Append("<td></td>");
                }
                s.Append("<td>" + pedido.Usuario + "</td>");
                comercialesPorPedido.TryGetValue(pedido, out ComercialesPedido comerciales);
                string comercial = comerciales?.NombresComerciales;
                if (string.IsNullOrWhiteSpace(comercial))
                {
                    comercial = pedido.Vendedor?.Trim() ?? string.Empty;
                }
                s.Append("<td>" + System.Net.WebUtility.HtmlEncode(comercial) + "</td>");
                s.AppendLine("</tr>");
            }
            s.AppendLine("</tr>");
            s.AppendLine("</tbody>");
            s.AppendLine("</table>");

            return s;
        }
    }
}
