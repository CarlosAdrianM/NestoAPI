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
    /// Genera el PDF del informe "Control de pedidos" con QuestPDF, sustituyendo el RDLC
    /// ControlPedidos.rdlc que Nesto renderizaba en local (roadmap: mover el render de informes
    /// al backend y eliminar RDLC). Mantiene las mismas columnas que el RDLC: Pedido, Producto,
    /// Nombre, Familia, Cantidad Pedido y Cantidad Total.
    /// </summary>
    public class GeneradorPdfControlPedidos
    {
        public ByteArrayContent GenerarPdf(List<ControlPedidosDTO> lineas)
        {
            // Una lista vacía es válida (no hay pedidos pendientes): se genera el informe con la
            // cabecera y la tabla sin filas, igual que haría el RDLC.
            List<ControlPedidosDTO> datos = lineas ?? new List<ControlPedidosDTO>();

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
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
                column.Item().Text("Control de pedidos").Bold().FontSize(14);
                column.Item().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
            });
        }

        private void ComponerTabla(IContainer container, List<ControlPedidosDTO> lineas)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(55);   // Pedido
                    columns.ConstantColumn(60);   // Producto
                    columns.RelativeColumn(3);    // Nombre
                    columns.RelativeColumn(1);    // Familia
                    columns.ConstantColumn(60);   // Cantidad Pedido
                    columns.ConstantColumn(55);   // Cantidad Total
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Pedido", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Producto", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Nombre", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Familia", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Cantidad Pedido", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Cantidad Total", alinearDerecha: true);
                });

                foreach (ControlPedidosDTO linea in lineas)
                {
                    CeldaDato(table.Cell(), linea.Pedido.ToString(), alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.Producto?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Nombre?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.Familia?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), linea.CantidadPedido.ToString(), alinearDerecha: true);
                    CeldaDato(table.Cell(), linea.CantidadTotal.ToString(), alinearDerecha: true);
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
