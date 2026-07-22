using NestoAPI.Models.Informes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Genera el PDF del "Manifiesto de agencia" con QuestPDF, sustituyendo el RDLC
    /// ManifiestoAgencia.rdlc que Nesto renderizaba en local (roadmap: mover el render de
    /// informes al backend y eliminar RDLC). Mismas columnas que el RDLC (Nombre, Dirección,
    /// C.P., Población, Provincia, Bultos, Reembolso, Fijo, Móvil, Observaciones) y mismo
    /// total de bultos; añade el total de reembolsos (útil para conciliar con la agencia).
    /// </summary>
    public class GeneradorPdfManifiestoAgencia
    {
        public ByteArrayContent GenerarPdf(List<ManifiestoAgenciaDTO> envios, string nombreAgencia, DateTime fecha)
        {
            // Una lista vacía es válida (día sin envíos tramitados): cabecera y tabla sin filas.
            List<ManifiestoAgenciaDTO> datos = envios ?? new List<ManifiestoAgenciaDTO>();

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.MarginVertical(0.8f, Unit.Centimetre);
                    page.MarginHorizontal(0.8f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header().Element(c => ComponerCabecera(c, nombreAgencia, fecha));
                    page.Content().Element(c => ComponerTabla(c, datos));
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private void ComponerCabecera(IContainer container, string nombreAgencia, DateTime fecha)
        {
            container.PaddingBottom(5).Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text($"Manifiesto de agencia {nombreAgencia?.Trim()}").Bold().FontSize(14);
                    column.Item().Text($"Fecha: {fecha:dd/MM/yyyy}").FontSize(9);
                });
                row.ConstantItem(150).AlignRight().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
            });
        }

        private void ComponerTabla(IContainer container, List<ManifiestoAgenciaDTO> envios)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);    // Nombre
                    columns.RelativeColumn(3);    // Dirección
                    columns.ConstantColumn(40);   // C.P.
                    columns.RelativeColumn(2);    // Población
                    columns.RelativeColumn(2);    // Provincia
                    columns.ConstantColumn(35);   // Bultos
                    columns.ConstantColumn(55);   // Reembolso
                    columns.ConstantColumn(60);   // Fijo
                    columns.ConstantColumn(60);   // Móvil
                    columns.RelativeColumn(2);    // Observaciones
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Nombre", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Dirección", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "C.P.", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Población", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Provincia", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Bultos", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Reembolso", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Fijo", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Móvil", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Observaciones", alinearDerecha: false);
                });

                foreach (ManifiestoAgenciaDTO envio in envios)
                {
                    CeldaDato(table.Cell(), envio.Nombre?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), envio.Direccion?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), envio.CodigoPostal?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), envio.Poblacion?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), envio.Provincia?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), envio.Bultos.ToString(), alinearDerecha: true);
                    CeldaDato(table.Cell(), envio.Reembolso == 0 ? "" : envio.Reembolso.ToString("N2"), alinearDerecha: true);
                    CeldaDato(table.Cell(), envio.TelefonoFijo?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), envio.TelefonoMovil?.Trim() ?? "", alinearDerecha: false);
                    CeldaDato(table.Cell(), envio.Observaciones?.Trim() ?? "", alinearDerecha: false);
                }

                // Total de bultos (como el Sum del RDLC) + total de reembolsos
                table.Footer(footer =>
                {
                    CeldaTotal(footer.Cell().ColumnSpan(5), $"Total envíos: {envios.Count}", alinearDerecha: false);
                    CeldaTotal(footer.Cell(), envios.Sum(e => (int)e.Bultos).ToString(), alinearDerecha: true);
                    CeldaTotal(footer.Cell(), envios.Sum(e => e.Reembolso).ToString("N2"), alinearDerecha: true);
                    _ = footer.Cell().ColumnSpan(3);
                });
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

        private static void CeldaTotal(IContainer celda, string texto, bool alinearDerecha)
        {
            IContainer contenido = celda.BorderTop(1).BorderColor(Colors.Grey.Lighten1).Padding(3).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).Bold().FontSize(8);
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
