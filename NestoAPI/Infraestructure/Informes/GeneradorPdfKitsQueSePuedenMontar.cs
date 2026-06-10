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
    /// Genera el PDF del informe "Kits que se pueden montar o desmontar" con QuestPDF, sustituyendo
    /// el RDLC KitsQueSePuedenMontar.rdlc que Nesto renderizaba en local (roadmap: mover el render
    /// de informes al backend y eliminar RDLC). Mantiene las columnas del RDLC (Código Barras, Kit,
    /// Nombre, Cantidad, Tipo) y el formato del Tipo: "Montar" en verde si es "m", "Desmontar" en rojo.
    /// </summary>
    public class GeneradorPdfKitsQueSePuedenMontar
    {
        public ByteArrayContent GenerarPdf(List<KitsQueSePuedenMontarDTO> kits)
        {
            // Una lista vacía es válida (no hay kits que montar/desmontar): se genera el informe con
            // la cabecera y la tabla sin filas, igual que haría el RDLC.
            List<KitsQueSePuedenMontarDTO> datos = kits ?? new List<KitsQueSePuedenMontarDTO>();

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
                column.Item().Text("Kits que se pueden montar o desmontar").Bold().FontSize(14);
                column.Item().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
            });
        }

        private void ComponerTabla(IContainer container, List<KitsQueSePuedenMontarDTO> kits)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(90);   // Código Barras
                    columns.ConstantColumn(60);   // Kit
                    columns.RelativeColumn(3);    // Nombre
                    columns.ConstantColumn(55);   // Cantidad
                    columns.ConstantColumn(65);   // Tipo
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Código Barras", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Kit", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Nombre", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Cantidad", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Tipo", alinearDerecha: false);
                });

                foreach (KitsQueSePuedenMontarDTO kit in kits)
                {
                    CeldaDato(table.Cell(), kit.CodigoBarras?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), kit.Kit?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), kit.Nombre?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), kit.CantidadAMontar.ToString(), alinearDerecha: true);
                    CeldaTipo(table.Cell(), kit.Tipo);
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

        // El RDLC mostraba "Montar" en verde si Tipo = "m", y "Desmontar" en rojo en caso contrario.
        private static void CeldaTipo(IContainer celda, string tipo)
        {
            bool esMontar = string.Equals(tipo?.Trim(), "m", StringComparison.OrdinalIgnoreCase);
            string texto = esMontar ? "Montar" : "Desmontar";
            string color = esMontar ? Colors.Green.Medium : Colors.Red.Medium;

            celda.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).AlignMiddle()
                .Text(texto).FontColor(color).FontSize(8);
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
