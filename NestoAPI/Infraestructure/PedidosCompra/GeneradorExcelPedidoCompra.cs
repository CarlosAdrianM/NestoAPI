using ClosedXML.Excel;
using NestoAPI.Models.Informes;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NestoAPI.Infraestructure.PedidosCompra
{
    /// <summary>
    /// Genera la "ORDEN DE COMPRA" a proveedor en formato Excel (.xlsx) con ClosedXML, espejando el
    /// diseño del PDF (ver <see cref="GeneradorPdfPedidoCompra"/>): logo de Nueva Visión + sello Madrid
    /// Excelente + datos de empresa en la cabecera, título centrado, recuadros de proveedor y pedido,
    /// tabla de líneas, total y notas. Así el proveedor que imprima el Excel ve la misma imagen que el
    /// que imprima el PDF. Algunos proveedores prefieren el Excel; hoy lo produce el RDLC (render
    /// EXCELOPENXML) y el objetivo es eliminar ese RDLC, generándolo aquí desde el mismo DTO.
    ///
    /// Si <see cref="PedidoCompraInformeDTO.PedidoValorado"/> es false NO se muestran precios.
    /// </summary>
    public class GeneradorExcelPedidoCompra
    {
        private const string EMPRESA_NOMBRE = "NUEVA VISIÓN, S.A.";
        private const string EMPRESA_DIRECCION = "C/ Río Tiétar, 11";
        private const string EMPRESA_POBLACION = "28119 Algete (Madrid)";
        private const string EMPRESA_CIF = "CIF: A/78368255";
        private const string EMPRESA_EMAIL = "compras@nuevavision.es";

        private const string URL_LOGO = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";

        private const string FORMATO_IMPORTE = "#,##0.00 €";
        private const string FORMATO_DESCUENTO = "0.00%";

        private static readonly byte[] _selloMadridExcelente = RecursosGraficos.SelloMadridExcelente;

        private readonly Func<byte[]> _cargarLogo;

        public GeneradorExcelPedidoCompra() : this(CargarLogoDesdeUrl) { }

        /// <summary>Constructor para test: permite inyectar la carga del logo (evita la llamada HTTP).</summary>
        internal GeneradorExcelPedidoCompra(Func<byte[]> cargarLogo)
        {
            _cargarLogo = cargarLogo ?? CargarLogoDesdeUrl;
        }

        public ByteArrayContent GenerarExcel(PedidoCompraInformeDTO pedido)
        {
            if (pedido == null)
            {
                throw new ArgumentNullException(nameof(pedido));
            }

            byte[] logoBytes = _cargarLogo();
            bool mostrarPrecios = pedido.PedidoValorado;

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Orden de compra");
                AnchosColumnas(ws);

                ComponerCabecera(ws, logoBytes);
                int fila = ComponerDatosProveedor(ws, pedido); // devuelve la primera fila libre tras los recuadros
                fila++;
                fila = ComponerTablaLineas(ws, pedido, mostrarPrecios, fila);
                fila++;
                ComponerNotas(ws, fila);

                using (var ms = new MemoryStream())
                {
                    workbook.SaveAs(ms);
                    var contenido = new ByteArrayContent(ms.ToArray());
                    contenido.Headers.ContentType = new MediaTypeHeaderValue(
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                    return contenido;
                }
            }
        }

        // Anchos fijos (en "caracteres" de Excel) de las 8 columnas de la tabla. Evita que AdjustToContents
        // infle la columna A con la referencia del proveedor o las notas largas.
        private static void AnchosColumnas(IXLWorksheet ws)
        {
            ws.Column(1).Width = 12;  // S/ Ref.
            ws.Column(2).Width = 12;  // N/ Ref.
            ws.Column(3).Width = 42;  // Descripción
            ws.Column(4).Width = 9;   // Tamaño
            ws.Column(5).Width = 11;  // Cantidad
            ws.Column(6).Width = 13;  // Precio
            ws.Column(7).Width = 9;   // Dto.
            ws.Column(8).Width = 13;  // Importe
        }

        private void ComponerCabecera(IXLWorksheet ws, byte[] logoBytes)
        {
            // Logo arriba a la izquierda y sello a su derecha (accesorio), como imágenes flotantes.
            AnadirImagen(ws, logoBytes, ws.Cell("A1"), 0, 210);
            AnadirImagen(ws, _selloMadridExcelente, ws.Cell("D1"), 5, 130);

            // Datos de empresa a la DERECHA (columnas F:H), centrados a la altura del logo.
            TextoCentrado(ws, "F1:H1", EMPRESA_NOMBRE, bold: true, tamanno: 11);
            TextoCentrado(ws, "F2:H2", EMPRESA_DIRECCION);
            TextoCentrado(ws, "F3:H3", EMPRESA_POBLACION);
            TextoCentrado(ws, "F4:H4", EMPRESA_CIF);
            TextoCentrado(ws, "F5:H5", EMPRESA_EMAIL);

            // Título centrado en todo el ancho, debajo de la cabecera.
            var titulo = ws.Range("A7:H7");
            titulo.Merge();
            titulo.FirstCell().Value = "ORDEN DE COMPRA";
            titulo.Style.Font.Bold = true;
            titulo.Style.Font.FontSize = 16;
            titulo.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(7).Height = 24;
        }

        // Pinta el recuadro PROVEEDOR (izquierda, A:D) y el recuadro PEDIDO (derecha, F:H).
        // Devuelve la fila de la más baja de las dos, para que el llamante siga debajo.
        private int ComponerDatosProveedor(IXLWorksheet ws, PedidoCompraInformeDTO pedido)
        {
            int filaInicio = 9;
            int fila = filaInicio;

            CabeceraRecuadro(ws, $"A{fila}:D{fila}", "PROVEEDOR");
            CabeceraRecuadro(ws, $"F{fila}:H{fila}", "PEDIDO");
            fila++;

            int filaProveedor = fila;
            CeldaRecuadro(ws, $"A{filaProveedor}:D{filaProveedor}", pedido.Nombre, bold: true); filaProveedor++;
            CeldaRecuadro(ws, $"A{filaProveedor}:D{filaProveedor}", pedido.Direccion); filaProveedor++;
            CeldaRecuadro(ws, $"A{filaProveedor}:D{filaProveedor}", PoblacionCompleta(pedido)); filaProveedor++;
            if (!string.IsNullOrWhiteSpace(pedido.Telefono))
            {
                CeldaRecuadro(ws, $"A{filaProveedor}:D{filaProveedor}", $"Tel. {pedido.Telefono}"); filaProveedor++;
            }
            CeldaRecuadro(ws, $"A{filaProveedor}:D{filaProveedor}", $"Cód. proveedor: {pedido.Proveedor}"); filaProveedor++;
            if (!string.IsNullOrWhiteSpace(pedido.Cif))
            {
                CeldaRecuadro(ws, $"A{filaProveedor}:D{filaProveedor}", $"CIF/NIF: {pedido.Cif}"); filaProveedor++;
            }

            int filaPedido = fila;
            CeldaRecuadro(ws, $"F{filaPedido}:H{filaPedido}", $"Nº Pedido: {pedido.Id}", bold: true); filaPedido++;
            CeldaRecuadro(ws, $"F{filaPedido}:H{filaPedido}", $"Fecha: {pedido.Fecha:dd/MM/yyyy}"); filaPedido++;

            // La primera fila libre es la de debajo del recuadro más largo.
            return Math.Max(filaProveedor, filaPedido);
        }

        private int ComponerTablaLineas(IXLWorksheet ws, PedidoCompraInformeDTO pedido, bool mostrarPrecios, int fila)
        {
            int numColumnas = mostrarPrecios ? 8 : 5;

            // Cabecera
            ws.Cell(fila, 1).Value = "S/ Ref.";
            ws.Cell(fila, 2).Value = "N/ Ref.";
            ws.Cell(fila, 3).Value = "Descripción";
            ws.Cell(fila, 4).Value = "Tamaño";
            ws.Cell(fila, 5).Value = "Cantidad";
            if (mostrarPrecios)
            {
                ws.Cell(fila, 6).Value = "Precio";
                ws.Cell(fila, 7).Value = "Dto.";
                ws.Cell(fila, 8).Value = "Importe";
            }
            var cabecera = ws.Range(fila, 1, fila, numColumnas);
            cabecera.Style.Font.Bold = true;
            cabecera.Style.Fill.BackgroundColor = XLColor.LightGray;
            cabecera.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ws.Range(fila, 4, fila, numColumnas).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            fila++;

            if (pedido.Lineas != null)
            {
                foreach (var linea in pedido.Lineas)
                {
                    ws.Cell(fila, 1).Value = linea.SuReferencia ?? "";
                    ws.Cell(fila, 2).Value = linea.NuestraReferencia ?? "";
                    ws.Cell(fila, 3).Value = linea.Descripcion ?? "";
                    if (linea.Tamanno.HasValue && linea.Tamanno.Value != 0)
                    {
                        ws.Cell(fila, 4).Value = linea.Tamanno.Value;
                    }
                    ws.Cell(fila, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    ws.Cell(fila, 5).Value = FormatoCantidad(linea.Cantidad, linea.UnidadMedida);
                    ws.Cell(fila, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    if (mostrarPrecios)
                    {
                        ws.Cell(fila, 6).Value = linea.PrecioUnitario;
                        ws.Cell(fila, 6).Style.NumberFormat.Format = FORMATO_IMPORTE;
                        ws.Cell(fila, 7).Value = linea.SumaDescuentos;
                        ws.Cell(fila, 7).Style.NumberFormat.Format = FORMATO_DESCUENTO;
                        ws.Cell(fila, 8).Value = linea.BaseImponible;
                        ws.Cell(fila, 8).Style.NumberFormat.Format = FORMATO_IMPORTE;
                    }
                    fila++;
                }
            }

            if (mostrarPrecios)
            {
                decimal total = pedido.Lineas?.Sum(l => l.BaseImponible) ?? 0m;
                ws.Cell(fila, 7).Value = "Total:";
                ws.Cell(fila, 7).Style.Font.Bold = true;
                ws.Cell(fila, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(fila, 8).Value = total;
                ws.Cell(fila, 8).Style.NumberFormat.Format = FORMATO_IMPORTE;
                ws.Cell(fila, 8).Style.Font.Bold = true;
                fila++;
            }

            return fila;
        }

        private void ComponerNotas(IXLWorksheet ws, int fila)
        {
            foreach (string nota in Notas)
            {
                var rango = ws.Range(fila, 1, fila, 8);
                rango.Merge();
                rango.FirstCell().Value = nota;
                rango.Style.Alignment.WrapText = true;
                rango.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                // Las celdas combinadas no autoajustan alto: lo fijamos según longitud (~110 car/línea).
                ws.Row(fila).Height = 15 * Math.Max(1, (int)Math.Ceiling(nota.Length / 110.0));
                fila++;
            }
        }

        private static readonly string[] Notas =
        {
            "Les rogamos nos avisen en el caso de que los precios que les ponemos no coincidan con sus precios.",
            "Solicitamos también confirmación de la fecha de entrega del pedido. El pedido no se considerará " +
                "definitivo hasta que no se haya aceptado dicha fecha de entrega.",
            "En nuestras instalaciones NO disponemos de maquinaria para descargar palets de los camiones. En " +
                "caso de recibir palets en camiones sin trampilla elevadora no aceptaremos la entrega de la mercancía.",
            "Muchas gracias"
        };

        private static void TextoCentrado(IXLWorksheet ws, string rango, string texto, bool bold = false, int tamanno = 0)
        {
            var r = ws.Range(rango);
            r.Merge();
            r.FirstCell().Value = texto;
            r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            r.Style.Font.Bold = bold;
            if (tamanno > 0)
            {
                r.Style.Font.FontSize = tamanno;
            }
        }

        private static void CabeceraRecuadro(IXLWorksheet ws, string rango, string texto)
        {
            var r = ws.Range(rango);
            r.Merge();
            r.FirstCell().Value = texto;
            r.Style.Font.Bold = true;
            r.Style.Fill.BackgroundColor = XLColor.LightGray;
            r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r.Style.Border.OutsideBorderColor = XLColor.LightGray;
        }

        private static void CeldaRecuadro(IXLWorksheet ws, string rango, string texto, bool bold = false)
        {
            var r = ws.Range(rango);
            r.Merge();
            r.FirstCell().Value = texto ?? "";
            r.Style.Font.Bold = bold;
        }

        private static void AnadirImagen(IXLWorksheet ws, byte[] bytes, IXLCell celda, int xOffset, double anchoObjetivoPx)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    var imagen = ws.AddPicture(ms);
                    imagen.MoveTo(celda, xOffset, 0);
                    if (imagen.Width > 0)
                    {
                        imagen.Scale(anchoObjetivoPx / imagen.Width);
                    }
                }
            }
            catch
            {
                // Una imagen que no se pueda cargar nunca debe romper la generación del Excel.
            }
        }

        private static string PoblacionCompleta(PedidoCompraInformeDTO pedido)
        {
            string provincia = string.IsNullOrWhiteSpace(pedido.Provincia) ? "" : $" ({pedido.Provincia})";
            return $"{pedido.CodigoPostal} {pedido.Poblacion}{provincia}".Trim();
        }

        private static string FormatoCantidad(short? cantidad, string unidadMedida)
        {
            string valor = (cantidad ?? 0).ToString();
            return string.IsNullOrWhiteSpace(unidadMedida) ? valor : $"{valor} {unidadMedida.Trim()}";
        }

        private static byte[] CargarLogoDesdeUrl()
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                try
                {
                    return client.GetByteArrayAsync(URL_LOGO).GetAwaiter().GetResult();
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
