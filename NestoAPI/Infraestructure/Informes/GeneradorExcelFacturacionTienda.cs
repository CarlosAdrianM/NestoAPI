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
    /// NestoAPI#658: el Excel (.xlsx, ClosedXML) de lo facturado en tienda. Dos hojas: «Facturas» (factura a factura) y «Por cliente»
    /// (agrupado), con los totales al pie. Mismo contenido que el JSON de GET api/Informes/FacturacionTienda.
    /// </summary>
    public class GeneradorExcelFacturacionTienda
    {
        public const string CONTENT_TYPE = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private const string FORMATO_IMPORTE = "#,##0.00 €";
        private const string FORMATO_FECHA = "dd/mm/yyyy";

        public ByteArrayContent GenerarExcel(FacturacionTiendaDTO informe)
        {
            ByteArrayContent contenido = new ByteArrayContent(GenerarBytes(informe));
            contenido.Headers.ContentType = new MediaTypeHeaderValue(CONTENT_TYPE);
            contenido.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = NombreFichero(informe)
            };
            return contenido;
        }

        public static string NombreFichero(FacturacionTiendaDTO informe)
            => $"FacturacionTienda_{informe.Tienda}_{informe.Desde:yyyyMMdd}_{informe.Hasta:yyyyMMdd}.xlsx";

        public byte[] GenerarBytes(FacturacionTiendaDTO informe)
        {
            if (informe == null)
            {
                throw new ArgumentNullException(nameof(informe));
            }

            using (XLWorkbook libro = new XLWorkbook())
            {
                HojaFacturas(libro.Worksheets.Add("Facturas"), informe);
                HojaClientes(libro.Worksheets.Add("Por cliente"), informe);
                using (MemoryStream ms = new MemoryStream())
                {
                    libro.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        private static string Titulo(FacturacionTiendaDTO informe)
            => $"Facturado en la tienda {informe.Tienda} del {informe.Desde:dd/MM/yyyy} al {informe.Hasta:dd/MM/yyyy} (cursos incluidos)";

        private static int Cabecera(IXLWorksheet ws, FacturacionTiendaDTO informe, string[] columnas)
        {
            ws.Cell(1, 1).Value = Titulo(informe);
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

        private static void HojaFacturas(IXLWorksheet ws, FacturacionTiendaDTO informe)
        {
            string[] columnas = { "Fecha", "Factura", "Serie", "Cliente", "Contacto", "Nombre", "Dirección", "C.P.", "Población",
                "Comercial", "Base imponible", "Total" };
            int fila = Cabecera(ws, informe, columnas);
            foreach (FacturaTiendaDTO f in informe.Facturas ?? new List<FacturaTiendaDTO>())
            {
                ws.Cell(fila, 1).Value = f.Fecha;
                ws.Cell(fila, 1).Style.NumberFormat.Format = FORMATO_FECHA;
                ws.Cell(fila, 2).Value = f.Factura;
                ws.Cell(fila, 3).Value = f.EsCurso ? $"{f.Serie} (curso)" : f.Serie;
                ws.Cell(fila, 4).Value = f.Cliente;
                ws.Cell(fila, 5).Value = f.Contacto;
                ws.Cell(fila, 6).Value = f.Nombre;
                ws.Cell(fila, 7).Value = f.Direccion;
                ws.Cell(fila, 8).Value = f.CodigoPostal;
                ws.Cell(fila, 9).Value = f.Poblacion;
                ws.Cell(fila, 10).Value = Comercial(f.Vendedor, f.NombreVendedor);
                Importe(ws.Cell(fila, 11), f.BaseImponible);
                Importe(ws.Cell(fila, 12), f.Total);
                fila++;
            }
            Pie(ws, fila, 10, $"Total ({informe.NumeroFacturas} facturas):", informe.TotalBaseImponible, informe.Total);
            if (informe.TotalBaseImponibleCursos != 0)
            {
                ws.Cell(fila + 1, 10).Value = "De ello, cursos:";
                ws.Cell(fila + 1, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                Importe(ws.Cell(fila + 1, 11), informe.TotalBaseImponibleCursos);
            }
            Ajustar(ws, columnas.Length);
        }

        private static void HojaClientes(IXLWorksheet ws, FacturacionTiendaDTO informe)
        {
            string[] columnas = { "Cliente", "Contacto", "Nombre", "Dirección", "C.P.", "Población", "Comercial",
                "Facturas", "Base imponible", "Total" };
            int fila = Cabecera(ws, informe, columnas);
            foreach (ClienteFacturacionTiendaDTO c in informe.Clientes ?? new List<ClienteFacturacionTiendaDTO>())
            {
                ws.Cell(fila, 1).Value = c.Cliente;
                ws.Cell(fila, 2).Value = c.Contacto;
                ws.Cell(fila, 3).Value = c.Nombre;
                ws.Cell(fila, 4).Value = c.Direccion;
                ws.Cell(fila, 5).Value = c.CodigoPostal;
                ws.Cell(fila, 6).Value = c.Poblacion;
                ws.Cell(fila, 7).Value = Comercial(c.Vendedor, c.NombreVendedor);
                ws.Cell(fila, 8).Value = c.NumeroFacturas;
                Importe(ws.Cell(fila, 9), c.BaseImponible);
                Importe(ws.Cell(fila, 10), c.Total);
                fila++;
            }
            Pie(ws, fila, 8, $"Total ({informe.NumeroClientes} clientes):", informe.TotalBaseImponible, informe.Total);
            Ajustar(ws, columnas.Length);
        }

        // Etiqueta en la columna anterior a la base; base y total a su derecha
        private static void Pie(IXLWorksheet ws, int fila, int columnaEtiqueta, string etiqueta, decimal baseImponible, decimal total)
        {
            ws.Cell(fila, columnaEtiqueta).Value = etiqueta;
            ws.Cell(fila, columnaEtiqueta).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            Importe(ws.Cell(fila, columnaEtiqueta + 1), baseImponible);
            Importe(ws.Cell(fila, columnaEtiqueta + 2), total);
            IXLRange pie = ws.Range(fila, 1, fila, columnaEtiqueta + 2);
            pie.Style.Font.Bold = true;
            pie.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        }

        private static void Importe(IXLCell celda, decimal importe)
        {
            celda.Value = importe;
            celda.Style.NumberFormat.Format = FORMATO_IMPORTE;
        }

        private static string Comercial(string vendedor, string nombre)
        {
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                return string.Empty;
            }
            return string.IsNullOrWhiteSpace(nombre) ? vendedor : $"{vendedor} - {nombre}";
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
