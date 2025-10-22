using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public class GestorPresupuestos
    {
        // Carlos 10/01/24: esto hay que refactorizarlo para poder hacerle tests
        // deben entrar ambos campos por inyección de dependencias
        private readonly NVEntities db = new NVEntities();
        private readonly IServicioVendedores servicioVendedores = new ServicioVendedores();

        private readonly PedidoVentaDTO pedido;
        private readonly RespuestaValidacion respuestaValidacion;
        private readonly string TEXTO_PEDIDO;

        private string nombreVendedorCabecera = "";
        private string nombreVendedorPeluqueria = "";

        public GestorPresupuestos(PedidoVentaDTO pedido) : this(pedido, null)
        {
        }

        public GestorPresupuestos(PedidoVentaDTO pedido, RespuestaValidacion respuestaValidacion)
        {
            this.pedido = pedido;
            this.respuestaValidacion = respuestaValidacion;
            TEXTO_PEDIDO = pedido.EsPresupuesto ? "Presupuesto" : "Pedido";

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
            SmtpClient client = new SmtpClient
            {
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };
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
            bool tieneLineasNoPeluqueria = db.LinPedidoVtas.Any(l => l.Empresa == pedido.empresa && l.Número == pedido.numero && l.Grupo != "PEL" && l.Base_Imponible != 0);
            if (tieneLineasNoPeluqueria)
            {
                mail.To.Add(new MailAddress(correoVendedor.ToLower()));
                nombreVendedorCabecera = vendedor.Descripción?.Trim();
                var jefeVentas = await servicioVendedores.JefeEquipo(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido.vendedor);
                if (!(jefeVentas is null))
                {
                    var jefeVentasVendedor = db.Vendedores.SingleOrDefault(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Número == jefeVentas.vendedor);
                    var correoJefeVentas = jefeVentasVendedor.Mail != null ? jefeVentasVendedor.Mail.Trim() : Constantes.Correos.INFORMATICA;
                    mail.CC.Add(new MailAddress(correoJefeVentas.ToLower()));
                }
            }

            // Miramos si ponemos copia al vendedor de peluquería
            bool tieneLineasPeluqueria = db.LinPedidoVtas.Any(l => l.Empresa == pedido.empresa && l.Número == pedido.numero && l.Grupo == "PEL" && l.Base_Imponible != 0);
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
                if (correoUsuario != null && correoUsuario.Trim() != "")
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

            // Agregar almacenes con reservas en copia
            HashSet<string> almacenesConReservas = ObtenerAlmacenesConReservas();
            foreach (string almacen in almacenesConReservas)
            {
                string correoAlmacen = ObtenerCorreoAlmacen(almacen);
                if (!string.IsNullOrEmpty(correoAlmacen))
                {
                    mail.CC.Add(correoAlmacen);
                }
            }

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
                mail.Subject = "[Entrega en 2h] " + mail.Subject;
                mail.Priority = MailPriority.High;
            }
            // Si falta la foto ponemos copia a tienda online
            if (mail.Body.Contains("www.productosdeesteticaypeluqueriaprofesional.com/-") ||
                mail.Body.Contains("-home_default/.jpg") ||
                pedido.Lineas.First().formaVenta == Constantes.FormasVenta.AMAZON ||
                pedido.Lineas.First().formaVenta == Constantes.FormasVenta.TIENDA_ONLINE)
            {
                mail.CC.Add(Constantes.Correos.TIENDA_ONLINE);
            }
            // Si tiene varios plazos y se podría servir junto, ponemos en copia a administración
            if (mail.Body.Contains("¡¡¡ ATENCIÓN !!!"))
            {
                mail.Priority = MailPriority.High;
                if (pedido.Prepagos.Any() || db.PlazosPago.Where(f => f.Empresa == pedido.empresa && f.Número == pedido.plazosPago && f.Nº_Plazos > 1).Any())
                {
                    mail.CC.Add(Constantes.Correos.CORREO_ADMON);
                }
            }

            // A veces no conecta a la primera, por lo que reintentamos 2s después
            try
            {
                client.Send(mail);
            }
            catch
            {
                await Task.Delay(2000);
                client.Send(mail);
            }
        }
        private async Task<StringBuilder> GenerarTablaHTML(PedidoVentaDTO pedido, string tipoCorreo)
        {
            StringBuilder s = new StringBuilder();

            // Estilo general del correo
            _ = s.AppendLine("<style>");
            _ = s.AppendLine("  body { font-family: 'Arial', sans-serif; }");
            _ = s.AppendLine("  h1 { color: #333; }");
            _ = s.AppendLine("  table { border-collapse: collapse; width: 100%; }");
            _ = s.AppendLine("  th, td { border: 1px solid #ddd; padding: 8px; text-align: right; }");
            _ = s.AppendLine("  th { background-color: #f2f2f2; }");
            _ = s.AppendLine("  tr:nth-child(even) { background-color: #f9f9f9; }");
            _ = s.AppendLine("  tr:hover { background-color: #f5f5f5; }");
            _ = s.AppendLine("</style>");

            _ = s.AppendLine(string.Format("<H1>{0} {1} {2}</H1>", tipoCorreo, TEXTO_PEDIDO, pedido.numero));

            _ = s.AppendLine("<table border=\"0\" style=\"width:100%\">");
            _ = s.AppendLine("<tr>");
            _ = s.AppendLine("<td width=\"50%\"><img src=\"http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg\"></td>");
            _ = s.AppendLine("<td width=\"50%\" style=\"text-align:center; vertical-align:middle\">" +
                "<b>NUEVA VISIÓN, S.A.</b><br>" +
                "<b>c/ Río Tiétar, 11</b><br>" +
                "<b>Políg. Ind. Los Nogales</b><br>" +
                "<b>28119 Algete (Madrid)</b><br>" +
                "</td>");
            _ = s.AppendLine("</tr>");
            _ = s.AppendLine("</table>");

            Cliente cliente = db.Clientes.SingleOrDefault(c => c.Empresa == pedido.empresa && c.Nº_Cliente == pedido.cliente && c.Contacto == pedido.contacto);
            DateTime fechaPedido = pedido.fecha ?? DateTime.Today;
            _ = s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            _ = s.AppendLine("<tr>");
            _ = s.AppendLine("<td width=\"50%\" style=\"text-align:left; vertical-align:middle\">" +
                "<b>" + TEXTO_PEDIDO + " " + pedido.numero.ToString() + "</b><br>" +
                "Nº Cliente: " + pedido.cliente + "/" + pedido.contacto + "<br>" +
                "CIF/NIF: " + cliente.CIF_NIF + "<br>");
            if (!string.IsNullOrEmpty(nombreVendedorCabecera))
            {
                _ = s.AppendLine("Vendedor: " + nombreVendedorCabecera + "<br>");
            }
            if (!string.IsNullOrEmpty(nombreVendedorPeluqueria))
            {
                _ = s.AppendLine("Vendedor Peluquería: " + nombreVendedorPeluqueria + "<br>");
            }
            _ = s.AppendLine("Fecha: " + fechaPedido.ToString("D") + "<br>" +
                "Le Atendió: " + pedido.Usuario + "<br>" +
                "</td>");
            _ = s.AppendLine("<td width=\"50%\" style=\"text-align:left; vertical-align:middle\">" +
                "<b>DIRECCIÓN DE ENTREGA</b><br>" +
                cliente.Nombre + "<br>" +
                cliente.Dirección + "<br>" +
                cliente.CodPostal + " " + cliente.Población + "<br>" +
                cliente.Provincia + "<br>" +
                "Tel. " + cliente.Teléfono + "<br>" +
                "<b>" + pedido.comentarios + "</b><br>" +
                "</td>");
            _ = s.AppendLine("</tr>");
            if (!string.IsNullOrEmpty(pedido.comentarios))
            {
                _ = s.AppendLine("<tr>");
                _ = s.AppendLine($"<td colspan='2'>Comentarios: {pedido.comentarios.Trim()}</td>");
                _ = s.AppendLine("</tr>");
            }
            if (!string.IsNullOrEmpty(pedido.comentarioPicking))
            {
                _ = s.AppendLine("<tr>");
                _ = s.AppendLine($"<td colspan='2'>Comentarios picking: {pedido.comentarioPicking.Trim()}</td>");
                _ = s.AppendLine("</tr>");
            }

            _ = s.AppendLine("</table>");

            // Primero detectamos si hay líneas rosas (con reservas necesarias)
            bool hayLineasConReservas = false;
            ServicioGestorStocks servicioGestorStocks = new ServicioGestorStocks();
            GestorStocks gestorStocks = new GestorStocks(servicioGestorStocks);

            foreach (LineaPedidoVentaDTO lineaTemp in pedido.Lineas)
            {
                if (lineaTemp.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    string colorTemp = gestorStocks.ColorStock(lineaTemp.Producto, lineaTemp.almacen);
                    if (colorTemp == "DeepPink")
                    {
                        hayLineasConReservas = true;
                        break;
                    }
                }
            }

            _ = s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            _ = s.AppendLine("<thead align = \"right\">");
            _ = s.Append("<tr><th>Imagen</th>");
            _ = s.Append("<th>Producto</th>");
            _ = s.Append("<th>Descripción</th>");
            _ = s.Append("<th>Cantidad</th>");
            _ = s.Append("<th>Precio Und.</th>");
            _ = s.Append("<th>Descuento</th>");
            _ = s.Append("<th>Importe</th>");
            if (hayLineasConReservas)
            {
                _ = s.Append("<th>Reservar</th>");
            }
            _ = s.AppendLine("</tr>");
            _ = s.AppendLine("</thead>");
            _ = s.AppendLine("<tbody align = \"right\">");

            bool faltaStockDeAlgo = false;
            bool tieneQueVenirAlgunProducto = false;

            // Estructura para guardar qué almacenes deben reservar stock
            Dictionary<string, HashSet<string>> almacenesConReservas = new Dictionary<string, HashSet<string>>();

            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                string colorCantidad = linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO ? gestorStocks.ColorStock(linea.Producto, linea.almacen) : "black";
                string textoReserva = "";

                if (colorCantidad == "DeepPink")
                {
                    tieneQueVenirAlgunProducto = true;
                    // Calcular qué almacenes tienen stock de este producto
                    textoReserva = CalcularReservasAlmacenes(linea, gestorStocks, servicioGestorStocks, almacenesConReservas);
                }
                else if (colorCantidad == "red")
                {
                    faltaStockDeAlgo = true;
                }

                _ = s.Append("<tr style=\"color: " + colorCantidad + ";\">");
                if (linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    string rutaImagen = await ProductoDTO.RutaImagen(linea.Producto).ConfigureAwait(false);
                    _ = s.Append("<td style=\"width: 100px; height: 100px; text-align:center; vertical-align:middle\"><img src=\"" + rutaImagen + "\" style=\"max-height:100%; max-width:100%\"></td>");
                }
                else
                {
                    _ = s.Append("<td style=\"width: 100px; height: 100px; text-align:center; vertical-align:middle\"></td>");
                }
                _ = s.Append("<td>" + linea.Producto + "</td>");
                if (linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    string rutaEnlace = await ProductoDTO.RutaEnlace(linea.Producto).ConfigureAwait(false);
                    rutaEnlace += "&utm_medium=correopedido";
                    _ = s.Append("<td><a href=\"" + rutaEnlace + "\">" + linea.texto + "</a></td>");
                }
                else
                {
                    _ = s.Append("<td>" + linea.texto + "</td>");
                }
                _ = s.Append("<td>" + linea.Cantidad.ToString() + "</td>");
                _ = s.Append("<td style=\"text-align:right\">" + linea.PrecioUnitario.ToString("C") + "</td>");
                _ = s.Append("<td style=\"text-align:right\">" + linea.DescuentoLinea.ToString("P") + "</td>");
                _ = s.Append("<td style=\"text-align:right\">" + linea.BaseImponible.ToString("C") + "</td>");
                if (hayLineasConReservas)
                {
                    _ = s.Append("<td style=\"text-align:left\">" + textoReserva + "</td>");
                }
                _ = s.AppendLine("</tr>");
            }
            if (!pedido.servirJunto || pedido.mantenerJunto)
            {
                string textoServirMantener = string.Empty;
                string colorServirJunto = "black";
                if (!faltaStockDeAlgo && tieneQueVenirAlgunProducto && !pedido.servirJunto && pedido.periodoFacturacion != Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES && !pedido.mantenerJunto)
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
                _ = s.AppendLine("<tr style=\"color: " + colorServirJunto + ";\">");
                int colspanNotas = hayLineasConReservas ? 8 : 7;
                _ = s.AppendLine($"<td colspan='{colspanNotas}'>{textoServirMantener.Trim()}</td>");
                _ = s.AppendLine("</tr>");
            }
            if (pedido.CreadoSinPasarValidacion)
            {
                _ = s.AppendLine("<tr style=\"color: red;\">");
                int colspanValidacion = hayLineasConReservas ? 8 : 7;
                if (respuestaValidacion != null)
                {
                    if (!respuestaValidacion.ValidacionSuperada)
                    {
                        // La validación actual NO pasa - mostrar el motivo del error
                        string motivo = !string.IsNullOrEmpty(respuestaValidacion.Motivo) ?
                            respuestaValidacion.Motivo :
                            (respuestaValidacion.Motivos != null && respuestaValidacion.Motivos.Any() ?
                                string.Join("<br/>• ", respuestaValidacion.Motivos.Select(m => m)) :
                                "Error de validación no especificado");
                        _ = s.AppendLine($"<td colspan='{colspanValidacion}'>Nota: pedido creado sin pasar validación (actualmente NO pasaría la validación)<br/><strong>Motivo:</strong><br/>• {motivo.Replace(Environment.NewLine, "<br/>• ")}</td>");
                    }
                    else
                    {
                        // La validación actual SÍ pasa - informar que ahora pasaría
                        string motivo = !string.IsNullOrEmpty(respuestaValidacion.Motivo) ?
                            respuestaValidacion.Motivo :
                            "El pedido ahora pasaría las validaciones correctamente";
                        _ = s.AppendLine($"<td colspan='{colspanValidacion}'>Nota: pedido creado sin pasar validación (actualmente pasaría la validación)<br/><strong>Info:</strong> {motivo.Replace(Environment.NewLine, "<br/>")}</td>");
                    }
                }
                else
                {
                    _ = s.AppendLine($"<td colspan='{colspanValidacion}'>Nota: pedido creado sin pasar validación (respuestaValidacion es null)</td>");
                }
                _ = s.AppendLine("</tr>");
            }
            _ = s.AppendLine("</tbody>");
            _ = s.AppendLine("</table>");

            _ = s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            _ = s.AppendLine("<thead align = \"right\">");
            _ = s.Append("<tr><th>Base Imponible</th>");
            _ = s.Append("<th>IVA</th>");
            _ = s.Append("<th>Importe IVA</th>");
            if (pedido.Lineas.Sum(l => l.ImporteRecargoEquivalencia) != 0)
            {
                _ = s.Append("<th>Importe RE</th>");
            }
            _ = s.Append("<th>Total</th></tr>");
            _ = s.AppendLine("</thead>");
            _ = s.AppendLine("<tbody align = \"right\">");
            _ = s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Sum(l => l.BaseImponible).ToString("C") + "</td>");
            _ = s.Append("<td style=\"text-align:right\">" + pedido.iva + "</td>");
            _ = s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Sum(l => l.ImporteIva).ToString("C") + "</td>");
            if (pedido.Lineas.Sum(l => l.ImporteRecargoEquivalencia) != 0)
            {
                _ = s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Sum(l => l.ImporteRecargoEquivalencia).ToString("C") + "</td>");
            }
            _ = s.Append("<td style=\"text-align:right\">" + pedido.Lineas.Sum(l => l.Total).ToString("C") + "</td>");
            _ = s.AppendLine("</tr>");
            _ = s.AppendLine("</tbody>");
            _ = s.AppendLine("</table>");

            if (pedido.periodoFacturacion == Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES)
            {
                _ = s.AppendLine("<table border=\"1\" style=\"width:100%\">");
                _ = s.AppendLine("<thead align = \"center\">");
                _ = s.Append("<tr><th>Periodo de facturación</th>");
                _ = s.AppendLine("</thead>");
                _ = s.AppendLine("<tbody align = \"right\">");
                _ = s.Append("<tr><td style=\"text-align:center\">FIN DE MES</td>");
                _ = s.AppendLine("</tbody>");
                _ = s.AppendLine("</table>");
            }
            else if (pedido.crearEfectosManualmente && pedido.Efectos.Any())
            {
                _ = s.AppendLine("<table border=\"1\" style=\"width:100%\">");
                _ = s.AppendLine("<thead align = \"center\">");
                _ = s.Append("<tr><th>Vencimiento</th>");
                _ = s.Append("<th>Importe</th>");
                _ = s.Append("<th>Forma Pago</th>");
                _ = s.Append("<th>CCC</th></tr>");
                _ = s.AppendLine("</thead>");
                _ = s.AppendLine("<tbody align = \"right\">");
                foreach (EfectoPedidoVentaDTO efecto in pedido.Efectos)
                {
                    _ = s.Append("<tr><td style=\"text-align:center\">" + efecto.FechaVencimiento.ToString("d") + "</td>");
                    _ = s.Append("<td style=\"text-align:right\">" + efecto.Importe.ToString("C") + "</td>");
                    _ = s.Append("<td style=\"text-align:center\">" + efecto.FormaPago + "</td>");
                    _ = s.Append("<td style=\"text-align:center\">" + efecto.Ccc + "</td></tr>");
                }
                _ = s.AppendLine("</tbody>");
                _ = s.AppendLine("</table>");
            }
            else
            {
                _ = s.AppendLine("<table border=\"1\" style=\"width:100%\">");
                _ = s.AppendLine("<thead align = \"center\">");
                _ = s.Append("<tr><th>Forma de pago</th>");
                _ = s.Append("<th>Plazos de pago</th>");
                _ = s.Append("<th>CCC</th></tr>");
                _ = s.AppendLine("</thead>");
                _ = s.AppendLine("<tbody align = \"right\">");
                _ = s.Append("<tr><td style=\"text-align:center\">" + pedido.formaPago + "</td>");
                _ = s.Append("<td style=\"text-align:center\">" + pedido.plazosPago + "</td>");
                _ = s.Append($"<td style=\"text-align:center\"> {pedido.ccc} </td></tr>");
                _ = s.AppendLine("</tbody>");
                _ = s.AppendLine("</table>");
            }

            return s;
        }

        private string CalcularReservasAlmacenes(LineaPedidoVentaDTO linea, GestorStocks gestorStocks,
            IServicioGestorStocks servicioGestorStocks, Dictionary<string, HashSet<string>> almacenesConReservas)
        {
            // Solo procesamos si es una línea de producto
            if (linea.tipoLinea != Constantes.TiposLineaVenta.PRODUCTO)
            {
                return "";
            }

            // Calculamos cuánto falta en el almacén solicitado
            int stockAlmacenSolicitado = gestorStocks.Stock(linea.Producto, linea.almacen);
            int pendientesAlmacenSolicitado = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(linea.Producto, linea.almacen);
            int disponibleAlmacenSolicitado = stockAlmacenSolicitado - pendientesAlmacenSolicitado;
            int cantidadFaltante = linea.Cantidad - disponibleAlmacenSolicitado;

            if (cantidadFaltante <= 0)
            {
                // No falta nada, no necesitamos reservas
                return "";
            }

            // Buscamos en otros almacenes (todos excepto el solicitado)
            List<string> almacenesDisponibles = new List<string>();
            StringBuilder textoReserva = new StringBuilder();

            foreach (string almacen in Constantes.Sedes.ListaSedes)
            {
                if (almacen == linea.almacen)
                {
                    continue; // Saltamos el almacén solicitado
                }

                int stockAlmacen = gestorStocks.Stock(linea.Producto, almacen);
                int pendientesAlmacen = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(linea.Producto, almacen);
                int disponibleAlmacen = stockAlmacen - pendientesAlmacen;

                if (disponibleAlmacen > 0)
                {
                    almacenesDisponibles.Add(almacen);

                    // Guardamos que este almacén tiene reservas
                    if (!almacenesConReservas.ContainsKey(almacen))
                    {
                        almacenesConReservas[almacen] = new HashSet<string>();
                    }
                    _ = almacenesConReservas[almacen].Add(linea.Producto);
                }
            }

            // Generamos el texto de reserva
            if (almacenesDisponibles.Count == 0)
            {
                return ""; // No hay stock en otros almacenes
            }
            else if (almacenesDisponibles.Count == 1)
            {
                // Solo hay un almacén con stock
                string almacen = almacenesDisponibles[0];
                int stockAlmacen = gestorStocks.Stock(linea.Producto, almacen);
                int pendientesAlmacen = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(linea.Producto, almacen);
                int disponibleAlmacen = stockAlmacen - pendientesAlmacen;
                int cantidadReservar = Math.Min(cantidadFaltante, disponibleAlmacen);

                _ = textoReserva.Append($"<strong>{almacen}: {cantidadReservar} uds</strong>");
            }
            else
            {
                // Hay varios almacenes con stock - mostrar todos
                _ = textoReserva.Append("<strong>");
                List<string> reservasPorAlmacen = new List<string>();

                foreach (string almacen in almacenesDisponibles)
                {
                    int stockAlmacen = gestorStocks.Stock(linea.Producto, almacen);
                    int pendientesAlmacen = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(linea.Producto, almacen);
                    int disponibleAlmacen = stockAlmacen - pendientesAlmacen;

                    reservasPorAlmacen.Add($"{almacen}: {disponibleAlmacen} uds");
                }

                _ = textoReserva.Append(string.Join("<br/>", reservasPorAlmacen));
                _ = textoReserva.Append("</strong>");
            }

            return textoReserva.ToString();
        }

        private HashSet<string> ObtenerAlmacenesConReservas()
        {
            HashSet<string> almacenes = new HashSet<string>();
            ServicioGestorStocks servicioGestorStocks = new ServicioGestorStocks();
            GestorStocks gestorStocks = new GestorStocks(servicioGestorStocks);

            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                if (linea.tipoLinea != Constantes.TiposLineaVenta.PRODUCTO)
                {
                    continue;
                }

                string colorCantidad = gestorStocks.ColorStock(linea.Producto, linea.almacen);
                if (colorCantidad != "DeepPink")
                {
                    continue;
                }

                // Esta línea tiene stock en otros almacenes pero no en el solicitado
                // Buscar en qué almacenes hay stock
                foreach (string almacen in Constantes.Sedes.ListaSedes)
                {
                    if (almacen == linea.almacen)
                    {
                        continue;
                    }

                    int stockAlmacen = gestorStocks.Stock(linea.Producto, almacen);
                    int pendientesAlmacen = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(linea.Producto, almacen);
                    int disponibleAlmacen = stockAlmacen - pendientesAlmacen;

                    if (disponibleAlmacen > 0)
                    {
                        _ = almacenes.Add(almacen);
                    }
                }
            }

            return almacenes;
        }

        private string ObtenerCorreoAlmacen(string almacen)
        {
            switch (almacen)
            {
                case Constantes.Almacenes.REINA:
                    return Constantes.Correos.TIENDA_REINA;
                case Constantes.Almacenes.ALCOBENDAS:
                    return Constantes.Correos.TIENDA_ALCOBENDAS;
                default:
                    return null;
            }
        }

        public void Rellenar(int numeroPedido)
        {
            // cargar el pedido completo a partir del número
        }
    }
}