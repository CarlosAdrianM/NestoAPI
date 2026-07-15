using NestoAPI.Models.Informes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Genera el PDF del informe de picking agrupado con QuestPDF, sustituyendo el RDLC
    /// Picking.rdlc que Nesto renderizaba en local (Nesto#340). Mantiene las columnas del
    /// RDLC: Prov., Prod., Código Barras, Descripción, Tamaño, Subgrupo, Cant., Cajas y
    /// la ubicación (Pas/Fil/Col).
    /// </summary>
    public class GeneradorPdfPicking
    {
        public ByteArrayContent GenerarPdf(int picking, List<PickingDTO> lineas)
        {
            List<PickingDTO> datos = lineas ?? new List<PickingDTO>();

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.MarginVertical(0.8f, Unit.Centimetre);
                    page.MarginHorizontal(0.8f, Unit.Centimetre);
                    // 10pt como el RDLC: este informe se lee de pie en los pasillos del almacén
                    // (a veces con poca luz) — legibilidad antes que densidad (#293).
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComponerCabecera(c, picking));
                    page.Content().Element(c => ComponerTabla(c, datos));
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private void ComponerCabecera(IContainer container, int picking)
        {
            container.PaddingBottom(5).Column(column =>
            {
                column.Item().Text($"Informe de picking agrupado Nº Picking {picking}").Bold().FontSize(14);
                column.Item().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
            });
        }

        private void ComponerTabla(IContainer container, List<PickingDTO> lineas)
        {
            container.Table(table =>
            {
                // Anchos proporcionales a los del RDLC (Descripción 9,2cm / Subgrupo 6,5cm): con
                // Subgrupo estrecho los nombres largos partían cada fila en 2-3 líneas (#293).
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(32);      // Prov.
                    columns.ConstantColumn(44);      // Prod.
                    columns.ConstantColumn(84);      // Código Barras
                    columns.RelativeColumn(2.85f);   // Descripción
                    columns.ConstantColumn(42);      // Tamaño
                    columns.ConstantColumn(30);      // U.M.
                    columns.RelativeColumn(2f);      // Subgrupo
                    columns.ConstantColumn(34);      // Cant.
                    columns.ConstantColumn(36);      // Cajas
                    columns.ConstantColumn(24);      // Pas
                    columns.ConstantColumn(24);      // Fil
                    columns.ConstantColumn(24);      // Col
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Prov.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Prod.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Código Barras", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Descripción", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Tamaño", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "U.M.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Subgrupo", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Cant.", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Cajas", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Pas", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Fil", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Col", alinearDerecha: false);
                });

                foreach (PickingDTO linea in lineas)
                {
                    CeldaDato(table.Cell(), linea.Proveedor?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Producto?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.CodigoBarras?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Descripcion?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Tamanno?.ToString() ?? "", alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.UnidadMedida?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Subgrupo?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Cantidad.ToString(), alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.CantidadCajas.ToString(), alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.Pasillo?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Fila?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Columna?.Trim() ?? "", alinearDerecha: false);
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
            contenido.Text(texto).Bold().FontSize(10);
        }

        private static void CeldaDato(IContainer celda, string texto, bool alinearDerecha)
        {
            // ShowEntire (#302): una fila multi-línea que cae en el corte de página no debe
            // partirse (las celdas ya pintadas se repetían en la continuación y el operario
            // contaba la unidad dos veces). La fila que no cabe pasa ENTERA a la siguiente página.
            IContainer contenido = celda.ShowEntire().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).FontSize(10);
        }

        private void ComponerPie(IContainer container)
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
