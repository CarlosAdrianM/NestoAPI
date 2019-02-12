using NestoAPI.Models;
using System;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public class GestorPresupuestos
    {
        private NVEntities db = new NVEntities();
        private PedidoVentaDTO pedido;
        private readonly string TEXTO_PEDIDO;
        
        string nombreVendedorCabecera = "";
        string nombreVendedorPeluqueria = "";

        public GestorPresupuestos(PedidoVentaDTO pedido)
        {
            this.pedido = pedido;
            if (pedido.EsPresupuesto)
            {
                TEXTO_PEDIDO = "Presupuesto";
            } else
            {
                TEXTO_PEDIDO = "Pedido";
            }
            
        }

        public async Task EnviarCorreo()
        {
            await EnviarCorreo("Nuevo");
        }

        public async Task EnviarCorreo(string tipoCorreo)
        {
            if (pedido.LineasPedido.Count == 0)
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

            string correoVendedor = null;
            string correoVendedorPeluqueria = null;
            string correoUsuario = null;

            // Miramos si ponemos copia al vendedor de la cabecera
            Vendedor vendedor = db.Vendedores.SingleOrDefault(v => v.Empresa == pedido.empresa && v.Número == pedido.vendedor);
            correoVendedor = vendedor.Mail != null ? vendedor.Mail.Trim() : Constantes.Correos.CORREO_DIRECCION;
            bool tieneLineasNoPeluqueria = db.LinPedidoVtas.Any(l => l.Empresa == pedido.empresa && l.Número == pedido.numero && l.Grupo != "PEL" && l.Base_Imponible!=0);
            if (tieneLineasNoPeluqueria)
            {
                mail.To.Add(new MailAddress(correoVendedor.ToLower()));
                nombreVendedorCabecera = vendedor.Descripción?.Trim(); ;
            }

            // Miramos si ponemos copia al vendedor de peluquería
            bool tieneLineasPeluqueria = db.LinPedidoVtas.Any(l => l.Empresa == pedido.empresa && l.Número == pedido.numero && l.Grupo == "PEL" && l.Base_Imponible!=0);
            if (tieneLineasPeluqueria)
            {
                string numeroVendedorPeluqueria = db.VendedoresPedidosGruposProductos.SingleOrDefault(v => v.Empresa == pedido.empresa && v.Pedido == pedido.numero)?.Vendedor;
                Vendedor vendedorPeluqueria = db.Vendedores.SingleOrDefault(v => v.Empresa == pedido.empresa && v.Número == numeroVendedorPeluqueria);
                if (vendedorPeluqueria == null)
                {
                    vendedorPeluqueria = vendedor;
                }
                correoVendedorPeluqueria = (vendedorPeluqueria != null && vendedorPeluqueria.Mail != null) ? vendedorPeluqueria.Mail.Trim().ToLower() : correoVendedor;
                mail.To.Add(new MailAddress(correoVendedorPeluqueria));
                nombreVendedorPeluqueria = vendedorPeluqueria?.Descripción?.Trim();
            }

            // Miramos si ponemos al usuario que metió el pedido
            ParametroUsuario parametroUsuario;
            string usuarioParametro = pedido.usuario.Substring(pedido.usuario.IndexOf("\\") + 1);
            if (usuarioParametro != null)
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Usuario == usuarioParametro && p.Clave == "CorreoDefecto");
                correoUsuario = parametroUsuario != null ? parametroUsuario.Valor : Constantes.Correos.CORREO_DIRECCION;
                if (correoUsuario != null && correoUsuario.Trim()!="")
                {
                    correoUsuario = correoUsuario.Trim().ToLower();
                    mail.CC.Add(correoUsuario);
                }
            }
            
            if (!pedido.EsPresupuesto && (
                (correoVendedor == correoUsuario && correoVendedorPeluqueria == null) ||
                (correoVendedorPeluqueria == correoUsuario && !tieneLineasNoPeluqueria) ||
                (correoUsuario == correoVendedor && correoUsuario == correoVendedorPeluqueria)
               ))
            {
                return;
            }
            if (pedido.cliente == "15191")
            {
                return;
            }
            mail.CC.Add(Constantes.Correos.CORREO_DIRECCION);
            mail.Subject = tipoCorreo + " "+TEXTO_PEDIDO+" c/ " + pedido.cliente.ToString();
            mail.Body = (await GenerarTablaHTML(pedido)).ToString();
            mail.IsBodyHtml = true;
            if (pedido.LineasPedido.FirstOrDefault().almacen == Constantes.Almacenes.REINA)
            {
                mail.CC.Add(Constantes.Correos.TIENDA_REINA);
            }
            if (pedido.ruta == Constantes.Pedidos.RUTA_GLOVO)
            {
                mail.Subject = "[GLOVO] " + mail.Subject;
            }
            // Si falta la foto ponemos copia a Quique
            if (mail.Body.Contains("www.productosdeesteticaypeluqueriaprofesional.com/-") || mail.Body.Contains("-home_default/.jpg"))
            {
                mail.CC.Add("kikeadrian82@gmail.com");
            }

            // A veces no conecta a la primera, por lo que reintentamos 2s después
            try
            {
                client.Send(mail);
            } catch
            {
                await Task.Delay(2000);
                client.Send(mail);
            }
        }
        private async Task<StringBuilder> GenerarTablaHTML(PedidoVentaDTO pedido)
        {
            StringBuilder s = new StringBuilder();

            s.AppendLine("<H1>"+TEXTO_PEDIDO+"</H1>");

            s.AppendLine("<table border=\"0\" style=\"width:100%\">");
            s.AppendLine("<tr>");
            s.AppendLine("<td width=\"50%\"><img src=\"http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg\"></td>");
            s.AppendLine("<td width=\"50%\" style=\"text-align:center; vertical-align:middle\">" +
                "<b>NUEVA VISIÓN, S.A.</b><br>" +
                "<b>c/ Río Tiétar, 11</b><br>"+
                "<b>Políg. Ind. Los Nogales</b><br>"+
                "<b>28110 Algete (Madrid)</b><br>"+
                "</td>");
            s.AppendLine("</tr>");
            s.AppendLine("</table>");

            Cliente cliente = db.Clientes.SingleOrDefault(c => c.Empresa == pedido.empresa && c.Nº_Cliente == pedido.cliente && c.Contacto == pedido.contacto);
            DateTime fechaPedido = pedido.fecha ?? DateTime.Today;
            s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            s.AppendLine("<tr>");
            s.AppendLine("<td width=\"50%\" style=\"text-align:left; vertical-align:middle\">" +
                "<b>" + TEXTO_PEDIDO + " " + pedido.numero.ToString() + "</b><br>" +
                "Nº Cliente: " + pedido.cliente + "<br>" +
                "CIF/NIF: " + cliente.CIF_NIF + "<br>");
            if (nombreVendedorCabecera.Trim()!="")
            {
                s.AppendLine("Vendedor: " + nombreVendedorCabecera + "<br>");
            }
            if (nombreVendedorPeluqueria.Trim() != "")
            {
                s.AppendLine("Vendedor Peluquería: " + nombreVendedorPeluqueria + "<br>");
            }
            s.AppendLine("Fecha: " + fechaPedido.ToString("D") + "<br>" +
                "Le Atendió: " + pedido.usuario + "<br>" +
                "</td>");
            s.AppendLine("<td width=\"50%\" style=\"text-align:left; vertical-align:middle\">"+
                "<b>DIRECCIÓN DE ENTREGA</b><br>" +
                cliente.Nombre+"<br>" +
                cliente.Dirección+"<br>" +
                cliente.CodPostal+ " " +cliente.Población+"<br>" +
                cliente.Provincia+"<br>"+
                "Tel. " + cliente.Teléfono + "<br>" +
                "<b>"+ pedido.comentarios + "</b><br>" +
                "</td>");
            s.AppendLine("</tr>");
            s.AppendLine("</table>");

            s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            s.AppendLine("<thead align = \"right\">");
            s.Append("<tr><th>Imagen</th>");
            s.Append("<th>Producto</th>");
            s.Append("<th>Descripción</th>");
            s.Append("<th>Cantidad</th>");
            s.Append("<th>Precio Und.</th>");
            s.Append("<th>Descuento</th>");
            s.Append("<th>Importe</th></tr>");
            s.AppendLine("</thead>");
            s.AppendLine("<tbody align = \"right\">");
            foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido)
            {
                if (linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    string rutaImagen = await ProductoDTO.RutaImagen(linea.producto);
                    s.Append("<td style=\"width: 100px; height: 100px; text-align:center; vertical-align:middle\"><img src=\"" + rutaImagen + "\" style=\"max-height:100%; max-width:100%\"></td>");
                } else
                {
                    s.Append("<td style=\"width: 100px; height: 100px; text-align:center; vertical-align:middle\"></td>");
                }
                s.Append("<td>" + linea.producto + "</td>");
                s.Append("<td>" + linea.texto + "</td>");
                s.Append("<td>" + linea.cantidad.ToString() + "</td>");
                s.Append("<td style=\"text-align:right\">" + linea.precio.ToString("C") + "</td>");
                s.Append("<td style=\"text-align:right\">" + linea.descuento.ToString("P") + "</td>");
                s.Append("<td style=\"text-align:right\">" + linea.baseImponible.ToString("C") + "</td>");
                s.AppendLine("</tr>");
            }
            s.AppendLine("</tr>");
            s.AppendLine("</tbody>");
            s.AppendLine("</table>");

            s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            s.AppendLine("<thead align = \"right\">");
            s.Append("<tr><th>Base Imponible</th>");
            s.Append("<th>IVA</th>");
            s.Append("<th>Importe IVA</th>");
            s.Append("<th>Total</th></tr>");
            s.AppendLine("</thead>");
            s.AppendLine("<tbody align = \"right\">");
            s.Append("<td style=\"text-align:right\">" + pedido.LineasPedido.Sum(l => l.baseImponible).ToString("C")+"</td>");
            s.Append("<td style=\"text-align:right\">" + pedido.iva + "</td>");
            s.Append("<td style=\"text-align:right\">" + pedido.LineasPedido.Sum(l=> l.importeIva).ToString("C") + "</td>");
            s.Append("<td style=\"text-align:right\">" + pedido.LineasPedido.Sum(l=>l.total).ToString("C") + "</td>");
            s.AppendLine("</tr>");
            s.AppendLine("</tbody>");
            s.AppendLine("</table>");

            return s;
        }
        public void Rellenar(int numeroPedido)
        {
            // cargar el pedido completo a partir del número
        }
    }
}