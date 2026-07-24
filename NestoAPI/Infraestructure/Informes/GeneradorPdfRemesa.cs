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
    /// NestoAPI#353: PDF de la remesa (relación de efectos remitidos al banco), sustituyendo
    /// al informe antiguo de VB6. Diferencias con el antiguo: IBAN completo en vez de CCC
    /// (también en la cuenta de abono), nombre del cliente junto al número, y agrupación por
    /// fecha de cargo con subtotales (remesas multi-vencimiento #345) además del total.
    /// </summary>
    public class GeneradorPdfRemesa
    {
        public ByteArrayContent GenerarPdf(RemesaInformeDTO remesa)
        {
            List<RemesaInformeEfectoDTO> efectos = remesa.Efectos ?? new List<RemesaInformeEfectoDTO>();
            // NestoAPI#358: un grupo por ASIENTO contable (= un abono del banco). En "respetar
            // vencimientos" el banco hace un abono por fecha de cargo solicitada y cada uno es su
            // propio asiento; dos asientos pueden compartir día de valor (#345) y se muestran
            // SEPARADOS. En modo forzado todo va en un único asiento -> un grupo. La fecha que se
            // pinta es e.Fecha (día de valor del apunte, lo que el banco muestra en el extracto).
            List<IGrouping<int, RemesaInformeEfectoDTO>> grupos = AgruparPorAsiento(efectos, remesa.Fecha);

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(1.2f, Unit.Centimetre);
                    page.MarginHorizontal(1.2f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Element(c => ComponerCabecera(c, remesa));
                    page.Content().Element(c => ComponerTabla(c, remesa, grupos));
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        /// <summary>NestoAPI#358: agrupa los efectos por asiento contable (un asiento = un abono
        /// del banco), ordenados por día de valor y luego por asiento (estable para asientos que
        /// comparten fecha). Interno para poder testear la agrupación sin generar el PDF.</summary>
        internal static List<IGrouping<int, RemesaInformeEfectoDTO>> AgruparPorAsiento(
            IEnumerable<RemesaInformeEfectoDTO> efectos, DateTime fechaRemesa)
        {
            return (efectos ?? Enumerable.Empty<RemesaInformeEfectoDTO>())
                .GroupBy(e => e.Asiento)
                .OrderBy(g => FechaDeGrupo(g, fechaRemesa))
                .ThenBy(g => g.Key)
                .ToList();
        }

        /// <summary>Día de valor de un asiento: la Fecha de sus efectos (todas iguales dentro de
        /// un asiento), con fallback a la fecha de la remesa para datos históricos incompletos.</summary>
        private static DateTime FechaDeGrupo(IEnumerable<RemesaInformeEfectoDTO> grupo, DateTime fechaRemesa)
        {
            return (grupo.FirstOrDefault()?.FechaCargo ?? fechaRemesa).Date;
        }

        /// <summary>IBAN en grupos de 4 para legibilidad: ES0621006273900200063554 →
        /// "ES06 2100 6273 9002 0006 3554". Cadena vacía o null → vacía.</summary>
        internal static string FormatearIban(string iban)
        {
            string limpio = iban?.Replace(" ", "").Trim() ?? string.Empty;
            IEnumerable<string> grupos = Enumerable.Range(0, (limpio.Length + 3) / 4)
                .Select(i => limpio.Substring(i * 4, Math.Min(4, limpio.Length - i * 4)));
            return string.Join(" ", grupos);
        }

        private void ComponerCabecera(IContainer container, RemesaInformeDTO remesa)
        {
            container.PaddingBottom(8).Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text(remesa.Empresa?.Trim() ?? "").Bold().FontSize(12);
                    row.ConstantItem(200).AlignRight().Column(derecha =>
                    {
                        derecha.Item().Text($"Remesa nº {remesa.Numero}").Bold().FontSize(12);
                        derecha.Item().Text($"Fecha: {remesa.Fecha:dd/MM/yyyy}").FontSize(9);
                    });
                });
                column.Item().PaddingTop(6).Text($"Relación de efectos remitidos por remesa al banco {remesa.Banco?.Trim()}").FontSize(10);
                column.Item().Text(text =>
                {
                    text.Span("para, salvo buen fin de la operación, abonar en la cuenta ").FontSize(10);
                    text.Span(FormatearIban(remesa.IbanAbono)).Bold().FontSize(10);
                });
            });
        }

        private void ComponerTabla(IContainer container, RemesaInformeDTO remesa,
            List<IGrouping<int, RemesaInformeEfectoDTO>> grupos)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(50);   // Cliente
                    columns.RelativeColumn();     // Nombre
                    columns.ConstantColumn(75);   // Nº Documento
                    columns.ConstantColumn(160);  // IBAN
                    columns.ConstantColumn(65);   // Importe
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Cliente", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Nombre", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Nº Documento", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "IBAN", alinearDerecha: false);
                    CeldaCabecera(header.Cell(), "Importe", alinearDerecha: true);
                });

                foreach (IGrouping<int, RemesaInformeEfectoDTO> grupo in grupos)
                {
                    DateTime fechaGrupo = FechaDeGrupo(grupo, remesa.Fecha);
                    _ = table.Cell().ColumnSpan(5).Background(Colors.Grey.Lighten3).Padding(3)
                        .Text($"Fecha de cargo: {fechaGrupo:dd/MM/yyyy}").Bold().FontSize(9);

                    foreach (RemesaInformeEfectoDTO efecto in grupo)
                    {
                        CeldaDato(table.Cell(), efecto.Cliente?.Trim() ?? "", alinearDerecha: false);
                        CeldaDato(table.Cell(), efecto.Nombre?.Trim() ?? "", alinearDerecha: false);
                        CeldaDato(table.Cell(), efecto.Documento?.Trim() ?? "", alinearDerecha: false);
                        CeldaDato(table.Cell(), FormatearIban(efecto.Iban), alinearDerecha: false);
                        CeldaDato(table.Cell(), efecto.Importe.ToString("N2") + " €", alinearDerecha: true);
                    }

                    // Con un solo asiento el subtotal coincidiría con el total: no aporta nada.
                    if (grupos.Count > 1)
                    {
                        CeldaSubtotal(table.Cell().ColumnSpan(4),
                            $"Subtotal {fechaGrupo:dd/MM/yyyy} ({EnEfectos(grupo.Count())})");
                        CeldaSubtotal(table.Cell(), grupo.Sum(e => e.Importe).ToString("N2") + " €", alinearDerecha: true);
                    }
                }

                table.Footer(footer =>
                {
                    CeldaTotal(footer.Cell().ColumnSpan(4),
                        $"Total remesa nº {remesa.Numero} ({EnEfectos(grupos.Sum(g => g.Count()))})");
                    CeldaTotal(footer.Cell(), grupos.Sum(g => g.Sum(e => e.Importe)).ToString("N2") + " €",
                        alinearDerecha: true);
                });
            });
        }

        private static string EnEfectos(int cantidad) => cantidad == 1 ? "1 efecto" : $"{cantidad} efectos";

        private static void CeldaCabecera(IContainer celda, string texto, bool alinearDerecha)
        {
            IContainer contenido = celda.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).Bold().FontSize(9);
        }

        private static void CeldaDato(IContainer celda, string texto, bool alinearDerecha)
        {
            IContainer contenido = celda.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).FontSize(9);
        }

        private static void CeldaSubtotal(IContainer celda, string texto, bool alinearDerecha = false)
        {
            IContainer contenido = celda.BorderBottom(1).BorderColor(Colors.Grey.Lighten1)
                .Padding(3).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).SemiBold().FontSize(9);
        }

        private static void CeldaTotal(IContainer celda, string texto, bool alinearDerecha = false)
        {
            IContainer contenido = celda.BorderTop(1).BorderColor(Colors.Grey.Lighten1).Padding(4).AlignMiddle();
            if (alinearDerecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).Bold().FontSize(10);
        }

        private void ComponerPie(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8));
                    text.Span("Página ");
                    text.CurrentPageNumber();
                    text.Span(" de ");
                    text.TotalPages();
                });
            });
        }
    }
}
