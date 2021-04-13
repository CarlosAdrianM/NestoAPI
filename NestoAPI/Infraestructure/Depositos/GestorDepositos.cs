using NestoAPI.Models;
using NestoAPI.Models.Depositos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Depositos
{
    public class GestorDepositos
    {
        private readonly IServicioDeposito servicio;
        private readonly IServicioGestorStocks servicioGestorStocks;

        public GestorDepositos(IServicioDeposito servicio, IServicioGestorStocks servicioGestorStocks)
        {
            this.servicio = servicio;
            this.servicioGestorStocks = servicioGestorStocks;
        }

        public async Task<List<DepositoCorreoProveedor>> EnviarCorreos()
        {
            List<PersonaContactoProveedorDTO> listaProveedores = await servicio.LeerProveedoresEnDeposito().ConfigureAwait(false);
            List<DepositoCorreoProveedor> listaCorreos = new List<DepositoCorreoProveedor>();

            foreach(var prov in listaProveedores)
            {
                List<ProductoDTO> productosProveedor = await servicio.LeerProductosProveedor(prov.ProveedorId).ConfigureAwait(false);
                List<DatosCorreoDeposito> datosCorreo = new List<DatosCorreoDeposito>();
                foreach(var prod in productosProveedor)
                {
                    int unidadesEnviadasProveedor = await servicio.LeerUnidadesEnviadasProveedor(prod.Producto).ConfigureAwait(false);
                    if (unidadesEnviadasProveedor == 0)
                    {
                        continue;
                    }

                    /*
                    var FechaPrimerMovimiento = await servicio.LeerFechaPrimerVencimiento(prod.Producto).ConfigureAwait(false);
                    var UnidadesStock = servicioGestorStocks.Stock(prod.Producto);
                    var UnidadesReservadas = servicioGestorStocks.UnidadesPendientesEntregar(prod.Producto);
                    var UnidadesVendidas = await servicio.LeerUnidadesVendidas(prod.Producto).ConfigureAwait(false);
                    var UnidadesDevueltas = await servicio.LeerUnidadesDevueltas(prod.Producto).ConfigureAwait(false);
                    */
                    DatosCorreoDeposito datos = new DatosCorreoDeposito
                    {
                        ProductoId = prod.Producto,
                        Nombre = prod.Nombre,
                        Enlace = await ProductoDTO.RutaEnlace(prod.Producto).ConfigureAwait(false),
                        Imagen = await ProductoDTO.RutaImagen(prod.Producto).ConfigureAwait(false),
                        FechaPrimerMovimiento = await servicio.LeerFechaPrimerVencimiento(prod.Producto).ConfigureAwait(false),
                        UnidadesStock = servicioGestorStocks.Stock(prod.Producto),
                        UnidadesEnviadasProveedor = unidadesEnviadasProveedor,
                        UnidadesReservadas = servicioGestorStocks.UnidadesPendientesEntregar(prod.Producto),
                        UnidadesVendidas = await servicio.LeerUnidadesVendidas(prod.Producto).ConfigureAwait(false),
                        UnidadesDevueltas = await servicio.LeerUnidadesDevueltas(prod.Producto).ConfigureAwait(false)
                    };
                    datosCorreo.Add(datos);
                }
                if (datosCorreo.Any())
                {
                    DepositoCorreoProveedor correo = new DepositoCorreoProveedor
                    {
                        DatosCorreo = datosCorreo,
                        DireccionCorreo = prov.CorreoElectronico,
                        NombrePersonaContacto = prov.NombrePersonaContacto,
                        NombreProveedor = prov.NombreProveedor
                    };
                    listaCorreos.Add(correo);
                }
            }

            // Enviamos los correos
            foreach(var correo in listaCorreos)
            {
                using (MailMessage mail = new MailMessage())
                {
                    //mail.From = new MailAddress(Constantes.Correos.COMPRAS, "NUEVA VISIÓN - COMPRAS");
                    mail.From = new MailAddress(Constantes.Correos.COMPRAS, "NUEVA VISIÓN - COMPRAS");
                    mail.To.Add(new MailAddress(correo.DireccionCorreo));
                    mail.Bcc.Add(new MailAddress("carlosadrian@nuevavision.es"));
                    mail.Subject = string.Format("Productos en depósito {0}", correo.NombreProveedor);
                    mail.Body = GenerarTablaHTML(correo).ToString();
                    mail.IsBodyHtml = true;

                    bool enviadoConExito = await servicio.EnviarCorreoSMTP(mail).ConfigureAwait(false);
                    correo.EnvioConExito = enviadoConExito;
                }
            }

            return listaCorreos;
        }

        private static StringBuilder GenerarTablaHTML(DepositoCorreoProveedor correo)
        {
            StringBuilder s = new StringBuilder();

            s.AppendLine("<H1>Estado del depósito</H1>");

            s.AppendLine("<table border=\"0\" style=\"width:100%\">");
            s.AppendLine("<tr>");
            s.AppendLine("<td width=\"50%\"><img src=\"http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg\"></td>");
            s.AppendLine("<td width=\"50%\" style=\"text-align:center; vertical-align:middle\">" +
                "<b>NUEVA VISIÓN, S.A.</b><br>" +
                "<b>c/ Río Tiétar, 11</b><br>" +
                "<b>Políg. Ind. Los Nogales</b><br>" +
                "<b>28119 Algete (Madrid)</b><br>" +
                "</td>");
            s.AppendLine("</tr>");
            s.AppendLine("</table>");
                        
            DateTime fechaPedido = DateTime.Today;
            s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            s.AppendLine("<tr>");
            string saludo;
            if (!string.IsNullOrEmpty(correo.NombrePersonaContacto))
            {
                saludo = "¡Hola "+correo.NombrePersonaContacto+"!";
            }
            else
            {
                saludo = "¡Hola!";
            }
            s.AppendLine("<td width=\"50%\" style=\"text-align:left; vertical-align:middle\">" +
                saludo + " Te informamos del estado de los productos que tienes en depósito en nuestro almacén.<br>" +
                "<b>Para aumentar las ventas online te recomendamos que entres a los enlaces de cada producto y pongas reseñas en todos ellos. " +
                "¡Cuántas más reseñas tengan mejor se verán tus productos! "+
                "También es importante que revises las fotos y descripciones que tenemos de tus productos en nuestra tienda online "+
                "y nos hagas llegar todos los materiales adicionales que consideres oportuno.</b><br>");

            s.AppendLine("Fecha: " + fechaPedido.ToString("D") + "<br></td>");
            s.AppendLine("<td width=\"50%\" style=\"text-align:left; vertical-align:middle\">" +
                "<b>Sugerencias en colores:</b><br>" +
                "<ul>"+
                "<li><font color=\"#008000\">En verde productos en los que se recomienda reponer stock</font></li>" +
                "<li><font color=\"#FF0000\">En rojo productos que se recomienda retirar stock</font></li>" +
                "<li><font color=\"#000000\">En negro productos en los que se recomienda esperar sin realizar ninguna acción</font></li>" +
                "</ul>" +
                "</td>");
            s.AppendLine("</tr>");
            s.AppendLine("</table>");

            s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            s.AppendLine("<thead align = \"center\">");
            s.Append("<tr><th>Imagen</th>");
            s.Append("<th>Producto</th>");
            s.Append("<th>Nombre</th>");
            s.Append("<th>Depósito</th>");
            s.Append("<th>Stock</th>");
            s.Append("<th>Reservadas</th>");
            s.Append("<th>Facturables</th></tr>");
            s.AppendLine("</thead>");
            s.AppendLine("<tbody align = \"center\">");

            foreach (var dato in correo.DatosCorreo)
            {
                s.AppendLine("<tr style=\"background-color:"+dato.ColorFondo+"\">");
                s.Append("<td valign=\"middle\" align=\"center\" height=\"50\"><img src=\"" + dato.Imagen + "\" height=\"100%\"></td>");

                s.Append("<td style=\"text-align:center\"><font color=\"" + dato.ColorTexto +"\">" + dato.ProductoId + "</font></td>");

                string rutaEnlace = dato.Enlace;
                rutaEnlace += "&utm_medium=correodeposito";
                s.Append("<td><a href=\"" + rutaEnlace + "\">" + dato.Nombre + "</a></td>");
                
                s.Append("<td style=\"text-align:center\"><font color=\"" + dato.ColorTexto + "\">" + dato.UnidadesEnviadasProveedor.ToString() + "</font></td>");
                s.Append("<td style=\"text-align:center\"><font color=\"" + dato.ColorTexto + "\">" + dato.UnidadesStock.ToString() + "</font></td>");
                s.Append("<td style=\"text-align:center\"><font color=\"" + dato.ColorTexto + "\">" + dato.UnidadesReservadas.ToString() + "</font></td>");
                s.Append("<td style=\"text-align:center\"><font color=\"" + dato.ColorTexto + "\">" + dato.UnidadesPendientesFacturar.ToString() + "</font></td>");
                s.AppendLine("</tr>");
            }
            s.AppendLine("</tbody>");
            s.AppendLine("</table>");


            return s;
        }
    }
}