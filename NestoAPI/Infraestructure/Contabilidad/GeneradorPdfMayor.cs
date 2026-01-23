using NestoAPI.Models;
using NestoAPI.Models.Mayor;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Contabilidad
{
    /// <summary>
    /// Generador de PDF para el Mayor de una cuenta (cliente o proveedor).
    /// Sigue el mismo patron que GeneradorPdfModelo347.
    /// </summary>
    public class GeneradorPdfMayor
    {
        private const string URL_LOGO = "http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg";

        private byte[] _logoBytes;

        public byte[] Generar(MayorCuentaDTO datos, Empresa empresa)
        {
            CargarImagenes();

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.2f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8.5f));

                    page.Header().Element(c => ComponerCabecera(c, datos, empresa));
                    page.Content().Element(c => ComponerContenido(c, datos));
                    page.Footer().Element(ComponerPie);
                });
            });

            return documento.GeneratePdf();
        }

        private void CargarImagenes()
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);

                try
                {
                    _logoBytes = client.GetByteArrayAsync(URL_LOGO).GetAwaiter().GetResult();
                }
                catch
                {
                    _logoBytes = null;
                }
            }
        }

        private void ComponerCabecera(IContainer container, MayorCuentaDTO datos, Empresa empresa)
        {
            container.Column(column =>
            {
                // Logo y titulo en la misma fila
                column.Item().Row(row =>
                {
                    // Logo a la izquierda
                    if (_logoBytes != null && _logoBytes.Length > 0)
                    {
                        row.ConstantItem(80).AlignLeft().Image(_logoBytes);
                    }
                    else
                    {
                        row.ConstantItem(80);
                    }

                    // Titulo centrado
                    row.RelativeItem().Column(col =>
                    {
                        string tipoTitulo = datos.TipoCuenta?.ToLower() == "proveedor"
                            ? "MAYOR DE PROVEEDOR"
                            : "MAYOR DE CLIENTE";

                        // Añadir sufijo solo para "Solo Facturas"
                        if (datos.SoloFacturas)
                        {
                            tipoTitulo += " (SOLO FACTURAS)";
                        }

                        col.Item().AlignCenter().Text(tipoTitulo).Bold().FontSize(14);
                        col.Item().AlignCenter().PaddingTop(3).Text(
                            $"Periodo: {datos.FechaDesde:dd/MM/yyyy} - {datos.FechaHasta:dd/MM/yyyy}").FontSize(10);
                    });

                    // Espacio a la derecha para equilibrar
                    row.ConstantItem(80);
                });

                column.Item().PaddingVertical(4);

                // Linea separadora
                column.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);

                column.Item().PaddingVertical(3);

                // Datos de la cuenta
                column.Item().Element(c => ComponerDatosCuenta(c, datos));

                column.Item().PaddingVertical(4);
            });
        }

        private void ComponerDatosCuenta(IContainer container, MayorCuentaDTO datos)
        {
            container.Border(1).BorderColor(Colors.Grey.Medium).Column(column =>
            {
                // Cabecera del bloque
                var colorFondo = datos.TipoCuenta?.ToLower() == "proveedor"
                    ? Colors.Orange.Lighten4
                    : Colors.Blue.Lighten4;
                var textoTipo = datos.TipoCuenta?.ToLower() == "proveedor"
                    ? "DATOS DEL PROVEEDOR"
                    : "DATOS DEL CLIENTE";

                column.Item().Background(colorFondo).Padding(4).Row(row =>
                {
                    row.RelativeItem().Text(textoTipo).Bold().FontSize(9);
                });

                // Contenido
                column.Item().Padding(6).Row(row =>
                {
                    row.RelativeItem(2).Column(col =>
                    {
                        col.Item().Text(text =>
                        {
                            text.Span("Cuenta: ").SemiBold();
                            text.Span($"{datos.NumeroCuenta?.Trim() ?? ""} - {datos.NombreCuenta?.Trim() ?? ""}");
                        });
                        col.Item().PaddingTop(2).Text(text =>
                        {
                            text.Span("NIF/CIF: ").SemiBold();
                            text.Span(datos.CifNif?.Trim() ?? "");
                        });
                    });

                    row.RelativeItem(3).Column(col =>
                    {
                        col.Item().Text(text =>
                        {
                            text.Span("Direccion: ").SemiBold();
                            text.Span(datos.Direccion?.Trim() ?? "");
                        });
                    });
                });
            });
        }

        private void ComponerContenido(IContainer container, MayorCuentaDTO datos)
        {
            container.Column(column =>
            {
                // Saldo anterior si existe
                if (datos.SaldoAnterior != 0)
                {
                    column.Item().Background(Colors.Grey.Lighten4).Padding(4).Row(row =>
                    {
                        row.RelativeItem().Text(text =>
                        {
                            text.Span("Saldo anterior: ").SemiBold();
                            text.Span(datos.SaldoAnterior.ToString("N2") + " EUR");
                        });
                    });
                    column.Item().PaddingVertical(3);
                }

                // Tabla de movimientos
                column.Item().Element(c => ComponerTablaMovimientos(c, datos));

                column.Item().PaddingVertical(6);

                // Totales
                column.Item().Element(c => ComponerTotales(c, datos));
            });
        }

        private void ComponerTablaMovimientos(IContainer container, MayorCuentaDTO datos)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(60);  // Fecha
                    columns.RelativeColumn(4);   // Concepto
                    columns.ConstantColumn(70);  // Documento
                    columns.ConstantColumn(70);  // Debe
                    columns.ConstantColumn(70);  // Haber
                    columns.ConstantColumn(80);  // Saldo
                });

                // Cabecera (se repite en cada pagina)
                table.Header(header =>
                {
                    header.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten3).Padding(4)
                        .Text("FECHA").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten3).Padding(4)
                        .Text("CONCEPTO").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten3).Padding(4)
                        .Text("DOCUMENTO").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten3).Padding(4)
                        .AlignRight().Text("DEBE").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten3).Padding(4)
                        .AlignRight().Text("HABER").Bold().FontSize(8);
                    header.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten3).Padding(4)
                        .AlignRight().Text("SALDO").Bold().FontSize(8);
                });

                // Filas de movimientos
                bool alternar = false;
                foreach (var mov in datos.Movimientos)
                {
                    var colorFondo = alternar ? Colors.Grey.Lighten5 : Colors.White;
                    alternar = !alternar;

                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(colorFondo).Padding(3)
                        .Text(mov.Fecha.ToString("dd/MM/yyyy")).FontSize(7.5f);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(colorFondo).Padding(3)
                        .Text(TruncateText(mov.Concepto, 50)).FontSize(7.5f);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(colorFondo).Padding(3)
                        .Text(mov.NumeroDocumento ?? "").FontSize(7.5f);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(colorFondo).Padding(3)
                        .AlignRight().Text(mov.Debe != 0 ? mov.Debe.ToString("N2") : "").FontSize(7.5f);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(colorFondo).Padding(3)
                        .AlignRight().Text(mov.Haber != 0 ? mov.Haber.ToString("N2") : "").FontSize(7.5f);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(colorFondo).Padding(3)
                        .AlignRight().Text(mov.Saldo.ToString("N2")).FontSize(7.5f).Bold();
                }

                // Si no hay movimientos
                if (datos.Movimientos == null || datos.Movimientos.Count == 0)
                {
                    table.Cell().ColumnSpan(6).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(10)
                        .AlignCenter().Text("No hay movimientos en el periodo seleccionado").FontSize(9).Italic();
                }
            });
        }

        private void ComponerTotales(IContainer container, MayorCuentaDTO datos)
        {
            container.Border(1).BorderColor(Colors.Grey.Medium).Column(column =>
            {
                // Fila del titulo
                column.Item().Background(Colors.Blue.Lighten5).Padding(6).Text("TOTALES").Bold().FontSize(10);

                // Fila con los totales usando tabla para alineacion consistente
                column.Item().Background(Colors.Blue.Lighten5).PaddingHorizontal(6).PaddingBottom(6).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1);  // Total Debe
                        columns.RelativeColumn(1);  // Total Haber
                        columns.RelativeColumn(1);  // Saldo Final
                    });

                    table.Cell().AlignRight().PaddingRight(15).Text(text =>
                    {
                        text.Span("Total Debe: ").SemiBold();
                        text.Span(datos.TotalDebe.ToString("N2") + " EUR");
                    });

                    table.Cell().AlignRight().PaddingRight(15).Text(text =>
                    {
                        text.Span("Total Haber: ").SemiBold();
                        text.Span(datos.TotalHaber.ToString("N2") + " EUR");
                    });

                    table.Cell().AlignRight().Text(text =>
                    {
                        text.Span("Saldo Final: ").Bold();
                        text.Span(datos.SaldoFinal.ToString("N2") + " EUR").Bold();
                    });
                });
            });
        }

        private void ComponerPie(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                column.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().AlignLeft().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(7).FontColor(Colors.Grey.Darken1));
                        text.Span("Generado el ");
                        text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                    });

                    row.RelativeItem().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                        text.Span("Pagina ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });

                    row.RelativeItem(); // Espacio vacio a la derecha
                });
            });
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3) + "...";
        }
    }
}
