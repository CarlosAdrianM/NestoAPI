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
        public static void EnviarCorreo(List<PedidoPicking> pedidosRetenidos)
        {
            if (pedidosRetenidos == null || pedidosRetenidos.Count == 0)
            {
                return;
            }

            MailMessage mail = new MailMessage();
            SmtpClient client = new SmtpClient();
            client.Port = 587;
            client.EnableSsl = true;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = false;
            string contrasenna = ConfigurationManager.AppSettings["office365password"];
            client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
            client.Host = "smtp.office365.com";
            mail.From = new MailAddress("nesto@nuevavision.es");
            mail.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
            mail.Subject = "Pedidos retenidos por prepago inexistente";
            mail.Body = GenerarTablaHTML(pedidosRetenidos).ToString();
            mail.IsBodyHtml = true;
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

        private static StringBuilder GenerarTablaHTML(List<PedidoPicking> pedidosRetenidos)
        {
            StringBuilder s = new StringBuilder();

            s.AppendLine("<h1>Pedidos Retenidos</h1>");
            s.AppendLine("<table border=\"1\">");
            s.AppendLine("<thead align = \"right\">");
            s.Append("<tr><th>Empresa</th>");
            s.Append("<th>Pedido</th>");
            s.Append("<th>Total</th>");
            s.Append("<th>Fecha Entrega</th>");
            s.AppendLine("</thead>");
            s.AppendLine("<tbody align = \"right\">");
            foreach (PedidoPicking pedido in pedidosRetenidos)
            {

                s.Append("<tr>");

                s.Append("<td>" + pedido.Empresa + "</td>");
                s.Append("<td>" + pedido.Id.ToString() + "</td>");
                s.Append("<td style=\"text-align:right\">" + pedido.ImporteTotalConIVA.ToString("C") + "</td>");
                s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Min(l => l.FechaEntrega).ToString("dd/MM/yyyy") + "</td>");
                s.AppendLine("</tr>");
            }
            s.AppendLine("</tr>");
            s.AppendLine("</tbody>");
            s.AppendLine("</table>");

            return s;
        }
    }
}