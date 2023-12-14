using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class GestorMargenes
    {
        public GestorMargenes()
        {
            lineasMargen = new List<LineaMargen>();
        }

        private const decimal MARGEN_MINIMO = .35M;
        private const decimal MARGEN_MAXIMO = .8M;

        public List<LineaMargen> lineasMargen { get; set; }
        private NVEntities db = new NVEntities();

        public void enviarCorreo()
        {
            if (lineasMargen.Count == 0)
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
            mail.To.Add(new MailAddress("carlosadrian@nuevavision.es"));
            mail.To.Add(new MailAddress("manuelrodriguez@nuevavision.es"));
            mail.To.Add(new MailAddress("alfredo@nuevavision.es"));
            mail.Subject = "Pedidos por Debajo del Margen Mínimo";
            mail.Body = generarTablaHTML(lineasMargen).ToString();
            mail.IsBodyHtml = true;
            // si da error al conectarse al servidor, vuelve a intentarlo 2s después.
            try
            {
                client.Send(mail);
            } catch
            {
                Task.Delay(2000);
                client.Send(mail);
            }
            
        }
        //private StringBuilder generarTablaHTML(List<LineaMargen> lineas)
        //{
        //    StringBuilder s = new StringBuilder();

        //    s.AppendLine("<H1>Pedidos por Debajo del Margen Mínimo</H1>");
        //    s.AppendLine("<table border=\"1\">");
        //    s.AppendLine("<thead align = \"right\">");
        //    s.Append("<tr><th>Empresa</th>");
        //    s.Append("<th>Pedido</th>");
        //    s.Append("<th>Base Imponible</th>");
        //    s.Append("<th>Coste</th>");
        //    s.Append("<th>Margen</th></tr>");
        //    s.AppendLine("</thead>");
        //    s.AppendLine("<tbody align = \"right\">");
        //    foreach (LineaMargen linea in lineas)
        //    {
        //        if (linea.Margen < MARGEN_MINIMO)
        //        {
        //            s.Append("<tr bgcolor=\"#FF5733\">");
        //        } else if (linea.Margen > MARGEN_MAXIMO)
        //        {
        //            s.Append("<tr bgcolor=\"#008000\">");
        //        }
        //        else 
        //        {
        //            s.Append("<tr>");
        //        }

        //        s.Append("<td>" + linea.Empresa + "</td>");
        //        s.Append("<td>" + linea.Pedido.ToString() + "</td>");
        //        s.Append("<td style=\"text-align:right\">" + linea.BaseImponible.ToString("C") + "</td>");
        //        s.Append("<td style=\"text-align:right\">" + linea.Coste.ToString("C") + "</td>");
        //        s.Append("<td style=\"text-align:right\">" + linea.Margen.ToString("P") + "</td>");
        //        s.AppendLine("</tr>");
        //    }                
        //    s.AppendLine("</tr>");
        //    s.AppendLine("</tbody>");
        //    s.AppendLine("</table>");

        //    return s;
        //}
        private StringBuilder generarTablaHTML(List<LineaMargen> lineas)
        {
            StringBuilder s = new StringBuilder();

            // Añadir logotipo de la empresa
            //s.AppendLine("<img src=\"http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg\" alt=\"Logo de Nueva Visión\">");

            // Estilo general del correo
            s.AppendLine("<style>");
            s.AppendLine("  body { font-family: 'Arial', sans-serif; }");
            s.AppendLine("  h1 { color: #333; }");
            s.AppendLine("  table { border-collapse: collapse; width: 100%; }");
            s.AppendLine("  th, td { border: 1px solid #ddd; padding: 8px; text-align: right; }");
            s.AppendLine("  th { background-color: #f2f2f2; }");
            s.AppendLine("  tr:nth-child(even) { background-color: #f9f9f9; }");
            s.AppendLine("  tr:hover { background-color: #f5f5f5; }");
            s.AppendLine("</style>");

            s.AppendLine("<H1>Pedidos por Debajo del Margen Mínimo</H1>");
            s.AppendLine("<table>");
            s.AppendLine("<thead>");
            s.Append("<tr><th>Empresa</th>");
            s.Append("<th>Pedido</th>");
            s.Append("<th>Base Imponible</th>");
            s.Append("<th>Coste</th>");
            s.Append("<th>Margen</th></tr>");
            s.AppendLine("</thead>");
            s.AppendLine("<tbody>");

            foreach (LineaMargen linea in lineas)
            {
                // Determinar el color de fondo según el margen
                string colorFondo = (linea.Margen < MARGEN_MINIMO) ? "#FF5733" : ((linea.Margen > MARGEN_MAXIMO) ? "#008000" : "");

                s.AppendFormat("<tr style=\"background-color: {0};\">", colorFondo);
                s.AppendFormat("<td>{0}</td>", linea.Empresa);
                s.AppendFormat("<td>{0}</td>", linea.Pedido);
                s.AppendFormat("<td style=\"text-align:right\">{0:C}</td>", linea.BaseImponible);
                s.AppendFormat("<td style=\"text-align:right\">{0:C}</td>", linea.Coste);
                s.AppendFormat("<td style=\"text-align:right\">{0:P}</td>", linea.Margen);
                s.AppendLine("</tr>");
            }

            s.AppendLine("</tbody>");
            s.AppendLine("</table>");

            return s;
        }

        public void Rellenar(int picking)
        {
            List<int> lineasPicking = db.LinPedidoVtas.Where(l => l.Picking == picking).Select(l => l.Número).ToList();
            lineasMargen = db.LinPedidoVtas.Where(l => lineasPicking.Contains(l.Número) && l.Familia != "Eva Visnú" && l.Grupo != "ACP" &&
            l.Nº_Cliente != "3433" && l.Nº_Cliente != "11948" && l.Nº_Cliente != "11736" && l.Nº_Cliente != "15191")
                .GroupBy(g => new { g.Empresa, g.Número })
                .Select(l => new LineaMargen
            {
                Empresa = l.Key.Empresa,
                Pedido = l.Key.Número,
                BaseImponible = l.Sum(c => c.Base_Imponible),
                Coste = l.Sum(c=> (short)c.Cantidad * c.Coste)
            }).ToList();
        }
    }

    public class LineaMargen
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Coste { get; set; }
        public decimal Margen {
            get {
                return BaseImponible == 0 ? 0 : 1 - (Coste / BaseImponible);
            }
        }
    }
}