using NestoAPI.Models.Informes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.PedidosCompra
{
    /// <summary>
    /// Genera el PDF de la "ORDEN DE COMPRA" a proveedores con QuestPDF, sustituyendo el RDLC
    /// PedidoCompra.rdlc que Nesto renderizaba en local (roadmap Nesto#340/#386: mover el render de
    /// informes al backend y eliminar RDLC). Conserva todo el contenido del RDLC (datos del
    /// proveedor, líneas, total, nota de precios) y le da el lenguaje visual del resto de documentos
    /// de Nueva Visión (logo + sello Madrid Excelente, NestoAPI#244, accesorio al logo).
    ///
    /// Si <see cref="PedidoCompraInformeDTO.PedidoValorado"/> es false, NO se muestran precios
    /// (Precio/Dto/Importe/Total), igual que hacía el RDLC.
    /// </summary>
    public class GeneradorPdfPedidoCompra
    {
        // Datos fijos de la empresa emisora (idénticos a los del pie del RDLC PedidoCompra.rdlc).
        private const string EMPRESA_NOMBRE = "NUEVA VISIÓN, S.A.";
        private const string EMPRESA_DIRECCION = "C/ Río Tiétar, 11";
        private const string EMPRESA_POBLACION = "28119 Algete (Madrid)";
        private const string EMPRESA_CIF = "CIF: A/78368255";
        private const string EMPRESA_EMAIL = "compras@nuevavision.es";

        // Logo de Nueva Visión (mismo que usan las facturas de la serie NV).
        private const string URL_LOGO = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";

        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-ES");
        private static readonly byte[] _selloMadridExcelente = RecursosGraficos.SelloMadridExcelente;

        private readonly Func<byte[]> _cargarLogo;

        public GeneradorPdfPedidoCompra() : this(CargarLogoDesdeUrl) { }

        /// <summary>Constructor para test: permite inyectar la carga del logo (evita la llamada HTTP).</summary>
        internal GeneradorPdfPedidoCompra(Func<byte[]> cargarLogo)
        {
            _cargarLogo = cargarLogo ?? CargarLogoDesdeUrl;
        }

        public ByteArrayContent GenerarPdf(PedidoCompraInformeDTO pedido)
        {
            if (pedido == null)
            {
                throw new ArgumentNullException(nameof(pedido));
            }

            byte[] logoBytes = _cargarLogo();
            bool mostrarPrecios = pedido.PedidoValorado;

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(0.8f, Unit.Centimetre);
                    page.MarginHorizontal(0.8f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header().Element(c => ComponerCabecera(c, logoBytes));
                    page.Content().Element(c => ComponerContenido(c, pedido, mostrarPrecios));
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private void ComponerCabecera(IContainer container, byte[] logoBytes)
        {
            container.Column(column =>
            {
                // FILA 1: logo (izquierda) + sello (hueco central, accesorio) + datos empresa (derecha).
                column.Item().Row(row =>
                {
                    if (logoBytes != null && logoBytes.Length > 0)
                    {
                        row.ConstantItem(6.05f, Unit.Centimetre).Height(3.96f, Unit.Centimetre)
                            .AlignLeft().AlignTop().Image(logoBytes, ImageScaling.FitArea);
                    }
                    else
                    {
                        row.ConstantItem(6.05f, Unit.Centimetre).Height(3.96f, Unit.Centimetre);
                    }

                    // Sello Madrid Excelente: accesorio al logo (3,5 cm < 6,05 cm), a su derecha y
                    // centrado a su altura, sin agrandar la cabecera (NestoAPI#244).
                    if (_selloMadridExcelente != null)
                    {
                        row.RelativeItem().AlignMiddle().AlignCenter()
                            .Width(3.5f, Unit.Centimetre).Image(_selloMadridExcelente, ImageScaling.FitWidth);
                    }
                    else
                    {
                        row.RelativeItem();
                    }
                    row.ConstantItem(10);

                    row.RelativeItem().AlignMiddle().AlignCenter().Column(col =>
                    {
                        col.Item().AlignCenter().Text(EMPRESA_NOMBRE).Bold().FontSize(10);
                        col.Item().AlignCenter().Text(EMPRESA_DIRECCION).FontSize(8);
                        col.Item().AlignCenter().Text(EMPRESA_POBLACION).FontSize(8);
                        col.Item().AlignCenter().Text(EMPRESA_CIF).FontSize(8);
                        col.Item().AlignCenter().Text(EMPRESA_EMAIL).FontSize(8);
                    });
                });

                column.Item().PaddingVertical(6);

                column.Item().AlignCenter().Text("ORDEN DE COMPRA").Bold().FontSize(16);

                column.Item().PaddingVertical(4);
            });
        }

        private void ComponerContenido(IContainer container, PedidoCompraInformeDTO pedido, bool mostrarPrecios)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComponerDatosProveedor(c, pedido));
                column.Item().PaddingVertical(6);
                column.Item().Element(c => ComponerTablaLineas(c, pedido, mostrarPrecios));

                if (mostrarPrecios)
                {
                    decimal total = pedido.Lineas?.Sum(l => l.BaseImponible) ?? 0m;
                    column.Item().PaddingTop(4).AlignRight().Text(text =>
                    {
                        text.Span("Total: ").Bold().FontSize(10);
                        text.Span(FormatoImporte(total)).Bold().FontSize(10);
                    });
                }

                // Condiciones fijas del pedido (texto literal del RDLC PedidoCompra.rdlc). Son
                // legal/operativamente relevantes (confirmación de fecha de entrega, sin medios para
                // descargar palets): deben aparecer siempre.
                column.Item().PaddingTop(12).Text(
                    "Les rogamos nos avisen en el caso de que los precios que les ponemos no coincidan con sus precios.")
                    .FontSize(8);
                column.Item().PaddingTop(6).Text(
                    "Solicitamos también confirmación de la fecha de entrega del pedido. El pedido no se " +
                    "considerará definitivo hasta que no se haya aceptado dicha fecha de entrega.")
                    .FontSize(8);
                column.Item().PaddingTop(6).Text(
                    "En nuestras instalaciones NO disponemos de maquinaria para descargar palets de los " +
                    "camiones. En caso de recibir palets en camiones sin trampilla elevadora no aceptaremos " +
                    "la entrega de la mercancía.")
                    .FontSize(8);
                column.Item().PaddingTop(8).Text("Muchas gracias").FontSize(8);
            });
        }

        private void ComponerDatosProveedor(IContainer container, PedidoCompraInformeDTO pedido)
        {
            container.Row(row =>
            {
                // PROVEEDOR (izquierda)
                row.RelativeItem(2).Border(1).BorderColor(Colors.Grey.Lighten1).Column(col =>
                {
                    col.Item().Background(Colors.Grey.Lighten3).Padding(3).Text("PROVEEDOR").Bold().FontSize(8);
                    col.Item().Padding(5).Column(inner =>
                    {
                        inner.Item().Text(pedido.Nombre ?? "").Bold().FontSize(9);
                        inner.Item().Text(pedido.Direccion ?? "").FontSize(8);
                        inner.Item().Text(PoblacionCompleta(pedido)).FontSize(8);
                        if (!string.IsNullOrWhiteSpace(pedido.Telefono))
                        {
                            inner.Item().Text($"Tel. {pedido.Telefono}").FontSize(8);
                        }
                        inner.Item().Text($"Cód. proveedor: {pedido.Proveedor}").FontSize(8);
                        if (!string.IsNullOrWhiteSpace(pedido.Cif))
                        {
                            inner.Item().Text($"CIF/NIF: {pedido.Cif}").FontSize(8);
                        }
                    });
                });

                row.ConstantItem(10);

                // DATOS DEL PEDIDO (derecha)
                row.RelativeItem(1).Border(1).BorderColor(Colors.Grey.Lighten1).Column(col =>
                {
                    col.Item().Background(Colors.Grey.Lighten3).Padding(3).Text("PEDIDO").Bold().FontSize(8);
                    col.Item().Padding(5).Column(inner =>
                    {
                        inner.Item().Text($"Nº Pedido: {pedido.Id}").Bold().FontSize(9);
                        inner.Item().Text($"Fecha: {pedido.Fecha:dd/MM/yyyy}").FontSize(8);
                    });
                });
            });
        }

        private void ComponerTablaLineas(IContainer container, PedidoCompraInformeDTO pedido, bool mostrarPrecios)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(55);   // S/ Ref.
                    columns.ConstantColumn(55);   // N/ Ref.
                    columns.RelativeColumn(3);    // Descripción
                    columns.ConstantColumn(38);   // Tamaño
                    columns.ConstantColumn(50);   // Cantidad
                    if (mostrarPrecios)
                    {
                        columns.ConstantColumn(55);   // Precio
                        columns.ConstantColumn(40);   // Dto.
                        columns.ConstantColumn(60);   // Importe
                    }
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "S/ Ref.");
                    CeldaCabecera(header.Cell(), "N/ Ref.");
                    CeldaCabecera(header.Cell(), "Descripción");
                    CeldaCabecera(header.Cell(), "Tamaño", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Cantidad", alinearDerecha: true);
                    if (mostrarPrecios)
                    {
                        CeldaCabecera(header.Cell(), "Precio", alinearDerecha: true);
                        CeldaCabecera(header.Cell(), "Dto.", alinearDerecha: true);
                        CeldaCabecera(header.Cell(), "Importe", alinearDerecha: true);
                    }
                });

                if (pedido.Lineas != null)
                {
                    foreach (var linea in pedido.Lineas)
                    {
                        CeldaDato(table.Cell(), linea.SuReferencia);
                        CeldaDato(table.Cell(), linea.NuestraReferencia);
                        CeldaDato(table.Cell(), linea.Descripcion);
                        CeldaDato(table.Cell(), FormatoTamanno(linea.Tamanno), alinearDerecha: true);
                        CeldaDato(table.Cell(), FormatoCantidad(linea.Cantidad, linea.UnidadMedida), alinearDerecha: true);
                        if (mostrarPrecios)
                        {
                            CeldaDato(table.Cell(), FormatoImporte(linea.PrecioUnitario), alinearDerecha: true);
                            CeldaDato(table.Cell(), FormatoDescuento(linea.SumaDescuentos), alinearDerecha: true);
                            CeldaDato(table.Cell(), FormatoImporte(linea.BaseImponible), alinearDerecha: true);
                        }
                    }
                }
            });
        }

        private void ComponerPie(IContainer container)
        {
            container.AlignCenter().Text($"{EMPRESA_NOMBRE}  ·  {EMPRESA_DIRECCION}  ·  {EMPRESA_POBLACION}  ·  {EMPRESA_EMAIL}")
                .FontSize(7).FontColor(Colors.Grey.Darken1);
        }

        private static void CeldaCabecera(IContainer celda, string texto, bool alinearDerecha = false)
        {
            var contenedor = celda.Background(Colors.Grey.Lighten3).BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Padding(3);
            if (alinearDerecha)
            {
                contenedor = contenedor.AlignRight();
            }
            contenedor.Text(texto).Bold().FontSize(8);
        }

        private static void CeldaDato(IContainer celda, string texto, bool alinearDerecha = false)
        {
            var contenedor = celda.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3);
            if (alinearDerecha)
            {
                contenedor = contenedor.AlignRight();
            }
            contenedor.Text(texto ?? "").FontSize(8);
        }

        private static string PoblacionCompleta(PedidoCompraInformeDTO pedido)
        {
            string provincia = string.IsNullOrWhiteSpace(pedido.Provincia) ? "" : $" ({pedido.Provincia})";
            return $"{pedido.CodigoPostal} {pedido.Poblacion}{provincia}".Trim();
        }

        private static string FormatoTamanno(short? tamanno)
            => tamanno.HasValue && tamanno.Value != 0 ? tamanno.Value.ToString(Cultura) : "";

        private static string FormatoCantidad(short? cantidad, string unidadMedida)
        {
            string valor = (cantidad ?? 0).ToString(Cultura);
            return string.IsNullOrWhiteSpace(unidadMedida) ? valor : $"{valor} {unidadMedida.Trim()}";
        }

        private static string FormatoImporte(decimal importe)
            => importe.ToString("N2", Cultura) + " €";

        private static string FormatoDescuento(decimal descuento)
            => descuento.ToString("0.00%", Cultura);

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
