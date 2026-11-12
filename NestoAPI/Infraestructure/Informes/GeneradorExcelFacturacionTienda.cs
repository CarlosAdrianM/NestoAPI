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

        /// <summary>NestoAPI#719: el Excel del «Acumulado» (hoja «Acumulado») o del «Comparativo» (hojas «Acumulado» y «Comparativo»).</summary>
        public ByteArrayContent GenerarExcel(FacturacionTiendaAnualDTO informe)
        {
            ByteArrayContent contenido = new ByteArrayContent(GenerarBytes(informe));
            contenido.Headers.ContentType = new MediaTypeHeaderValue(CONTENT_TYPE);
            contenido.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = NombreFichero(informe)
            };
            return contenido;
        }

        public static string NombreFichero(FacturacionTiendaAnualDTO informe)
            => $"FacturacionTienda_{informe.Tienda}_{informe.Tipo}_{informe.Hasta:yyyyMMdd}.xlsx";

        public const string HOJA_ACUMULADO = "Acumulado";
        public const string HOJA_COMPARATIVO = "Comparativo";
        private const string FORMATO_PORCENTAJE = "0.00 %";

        public byte[] GenerarBytes(FacturacionTiendaAnualDTO informe)
        {
            if (informe == null)
            {
                throw new ArgumentNullException(nameof(informe));
            }

            using (XLWorkbook libro = new XLWorkbook())
            {
                HojaAcumulado(libro.Worksheets.Add(HOJA_ACUMULADO), informe);
                if (informe.AniosAnteriores != null && informe.AniosAnteriores.Count > 0)
                {
                    HojaComparativo(libro.Worksheets.Add(HOJA_COMPARATIVO), informe);
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    libro.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>NestoAPI#719: columnas de la hoja «Acumulado».</summary>
        public static readonly string[] COLUMNAS_ACUMULADO = { "Mes", "Facturas", "Base imponible", "De ello, cursos", "Acumulado" };

        /// <summary>NestoAPI#719: columnas de la hoja «Comparativo» («Mes», la base de cada año y la variación respecto a cada anterior).</summary>
        public static string[] ColumnasComparativo(FacturacionTiendaAnualDTO informe)
        {
            var columnas = new List<string> { "Mes", $"Base {informe.Anio}" };
            foreach (int anio in informe.AniosAnteriores)
            {
                columnas.Add($"Base {anio}");
            }
            foreach (int anio in informe.AniosAnteriores)
            {
                columnas.Add($"Var. € vs {anio}");
                columnas.Add($"Var. % vs {anio}");
            }
            return columnas.ToArray();
        }

        private static string TituloAnual(FacturacionTiendaAnualDTO informe, string que)
            => $"{que} de la tienda {informe.Tienda} del {informe.Desde:dd/MM/yyyy} al {informe.Hasta:dd/MM/yyyy} " +
               "(base imponible, cursos incluidos)";

        private static int CabeceraAnual(IXLWorksheet ws, string titulo, string[] columnas)
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

        private static string NombreMes(MesFacturacionTiendaDTO mes, DateTime hasta)
        {
            bool parcial = mes.Mes != 0 && mes.Hasta == hasta && mes.Hasta.AddDays(1).Month == mes.Hasta.Month;
            return parcial ? $"{mes.NombreMes} (hasta el {mes.Hasta.Day})" : mes.NombreMes;
        }

        private static void HojaAcumulado(IXLWorksheet ws, FacturacionTiendaAnualDTO informe)
        {
            int fila = CabeceraAnual(ws, TituloAnual(informe, "Facturado mes a mes"), COLUMNAS_ACUMULADO);
            var filas = new List<MesFacturacionTiendaDTO>(informe.Meses ?? new List<MesFacturacionTiendaDTO>());
            if (informe.Total != null)
            {
                filas.Add(informe.Total);
            }
            foreach (MesFacturacionTiendaDTO mes in filas)
            {
                ws.Cell(fila, 1).Value = NombreMes(mes, informe.Hasta);
                ws.Cell(fila, 2).Value = mes.NumeroFacturas;
                Importe(ws.Cell(fila, 3), mes.BaseImponible);
                Importe(ws.Cell(fila, 4), mes.BaseImponibleCursos);
                Importe(ws.Cell(fila, 5), mes.Acumulado);
                if (mes.Mes == 0)
                {
                    PieAnual(ws, fila, COLUMNAS_ACUMULADO.Length);
                }
                fila++;
            }
            Ajustar(ws, COLUMNAS_ACUMULADO.Length);
        }

        private static void HojaComparativo(IXLWorksheet ws, FacturacionTiendaAnualDTO informe)
        {
            string[] columnas = ColumnasComparativo(informe);
            int fila = CabeceraAnual(ws, TituloAnual(informe, "Comparativo con el mismo periodo de años anteriores"), columnas);
            int anteriores = informe.AniosAnteriores.Count;
            var filas = new List<MesFacturacionTiendaDTO>(informe.Meses ?? new List<MesFacturacionTiendaDTO>());
            if (informe.Total != null)
            {
                filas.Add(informe.Total);
            }
            foreach (MesFacturacionTiendaDTO mes in filas)
            {
                ws.Cell(fila, 1).Value = NombreMes(mes, informe.Hasta);
                Importe(ws.Cell(fila, 2), mes.BaseImponible);
                for (int i = 0; i < anteriores && i < mes.Anteriores.Count; i++)
                {
                    ComparativoMesFacturacionTiendaDTO anterior = mes.Anteriores[i];
                    Importe(ws.Cell(fila, 3 + i), anterior.BaseImponible);
                    Importe(ws.Cell(fila, 3 + anteriores + 2 * i), anterior.VariacionImporte);
                    if (anterior.VariacionPorcentaje.HasValue)
                    {
                        IXLCell celda = ws.Cell(fila, 4 + anteriores + 2 * i);
                        celda.Value = anterior.VariacionPorcentaje.Value / 100;
                        celda.Style.NumberFormat.Format = FORMATO_PORCENTAJE;
                    }
                }
                if (mes.Mes == 0)
                {
                    PieAnual(ws, fila, columnas.Length);
                }
                fila++;
            }
            Ajustar(ws, columnas.Length);
        }

        private static void PieAnual(IXLWorksheet ws, int fila, int columnas)
        {
            IXLRange pie = ws.Range(fila, 1, fila, columnas);
            pie.Style.Font.Bold = true;
            pie.Style.Border.TopBorder = XLBorderStyleValues.Thin;
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
            // Sugerencia 648: «Curso» (Sí en la serie de cursos) y «Forma venta» en columnas propias
            // Sugerencia 648: contacto, nombre y dirección son los de entrega del pedido (el primero si la factura junta varios)
            string[] columnas = { "Fecha", "Factura", "Pedido", "Serie", "Curso", "Cliente", "Contacto", "Nombre", "Dirección de entrega",
                "C.P.", "Población", "Comercial", "Forma venta", "Base imponible", "Total" };
            int fila = Cabecera(ws, informe, columnas);
            foreach (FacturaTiendaDTO f in informe.Facturas ?? new List<FacturaTiendaDTO>())
            {
                ws.Cell(fila, 1).Value = f.Fecha;
                ws.Cell(fila, 1).Style.NumberFormat.Format = FORMATO_FECHA;
                ws.Cell(fila, 2).Value = f.Factura;
                if (f.Pedido.HasValue)
                {
                    ws.Cell(fila, 3).Value = f.Pedido.Value;
                }
                ws.Cell(fila, 4).Value = f.Serie;
                ws.Cell(fila, 5).Value = f.EsCurso ? "Sí" : string.Empty;
                ws.Cell(fila, 6).Value = f.Cliente;
                ws.Cell(fila, 7).Value = f.Contacto;
                ws.Cell(fila, 8).Value = f.Nombre;
                ws.Cell(fila, 9).Value = f.Direccion;
                ws.Cell(fila, 10).Value = f.CodigoPostal;
                ws.Cell(fila, 11).Value = f.Poblacion;
                ws.Cell(fila, 12).Value = Comercial(f.Vendedor, f.NombreVendedor);
                ws.Cell(fila, 13).Value = f.FormaVenta ?? string.Empty;
                Importe(ws.Cell(fila, 14), f.BaseImponible);
                Importe(ws.Cell(fila, 15), f.Total);
                fila++;
            }
            Pie(ws, fila, 13, $"Total ({informe.NumeroFacturas} facturas):", informe.TotalBaseImponible, informe.Total);
            if (informe.TotalBaseImponibleCursos != 0)
            {
                ws.Cell(fila + 1, 13).Value = "De ello, cursos:";
                ws.Cell(fila + 1, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                Importe(ws.Cell(fila + 1, 14), informe.TotalBaseImponibleCursos);
            }
            Ajustar(ws, columnas.Length);
        }

        private static void HojaClientes(IXLWorksheet ws, FacturacionTiendaDTO informe)
        {
            string[] columnas = { "Cliente", "Contacto", "Nombre", "Dirección de entrega", "C.P.", "Población", "Comercial",
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
