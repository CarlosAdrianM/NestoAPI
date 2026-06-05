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
    /// Genera el PDF del informe "Resumen de ventas" con QuestPDF, sustituyendo el RDLC
    /// ResumenVentas.rdlc que Nesto renderizaba en local. No se toca el SP: se recolocan los datos
    /// que devuelve en una vista comparativa Año Actual vs. Año Anterior (ver
    /// <see cref="ResumenVentasComparativaDTO"/>). Mantiene la agrupación por Grupo con subtotales
    /// y total general del informe original.
    /// </summary>
    public class GeneradorPdfResumenVentas
    {
        /// <summary>
        /// Issue #221: el SP prdInformeResumenVentas despacha a sub-procedimientos distintos según la
        /// fecha. A partir de esta fecha (igual umbral que el SP) las columnas significan Año Actual /
        /// Año Anterior (layout comparativo); antes, cada columna es una empresa (layout antiguo).
        /// </summary>
        public static readonly DateTime FechaCorteLayoutComparativo = new DateTime(2022, 1, 1);

        /// <summary>¿Se usa el layout comparativo (Año Actual vs. Año Anterior)? Si no, el de por empresa.</summary>
        public static bool UsarLayoutComparativo(DateTime fechaDesde) => fechaDesde >= FechaCorteLayoutComparativo;

        public ByteArrayContent GenerarPdf(List<ResumenVentasDTO> datos, DateTime fechaDesde, DateTime fechaHasta, bool soloFacturas)
        {
            bool layoutComparativo = UsarLayoutComparativo(fechaDesde);
            List<ResumenVentasDTO> datosSeguro = datos ?? new List<ResumenVentasDTO>();
            List<ResumenVentasComparativaDTO> filasComparativa = layoutComparativo ? Transformar(datosSeguro) : null;

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(0.8f, Unit.Centimetre);
                    page.MarginHorizontal(0.8f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(c => ComponerCabecera(c, fechaDesde, fechaHasta, soloFacturas));
                    page.Content().Element(c =>
                    {
                        if (layoutComparativo)
                        {
                            ComponerTabla(c, filasComparativa, soloFacturas);
                        }
                        else
                        {
                            ComponerTablaPorEmpresa(c, datosSeguro);
                        }
                    });
                    page.Footer().Element(ComponerPie);
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return new ByteArrayContent(pdfBytes);
        }

        /// <summary>
        /// Transforma las filas crudas del SP en la vista comparativa. Función pura (testeable).
        /// </summary>
        public static List<ResumenVentasComparativaDTO> Transformar(List<ResumenVentasDTO> datos)
        {
            if (datos == null)
            {
                return new List<ResumenVentasComparativaDTO>();
            }

            return datos.Select(Comparar).ToList();
        }

        public static ResumenVentasComparativaDTO Comparar(ResumenVentasDTO origen)
        {
            // Visnú (VtaVC) y Unión Láser (VtaUL) hoy son 0; si no lo fueran, se suman al Año Actual
            // y no se muestran como columnas propias.
            decimal annoActual = origen.VtaNV + origen.VtaVC + origen.VtaUL;
            decimal annoAnterior = origen.VtaCV;

            return new ResumenVentasComparativaDTO
            {
                Grupo = origen.Grupo,
                Vendedor = origen.Vendedor,
                NombreVendedor = origen.NombreVendedor,
                AnnoActual = annoActual,
                AnnoAnterior = annoAnterior,
                DiferenciaEuros = origen.VtaTotal,
                DiferenciaPorcentaje = CalcularDiferenciaPorcentaje(annoActual, annoAnterior)
            };
        }

        /// <summary>
        /// Diferencia (%) = AñoActual / AñoAnterior - 1. Cuando no hay base de comparación
        /// (Año Anterior = 0): +100% si hay ventas este año, -100% si son negativas, 0% si no hay nada.
        /// </summary>
        public static decimal CalcularDiferenciaPorcentaje(decimal annoActual, decimal annoAnterior)
        {
            if (annoAnterior == 0)
            {
                if (annoActual > 0) return 1m;
                if (annoActual < 0) return -1m;
                return 0m;
            }
            return annoActual / annoAnterior - 1m;
        }

        private void ComponerCabecera(IContainer container, DateTime fechaDesde, DateTime fechaHasta, bool soloFacturas)
        {
            container.PaddingBottom(5).Column(column =>
            {
                column.Item().Text("Resumen de ventas").Bold().FontSize(14);
                column.Item().Text($"Desde {fechaDesde:dd/MM/yyyy} hasta {fechaHasta:dd/MM/yyyy}").FontSize(8);
                column.Item().Text(soloFacturas ? "Solo facturas" : "Facturas y albaranes").FontSize(8);
            });
        }

        private void ComponerTabla(IContainer container, List<ResumenVentasComparativaDTO> filas, bool soloFacturas)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);    // Vendedor
                    columns.ConstantColumn(95);   // Año Actual
                    columns.ConstantColumn(95);   // Año Anterior
                    columns.ConstantColumn(95);   // Diferencia (€)
                    columns.ConstantColumn(70);   // Diferencia (%)
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Vendedor", derecha: false);
                    CeldaCabecera(header.Cell(), "Año Actual", derecha: true);
                    CeldaCabecera(header.Cell(), "Año Anterior", derecha: true);
                    CeldaCabecera(header.Cell(), "Diferencia (€)", derecha: true);
                    CeldaCabecera(header.Cell(), "Diferencia (%)", derecha: true);
                });

                // Agrupar por Grupo en orden DESCENDENTE, igual que el RDLC (así FAC queda arriba y
                // ALB abajo), con subtotal por grupo.
                foreach (var grupo in filas.GroupBy(f => f.Grupo).OrderByDescending(g => g.Key))
                {
                    table.Cell().ColumnSpan(5u).PaddingTop(6).PaddingBottom(2)
                        .Text(NombreGrupo(grupo.Key)).Bold().FontSize(12);

                    foreach (ResumenVentasComparativaDTO fila in grupo)
                    {
                        // Solo en informes de "solo facturas" (periodos ya completos): en rojo las
                        // líneas que venden menos este año que el anterior.
                        string color = DebeMarcarseEnRojo(soloFacturas, fila.AnnoActual, fila.AnnoAnterior)
                            ? Colors.Red.Medium
                            : null;

                        CeldaTexto(table.Cell(), fila.NombreVendedor?.Trim() ?? "", derecha: false, color);
                        CeldaTexto(table.Cell(), fila.AnnoActual.ToString("N2"), derecha: true, color);
                        CeldaTexto(table.Cell(), fila.AnnoAnterior.ToString("N2"), derecha: true, color);
                        CeldaTexto(table.Cell(), fila.DiferenciaEuros.ToString("N2"), derecha: true, color);
                        CeldaTexto(table.Cell(), FormatearPorcentaje(fila.DiferenciaPorcentaje), derecha: true, color);
                    }

                    // El subtotal NO va en negrita (como en el RDLC): así el TOTAL general destaca.
                    ComponerFilaTotales(table, $"Subtotal {NombreGrupo(grupo.Key)}", grupo.ToList(), Colors.Grey.Lighten1, negrita: false);
                }

                if (filas.Any())
                {
                    ComponerFilaTotales(table, "TOTAL", filas, Colors.Grey.Darken1, negrita: true);
                }
            });
        }

        /// <summary>
        /// Issue #221: layout antiguo "una columna por empresa" para informes con fechaDesde &lt; 2022,
        /// igual que el RDLC original ResumenVentas.rdlc (Nueva Visión, Cursos Visión, Visnú Cosméticos,
        /// Unión Láser, Total). Las columnas del SP aquí son empresas, no años.
        /// </summary>
        private void ComponerTablaPorEmpresa(IContainer container, List<ResumenVentasDTO> filas)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);    // Vendedor
                    columns.ConstantColumn(90);   // Nueva Visión (VtaNV)
                    columns.ConstantColumn(90);   // Cursos Visión (VtaCV)
                    columns.ConstantColumn(90);   // Visnú Cosméticos (VtaVC)
                    columns.ConstantColumn(90);   // Unión Láser (VtaUL)
                    columns.ConstantColumn(90);   // Total (VtaTotal)
                });

                table.Header(header =>
                {
                    CeldaCabecera(header.Cell(), "Vendedor", derecha: false);
                    CeldaCabecera(header.Cell(), "Nueva Visión", derecha: true);
                    CeldaCabecera(header.Cell(), "Cursos Visión", derecha: true);
                    CeldaCabecera(header.Cell(), "Visnú Cosméticos", derecha: true);
                    CeldaCabecera(header.Cell(), "Unión Láser", derecha: true);
                    CeldaCabecera(header.Cell(), "Total", derecha: true);
                });

                foreach (var grupo in filas.GroupBy(f => f.Grupo).OrderByDescending(g => g.Key))
                {
                    table.Cell().ColumnSpan(6u).PaddingTop(6).PaddingBottom(2)
                        .Text(NombreGrupo(grupo.Key)).Bold().FontSize(12);

                    foreach (ResumenVentasDTO fila in grupo)
                    {
                        CeldaTexto(table.Cell(), fila.NombreVendedor?.Trim() ?? "", derecha: false, null);
                        CeldaTexto(table.Cell(), fila.VtaNV.ToString("N2"), derecha: true, null);
                        CeldaTexto(table.Cell(), fila.VtaCV.ToString("N2"), derecha: true, null);
                        CeldaTexto(table.Cell(), fila.VtaVC.ToString("N2"), derecha: true, null);
                        CeldaTexto(table.Cell(), fila.VtaUL.ToString("N2"), derecha: true, null);
                        CeldaTexto(table.Cell(), fila.VtaTotal.ToString("N2"), derecha: true, null);
                    }

                    ComponerFilaTotalesPorEmpresa(table, $"Subtotal {NombreGrupo(grupo.Key)}", grupo.ToList(), Colors.Grey.Lighten1, negrita: false);
                }

                if (filas.Any())
                {
                    ComponerFilaTotalesPorEmpresa(table, "TOTAL", filas, Colors.Grey.Darken1, negrita: true);
                }
            });
        }

        private static void ComponerFilaTotalesPorEmpresa(TableDescriptor table, string etiqueta, List<ResumenVentasDTO> filas, string colorBorde, bool negrita)
        {
            CeldaTotal(table.Cell(), etiqueta, derecha: false, colorBorde, negrita);
            CeldaTotal(table.Cell(), filas.Sum(f => f.VtaNV).ToString("N2"), derecha: true, colorBorde, negrita);
            CeldaTotal(table.Cell(), filas.Sum(f => f.VtaCV).ToString("N2"), derecha: true, colorBorde, negrita);
            CeldaTotal(table.Cell(), filas.Sum(f => f.VtaVC).ToString("N2"), derecha: true, colorBorde, negrita);
            CeldaTotal(table.Cell(), filas.Sum(f => f.VtaUL).ToString("N2"), derecha: true, colorBorde, negrita);
            CeldaTotal(table.Cell(), filas.Sum(f => f.VtaTotal).ToString("N2"), derecha: true, colorBorde, negrita);
        }

        // Fila de subtotal/total: el % se recalcula a partir de los importes sumados, no se suman los %.
        private static void ComponerFilaTotales(TableDescriptor table, string etiqueta, List<ResumenVentasComparativaDTO> filas, string colorBorde, bool negrita)
        {
            decimal sumActual = filas.Sum(f => f.AnnoActual);
            decimal sumAnterior = filas.Sum(f => f.AnnoAnterior);
            decimal sumEuros = filas.Sum(f => f.DiferenciaEuros);
            decimal porcentaje = CalcularDiferenciaPorcentaje(sumActual, sumAnterior);

            CeldaTotal(table.Cell(), etiqueta, derecha: false, colorBorde, negrita);
            CeldaTotal(table.Cell(), sumActual.ToString("N2"), derecha: true, colorBorde, negrita);
            CeldaTotal(table.Cell(), sumAnterior.ToString("N2"), derecha: true, colorBorde, negrita);
            CeldaTotal(table.Cell(), sumEuros.ToString("N2"), derecha: true, colorBorde, negrita);
            CeldaTotal(table.Cell(), FormatearPorcentaje(porcentaje), derecha: true, colorBorde, negrita);
        }

        private static string FormatearPorcentaje(decimal ratio)
        {
            // El especificador "%" multiplica por 100; el formato con secciones añade el signo.
            return ratio.ToString("+0.0%;-0.0%;0.0%");
        }

        private static void CeldaCabecera(IContainer celda, string texto, bool derecha)
        {
            IContainer contenido = celda.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3).AlignMiddle();
            if (derecha)
            {
                contenido = contenido.AlignRight();
            }
            contenido.Text(texto).Bold().FontSize(10);
        }

        private static void CeldaTexto(IContainer celda, string texto, bool derecha, string color)
        {
            IContainer contenido = celda.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).AlignMiddle();
            if (derecha)
            {
                contenido = contenido.AlignRight();
            }
            var span = contenido.Text(texto).FontSize(10);
            if (color != null)
            {
                span.FontColor(color);
            }
        }

        /// <summary>Etiqueta legible del grupo: FAC -> Facturas, ALB -> Albaranes; el resto, tal cual.</summary>
        public static string NombreGrupo(string grupo)
        {
            string g = (grupo ?? "").Trim();
            if (string.Equals(g, "FAC", StringComparison.OrdinalIgnoreCase)) return "Facturas";
            if (string.Equals(g, "ALB", StringComparison.OrdinalIgnoreCase)) return "Albaranes";
            return g;
        }

        /// <summary>
        /// Una línea se marca en rojo solo en informes de "solo facturas" (periodos ya completos)
        /// cuando vende menos este año que el anterior.
        /// </summary>
        public static bool DebeMarcarseEnRojo(bool soloFacturas, decimal annoActual, decimal annoAnterior)
        {
            return soloFacturas && annoActual < annoAnterior;
        }

        private static void CeldaTotal(IContainer celda, string texto, bool derecha, string colorBorde, bool negrita)
        {
            IContainer contenido = celda.BorderTop(1).BorderColor(colorBorde).Padding(2).AlignMiddle();
            if (derecha)
            {
                contenido = contenido.AlignRight();
            }
            var span = contenido.Text(texto).FontSize(10);
            if (negrita)
            {
                span.Bold();
            }
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
