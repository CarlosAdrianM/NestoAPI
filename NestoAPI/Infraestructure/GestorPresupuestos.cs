using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
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
        private readonly IServicioGestorStocks servicioGestorStocks;
        private readonly IServicioCorreoElectronico servicioCorreo;

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
            servicioGestorStocks = new ServicioGestorStocks();
            servicioCorreo = new ServicioCorreoElectronico(); // En producción, sin logger
        }

        public GestorPresupuestos(
            PedidoVentaDTO pedido,
            RespuestaValidacion respuestaValidacion,
            NVEntities db,
            IServicioVendedores servicioVendedores,
            IServicioGestorStocks servicioGestorStocks,
            IServicioCorreoElectronico servicioCorreo = null)
        {
            this.pedido = pedido;
            this.respuestaValidacion = respuestaValidacion;
            this.db = db;
            this.servicioVendedores = servicioVendedores;
            this.servicioGestorStocks = servicioGestorStocks;
            this.servicioCorreo = servicioCorreo;
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

            MailMessage mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es")
            };

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

            if (pedido.cliente == Constantes.ClientesEspeciales.EL_EDEN)
            {
                return;
            }
            mail.CC.Add("carlosadrian@nuevavision.es");
            mail.CC.Add("manuelrodriguez@nuevavision.es");

            // Carlos 23/10/25: Agregar almacenes con reservas en copia
            // En modificaciones, incluimos tanto los almacenes que tenían reservas antes como los que las tienen ahora
            HashSet<string> almacenesParaCC = new HashSet<string>();

            if (tipoCorreo == "Modificación")
            {
                // Almacenes que TENÍAN reservas antes (para que sepan que algo cambió)
                HashSet<string> almacenesAnteriores = ObtenerAlmacenesConReservasAnteriores();
                foreach (string almacen in almacenesAnteriores)
                {
                    _ = almacenesParaCC.Add(almacen);
                }
            }

            // Almacenes que TIENEN reservas ahora (para nuevo pedido o para modificación)
            HashSet<string> almacenesActuales = ObtenerAlmacenesConReservas();
            foreach (string almacen in almacenesActuales)
            {
                _ = almacenesParaCC.Add(almacen);
            }

            // Añadir los correos de los almacenes
            foreach (string almacen in almacenesParaCC)
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

            // Carlos 23/10/25: Usar siempre el servicio de correo (puede ser real o mockeado)
            // El ServicioCorreoElectronico ya tiene lógica de retry interna
            servicioCorreo.EnviarCorreoSMTP(mail);
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

            // Carlos 23/10/25: Si el almacén no es ALG, añadimos una coletilla para que se vea claramente
            string almacenPedido = pedido.Lineas.FirstOrDefault()?.almacen;
            string textoAlmacen = "";
            if (!string.IsNullOrEmpty(almacenPedido) && almacenPedido != Constantes.Almacenes.ALGETE)
            {
                textoAlmacen = $" (para recoger en {almacenPedido})";
            }
            _ = s.AppendLine(string.Format("<H1>{0} {1} {2}{3}</H1>", tipoCorreo, TEXTO_PEDIDO, pedido.numero, textoAlmacen));

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

            // Carlos 24/10/25: Detectamos si hay líneas con reservas mirando los almacenes
            // Solo mostramos la columna si realmente hay almacenes con stock disponible para reservar
            bool hayLineasConReservas = false;
            GestorStocks gestorStocks = new GestorStocks(servicioGestorStocks);

            // Usar los métodos que ya existen para obtener almacenes con reservas
            HashSet<string> almacenesActuales = ObtenerAlmacenesConReservas();
            HashSet<string> almacenesAnteriores = tipoCorreo == "Modificación" ?
                ObtenerAlmacenesConReservasAnteriores() : new HashSet<string>();

            // Si hay algún almacén con reservas (actuales o anteriores), mostramos la columna
            hayLineasConReservas = almacenesActuales.Count > 0 || almacenesAnteriores.Count > 0;

            // Carlos 27/01/26: Issue #79 - Detectar si hay descuentos para mostrar/ocultar la columna
            bool hayDescuentos = pedido.Lineas.Any(l => l.SumaDescuentosSinPP > 0);

            _ = s.AppendLine("<table border=\"1\" style=\"width:100%\">");
            _ = s.AppendLine("<thead align = \"right\">");
            _ = s.Append("<tr><th>Imagen</th>");
            _ = s.Append("<th>Producto</th>");
            _ = s.Append("<th>Descripción</th>");
            _ = s.Append("<th>Cantidad</th>");
            _ = s.Append("<th>Precio Und.</th>");
            if (hayDescuentos)
            {
                _ = s.Append("<th>Descuento</th>");
            }
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

                // Carlos 23/10/25: Calcular si la línea necesita reservas AHORA
                if (colorCantidad == "DeepPink")
                {
                    tieneQueVenirAlgunProducto = true;
                    // Mostrar reserva si es línea nueva, si la cantidad cambió, o si cambió el producto
                    if (linea.EsLineaNueva || linea.CambioProducto || (linea.CantidadAnterior.HasValue && linea.CantidadAnterior.Value != linea.Cantidad))
                    {
                        textoReserva = CalcularReservasAlmacenes(linea, gestorStocks, servicioGestorStocks, almacenesConReservas);
                    }
                }
                else if (colorCantidad == "red")
                {
                    faltaStockDeAlgo = true;
                }
                // Carlos 23/10/25: En modificaciones, si la línea TENÍA reservas antes pero ya NO las necesita, informar
                // Carlos 28/10/25: FIX - Verificar SIEMPRE si antes necesitaba reservas, no solo cuando cambió algo
                else if (tipoCorreo == "Modificación" && linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !linea.EsLineaNueva)
                {
                    // Verificar si ANTES necesitaba reservas (solo si tenemos CantidadAnterior)
                    if (linea.CantidadAnterior.HasValue)
                    {
                        string productoAnterior = linea.CambioProducto ? linea.ProductoAnterior : linea.Producto;
                        int cantidadAnterior = linea.CantidadAnterior.Value;

                        // Calcular si ANTES necesitaba reservas
                        int stockAlmacenAntes = gestorStocks.Stock(productoAnterior, linea.almacen);
                        int pendientesTotalesAntes = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(productoAnterior, linea.almacen);
                        // Restar la cantidad actual del pedido para calcular pendientes de otros pedidos
                        int pendientesOtrosPedidosAntes = Math.Max(0, pendientesTotalesAntes - linea.Cantidad);
                        int disponibleAntes = stockAlmacenAntes - pendientesOtrosPedidosAntes;
                        int faltanteAntes = cantidadAnterior - disponibleAntes;

                        if (faltanteAntes > 0)
                        {
                            // Antes SÍ necesitaba reservas, pero ahora NO (ya que colorCantidad != "DeepPink")
                            // Informar al almacén que debe LIBERAR reservas
                            // Esto puede pasar por:
                            // 1. Cambió el producto o la cantidad
                            // 2. Llegó stock al almacén principal
                            // 3. Se liberaron otros pedidos
                            textoReserva = GenerarTextoLiberarReservas(linea, productoAnterior, cantidadAnterior, gestorStocks, servicioGestorStocks);
                        }
                    }
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
                if (hayDescuentos)
                {
                    _ = s.Append("<td style=\"text-align:right\">" + linea.SumaDescuentosSinPP.ToString("P") + "</td>");
                }
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
                int colspanNotas = 6 + (hayDescuentos ? 1 : 0) + (hayLineasConReservas ? 1 : 0);
                _ = s.AppendLine($"<td colspan='{colspanNotas}'>{textoServirMantener.Trim()}</td>");
                _ = s.AppendLine("</tr>");
            }
            // Carlos 01/12/25: Refactorizado para usar método testeable (Issue #48)
            int colspanValidacion = 6 + (hayDescuentos ? 1 : 0) + (hayLineasConReservas ? 1 : 0);
            string htmlValidacion = GenerarHtmlSeccionValidacion(pedido.CreadoSinPasarValidacion, respuestaValidacion, colspanValidacion);
            if (!string.IsNullOrEmpty(htmlValidacion))
            {
                _ = s.Append(htmlValidacion);
            }
            _ = s.AppendLine("</tbody>");
            _ = s.AppendLine("</table>");

            // Carlos 27/01/26: Issue #79 - Mostrar descuento pronto pago si existe
            if (pedido.DescuentoPP > 0)
            {
                decimal importeDtoPP = pedido.Lineas.Sum(l => l.Bruto) * pedido.DescuentoPP;
                _ = s.AppendLine("<table border=\"1\" style=\"width:100%\">");
                _ = s.AppendLine("<tbody align=\"right\">");
                _ = s.AppendLine("<tr>");
                _ = s.AppendLine($"<td style=\"text-align:right\"><strong>Dto. PP: {pedido.DescuentoPP.ToString("P")}</strong></td>");
                _ = s.AppendLine($"<td style=\"text-align:right\"><strong>{importeDtoPP.ToString("C")}</strong></td>");
                _ = s.AppendLine("</tr>");
                _ = s.AppendLine("</tbody>");
                _ = s.AppendLine("</table>");
            }

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

            // Carlos 23/10/25: Mostrar periodo de facturación, efectos y/o forma de pago
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

            // Carlos 23/10/25: Mostrar tabla de prepagos SIEMPRE que existan (independiente de efectos)
            if (pedido.Prepagos != null && pedido.Prepagos.Any())
            {
                _ = s.AppendLine("<table border=\"1\" style=\"width:100%\">");
                _ = s.AppendLine("<thead align = \"center\">");
                _ = s.Append("<tr><th>Concepto</th>");
                _ = s.Append("<th>Importe</th></tr>");
                _ = s.AppendLine("</thead>");
                _ = s.AppendLine("<tbody align = \"right\">");
                foreach (PrepagoDTO prepago in pedido.Prepagos)
                {
                    string concepto = string.IsNullOrWhiteSpace(prepago.ConceptoAdicional)
                        ? "Prepago"
                        : $"Prepago {prepago.ConceptoAdicional.Trim()}";
                    _ = s.Append("<tr><td style=\"text-align:left\">" + concepto + "</td>");
                    _ = s.Append("<td style=\"text-align:right\">" + prepago.Importe.ToString("C") + "</td></tr>");
                }
                _ = s.AppendLine("</tbody>");
                _ = s.AppendLine("</table>");
            }

            return s;
        }

        // Carlos 23/10/25: Cambiado de private a internal para poder hacer tests
        internal string CalcularReservasAlmacenes(LineaPedidoVentaDTO linea, GestorStocks gestorStocks,
            IServicioGestorStocks servicioGestorStocks, Dictionary<string, HashSet<string>> almacenesConReservas)
        {
            // Solo procesamos si es una línea de producto
            if (linea.tipoLinea != Constantes.TiposLineaVenta.PRODUCTO)
            {
                return "";
            }

            // Carlos 24/10/25: No reservamos para líneas en estado PRESUPUESTO (no confirmadas)
            if (linea.estado == Constantes.EstadosLineaVenta.PRESUPUESTO)
            {
                return "";
            }

            // Carlos 24/10/25: Solo mostramos "Antes: X uds" si el producto anterior TAMBIÉN necesitaba reservas
            StringBuilder prefijo = new StringBuilder();
            bool productoAnteriorNecesitabaReservas = false;

            // Verificar si había un cambio de cantidad o producto
            if ((linea.CantidadAnterior.HasValue && linea.CantidadAnterior.Value != linea.Cantidad) || linea.CambioProducto)
            {
                // Determinar qué producto y cantidad usar para verificar si necesitaba reservas antes
                string productoAVerificar = linea.CambioProducto ? linea.ProductoAnterior : linea.Producto;
                int cantidadAVerificar = linea.CantidadAnterior ?? linea.Cantidad;

                // Calcular si el producto anterior necesitaba reservas
                int stockAlmacenAnterior = gestorStocks.Stock(productoAVerificar, linea.almacen);
                int pendientesTotalesAnterior = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(productoAVerificar, linea.almacen);
                // Restar la cantidad actual del pedido para calcular pendientes de otros pedidos
                int pendientesOtrosPedidosAnterior = Math.Max(0, pendientesTotalesAnterior - linea.Cantidad);
                int disponibleAnterior = stockAlmacenAnterior - pendientesOtrosPedidosAnterior;
                int faltanteAnterior = Math.Max(0, cantidadAVerificar - disponibleAnterior);

                productoAnteriorNecesitabaReservas = faltanteAnterior > 0;

                // Solo mostramos el prefijo si el producto anterior TAMBIÉN necesitaba reservas
                if (productoAnteriorNecesitabaReservas && linea.CantidadAnterior.HasValue)
                {
                    _ = prefijo.Append($"<em>Antes: {linea.CantidadAnterior.Value} uds → Ahora: {linea.Cantidad} uds</em><br/>");
                }
            }

            // Calculamos cuánto falta en el almacén solicitado
            int stockAlmacenSolicitado = gestorStocks.Stock(linea.Producto, linea.almacen);
            int pendientesTotales = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(linea.Producto, linea.almacen);
            int pendientesOtrosPedidos = Math.Max(0, pendientesTotales - linea.Cantidad);
            int disponibleReal = stockAlmacenSolicitado - pendientesOtrosPedidos;
            int cantidadFaltante = Math.Max(0, linea.Cantidad - disponibleReal);

            if (cantidadFaltante <= 0)
            {
                // No falta nada, no necesitamos reservas
                // Carlos 23/10/25: pero si la cantidad cambió, devolvemos el prefijo
                return prefijo.ToString();
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
                // Carlos 23/10/25: si no hay stock en otros almacenes, devolvemos solo el prefijo (si existe)
                return prefijo.ToString();
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
                // Carlos 23/10/25: Hay varios almacenes con stock
                // Mostramos cuánto falta EN TOTAL y qué almacenes tienen stock disponible
                _ = textoReserva.Append("<strong>");

                // Primero mostramos cuánto falta en total
                _ = textoReserva.Append($"Faltan {cantidadFaltante} uds. Stock disponible:<br/>");

                // Luego listamos los almacenes con stock disponible
                List<string> reservasPorAlmacen = new List<string>();
                int totalDisponibleOtrosAlmacenes = 0;

                foreach (string almacen in almacenesDisponibles)
                {
                    int stockAlmacen = gestorStocks.Stock(linea.Producto, almacen);
                    int pendientesAlmacen = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(linea.Producto, almacen);
                    int disponibleAlmacen = stockAlmacen - pendientesAlmacen;
                    totalDisponibleOtrosAlmacenes += disponibleAlmacen;

                    reservasPorAlmacen.Add($"{almacen}: {disponibleAlmacen} uds");
                }

                _ = textoReserva.Append(string.Join("<br/>", reservasPorAlmacen));
                _ = textoReserva.Append("</strong>");
            }

            // Carlos 23/10/25: combinamos el prefijo (si existe) con el texto de reserva
            return prefijo.ToString() + textoReserva.ToString();
        }

        private HashSet<string> ObtenerAlmacenesConReservas()
        {
            HashSet<string> almacenes = new HashSet<string>();
            GestorStocks gestorStocks = new GestorStocks(servicioGestorStocks);

            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                // Carlos 24/10/25: Ignorar líneas en estado PRESUPUESTO
                if (linea.tipoLinea != Constantes.TiposLineaVenta.PRODUCTO ||
                    linea.estado == Constantes.EstadosLineaVenta.PRESUPUESTO)
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

        // Carlos 23/10/25: Obtener almacenes que TENÍAN reservas antes de la modificación
        private HashSet<string> ObtenerAlmacenesConReservasAnteriores()
        {
            HashSet<string> almacenes = new HashSet<string>();
            GestorStocks gestorStocks = new GestorStocks(servicioGestorStocks);

            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                // Carlos 24/10/25: Ignorar líneas nuevas, no productos y en estado PRESUPUESTO
                if (linea.EsLineaNueva ||
                    linea.tipoLinea != Constantes.TiposLineaVenta.PRODUCTO ||
                    linea.estado == Constantes.EstadosLineaVenta.PRESUPUESTO)
                {
                    continue;
                }

                // Si no tiene cantidad anterior, asumimos que no había reservas antes
                if (!linea.CantidadAnterior.HasValue)
                {
                    continue;
                }

                // Si cambió el producto, usamos el producto anterior para calcular
                string productoAEvaluar = linea.CambioProducto ? linea.ProductoAnterior : linea.Producto;

                // Calculamos el color que TENÍA antes (usando la cantidad anterior y el producto anterior si cambió)
                int stockAlmacenSolicitado = gestorStocks.Stock(productoAEvaluar, linea.almacen);
                int pendientesTotalesAlmacenSolicitado = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(productoAEvaluar, linea.almacen);
                // Restar la cantidad actual del pedido para calcular pendientes de otros pedidos
                int pendientesOtrosPedidos = Math.Max(0, pendientesTotalesAlmacenSolicitado - linea.Cantidad);
                int disponibleAlmacenSolicitado = stockAlmacenSolicitado - pendientesOtrosPedidos;

                // Si antes no faltaba stock, no había reservas
                int cantidadFaltanteAnterior = linea.CantidadAnterior.Value - disponibleAlmacenSolicitado;
                if (cantidadFaltanteAnterior <= 0)
                {
                    continue;
                }

                // Si llegamos aquí, antes SÍ faltaba stock. Buscar en qué almacenes había stock disponible
                foreach (string almacen in Constantes.Sedes.ListaSedes)
                {
                    if (almacen == linea.almacen)
                    {
                        continue;
                    }

                    int stockAlmacen = gestorStocks.Stock(productoAEvaluar, almacen);
                    int pendientesAlmacen = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(productoAEvaluar, almacen);
                    int disponibleAlmacen = stockAlmacen - pendientesAlmacen;

                    if (disponibleAlmacen > 0)
                    {
                        _ = almacenes.Add(almacen);
                    }
                }
            }

            return almacenes;
        }

        // Carlos 23/10/25: Generar texto para informar que se deben LIBERAR reservas
        // Cambiado de private a internal para poder hacer tests
        internal string GenerarTextoLiberarReservas(
            LineaPedidoVentaDTO linea,
            string productoAnterior,
            int cantidadAnterior,
            GestorStocks gestorStocks,
            IServicioGestorStocks servicioGestorStocks)
        {
            StringBuilder textoLiberar = new StringBuilder();

            // Calcular qué almacenes tenían stock del producto anterior
            int stockAlmacenSolicitado = gestorStocks.Stock(productoAnterior, linea.almacen);
            int pendientesTotalesAlmacenSolicitado = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(productoAnterior, linea.almacen);
            // Restar la cantidad actual del pedido para calcular pendientes de otros pedidos
            int pendientesOtrosPedidos = Math.Max(0, pendientesTotalesAlmacenSolicitado - linea.Cantidad);
            int disponibleAlmacenSolicitado = stockAlmacenSolicitado - pendientesOtrosPedidos;
            int cantidadFaltanteAnterior = Math.Max(0, cantidadAnterior - disponibleAlmacenSolicitado);

            if (cantidadFaltanteAnterior <= 0)
            {
                return ""; // No había reservas antes
            }

            // Buscar en qué almacenes había stock disponible
            List<string> almacenesConStockAnterior = new List<string>();
            foreach (string almacen in Constantes.Sedes.ListaSedes)
            {
                if (almacen == linea.almacen)
                {
                    continue;
                }

                int stockAlmacen = gestorStocks.Stock(productoAnterior, almacen);
                int pendientesAlmacen = servicioGestorStocks.UnidadesPendientesEntregarAlmacen(productoAnterior, almacen);
                int disponibleAlmacen = stockAlmacen - pendientesAlmacen;

                if (disponibleAlmacen > 0)
                {
                    almacenesConStockAnterior.Add(almacen);
                }
            }

            if (almacenesConStockAnterior.Count == 0)
            {
                return ""; // No había almacenes con stock antes
            }

            // Generar el mensaje de liberar
            _ = textoLiberar.Append("<strong style=\"color: green;\">✓ LIBERAR: ");

            if (linea.CambioProducto)
            {
                // Cambió el producto
                _ = textoLiberar.Append($"Antes: {productoAnterior} ({cantidadAnterior} uds)");
                if (linea.Cantidad == 0)
                {
                    _ = textoLiberar.Append(" → ELIMINADO");
                }
                else
                {
                    _ = textoLiberar.Append($" → Ahora: {linea.Producto} ({linea.Cantidad} uds)");
                }
            }
            else if (linea.Cantidad == 0)
            {
                // Se eliminó la línea (cantidad = 0)
                _ = textoLiberar.Append($"{cantidadAnterior} uds ELIMINADAS");
            }
            else if (cantidadAnterior != linea.Cantidad)
            {
                // Cambió la cantidad (disminuyó o aumentó)
                _ = textoLiberar.Append($"Antes: {cantidadAnterior} uds → Ahora: {linea.Cantidad} uds");
            }
            else
            {
                // Carlos 28/10/25: NO cambió nada en la línea, pero llegó stock al almacén principal
                _ = textoLiberar.Append($"{linea.Cantidad} uds - Stock disponible en almacén principal");
            }

            _ = textoLiberar.Append("</strong>");

            return textoLiberar.ToString();
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

        /// <summary>
        /// Carlos 01/12/25: Método interno para generar el HTML de la sección de validación (Issue #48)
        /// Extraído para poder testear que la respuestaValidacion se muestra correctamente
        /// </summary>
        /// <param name="creadoSinPasarValidacion">Si el pedido fue creado sin pasar validación</param>
        /// <param name="respuesta">La respuesta de validación (puede ser null)</param>
        /// <param name="colspan">Número de columnas para el colspan</param>
        /// <returns>HTML de la sección de validación, o string vacío si no aplica</returns>
        internal string GenerarHtmlSeccionValidacion(bool creadoSinPasarValidacion, RespuestaValidacion respuesta, int colspan)
        {
            if (!creadoSinPasarValidacion)
            {
                return string.Empty;
            }

            StringBuilder s = new StringBuilder();
            _ = s.AppendLine("<tr style=\"color: red;\">");

            if (respuesta != null)
            {
                if (!respuesta.ValidacionSuperada)
                {
                    // La validación actual NO pasa - mostrar el motivo del error
                    string motivo = !string.IsNullOrEmpty(respuesta.Motivo) ?
                        respuesta.Motivo :
                        (respuesta.Motivos != null && respuesta.Motivos.Any() ?
                            string.Join("<br/>• ", respuesta.Motivos.Select(m => m)) :
                            "Error de validación no especificado");
                    _ = s.AppendLine($"<td colspan='{colspan}'>Nota: pedido creado sin pasar validación (actualmente NO pasaría la validación)<br/><strong>Motivo:</strong><br/>• {motivo.Replace(Environment.NewLine, "<br/>• ")}</td>");
                }
                else
                {
                    // La validación actual SÍ pasa - informar que ahora pasaría
                    string motivo = !string.IsNullOrEmpty(respuesta.Motivo) ?
                        respuesta.Motivo :
                        "El pedido ahora pasaría las validaciones correctamente";
                    _ = s.AppendLine($"<td colspan='{colspan}'>Nota: pedido creado sin pasar validación (actualmente pasaría la validación)<br/><strong>Info:</strong> {motivo.Replace(Environment.NewLine, "<br/>")}</td>");
                }
            }
            else
            {
                _ = s.AppendLine($"<td colspan='{colspan}'>Nota: pedido creado sin pasar validación (respuestaValidacion es null)</td>");
            }

            _ = s.AppendLine("</tr>");
            return s.ToString();
        }

        public void Rellenar(int numeroPedido)
        {
            // cargar el pedido completo a partir del número
        }
    }
}