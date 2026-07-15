using NestoAPI.Models.Informes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Collections.Generic;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Genera el PDF del informe "Montar kit" con QuestPDF, sustituyendo el RDLC
    /// MontarKitProductos.rdlc que Nesto renderizaba en local al montar un kit en el almacén
    /// central (Nesto#340). Mantiene las columnas y proporciones del RDLC (A4 apaisado):
    /// Producto, Nombre, Tamaño, Und.M, Familia, Cant., Pas., Fila, Col. y Código Barras.
    /// </summary>
    public class GeneradorPdfMontarKitProductos
    {
        public ByteArrayContent GenerarPdf(int traspaso, List<MontarKitProductosDTO> lineas)
        {
            List<MontarKitProductosDTO> datos = lineas ?? new List<MontarKitProductosDTO>();

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.MarginVertical(0.8f, Unit.Centimetre);
                    page.MarginHorizontal(0.8f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header().Element(c => ComponerCabecera(c, traspaso));
                    page.Content().Element(c => ComponerTabla(c, datos));
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private static void ComponerCabecera(IContainer container, int traspaso)
        {
            container.PaddingBottom(5)
                .Text($"MONTAR KIT (traspaso {traspaso})").Bold().FontSize(14);
        }

        private static void ComponerTabla(IContainer container, List<MontarKitProductosDTO> lineas)
        {
            container.Table(table =>
            {
                // Proporciones de las columnas del RDLC (anchos en cm).
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.81f);  // Producto
                    columns.RelativeColumn(13.9f);  // Nombre
                    columns.RelativeColumn(1.89f);  // Tamaño
                    columns.RelativeColumn(1.23f);  // Und.M
                    columns.RelativeColumn(2.5f);   // Familia
                    columns.RelativeColumn(0.99f);  // Cant.
                    columns.RelativeColumn(0.89f);  // Pas.
                    columns.RelativeColumn(0.81f);  // Fila
                    columns.RelativeColumn(0.83f);  // Col.
                    columns.RelativeColumn(3.15f);  // Código Barras
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Producto", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Nombre", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Tamaño", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Und.M", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Familia", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Cant.", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Pas.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Fila", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Col.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Codigo Barras", alinearDerecha: false);
                });

                foreach (MontarKitProductosDTO linea in lineas)
                {
                    CeldaDato(table.Cell(), linea.Producto?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Nombre?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Tamanno?.ToString() ?? "", alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.UnidadMedida?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Familia?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Cantidad.ToString(), alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.Pasillo?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Fila?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Columna?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.CodigoBarras?.Trim() ?? "", alinearDerecha: false);
                }
            });
        }

        private static void CeldaCabecera(IContainer celda, string texto, bool alinearDerecha)
        {
            IContainer contenido = celda.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).Bold().FontSize(8);
        }

        private static void CeldaDato(IContainer celda, string texto, bool alinearDerecha)
        {
            IContainer contenido = celda.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).FontSize(8);
        }

        private static void ComponerPie(IContainer container)
        {
            container.AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8));
                text.Span("Página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        }
    }
}
