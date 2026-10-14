using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#630: el Excel mensual de propuestas de stock (ClosedXML, como <see cref="PedidosCompra.GeneradorExcelPedidoCompra"/>).
    /// Hojas: «Top 50 por exceso», «Todos los productos», «Nuevos (fase A)» y «Calibración».
    /// </summary>
    public class GeneradorExcelPropuestasStock
    {
        public const string HOJA_TOP = "Top {0} por exceso";
        public const string HOJA_TODOS = "Todos los productos";
        public const string HOJA_NUEVOS = "Nuevos (fase A)";
        public const string HOJA_CALIBRACION = "Calibración";

        private const string FORMATO_EUROS = "#,##0.00 €";
        private const string FORMATO_DECIMAL = "#,##0.0";
        private const string FORMATO_RATIO = "0.00";
        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        public static string NombreHojaTop(int filas) => string.Format(HOJA_TOP, filas);

        public byte[] Generar(ResultadoPropuestasStock resultado)
        {
            if (resultado == null)
            {
                throw new ArgumentNullException(nameof(resultado));
            }
            ParametrosTendencias p = resultado.Parametros ?? new ParametrosTendencias();
            string mes = resultado.Mes.ToString("MMMM yyyy", ES);
            using (XLWorkbook libro = new XLWorkbook())
            {
                List<ResultadoTendencia> ordenados = MotorTendencias.OrdenarPorExceso(resultado.Productos);
                HojaProductos(libro.Worksheets.Add(NombreHojaTop(p.FilasTop)), ordenados.Take(p.FilasTop).ToList(),
                    $"Propuestas de stock — {mes}: los {p.FilasTop} con más euros atrapados (exceso total de todos: {MotorTendencias.Euros(ordenados.Sum(r => r.ExcesoValorado))})", resumida: true);
                HojaProductos(libro.Worksheets.Add(HOJA_TODOS), ordenados,
                    $"Propuestas de stock — {mes}: {ordenados.Count} productos (stock, venta en 24 meses, pendientes o compras abiertas)", resumida: false);
                HojaNuevos(libro.Worksheets.Add(HOJA_NUEVOS), ordenados.Where(r => r.Fase == FasesProducto.NUEVO).ToList(), mes);
                HojaCalibracion(libro.Worksheets.Add(HOJA_CALIBRACION), resultado.Calibracion ?? new ResultadoCalibracion(), p, mes);

                using (MemoryStream ms = new MemoryStream())
                {
                    libro.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        private static void Titulo(IXLWorksheet hoja, string texto)
        {
            hoja.Cell(1, 1).Value = texto;
            hoja.Cell(1, 1).Style.Font.Bold = true;
            hoja.Cell(1, 1).Style.Font.FontSize = 13;
        }

        private static void Cabecera(IXLWorksheet hoja, int fila, IList<string> columnas)
        {
            for (int c = 0; c < columnas.Count; c++)
            {
                IXLCell celda = hoja.Cell(fila, c + 1);
                celda.Value = columnas[c];
                celda.Style.Font.Bold = true;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
                celda.Style.Alignment.WrapText = true;
            }
            hoja.SheetView.FreezeRows(fila);
        }

        private static void Valor(IXLCell celda, object valor, string formato = null)
        {
            switch (valor)
            {
                case null:
                    return;
                case string s:
                    celda.Value = s;
                    break;
                case int i:
                    celda.Value = i;
                    break;
                case short sh:
                    celda.Value = sh;
                    break;
                case decimal d:
                    celda.Value = d;
                    break;
                case bool b:
                    celda.Value = b ? "Sí" : "No";
                    break;
                default:
                    celda.Value = valor.ToString();
                    break;
            }
            if (formato != null)
            {
                celda.Style.NumberFormat.Format = formato;
            }
        }

        private static void HojaProductos(IXLWorksheet hoja, List<ResultadoTendencia> filas, string titulo, bool resumida)
        {
            Titulo(hoja, titulo);
            var columnas = new List<(string nombre, Func<ResultadoTendencia, object> valor, string formato)>
            {
                ("Producto", r => r.Producto, null),
                ("Nombre", r => r.Nombre, null),
                ("Familia", r => r.Familia, null),
                ("Proveedor", r => r.Proveedor, null),
                ("Fase", r => r.Fase, null),
                ("Estado", r => r.EstadoProducto, null),
                ("Stock", r => r.Stock, null),
                ("Precio medio", r => r.PrecioMedio, FORMATO_EUROS),
                ("Venta media 6 m", r => r.VentaMedia6Meses, FORMATO_DECIMAL),
                ("Cobertura (meses)", r => r.CoberturaMeses, FORMATO_DECIMAL),
                ("Cobertura alcanzable (días)", r => r.CoberturaAlcanzableDias, null),
                ("Pendiente %/mes", r => r.PendienteRelativa, FORMATO_DECIMAL),
                ("Tendencia", r => r.Tendencia, null),
                ("Exceso (uds)", r => r.ExcesoUnidades, null),
                ("Exceso €", r => r.ExcesoValorado, FORMATO_EUROS),
                ("Acción", r => r.Accion, null),
                ("Máx actual", r => r.StockMaximoActual, null),
                ("Mín actual", r => r.StockMinimoActual, null),
                ("Máx propuesto", r => r.StockMaximoPropuesto, null),
                ("Mín propuesto", r => r.StockMinimoPropuesto, null)
            };
            if (!resumida)
            {
                columnas.InsertRange(9, new List<(string, Func<ResultadoTendencia, object>, string)>
                {
                    ("Grupo", r => r.Grupo, null),
                    ("Meses desde 1ª venta", r => r.MesesDesdePrimeraVenta, null),
                    ("Ventas 12 m", r => r.Ventas12Meses, null),
                    ("Venta ponderada", r => r.VentaMediaPonderada, FORMATO_DECIMAL),
                    ("Factor estacional", r => r.FactorEstacional, FORMATO_RATIO),
                    ("Días en servir", r => r.DiasEnServir, null),
                    ("Múltiplos", r => r.Multiplos, null),
                    ("Cobertura objetivo (días)", r => r.CoberturaObjetivoDias, null),
                    ("Trimestre vs año anterior", r => r.RatioTrimestre, FORMATO_RATIO),
                    ("Año pico", r => r.AnioPico, null),
                    ("% del pico", r => r.PorcentajeDelPico, FORMATO_DECIMAL),
                    ("Años desde el pico", r => r.AniosDesdePico, null),
                    ("Pendiente de servir", r => r.DemandaNoServida, null),
                    ("Pedido compra abierto", r => r.PedidoCompraAbierto, null)
                });
            }
            columnas.Add(("Explicación", r => r.Explicacion, null));

            const int FILA_CABECERA = 3;
            Cabecera(hoja, FILA_CABECERA, columnas.Select(c => c.nombre).ToList());
            int fila = FILA_CABECERA + 1;
            foreach (ResultadoTendencia r in filas)
            {
                for (int c = 0; c < columnas.Count; c++)
                {
                    Valor(hoja.Cell(fila, c + 1), columnas[c].valor(r), columnas[c].formato);
                }
                fila++;
            }
            hoja.Columns(1, columnas.Count - 1).AdjustToContents(FILA_CABECERA, Math.Min(fila, FILA_CABECERA + 200));
            foreach (IXLColumn columna in hoja.Columns(1, columnas.Count - 1))
            {
                if (columna.Width > 40)
                {
                    columna.Width = 40;
                }
            }
            hoja.Column(columnas.Count).Width = 120;
            if (filas.Count > 0)
            {
                hoja.Range(FILA_CABECERA, 1, fila - 1, columnas.Count).SetAutoFilter();
            }
        }

        private static void HojaNuevos(IXLWorksheet hoja, List<ResultadoTendencia> filas, string mes)
        {
            Titulo(hoja, $"Productos nuevos (fase A) — {mes}: {filas.Count}");
            var columnas = new List<(string nombre, Func<ResultadoTendencia, object> valor, string formato)>
            {
                ("Producto", r => r.Producto, null),
                ("Nombre", r => r.Nombre, null),
                ("Familia", r => r.Familia, null),
                ("Proveedor", r => r.Proveedor, null),
                ("Meses desde 1ª venta", r => r.MesesDesdePrimeraVenta, null),
                ("Velocidad (uds/mes)", r => r.Velocidad, FORMATO_DECIMAL),
                ("Recompra %", r => r.TasaRecompra, FORMATO_DECIMAL),
                ("Pendiente de servir", r => r.DemandaNoServida, null),
                ("Stock", r => r.Stock, null),
                ("Pedido compra abierto", r => r.PedidoCompraAbierto, null),
                ("Exceso €", r => r.ExcesoValorado, FORMATO_EUROS),
                ("Acción", r => r.Accion, null),
                ("Máx actual", r => r.StockMaximoActual, null),
                ("Mín actual", r => r.StockMinimoActual, null),
                ("Máx propuesto", r => r.StockMaximoPropuesto, null),
                ("Mín propuesto", r => r.StockMinimoPropuesto, null),
                ("Explicación", r => r.Explicacion, null)
            };
            const int FILA_CABECERA = 3;
            Cabecera(hoja, FILA_CABECERA, columnas.Select(c => c.nombre).ToList());
            int fila = FILA_CABECERA + 1;
            foreach (ResultadoTendencia r in filas.OrderBy(r => r.TasaRecompra ?? decimal.MaxValue).ThenByDescending(r => r.Stock * r.PrecioMedio))
            {
                for (int c = 0; c < columnas.Count; c++)
                {
                    Valor(hoja.Cell(fila, c + 1), columnas[c].valor(r), columnas[c].formato);
                }
                fila++;
            }
            hoja.Columns(1, columnas.Count - 1).AdjustToContents(FILA_CABECERA, Math.Min(fila, FILA_CABECERA + 200));
            hoja.Column(2).Width = Math.Min(hoja.Column(2).Width, 40);
            hoja.Column(columnas.Count).Width = 120;
        }

        private static void HojaCalibracion(IXLWorksheet hoja, ResultadoCalibracion calibracion, ParametrosTendencias p, string mes)
        {
            Titulo(hoja, $"Calibración — {mes}: cómo estaban 6, 9 y 12 meses antes los que acabaron mal, frente a los vivos");
            hoja.Cell(2, 1).Value = "Umbrales en uso: PROVISIONALES hasta revisar esta hoja (se cambian en appSettings PropuestasStock:<Nombre>).";
            hoja.Cell(2, 1).Style.Font.Italic = true;

            int fila = 4;
            Cabecera(hoja, fila, new[] { "Umbral", "Valor", "Significado" });
            fila++;
            foreach (var (nombre, valor, significado) in p.Descripcion())
            {
                hoja.Cell(fila, 1).Value = nombre;
                hoja.Cell(fila, 2).Value = valor;
                hoja.Cell(fila, 3).Value = significado;
                fila++;
            }

            fila += 2;
            hoja.Cell(fila++, 1).Value = "Resumen por grupo";
            string[] cabResumen = { "Grupo", "Meses antes", "Productos", "Sin venta en 6 meses", "Mediana cobertura (meses)", "Mediana pendiente %/mes" };
            for (int c = 0; c < cabResumen.Length; c++)
            {
                IXLCell celda = hoja.Cell(fila, c + 1);
                celda.Value = cabResumen[c];
                celda.Style.Font.Bold = true;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            }
            fila++;
            foreach (ResumenCalibracion r in calibracion.Resumen)
            {
                hoja.Cell(fila, 1).Value = r.Grupo;
                hoja.Cell(fila, 2).Value = r.MesesAntes;
                hoja.Cell(fila, 3).Value = r.Productos;
                hoja.Cell(fila, 4).Value = r.SinVenta6Meses;
                Valor(hoja.Cell(fila, 5), r.MedianaCobertura, FORMATO_DECIMAL);
                Valor(hoja.Cell(fila, 6), r.MedianaPendiente, FORMATO_DECIMAL);
                fila++;
            }

            fila += 2;
            hoja.Cell(fila++, 1).Value = "Reglas candidatas: % de los que acabaron mal que habría marcado (aciertos) y % de vivos (falsas alarmas)";
            string[] cabUmbral = { "Regla", "Meses antes", "% muertos marcados", "% vivos marcados", "Muertos", "Vivos" };
            for (int c = 0; c < cabUmbral.Length; c++)
            {
                IXLCell celda = hoja.Cell(fila, c + 1);
                celda.Value = cabUmbral[c];
                celda.Style.Font.Bold = true;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            }
            fila++;
            foreach (UmbralCalibracion u in calibracion.Umbrales)
            {
                hoja.Cell(fila, 1).Value = u.Regla;
                hoja.Cell(fila, 2).Value = u.MesesAntes;
                Valor(hoja.Cell(fila, 3), u.PorcentajeMuertosMarcados, FORMATO_DECIMAL);
                Valor(hoja.Cell(fila, 4), u.PorcentajeVivosMarcados, FORMATO_DECIMAL);
                hoja.Cell(fila, 5).Value = u.Muertos;
                hoja.Cell(fila, 6).Value = u.Vivos;
                fila++;
            }

            fila += 2;
            hoja.Cell(fila++, 1).Value = "Detalle por producto (cobertura vacía = sin venta en los 6 meses anteriores)";
            List<string> cabDetalle = new List<string> { "Producto", "Nombre", "Grupo", "Mes del evento" };
            foreach (int h in CalibracionTendencias.HORIZONTES)
            {
                cabDetalle.Add($"Cobertura {h} m antes");
                cabDetalle.Add($"Pendiente {h} m antes");
            }
            for (int c = 0; c < cabDetalle.Count; c++)
            {
                IXLCell celda = hoja.Cell(fila, c + 1);
                celda.Value = cabDetalle[c];
                celda.Style.Font.Bold = true;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            }
            int filaCabDetalle = fila;
            fila++;
            foreach (FilaCalibracion f in calibracion.Detalle.OrderBy(f => f.Grupo).ThenBy(f => f.Producto))
            {
                hoja.Cell(fila, 1).Value = f.Producto;
                hoja.Cell(fila, 2).Value = f.Nombre;
                hoja.Cell(fila, 3).Value = f.Grupo;
                hoja.Cell(fila, 4).Value = f.MesEvento.ToString("yyyy-MM");
                int c = 5;
                foreach (int h in CalibracionTendencias.HORIZONTES)
                {
                    Valor(hoja.Cell(fila, c++), f.Cobertura.TryGetValue(h, out decimal? cob) ? cob : null, FORMATO_DECIMAL);
                    Valor(hoja.Cell(fila, c++), f.Pendiente.TryGetValue(h, out decimal? pen) ? pen : null, FORMATO_DECIMAL);
                }
                fila++;
            }
            if (calibracion.Detalle.Count > 0)
            {
                hoja.Range(filaCabDetalle, 1, fila - 1, cabDetalle.Count).SetAutoFilter();
            }
            hoja.Column(1).Width = 38;
            hoja.Column(2).Width = 40;
            hoja.Column(3).Width = 60;
            for (int c = 4; c <= cabDetalle.Count; c++)
            {
                hoja.Column(c).Width = 16;
            }
        }
    }
}
