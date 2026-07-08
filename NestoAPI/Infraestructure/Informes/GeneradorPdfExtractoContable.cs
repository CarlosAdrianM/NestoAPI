using NestoAPI.Models.Informes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Genera el PDF del "EXTRACTO CONTABLE" (libro mayor de una cuenta) con QuestPDF, sustituyendo el
    /// RDLC ExtractoContable.rdlc que Nesto renderiza en local (roadmap Nesto#340, Fase 2 RDLC->QuestPDF).
    /// Conserva el contenido del RDLC (título con cuenta + rango de fechas, tabla Fecha/Documento/Concepto/
    /// Debe/Haber/Saldo) y le da el lenguaje visual del resto de documentos de Nueva Visión (logo + sello
    /// Madrid Excelente, NestoAPI#244). El Saldo viene ya calculado por fila desde el servicio.
    /// </summary>
    public class GeneradorPdfExtractoContable
    {
        // Datos fijos de la empresa emisora (idénticos a los del resto de informes de Nueva Visión).
        private const string EMPRESA_NOMBRE = "NUEVA VISIÓN, S.A.";
        private const string EMPRESA_DIRECCION = "C/ Río Tiétar, 11";
        private const string EMPRESA_POBLACION = "28119 Algete (Madrid)";
        private const string EMPRESA_CIF = "CIF: A/78368255";
        private const string EMPRESA_EMAIL = "administracion@nuevavision.es";

        private const string URL_LOGO = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";

        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-ES");
        private static readonly byte[] _selloMadridExcelente = RecursosGraficos.SelloMadridExcelente;

        private readonly Func<byte[]> _cargarLogo;

        public GeneradorPdfExtractoContable() : this(CargarLogoDesdeUrl) { }

        /// <summary>Constructor para test: permite inyectar la carga del logo (evita la llamada HTTP).</summary>
        internal GeneradorPdfExtractoContable(Func<byte[]> cargarLogo)
        {
            _cargarLogo = cargarLogo ?? CargarLogoDesdeUrl;
        }

        public ByteArrayContent GenerarPdf(string cuenta, DateTime fechaDesde, DateTime fechaHasta, IList<ExtractoContableDTO> movimientos)
        {
            var lineas = movimientos ?? new List<ExtractoContableDTO>();
            byte[] logoBytes = _cargarLogo();

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(0.8f, Unit.Centimetre);
                    page.MarginHorizontal(0.8f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header().Element(c => ComponerCabecera(c, logoBytes, cuenta, fechaDesde, fechaHasta));
                    page.Content().Element(c => ComponerTabla(c, lineas));
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        private void ComponerCabecera(IContainer container, byte[] logoBytes, string cuenta, DateTime fechaDesde, DateTime fechaHasta)
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

                column.Item().AlignCenter().Text($"EXTRACTO CONTABLE {cuenta?.Trim()}").Bold().FontSize(16);
                column.Item().AlignCenter().Text($"(del {fechaDesde:dd/MM/yyyy} al {fechaHasta:dd/MM/yyyy})").FontSize(9);

                column.Item().PaddingVertical(4);
            });
        }

        private void ComponerTabla(IContainer container, IList<ExtractoContableDTO> movimientos)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(60);   // Fecha
                    columns.ConstantColumn(70);   // Documento
                    columns.RelativeColumn(3);    // Concepto
                    columns.ConstantColumn(65);   // Debe
                    columns.ConstantColumn(65);   // Haber
                    columns.ConstantColumn(70);   // Saldo
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Fecha");
                    CeldaCabecera(header.Cell(), "Documento");
                    CeldaCabecera(header.Cell(), "Concepto");
                    CeldaCabecera(header.Cell(), "Debe", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Haber", alinearDerecha: true);
                    CeldaCabecera(header.Cell(), "Saldo", alinearDerecha: true);
                });

                foreach (var m in movimientos)
                {
                    CeldaDato(table.Cell(), m.Fecha.ToString("dd/MM/yyyy", Cultura));
                    CeldaDato(table.Cell(), m.Documento?.Trim());
                    CeldaDato(table.Cell(), m.Concepto?.Trim());
                    CeldaDato(table.Cell(), FormatoImporte(m.Debe), alinearDerecha: true);
                    CeldaDato(table.Cell(), FormatoImporte(m.Haber), alinearDerecha: true);
                    CeldaDato(table.Cell(), FormatoImporte(m.Saldo), alinearDerecha: true);
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

        private static string FormatoImporte(decimal importe)
            => importe.ToString("N2", Cultura) + " €";

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
