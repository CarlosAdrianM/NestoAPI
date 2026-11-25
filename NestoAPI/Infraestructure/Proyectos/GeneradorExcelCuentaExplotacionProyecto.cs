using ClosedXML.Excel;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Proyectos;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NestoAPI.Infraestructure.Proyectos
{
    /// <summary>NestoAPI#901: la cuenta de explotación de un proyecto en Excel (ClosedXML): hojas «Resumen» y «Detalle».</summary>
    public class GeneradorExcelCuentaExplotacionProyecto
    {
        public const string CONTENT_TYPE = GeneradorExcelFacturacionTienda.CONTENT_TYPE;
        public const string HOJA_RESUMEN = "Resumen";
        public const string HOJA_DETALLE = "Detalle";
        private const string FORMATO_IMPORTE = "#,##0.00 €";
        private const string FORMATO_PORCENTAJE = "0.00\" %\"";
        private const string FORMATO_FECHA = "dd/mm/yyyy";

        public static readonly string[] COLUMNAS_DETALLE =
        {
            "Fecha", "Nº orden", "Asiento", "Cuenta", "Nombre de la cuenta", "Gasto o ingreso", "Documento", "Concepto", "Tercero", "Nombre del tercero",
            "Debe", "Haber", "Importe del apunte", "Imputado", "% del apunte", "Efecto", "Comentario", "Imputado por", "Fecha de imputación"
        };

        public ByteArrayContent GenerarExcel(CuentaExplotacionProyectoDTO cuenta)
        {
            var contenido = new ByteArrayContent(GenerarBytes(cuenta));
            contenido.Headers.ContentType = new MediaTypeHeaderValue(CONTENT_TYPE);
            contenido.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = NombreFichero(cuenta) };
            return contenido;
        }

        public static string NombreFichero(CuentaExplotacionProyectoDTO cuenta)
        {
            string codigo = new string((cuenta?.Proyecto?.Codigo ?? "Proyecto").Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
            return $"CuentaExplotacion_{codigo}.xlsx";
        }

        public byte[] GenerarBytes(CuentaExplotacionProyectoDTO cuenta)
        {
            if (cuenta == null)
            {
                throw new ArgumentNullException(nameof(cuenta));
            }
            using (var libro = new XLWorkbook())
            {
                Resumen(libro.Worksheets.Add(HOJA_RESUMEN), cuenta);
                Detalle(libro.Worksheets.Add(HOJA_DETALLE), cuenta);
                using (var ms = new MemoryStream())
                {
                    libro.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        private static void Resumen(IXLWorksheet hoja, CuentaExplotacionProyectoDTO cuenta)
        {
            ProyectoDTO p = cuenta.Proyecto;
            int fila = 1;
            hoja.Cell(fila, 1).Value = $"Cuenta de explotación · {p?.Codigo} · {p?.Nombre}";
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            hoja.Cell(fila, 1).Style.Font.FontSize = 14;
            fila++;
            hoja.Cell(fila, 1).Value = $"{p?.Tipo} · {p?.Estado} · desde el {p?.FechaDesde:dd/MM/yyyy}" +
                (p?.FechaHasta.HasValue == true ? $" hasta el {p.FechaHasta:dd/MM/yyyy}" : " (sin fecha de fin)") +
                (string.IsNullOrEmpty(p?.Producto) ? string.Empty : $" · producto {p.Producto}");
            fila += 2;

            fila = Bloque(hoja, fila, "Ingresos", cuenta.Ingresos, cuenta.TotalIngresos);
            fila++;
            fila = Bloque(hoja, fila, "Gastos", cuenta.Gastos, cuenta.TotalGastos);
            fila++;
            hoja.Cell(fila, 1).Value = "Resultado";
            hoja.Cell(fila, 3).Value = cuenta.Resultado;
            hoja.Cell(fila, 3).Style.NumberFormat.Format = FORMATO_IMPORTE;
            hoja.Range(fila, 1, fila, 3).Style.Font.Bold = true;
            fila++;
            hoja.Cell(fila, 1).Value = "Margen sobre ingresos";
            if (cuenta.Margen.HasValue)
            {
                hoja.Cell(fila, 3).Value = cuenta.Margen.Value;
                hoja.Cell(fila, 3).Style.NumberFormat.Format = FORMATO_PORCENTAJE;
            }
            else
            {
                hoja.Cell(fila, 3).Value = "sin ingresos";
            }
            fila += 2;

            if (cuenta.Presupuesto != null)
            {
                hoja.Cell(fila, 1).Value = "Presupuesto frente a real";
                hoja.Cell(fila, 1).Style.Font.Bold = true;
                fila++;
                hoja.Cell(fila, 2).Value = "Presupuesto";
                hoja.Cell(fila, 3).Value = "Real";
                hoja.Cell(fila, 4).Value = "Desviación";
                hoja.Range(fila, 1, fila, 4).Style.Font.Bold = true;
                fila++;
                fila = FilaPresupuesto(hoja, fila, "Ingresos", cuenta.Presupuesto.PresupuestoIngresos, cuenta.TotalIngresos, cuenta.Presupuesto.DesviacionIngresos);
                fila = FilaPresupuesto(hoja, fila, "Gastos", cuenta.Presupuesto.PresupuestoGastos, cuenta.TotalGastos, cuenta.Presupuesto.DesviacionGastos);
                FilaPresupuesto(hoja, fila, "Resultado", cuenta.Presupuesto.PresupuestoResultado, cuenta.Resultado, cuenta.Presupuesto.DesviacionResultado);
            }
            hoja.Columns(1, 4).AdjustToContents();
        }

        private static int FilaPresupuesto(IXLWorksheet hoja, int fila, string que, decimal? presupuesto, decimal real, decimal? desviacion)
        {
            hoja.Cell(fila, 1).Value = que;
            if (presupuesto.HasValue)
            {
                hoja.Cell(fila, 2).Value = presupuesto.Value;
            }
            hoja.Cell(fila, 3).Value = real;
            if (desviacion.HasValue)
            {
                hoja.Cell(fila, 4).Value = desviacion.Value;
            }
            hoja.Range(fila, 2, fila, 4).Style.NumberFormat.Format = FORMATO_IMPORTE;
            return fila + 1;
        }

        private static int Bloque(IXLWorksheet hoja, int fila, string titulo, System.Collections.Generic.List<LineaCuentaExplotacionDTO> lineas, decimal total)
        {
            hoja.Cell(fila, 1).Value = titulo;
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            fila++;
            hoja.Cell(fila, 1).Value = "Cuenta";
            hoja.Cell(fila, 2).Value = "Nombre";
            hoja.Cell(fila, 3).Value = "Importe";
            hoja.Cell(fila, 4).Value = "Apuntes";
            hoja.Range(fila, 1, fila, 4).Style.Font.Bold = true;
            hoja.Range(fila, 1, fila, 4).Style.Fill.BackgroundColor = XLColor.LightGray;
            fila++;
            foreach (LineaCuentaExplotacionDTO l in lineas)
            {
                hoja.Cell(fila, 1).SetValue(l.Cuenta ?? string.Empty);
                hoja.Cell(fila, 2).Value = l.NombreCuenta ?? string.Empty;
                hoja.Cell(fila, 3).Value = l.Importe;
                hoja.Cell(fila, 3).Style.NumberFormat.Format = FORMATO_IMPORTE;
                hoja.Cell(fila, 4).Value = l.NumeroApuntes;
                fila++;
            }
            hoja.Cell(fila, 1).Value = "Total " + titulo.ToLowerInvariant();
            hoja.Cell(fila, 3).Value = total;
            hoja.Cell(fila, 3).Style.NumberFormat.Format = FORMATO_IMPORTE;
            hoja.Range(fila, 1, fila, 3).Style.Font.Bold = true;
            return fila + 1;
        }

        private static void Detalle(IXLWorksheet hoja, CuentaExplotacionProyectoDTO cuenta)
        {
            for (int c = 0; c < COLUMNAS_DETALLE.Length; c++)
            {
                hoja.Cell(1, c + 1).Value = COLUMNAS_DETALLE[c];
            }
            hoja.Range(1, 1, 1, COLUMNAS_DETALLE.Length).Style.Font.Bold = true;
            hoja.Range(1, 1, 1, COLUMNAS_DETALLE.Length).Style.Fill.BackgroundColor = XLColor.LightGray;
            int fila = 2;
            foreach (DetalleCuentaExplotacionDTO d in cuenta.Detalle)
            {
                hoja.Cell(fila, 1).Value = d.Fecha;
                hoja.Cell(fila, 1).Style.NumberFormat.Format = FORMATO_FECHA;
                hoja.Cell(fila, 2).Value = d.NumeroOrdenApunte;
                if (d.Asiento.HasValue)
                {
                    hoja.Cell(fila, 3).Value = d.Asiento.Value;
                }
                hoja.Cell(fila, 4).SetValue(d.Cuenta ?? string.Empty);
                hoja.Cell(fila, 5).Value = d.NombreCuenta ?? string.Empty;
                hoja.Cell(fila, 6).Value = d.Naturaleza ?? string.Empty;
                hoja.Cell(fila, 7).SetValue(d.Documento ?? string.Empty);
                hoja.Cell(fila, 8).Value = d.Concepto ?? string.Empty;
                hoja.Cell(fila, 9).SetValue(d.Tercero ?? string.Empty);
                hoja.Cell(fila, 10).Value = d.NombreTercero ?? string.Empty;
                hoja.Cell(fila, 11).Value = d.Debe;
                hoja.Cell(fila, 12).Value = d.Haber;
                hoja.Cell(fila, 13).Value = d.ImporteApunte;
                hoja.Cell(fila, 14).Value = d.Importe;
                hoja.Cell(fila, 15).Value = d.Porcentaje;
                hoja.Cell(fila, 15).Style.NumberFormat.Format = FORMATO_PORCENTAJE;
                hoja.Cell(fila, 16).Value = d.Efecto;
                hoja.Cell(fila, 17).Value = d.Comentario ?? string.Empty;
                hoja.Cell(fila, 18).Value = d.Usuario ?? string.Empty;
                hoja.Cell(fila, 19).Value = d.FechaImputacion;
                hoja.Cell(fila, 19).Style.NumberFormat.Format = FORMATO_FECHA;
                hoja.Range(fila, 11, fila, 14).Style.NumberFormat.Format = FORMATO_IMPORTE;
                hoja.Cell(fila, 16).Style.NumberFormat.Format = FORMATO_IMPORTE;
                fila++;
            }
            hoja.SheetView.FreezeRows(1);
            hoja.Columns(1, COLUMNAS_DETALLE.Length).AdjustToContents();
        }
    }
}
