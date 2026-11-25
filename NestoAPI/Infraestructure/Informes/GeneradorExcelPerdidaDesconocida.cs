using ClosedXML.Excel;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// NestoAPI#893: el Excel (.xlsx, ClosedXML, el mismo estilo que <see cref="GeneradorExcelAltasBajasTienda"/>) de la pérdida desconocida.
    /// Hojas «Resumen» (almacén × mes y acumulado, más «En tránsito»), «Productos» (acumulado del año), «Diarios sin clasificar» y «Notas».
    /// Valor a COSTE (precio medio de hoy): es para Dirección.
    /// </summary>
    public class GeneradorExcelPerdidaDesconocida
    {
        public const string CONTENT_TYPE = GeneradorExcelFacturacionTienda.CONTENT_TYPE;
        public const string HOJA_RESUMEN = "Resumen";
        public const string HOJA_PRODUCTOS = "Productos";
        public const string HOJA_DIARIOS = "Diarios sin clasificar";
        public const string HOJA_NOTAS = "Notas";
        public const string EN_TRANSITO = "En tránsito";
        private const string FORMATO_IMPORTE = "#,##0.00 €";

        public static readonly string[] COLUMNAS_RESUMEN =
        {
            "Almacén", "Mes", "Falta (uds.)", "Falta (€)", "Cuadra con otro almacén (uds.)", "Cuadra con otro almacén (€)",
            "Pérdida de verdad (uds.)", "Pérdida de verdad (€)", "Sobra (uds.)", "Sobra (€)", "Sobra sin cuadrar (uds.)",
            "Sobra sin cuadrar (€)", "Neto (uds.)", "Neto (€)"
        };

        public static readonly string[] COLUMNAS_PRODUCTOS =
        {
            "Almacén", "Producto", "Nombre", "Familia", "Precio medio", "Altas", "Bajas", "Neto", "Neto (€)", "Cuadradas", "Cuadra con",
            "Sin cuadrar", "Sin cuadrar (€)", "Aparece y desaparece"
        };

        public static readonly string[] COLUMNAS_DIARIOS = { "Almacén", "Diario", "Altas", "Bajas", "Neto", "Neto (€)", "Productos" };

        public ByteArrayContent GenerarExcel(PerdidaDesconocidaDTO informe)
        {
            ByteArrayContent contenido = new ByteArrayContent(GenerarBytes(informe));
            contenido.Headers.ContentType = new MediaTypeHeaderValue(CONTENT_TYPE);
            contenido.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = NombreFichero(informe) };
            return contenido;
        }

        public static string NombreFichero(PerdidaDesconocidaDTO informe) => $"PerdidaDesconocida_{informe.Desde:yyyyMMdd}_{informe.Hasta:yyyyMMdd}.xlsx";

        public byte[] GenerarBytes(PerdidaDesconocidaDTO informe)
        {
            if (informe == null)
            {
                throw new ArgumentNullException(nameof(informe));
            }
            using (XLWorkbook libro = new XLWorkbook())
            {
                HojaResumen(libro.Worksheets.Add(HOJA_RESUMEN), informe);
                HojaProductos(libro.Worksheets.Add(HOJA_PRODUCTOS), informe);
                HojaDiarios(libro.Worksheets.Add(HOJA_DIARIOS), informe);
                HojaNotas(libro.Worksheets.Add(HOJA_NOTAS), informe);
                using (MemoryStream ms = new MemoryStream())
                {
                    libro.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        public static string NombreMes(int mes) => mes == 0
            ? "Acumulado"
            : CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(mes);

        private static string Titulo(PerdidaDesconocidaDTO informe, string que)
            => $"{que} del {informe.Desde:dd/MM/yyyy} al {informe.Hasta:dd/MM/yyyy} (a coste, precio medio de hoy)";

        private static int Cabecera(IXLWorksheet ws, string titulo, string[] columnas)
        {
            ws.Cell(1, 1).Value = titulo;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 13;
            for (int i = 0; i < columnas.Length; i++)
            {
                ws.Cell(3, i + 1).Value = columnas[i];
            }
            IXLRange cabecera = ws.Range(3, 1, 3, columnas.Length);
            cabecera.Style.Font.Bold = true;
            cabecera.Style.Fill.BackgroundColor = XLColor.LightGray;
            cabecera.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            return 4;
        }

        private static void HojaResumen(IXLWorksheet ws, PerdidaDesconocidaDTO informe)
        {
            int fila = Cabecera(ws, Titulo(informe, "Pérdida desconocida"), COLUMNAS_RESUMEN);
            void Linea(string almacen, PeriodoPerdidaDesconocidaDTO p, bool negrita)
            {
                if (p == null)
                {
                    return;
                }
                ws.Cell(fila, 1).Value = almacen;
                ws.Cell(fila, 2).Value = NombreMes(p.Mes);
                ws.Cell(fila, 3).Value = p.FaltaUnidades;
                Importe(ws.Cell(fila, 4), p.FaltaValor);
                ws.Cell(fila, 5).Value = p.FaltaCuadradaUnidades;
                Importe(ws.Cell(fila, 6), p.FaltaCuadradaValor);
                ws.Cell(fila, 7).Value = p.PerdidaRealUnidades;
                Importe(ws.Cell(fila, 8), p.PerdidaRealValor);
                ws.Cell(fila, 9).Value = p.SobraUnidades;
                Importe(ws.Cell(fila, 10), p.SobraValor);
                ws.Cell(fila, 11).Value = p.SobraRealUnidades;
                Importe(ws.Cell(fila, 12), p.SobraRealValor);
                ws.Cell(fila, 13).Value = p.NetoUnidades;
                Importe(ws.Cell(fila, 14), p.NetoValor);
                if (negrita)
                {
                    IXLRange r = ws.Range(fila, 1, fila, COLUMNAS_RESUMEN.Length);
                    r.Style.Font.Bold = true;
                    r.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                }
                fila++;
            }
            foreach (AlmacenPerdidaDesconocidaDTO a in informe.Almacenes ?? new List<AlmacenPerdidaDesconocidaDTO>())
            {
                foreach (PeriodoPerdidaDesconocidaDTO m in a.Meses ?? new List<PeriodoPerdidaDesconocidaDTO>())
                {
                    Linea(a.Almacen, m, false);
                }
                Linea(a.Almacen, a.Acumulado, true);
                fila++;
            }
            foreach (PeriodoPerdidaDesconocidaDTO m in informe.EnTransito ?? new List<PeriodoPerdidaDesconocidaDTO>())
            {
                Linea(EN_TRANSITO, m, false);
            }
            Linea(EN_TRANSITO, informe.EnTransitoAcumulado, true);
            fila++;
            Linea("TOTAL", informe.Total, true);
            Ajustar(ws, COLUMNAS_RESUMEN.Length);
        }

        private static void HojaProductos(IXLWorksheet ws, PerdidaDesconocidaDTO informe)
        {
            int fila = Cabecera(ws, Titulo(informe, "Productos (acumulado)"), COLUMNAS_PRODUCTOS);
            foreach (ProductoPerdidaDesconocidaDTO p in informe.Productos ?? new List<ProductoPerdidaDesconocidaDTO>())
            {
                ws.Cell(fila, 1).Value = p.Almacen;
                ws.Cell(fila, 2).Value = p.Producto;
                ws.Cell(fila, 3).Value = p.Nombre ?? string.Empty;
                ws.Cell(fila, 4).Value = p.Familia ?? string.Empty;
                Importe(ws.Cell(fila, 5), p.PrecioMedio);
                ws.Cell(fila, 6).Value = p.UnidadesAlta;
                ws.Cell(fila, 7).Value = p.UnidadesBaja;
                ws.Cell(fila, 8).Value = p.UnidadesNeto;
                Importe(ws.Cell(fila, 9), p.ValorNeto);
                ws.Cell(fila, 10).Value = p.UnidadesCuadradas;
                ws.Cell(fila, 11).Value = string.Join(", ", p.CuadraCon ?? new List<string>());
                ws.Cell(fila, 12).Value = p.UnidadesSinCuadrar;
                Importe(ws.Cell(fila, 13), p.ValorSinCuadrar);
                ws.Cell(fila, 14).Value = p.ApareceYDesaparece ? "Sí" : string.Empty;
                fila++;
            }
            Ajustar(ws, COLUMNAS_PRODUCTOS.Length);
        }

        private static void HojaDiarios(IXLWorksheet ws, PerdidaDesconocidaDTO informe)
        {
            int fila = Cabecera(ws, Titulo(informe, "Diarios de producto a mano que no cuentan (sin clasificar)"), COLUMNAS_DIARIOS);
            foreach (DiarioSinClasificarDTO d in informe.DiariosSinClasificar ?? new List<DiarioSinClasificarDTO>())
            {
                ws.Cell(fila, 1).Value = d.Almacen;
                ws.Cell(fila, 2).Value = d.Diario;
                ws.Cell(fila, 3).Value = d.UnidadesAlta;
                ws.Cell(fila, 4).Value = d.UnidadesBaja;
                ws.Cell(fila, 5).Value = d.UnidadesNeto;
                Importe(ws.Cell(fila, 6), d.ValorNeto);
                ws.Cell(fila, 7).Value = d.NumeroProductos;
                fila++;
            }
            Ajustar(ws, COLUMNAS_DIARIOS.Length);
        }

        private static void HojaNotas(IXLWorksheet ws, PerdidaDesconocidaDTO informe)
        {
            ws.Cell(1, 1).Value = "Cómo se calcula";
            ws.Cell(1, 1).Style.Font.Bold = true;
            int fila = 3;
            foreach (string nota in informe.Notas ?? new List<string>())
            {
                ws.Cell(fila++, 1).Value = nota;
            }
            ws.Column(1).Width = 120;
        }

        private static void Importe(IXLCell celda, decimal importe)
        {
            celda.Value = importe;
            celda.Style.NumberFormat.Format = FORMATO_IMPORTE;
        }

        private static void Ajustar(IXLWorksheet ws, int columnas)
        {
            ws.Columns(1, columnas).AdjustToContents(3, 1000);
            foreach (IXLColumn columna in ws.Columns(1, columnas).Where(c => c.Width > 45))
            {
                columna.Width = 45;
            }
            ws.SheetView.FreezeRows(3);
        }
    }
}
