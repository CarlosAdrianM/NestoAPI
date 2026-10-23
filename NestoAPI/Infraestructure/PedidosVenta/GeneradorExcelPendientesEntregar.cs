using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Nesto#552: el Excel (.xlsx, ClosedXML) de las líneas pendientes de entregar. Mismas filas que el JSON de
    /// GET api/PedidosVenta/PendientesEntregar, con el total de bruto al pie.
    /// </summary>
    public class GeneradorExcelPendientesEntregar
    {
        public const string CONTENT_TYPE = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        internal static readonly string[] COLUMNAS = { "Pedido", "Cliente", "Contacto", "Nombre", "Vendedor", "Almacén",
            "Fecha entrega", "Producto", "Descripción", "Cantidad", "Bruto", "Usuario" };

        public ByteArrayContent GenerarExcel(List<LineaPendienteEntregarDTO> lineas, DateTime hoy)
        {
            ByteArrayContent contenido = new ByteArrayContent(GenerarBytes(lineas));
            contenido.Headers.ContentType = new MediaTypeHeaderValue(CONTENT_TYPE);
            contenido.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = $"PendientesEntregar_{hoy:yyyyMMdd}.xlsx"
            };
            return contenido;
        }

        public byte[] GenerarBytes(List<LineaPendienteEntregarDTO> lineas)
        {
            lineas = lineas ?? new List<LineaPendienteEntregarDTO>();
            using (XLWorkbook libro = new XLWorkbook())
            {
                IXLWorksheet ws = libro.Worksheets.Add("Pendientes de entregar");
                for (int i = 0; i < COLUMNAS.Length; i++)
                {
                    ws.Cell(1, i + 1).Value = COLUMNAS[i];
                }
                IXLRange cabecera = ws.Range(1, 1, 1, COLUMNAS.Length);
                cabecera.Style.Font.Bold = true;
                cabecera.Style.Fill.BackgroundColor = XLColor.LightGray;

                int fila = 2;
                decimal totalBruto = 0;
                foreach (LineaPendienteEntregarDTO l in lineas)
                {
                    ws.Cell(fila, 1).Value = l.Pedido;
                    ws.Cell(fila, 2).Value = l.Cliente;
                    ws.Cell(fila, 3).Value = l.Contacto;
                    ws.Cell(fila, 4).Value = l.NombreCliente;
                    ws.Cell(fila, 5).Value = l.Vendedor;
                    ws.Cell(fila, 6).Value = l.Almacen;
                    ws.Cell(fila, 7).Value = l.FechaEntrega;
                    ws.Cell(fila, 7).Style.NumberFormat.Format = "dd/mm/yyyy";
                    ws.Cell(fila, 8).Value = l.Producto;
                    ws.Cell(fila, 9).Value = l.Texto;
                    ws.Cell(fila, 10).Value = l.Cantidad;
                    ws.Cell(fila, 11).Value = l.Bruto;
                    ws.Cell(fila, 11).Style.NumberFormat.Format = "#,##0.00 €";
                    ws.Cell(fila, 12).Value = l.Usuario;
                    totalBruto += l.Bruto;
                    fila++;
                }
                ws.Cell(fila, 10).Value = $"Total ({lineas.Count} líneas):";
                ws.Cell(fila, 11).Value = totalBruto;
                ws.Cell(fila, 11).Style.NumberFormat.Format = "#,##0.00 €";
                ws.Range(fila, 10, fila, 11).Style.Font.Bold = true;
                ws.SheetView.FreezeRows(1);
                ws.Columns(1, COLUMNAS.Length).AdjustToContents();
                using (MemoryStream ms = new MemoryStream())
                {
                    libro.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }
    }
}
