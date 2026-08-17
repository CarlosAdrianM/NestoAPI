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
    /// NestoAPI#350: PDF de balances y cuentas de resultados (BPY, PGP...), sustituyendo al
    /// informe del Nesto viejo. Misma información, presentación nueva EN VERTICAL (petición de
    /// Carlos 17/08/26, y es el formato de los modelos oficiales del PGC Pymes): el ACTIVO en
    /// su página y el PATRIMONIO NETO Y PASIVO en la siguiente, cada uno a ancho completo; las
    /// cuentas de resultados (sin líneas 'P'), una sola sección. Sin descripciones truncadas ni
    /// importes solapados con la columna %, y los porcentajes sin base (año anterior 0) van en
    /// blanco.
    /// </summary>
    public class GeneradorPdfBalance
    {
        public ByteArrayContent GenerarPdf(BalanceInformeDTO balance)
        {
            List<LineaBalanceInformeDTO> lineas = balance.Lineas ?? new List<LineaBalanceInformeDTO>();
            List<LineaBalanceInformeDTO> lineasActivo = lineas.Where(l => l.Tipo != "P").ToList();
            List<LineaBalanceInformeDTO> lineasPasivo = lineas.Where(l => l.Tipo == "P").ToList();
            bool esBalance = lineasPasivo.Any();

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(1.2f, Unit.Centimetre);
                    page.MarginHorizontal(1.2f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Element(c => ComponerCabecera(c, balance));
                    page.Content().Column(contenido =>
                    {
                        contenido.Item().Element(c => ComponerPanel(c, esBalance ? "ACTIVO" : null, lineasActivo));
                        if (esBalance)
                        {
                            // El PN y pasivo empieza en página nueva, con todo el ancho para él
                            contenido.Item().PageBreak();
                            contenido.Item().Element(c => ComponerPanel(c, "PATRIMONIO NETO Y PASIVO", lineasPasivo));
                        }
                    });
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Darken1));
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                });
            });

            return new ByteArrayContent(documento.GeneratePdf());
        }

        private static void ComponerCabecera(IContainer container, BalanceInformeDTO balance)
        {
            container.PaddingBottom(10).Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text(balance.NombreEmpresa ?? string.Empty).Bold().FontSize(11);
                    row.RelativeItem().AlignCenter().Column(centro =>
                    {
                        centro.Item().AlignCenter().Text(balance.Descripcion ?? balance.Numero).Bold().FontSize(12);
                        centro.Item().AlignCenter()
                            .Text($"Desde {balance.Desde:dd/MM/yyyy} hasta {balance.Hasta:dd/MM/yyyy}").FontSize(9);
                    });
                    row.RelativeItem().AlignRight().Text($"{DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8);
                });
            });
        }

        private static void ComponerPanel(IContainer container, string titulo, List<LineaBalanceInformeDTO> lineas)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();     // Descripción
                    columns.ConstantColumn(72);   // Ejercicio N
                    columns.ConstantColumn(45);   // %
                    columns.ConstantColumn(72);   // Ejercicio N-1
                });

                table.Header(header =>
                {
                    header.Cell().Element(CeldaCabeceraTabla).Text(titulo ?? string.Empty).Bold();
                    header.Cell().Element(CeldaCabeceraTabla).AlignRight().Text("Ejerc. N").Bold();
                    header.Cell().Element(CeldaCabeceraTabla).AlignRight().Text("%").Bold();
                    header.Cell().Element(CeldaCabeceraTabla).AlignRight().Text("Ejerc. N-1").Bold();
                });

                foreach (LineaBalanceInformeDTO linea in lineas)
                {
                    bool destacada = linea.EsTotal || linea.EsCabecera;
                    Func<IContainer, IContainer> celda = c => Celda(c, linea);

                    IContainer celdaDescripcion = celda(table.Cell());
                    // Sangría para las líneas numeradas ("1. Proveedores") como el modelo oficial
                    if (!destacada && EmpiezaPorNumero(linea.Descripcion))
                    {
                        celdaDescripcion = celdaDescripcion.PaddingLeft(10);
                    }
                    var textoDescripcion = celdaDescripcion.Text(linea.Descripcion ?? string.Empty);
                    if (destacada) { textoDescripcion.Bold(); }

                    var textoActual = celda(table.Cell()).AlignRight().Text(FormatearImporte(linea.SaldoActual, linea));
                    if (linea.EsTotal) { textoActual.Bold(); }
                    var textoPorcentaje = celda(table.Cell()).AlignRight().Text(FormatearPorcentaje(linea.Porcentaje));
                    if (linea.EsTotal) { textoPorcentaje.Bold(); }
                    var textoAnterior = celda(table.Cell()).AlignRight().Text(FormatearImporte(linea.SaldoAnterior, linea));
                    if (linea.EsTotal) { textoAnterior.Bold(); }
                }
            });
        }

        private static IContainer CeldaCabeceraTabla(IContainer container)
        {
            return container.BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingVertical(3).PaddingHorizontal(2);
        }

        private static IContainer Celda(IContainer container, LineaBalanceInformeDTO linea)
        {
            IContainer celda = container.PaddingVertical(1.5f).PaddingHorizontal(2);
            if (linea.EsTotal)
            {
                celda = celda.BorderTop(0.75f).BorderColor(Colors.Grey.Darken1);
            }
            return celda;
        }

        internal static bool EmpiezaPorNumero(string descripcion)
        {
            return !string.IsNullOrEmpty(descripcion) && char.IsDigit(descripcion[0]);
        }

        /// <summary>Importe con separador de miles; las cabeceras y los ceros de líneas de
        /// detalle van en blanco (menos ruido, como los modelos oficiales); los totales
        /// muestran siempre su importe aunque sea 0.</summary>
        internal static string FormatearImporte(decimal? valor, LineaBalanceInformeDTO linea)
        {
            if (!valor.HasValue || (valor.Value == 0 && !linea.EsTotal))
            {
                return string.Empty;
            }
            return valor.Value.ToString("N2") + " €";
        }

        internal static string FormatearPorcentaje(decimal? porcentaje)
        {
            return porcentaje.HasValue ? porcentaje.Value.ToString("N2") : string.Empty;
        }
    }
}
