using ClosedXML.Excel;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// NestoAPI#886: el Excel (.xlsx, ClosedXML, mismo estilo que <see cref="GeneradorExcelFacturacionTienda"/>) de las altas y bajas de
    /// stock de una tienda. Hojas «Movimientos», «Aparece y desaparece» y «Resumen». Valor SIEMPRE a PVP: nunca coste.
    /// </summary>
    public class GeneradorExcelAltasBajasTienda
    {
        public const string CONTENT_TYPE = GeneradorExcelFacturacionTienda.CONTENT_TYPE;
        public const string HOJA_MOVIMIENTOS = "Movimientos";
        public const string HOJA_APARECE = "Aparece y desaparece";
        public const string HOJA_RESUMEN = "Resumen";
        private const string FORMATO_IMPORTE = "#,##0.00 €";
        private const string FORMATO_FECHA_HORA = "dd/mm/yyyy hh:mm";

        public static readonly string[] COLUMNAS_MOVIMIENTOS =
            { "Fecha y hora", "Producto", "Nombre", "Familia", "Unidades", "Motivo", "Usuario", "PVP unitario", "Valor a PVP" };
        public static readonly string[] COLUMNAS_APARECE =
            { "Producto", "Nombre", "Familia", "Unidades de alta", "Unidades de baja", "Neto", "Valor neto a PVP" };

        public ByteArrayContent GenerarExcel(AltasBajasTiendaDTO informe)
        {
            ByteArrayContent contenido = new ByteArrayContent(GenerarBytes(informe));
            contenido.Headers.ContentType = new MediaTypeHeaderValue(CONTENT_TYPE);
            contenido.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = NombreFichero(informe)
            };
            return contenido;
        }

        public static string NombreFichero(AltasBajasTiendaDTO informe)
            => $"AltasBajasTienda_{informe.Almacen}_{informe.Desde:yyyyMMdd}_{informe.Hasta:yyyyMMdd}.xlsx";

        public byte[] GenerarBytes(AltasBajasTiendaDTO informe)
        {
            if (informe == null)
            {
                throw new ArgumentNullException(nameof(informe));
            }
            using (XLWorkbook libro = new XLWorkbook())
            {
                HojaMovimientos(libro.Worksheets.Add(HOJA_MOVIMIENTOS), informe);
                HojaAparece(libro.Worksheets.Add(HOJA_APARECE), informe);
                HojaResumen(libro.Worksheets.Add(HOJA_RESUMEN), informe);
                using (MemoryStream ms = new MemoryStream())
                {
                    libro.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        private static string Titulo(AltasBajasTiendaDTO informe, string que)
            => $"{que} de {informe.NombreTienda} ({informe.Almacen}) del {informe.Desde:dd/MM/yyyy} al {informe.Hasta:dd/MM/yyyy} (valor a PVP)";

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

        private static void HojaMovimientos(IXLWorksheet ws, AltasBajasTiendaDTO informe)
        {
            int fila = Cabecera(ws, Titulo(informe, "Altas y bajas de stock"), COLUMNAS_MOVIMIENTOS);
            foreach (MovimientoAltaBajaTiendaDTO m in informe.Movimientos ?? new List<MovimientoAltaBajaTiendaDTO>())
            {
                ws.Cell(fila, 1).Value = m.FechaHora;
                ws.Cell(fila, 1).Style.NumberFormat.Format = FORMATO_FECHA_HORA;
                ws.Cell(fila, 2).Value = m.Producto;
                ws.Cell(fila, 3).Value = m.Nombre ?? string.Empty;
                ws.Cell(fila, 4).Value = m.Familia ?? string.Empty;
                ws.Cell(fila, 5).Value = m.Unidades;
                ws.Cell(fila, 6).Value = m.Motivo ?? string.Empty;
                ws.Cell(fila, 7).Value = m.Usuario ?? string.Empty;
                Importe(ws.Cell(fila, 8), m.PvpUnitario);
                Importe(ws.Cell(fila, 9), m.ValorPvp);
                fila++;
            }
            ws.Cell(fila, 4).Value = "Neto:";
            ws.Cell(fila, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(fila, 5).Value = informe.UnidadesNeto;
            Importe(ws.Cell(fila, 9), informe.ValorPvpNeto);
            IXLRange pie = ws.Range(fila, 1, fila, COLUMNAS_MOVIMIENTOS.Length);
            pie.Style.Font.Bold = true;
            pie.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            Ajustar(ws, COLUMNAS_MOVIMIENTOS.Length);
        }

        private static void HojaAparece(IXLWorksheet ws, AltasBajasTiendaDTO informe)
        {
            int fila = Cabecera(ws, Titulo(informe, "Productos con alta y baja"), COLUMNAS_APARECE);
            foreach (ProductoApareceDesapareceDTO p in informe.ApareceYDesaparece ?? new List<ProductoApareceDesapareceDTO>())
            {
                ws.Cell(fila, 1).Value = p.Producto;
                ws.Cell(fila, 2).Value = p.Nombre ?? string.Empty;
                ws.Cell(fila, 3).Value = p.Familia ?? string.Empty;
                ws.Cell(fila, 4).Value = p.UnidadesAlta;
                ws.Cell(fila, 5).Value = p.UnidadesBaja;
                ws.Cell(fila, 6).Value = p.UnidadesNeto;
                Importe(ws.Cell(fila, 7), p.ValorPvpNeto);
                fila++;
            }
            Ajustar(ws, COLUMNAS_APARECE.Length);
        }

        private static void HojaResumen(IXLWorksheet ws, AltasBajasTiendaDTO informe)
        {
            int fila = Cabecera(ws, Titulo(informe, "Resumen de altas y bajas"), new[] { "", "Unidades", "Valor a PVP" });
            void Linea(string etiqueta, int unidades, decimal valor, bool negrita = false)
            {
                ws.Cell(fila, 1).Value = etiqueta;
                ws.Cell(fila, 2).Value = unidades;
                Importe(ws.Cell(fila, 3), valor);
                if (negrita)
                {
                    IXLRange r = ws.Range(fila, 1, fila, 3);
                    r.Style.Font.Bold = true;
                    r.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                }
                fila++;
            }
            Linea("Altas", informe.UnidadesAlta, informe.ValorPvpAlta);
            Linea("Bajas", -informe.UnidadesBaja, -informe.ValorPvpBaja);
            Linea("Neto", informe.UnidadesNeto, informe.ValorPvpNeto, true);
            fila++;
            ws.Cell(fila, 1).Value = $"Movimientos: {(informe.Movimientos ?? new List<MovimientoAltaBajaTiendaDTO>()).Count}";
            ws.Cell(fila + 1, 1).Value = $"Productos con alta y baja: {(informe.ApareceYDesaparece ?? new List<ProductoApareceDesapareceDTO>()).Count}";
            Ajustar(ws, 3);
        }

        private static void Importe(IXLCell celda, decimal importe)
        {
            celda.Value = importe;
            celda.Style.NumberFormat.Format = FORMATO_IMPORTE;
        }

        private static void Ajustar(IXLWorksheet ws, int columnas)
        {
            ws.Columns(1, columnas).AdjustToContents(3, 1000);
            foreach (IXLColumn columna in ws.Columns(1, columnas))
            {
                if (columna.Width > 45)
                {
                    columna.Width = 45;
                }
            }
            ws.SheetView.FreezeRows(3);
        }
    }
}
