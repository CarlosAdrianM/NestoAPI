using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
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
            if (pedido.Lineas.Count == 0)
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
            correoVendedor = vendedor.Mail != null ? vendedor.Mail.Trim() : Constantes.Correos.INFORMATICA;
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
            string usuarioParametro = pedido.Usuario.Substring(pedido.Usuario.IndexOf("\\") + 1);
            if (usuarioParametro != null)
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Usuario == usuarioParametro && p.Clave == "CorreoDefecto");
                correoUsuario = parametroUsuario != null ? parametroUsuario.Valor : Constantes.Correos.INFORMATICA;
                if (correoUsuario != null && correoUsuario.Trim()!="")
                {
                    correoUsuario = correoUsuario.Trim().ToLower();
                    mail.CC.Add(correoUsuario);
                }
            }

            if (pedido.cliente == "15191")
            {
                return;
            }
            mail.CC.Add("carlosadrian@nuevavision.es");
            mail.CC.Add("manuelrodriguez@nuevavision.es");
            mail.Subject = string.Format("{0} {1} - c/ {2}", TEXTO_PEDIDO, pedido.numero, pedido.cliente);
            mail.Body = (await GenerarTablaHTML(pedido, tipoCorreo)).ToString();
            mail.IsBodyHtml = true;
            if (pedido.Lineas.FirstOrDefault().almacen == Constantes.Almacenes.REINA)
            {
                mail.CC.Add(Constantes.Correos.TIENDA_REINA);
            }
            else if (pedido.Lineas.FirstOrDefault().almacen == Constantes.Almacenes.ALCOBENDAS)
            {
                mail.CC.Add(Constantes.Correos.TIENDA_ALCOBENDAS);
            }
            if (pedido.ruta == Constantes.Pedidos.RUTA_GLOVO)
            {
                mail.Subject = "[GLOVO] " + mail.Subject;
            }
            // Si falta la foto ponemos copia a tienda online
            if (mail.Body.Contains("www.productosdeesteticaypeluqueriaprofesional.com/-") || mail.Body.Contains("-home_default/.jpg"))
            {
                mail.CC.Add(Constantes.Correos.TIENDA_ONLINE);
            }
            // Si tiene varios plazos y se podría servir junto, ponemos en copia a administración
            if (mail.Body.Contains("¡¡¡ ATENCIÓN !!!"))
            {
                if (pedido.Prepagos.Any() || db.PlazosPago.Where(f => f.Empresa == pedido.empresa && f.Número == pedido.plazosPago && f.Nº_Plazos > 1).Any())
                {
                    mail.CC.Add(Constantes.Correos.CORREO_ADMON);
                }
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
        private async Task<StringBuilder> GenerarTablaHTML(PedidoVentaDTO pedido, string tipoCorreo)
        {
            StringBuilder s = new StringBuilder();

            s.AppendLine(string.Format("<H1>{0} {1} {2}</H1>", tipoCorreo, TEXTO_PEDIDO, pedido.numero));

            s.AppendLine("<table border=\"0\" style=\"width:100%\">");
            s.AppendLine("<tr>");
            s.AppendLine("<td width=\"50%\"><img src=\"http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg\"></td>");
            s.AppendLine("<td width=\"50%\" style=\"text-align:center; vertical-align:middle\">" +
                "<b>NUEVA VISIÓN, S.A.</b><br>" +
                "<b>c/ Río Tiétar, 11</b><br>"+
                "<b>Políg. Ind. Los Nogales</b><br>"+
                "<b>28119 Algete (Madrid)</b><br>"+
                "</td>");
            s.AppendLine("</tr>");
            s.AppendLine("</table>");

            Cliente cliente = db.Clientes.SingleOrDefault(c => c.Empresa == pedido.empresa && c.Nº_Cliente == pedido.cliente && c.Contacto == pedido.contacto);
            DateTime fechaPedido = pedido.fecha ?? DateTime.Today;
            s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            s.AppendLine("<tr>");
            s.AppendLine("<td width=\"50%\" style=\"text-align:left; vertical-align:middle\">" +
                "<b>" + TEXTO_PEDIDO + " " + pedido.numero.ToString() + "</b><br>" +
                "Nº Cliente: " + pedido.cliente + "/" + pedido.contacto + "<br>" +
                "CIF/NIF: " + cliente.CIF_NIF + "<br>");
            if (!string.IsNullOrEmpty(nombreVendedorCabecera))
            {
                s.AppendLine("Vendedor: " + nombreVendedorCabecera + "<br>");
            }
            if (!string.IsNullOrEmpty(nombreVendedorPeluqueria))
            {
                s.AppendLine("Vendedor Peluquería: " + nombreVendedorPeluqueria + "<br>");
            }
            s.AppendLine("Fecha: " + fechaPedido.ToString("D") + "<br>" +
                "Le Atendió: " + pedido.Usuario + "<br>" +
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
            if (!string.IsNullOrEmpty(pedido.comentarios))
            {
                s.AppendLine("<tr>");
                s.AppendLine($"<td colspan='2'>Comentarios: {pedido.comentarios.Trim()}</td>");
                s.AppendLine("</tr>");
            }
            if (!string.IsNullOrEmpty(pedido.comentarioPicking))
            {
                s.AppendLine("<tr>");
                s.AppendLine($"<td colspan='2'>Comentarios picking: {pedido.comentarioPicking.Trim()}</td>");
                s.AppendLine("</tr>");
            }

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

            bool faltaStockDeAlgo = false;
            bool tieneQueVenirAlgunProducto = false;
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                string colorCantidad = "black";
                if (linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    ServicioGestorStocks servicioGestorStocks = new ServicioGestorStocks();
                    GestorStocks gestorStocks = new GestorStocks(servicioGestorStocks);
                    int stockAlmacen = gestorStocks.Stock(linea.Producto, linea.almacen);
                    int pendientesEntregar = gestorStocks.UnidadesPendientesEntregarAlmacen(linea.Producto, linea.almacen);
                    if (stockAlmacen - pendientesEntregar >= 0)
                    {
                        colorCantidad = "green";
                    }
                    else
                    {
                        int cantidadDisponible = gestorStocks.UnidadesDisponiblesTodosLosAlmacenes(linea.Producto);
                        if (cantidadDisponible >= 0)
                        {
                            tieneQueVenirAlgunProducto = true;
                            colorCantidad = "DeepPink";
                        }
                        else
                        {
                            faltaStockDeAlgo = true;
                            colorCantidad = "red";
                        }
                    }
                }
                s.Append("<tr style=\"color: "+ colorCantidad +";\">");
                if (linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    string rutaImagen = await ProductoDTO.RutaImagen(linea.Producto).ConfigureAwait(false);
                    s.Append("<td style=\"width: 100px; height: 100px; text-align:center; vertical-align:middle\"><img src=\"" + rutaImagen + "\" style=\"max-height:100%; max-width:100%\"></td>");
                } else
                {
                    s.Append("<td style=\"width: 100px; height: 100px; text-align:center; vertical-align:middle\"></td>");
                }
                s.Append("<td>" + linea.Producto + "</td>");
                if (linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    string rutaEnlace = await ProductoDTO.RutaEnlace(linea.Producto).ConfigureAwait(false);
                    rutaEnlace += "&utm_medium=correopedido";
                    s.Append("<td><a href=\""+rutaEnlace+"\">" + linea.texto + "</a></td>");
                } else
                {
                    s.Append("<td>" + linea.texto + "</td>");
                }
                s.Append("<td>" + linea.Cantidad.ToString() + "</td>");  
                s.Append("<td style=\"text-align:right\">" + linea.PrecioUnitario.ToString("C") + "</td>");
                s.Append("<td style=\"text-align:right\">" + linea.DescuentoLinea.ToString("P") + "</td>");
                s.Append("<td style=\"text-align:right\">" + linea.BaseImponible.ToString("C") + "</td>");
                s.AppendLine("</tr>");
            }
            if (!pedido.servirJunto || pedido.mantenerJunto)
            {
                string textoServirMantener = string.Empty;
                string colorServirJunto = "black";
                if (!faltaStockDeAlgo && tieneQueVenirAlgunProducto && !pedido.servirJunto)
                {
                    colorServirJunto = "red";
                    textoServirMantener += "¡¡¡ ATENCIÓN !!!";
                }                
                if (!pedido.servirJunto)
                {
                    textoServirMantener += " Desmarcado servir junto ";
                }
                if (!pedido.servirJunto && pedido.mantenerJunto)
                {
                    textoServirMantener += "y";
                }
                if (pedido.mantenerJunto)
                {
                    textoServirMantener += " Marcado mantener junto ";
                }
                s.AppendLine("<tr style=\"color: " + colorServirJunto + ";\">");
                s.AppendLine($"<td colspan='7'>{textoServirMantener.Trim()}</td>");
                s.AppendLine("</tr>");
            }
            s.AppendLine("</tbody>");
            s.AppendLine("</table>");

            s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            s.AppendLine("<thead align = \"right\">");
            s.Append("<tr><th>Base Imponible</th>");
            s.Append("<th>IVA</th>");
            s.Append("<th>Importe IVA</th>");
            if (pedido.Lineas.Sum(l => l.ImporteRecargoEquivalencia) != 0)
            {
                s.Append("<th>Importe RE</th>");
            }
            s.Append("<th>Total</th></tr>");
            s.AppendLine("</thead>");
            s.AppendLine("<tbody align = \"right\">");
            s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Sum(l => l.BaseImponible).ToString("C")+"</td>");
            s.Append("<td style=\"text-align:right\">" + pedido.iva + "</td>");
            s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Sum(l => l.ImporteIva).ToString("C") + "</td>");
            if (pedido.Lineas.Sum(l => l.ImporteRecargoEquivalencia) != 0)
            {
                s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Sum(l => l.ImporteRecargoEquivalencia).ToString("C") + "</td>");
            }            
            s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Sum(l=>l.Total).ToString("C") + "</td>");
            s.AppendLine("</tr>");
            s.AppendLine("</tbody>");
            s.AppendLine("</table>");

            if (pedido.crearEfectosManualmente && pedido.Efectos.Any())
            {
                s.AppendLine("<table border=\"1\" style=\"width:100%\">");
                s.AppendLine("<thead align = \"center\">");
                s.Append("<tr><th>Vencimiento</th>");
                s.Append("<th>Importe</th>");
                s.Append("<th>Forma Pago</th>");
                s.Append("<th>CCC</th></tr>");
                s.AppendLine("</thead>");
                s.AppendLine("<tbody align = \"right\">");
                foreach (EfectoPedidoVentaDTO efecto in pedido.Efectos)
                {
                    s.Append("<tr><td style=\"text-align:center\">" + efecto.FechaVencimiento.ToString("d") + "</td>");
                    s.Append("<td style=\"text-align:right\">" + efecto.Importe.ToString("C") + "</td>");
                    s.Append("<td style=\"text-align:center\">" + efecto.FormaPago + "</td>");
                    s.Append("<td style=\"text-align:center\">" + efecto.Ccc + "</td></tr>");
                }
                s.AppendLine("</tbody>");
                s.AppendLine("</table>");
            } 
            else
            {
                s.AppendLine("<table border=\"1\" style=\"width:100%\">");
                s.AppendLine("<thead align = \"center\">");
                s.Append("<tr><th>Forma de pago</th>");
                s.Append("<th>Plazos de pago</th>");
                s.Append("<th>CCC</th></tr>");
                s.AppendLine("</thead>");
                s.AppendLine("<tbody align = \"right\">");
                s.Append("<tr><td style=\"text-align:center\">" + pedido.formaPago + "</td>");
                s.Append("<td style=\"text-align:center\">" + pedido.plazosPago + "</td>");
                s.Append($"<td style=\"text-align:center\"> { pedido.ccc } </td></tr>");
                s.AppendLine("</tbody>");
                s.AppendLine("</table>");
            }

            return s;
        }
        public void Rellenar(int numeroPedido)
        {
            // cargar el pedido completo a partir del número
        }
    }
}