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
    /// <summary>Las líneas de una ubicación (pasillo, fila y columna): una hoja del informe.</summary>
    public class HojaUbicacionInventario
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public List<UbicacionesInventarioDTO> Lineas { get; set; } = new List<UbicacionesInventarioDTO>();

        /// <summary>El rótulo de la hoja, como el del RDLC: «Pasillo 12. Fila 3. Columna 04».</summary>
        public string Titulo => "Pasillo " + Pasillo + ". Fila " + Fila + ". Columna " + Columna;
    }

    /// <summary>
    /// El PDF del informe «Ubicación de Productos en Almacén» (las hojas del inventario de Algete), con QuestPDF. Sustituye al
    /// RDLC UbicacionesInventario.rdlc que Nesto renderizaba en local (Nesto#340) y tiene que ser COMO ÉL (Carlos 10/11/26: la
    /// primera versión en PDF, apaisada y con pasillo/fila/columna en columnas de una tabla corrida, no valía para contar):
    /// <list type="bullet">
    /// <item>A4 vertical y UNA HOJA POR UBICACIÓN (salto de página entre ubicaciones): antes de contar se deja cada hoja en su
    /// ubicación y se cuenta a mano, sin PDA.</item>
    /// <item>Pasillo, fila y columna van en el rótulo de la hoja («Pasillo 12. Fila 3. Columna 04»), no en columnas.</item>
    /// <item>Columnas: Producto, un hueco en blanco para apuntar lo contado, Código Barras, Nombre, Tamaño, U.M. y Familia.</item>
    /// <item>Pie: «Contado por» y «Contabilizado por» con su raya para firmar, página N/M y fecha de impresión.</item>
    /// <item>Orden de las hojas: pasillo, columna, fila (el del recorrido), y dentro, por producto.</item>
    /// </list>
    /// </summary>
    public class GeneradorPdfUbicacionesInventario
    {
        internal const string TITULO = "Ubicación de Productos en Almacén";

        // Una hoja por ubicación son unas 700 páginas (671 ubicaciones el 10/11/26) y QuestPDF corta por defecto a las 250
        // («Composed layout generates infinite document»). Mismo ajuste GLOBAL y mismo tope que GeneradorPdfDetalleRapports.
        static GeneradorPdfUbicacionesInventario()
        {
            if (QuestPDF.Settings.DocumentLayoutExceptionThreshold < GeneradorPdfDetalleRapports.PAGINAS_MAXIMAS)
            {
                QuestPDF.Settings.DocumentLayoutExceptionThreshold = GeneradorPdfDetalleRapports.PAGINAS_MAXIMAS;
            }
        }

        public ByteArrayContent GenerarPdf(List<UbicacionesInventarioDTO> lineas)
        {
            // Una lista vacía es válida (no hay ubicaciones): sale una hoja con la cabecera y sin filas.
            List<HojaUbicacionInventario> hojas = Agrupar(lineas);

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(ComponerCabecera);
                    page.Content().Element(c => ComponerHojas(c, hojas));
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        /// <summary>
        /// Las líneas repartidas en hojas, una por (pasillo, fila, columna), ordenadas como el RDLC: pasillo, columna, fila y,
        /// dentro de la hoja, producto. Los char de la BD llegan con relleno: se comparan sin él.
        /// </summary>
        internal static List<HojaUbicacionInventario> Agrupar(List<UbicacionesInventarioDTO> lineas)
        {
            return (lineas ?? new List<UbicacionesInventarioDTO>())
                .Where(l => l != null)
                .GroupBy(l => new { Pasillo = Limpio(l.Pasillo), Fila = Limpio(l.Fila), Columna = Limpio(l.Columna) })
                .OrderBy(g => g.Key.Pasillo, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Columna, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Fila, StringComparer.Ordinal)
                .Select(g => new HojaUbicacionInventario
                {
                    Pasillo = g.Key.Pasillo,
                    Fila = g.Key.Fila,
                    Columna = g.Key.Columna,
                    Lineas = g.OrderBy(l => Limpio(l.Producto), StringComparer.Ordinal).ToList()
                })
                .ToList();
        }

        private static string Limpio(string valor) => (valor ?? string.Empty).Trim();

        private void ComponerCabecera(IContainer container)
        {
            container.PaddingBottom(4).AlignCenter().Text(TITULO).Bold().FontSize(13);
        }

        private void ComponerHojas(IContainer container, List<HojaUbicacionInventario> hojas)
        {
            container.Column(column =>
            {
                if (hojas.Count == 0)
                {
                    column.Item().Element(c => ComponerTabla(c, null));
                    return;
                }
                for (int i = 0; i < hojas.Count; i++)
                {
                    HojaUbicacionInventario hoja = hojas[i];
                    if (i > 0)
                    {
                        column.Item().PageBreak();
                    }
                    column.Item().Element(c => ComponerTabla(c, hoja));
                }
            });
        }

        private void ComponerTabla(IContainer container, HojaUbicacionInventario hoja)
        {
            container.Table(table =>
            {
                // Los anchos del RDLC (cm): 1,60 / 1,89 / 3,06 / 8,94 / 1,39 / 1,04 / 2,08 = 20 cm
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(1.80f, Unit.Centimetre);   // Producto (1,60 en el RDLC: el rótulo no cabía)
                    columns.ConstantColumn(1.69f, Unit.Centimetre);   // hueco para apuntar lo contado
                    columns.ConstantColumn(3.06f, Unit.Centimetre);   // Código Barras
                    columns.RelativeColumn();                         // Nombre
                    columns.ConstantColumn(1.39f, Unit.Centimetre);   // Tamaño
                    columns.ConstantColumn(1.04f, Unit.Centimetre);   // U.M.
                    columns.ConstantColumn(2.08f, Unit.Centimetre);   // Familia
                });

                // En la cabecera de la tabla para que se repita si una ubicación no cabe en una hoja
                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Producto");
                    CeldaCabecera(header.Cell(), "");
                    CeldaCabecera(header.Cell(), "Código Barras");
                    CeldaCabecera(header.Cell(), "Nombre");
                    CeldaCabecera(header.Cell(), "Tamaño");
                    CeldaCabecera(header.Cell(), "U.M.");
                    CeldaCabecera(header.Cell(), "Familia");
                    if (hoja != null)
                    {
                        header.Cell().ColumnSpan(7).Border(1).BorderColor(Colors.Grey.Lighten1).MinHeight(0.6f, Unit.Centimetre)
                            .AlignCenter().AlignMiddle().Text(hoja.Titulo).Bold().FontSize(12);
                    }
                });

                if (hoja == null)
                {
                    return;
                }
                foreach (UbicacionesInventarioDTO linea in hoja.Lineas)
                {
                    CeldaDato(table.Cell(), Limpio(linea.Producto));
                    CeldaDato(table.Cell(), "");
                    CeldaDato(table.Cell(), Limpio(linea.CodigoBarras));
                    CeldaDato(table.Cell(), Limpio(linea.Nombre));
                    CeldaDato(table.Cell(), linea.Tamanno?.ToString() ?? "");
                    CeldaDato(table.Cell(), Limpio(linea.UnidadMedida));
                    CeldaDato(table.Cell(), Limpio(linea.Familia));
                }
            });
        }

        private static void CeldaCabecera(IContainer celda, string texto)
        {
            celda.Border(1).BorderColor(Colors.Grey.Lighten1).MinHeight(0.6f, Unit.Centimetre).PaddingHorizontal(2).AlignMiddle()
                .Text(texto).Bold().FontSize(9);
        }

        private static void CeldaDato(IContainer celda, string texto)
        {
            celda.Border(1).BorderColor(Colors.Grey.Lighten1).MinHeight(0.6f, Unit.Centimetre).PaddingHorizontal(2).AlignMiddle()
                .Text(texto);
        }

        private void ComponerPie(IContainer container)
        {
            container.PaddingTop(6).Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("Contado por: ____________________________________");
                    row.RelativeItem().AlignRight().Text("Contabilizado por: ____________________________________");
                });
                column.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.CurrentPageNumber();
                        text.Span("/");
                        text.TotalPages();
                    });
                    row.RelativeItem().AlignRight().Text(DateTime.Now.ToString("dd/MM/yyyy H:mm"));
                });
            });
        }
    }
}
