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
    /// Genera el PDF del informe "Ubicaciones de inventario" con QuestPDF, sustituyendo el RDLC
    /// UbicacionesInventario.rdlc que Nesto renderizaba en local (roadmap: mover el render de
    /// informes al backend y eliminar RDLC). Mantiene las mismas columnas que el RDLC: Pasillo,
    /// Fila, Columna, Producto, Código de barras, Nombre, Tamaño, U.M. y Familia.
    /// </summary>
    public class GeneradorPdfUbicacionesInventario
    {
        public ByteArrayContent GenerarPdf(List<UbicacionesInventarioDTO> lineas)
        {
            // Una lista vacía es válida (no hay ubicaciones): se genera el informe con la
            // cabecera y la tabla sin filas, igual que haría el RDLC.
            List<UbicacionesInventarioDTO> datos = lineas ?? new List<UbicacionesInventarioDTO>();

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.MarginVertical(0.8f, Unit.Centimetre);
                    page.MarginHorizontal(0.8f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header().Element(ComponerCabecera);
                    page.Content().Element(c => ComponerTabla(c, datos));
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private void ComponerCabecera(IContainer container)
        {
            container.PaddingBottom(5).Column(column =>
            {
                column.Item().Text("Ubicaciones de inventario").Bold().FontSize(14);
                column.Item().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
            });
        }

        private void ComponerTabla(IContainer container, List<UbicacionesInventarioDTO> lineas)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(45);   // Pasillo
                    columns.ConstantColumn(35);   // Fila
                    columns.ConstantColumn(45);   // Columna
                    columns.ConstantColumn(55);   // Producto
                    columns.ConstantColumn(90);   // Código de barras
                    columns.RelativeColumn(3);    // Nombre
                    columns.ConstantColumn(45);   // Tamaño
                    columns.ConstantColumn(40);   // U.M.
                    columns.RelativeColumn(1);    // Familia
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Pasillo", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Fila", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Columna", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Producto", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Cód. barras", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Nombre", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Tamaño", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "U.M.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Familia", alinearDerecha: false);
                });

                foreach (UbicacionesInventarioDTO linea in lineas)
                {
                    CeldaDato(table.Cell(), linea.Pasillo?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Fila?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Columna?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Producto?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.CodigoBarras?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Nombre?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Tamanno?.ToString() ?? "", alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.UnidadMedida?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Familia?.Trim() ?? "", alinearDerecha: false);
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
