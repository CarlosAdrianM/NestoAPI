using ClosedXML.Excel;
using NestoAPI.Infraestructure.IndicadoresStock;
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
    /// <para>NestoAPI#655: delante, «Resumen» (valor de stock y exceso con y sin propios); detrás de los nuevos, «Propios» (familias
    /// PRP, fórmula P). Las tres hojas de productos van sin los propios.</para>
    /// <para>NestoAPI#628: en «Resumen», el cuadre del total con IndicadoresStock (lo analizado + MTP y MMP, que el motor deja
    /// fuera) y el desglose por familia de MTP y MMP (<see cref="ResultadoPropuestasStock.Indicadores"/>).</para>
    /// </summary>
    public class GeneradorExcelPropuestasStock
    {
        public const string HOJA_TOP = "Top {0} por exceso";
        public const string HOJA_TODOS = "Todos los productos";
        public const string HOJA_NUEVOS = "Nuevos (fase A)";
        public const string HOJA_CALIBRACION = "Calibración";
        /// <summary>NestoAPI#655: valor de stock y exceso con y sin propios.</summary>
        public const string HOJA_RESUMEN = "Resumen";
        /// <summary>NestoAPI#655: las familias de fabricación propia (PRP), con la fórmula P y sus acciones.</summary>
        public const string HOJA_PROPIOS = "Propios";

        /// <summary>NestoAPI#656: líneas −1 con la fecha de entrega vencida al acabar el mes (antes, −1 y 1 sin mirar la fecha).</summary>
        public const string COLUMNA_PENDIENTE_VENCIDO = "Pendiente de servir (vencido)";
        /// <summary>NestoAPI#656: unidades albaranadas en 12 meses más de PropuestasStock:DiasRetraso días después de la fecha de entrega.</summary>
        public const string COLUMNA_SERVIDO_CON_RETRASO = "Servido con retraso 12 m (uds)";
        /// <summary>NestoAPI#630: el múltiplo de compra son más de PropuestasStock:MesesLoteGrande meses de venta.</summary>
        public const string COLUMNA_MULTIPLO_GRANDE = "Múltiplo grande (pedir uno menor)";

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
                List<ResultadoTendencia> todos = MotorTendencias.OrdenarPorExceso(resultado.Productos);
                // NestoAPI#655: los propios (familias PRP, fórmula P) van en su hoja: con ellos, la general se llenaba de «exceso».
                List<ResultadoTendencia> ordenados = todos.Where(r => !r.FabricacionPropia).ToList();
                List<ResultadoTendencia> propios = todos.Where(r => r.FabricacionPropia).ToList();
                HojaResumen(libro.Worksheets.Add(HOJA_RESUMEN), todos, ordenados, propios, mes, resultado.Indicadores);
                string nota = NotaPendientes(p);
                HojaProductos(libro.Worksheets.Add(NombreHojaTop(p.FilasTop)), ordenados.Take(p.FilasTop).ToList(),
                    $"Propuestas de stock — {mes}: los {p.FilasTop} con más euros atrapados, sin propios (exceso total sin propios: {MotorTendencias.Euros(ordenados.Sum(r => r.ExcesoValorado))})", resumida: true, p);
                HojaProductos(libro.Worksheets.Add(HOJA_TODOS), ordenados,
                    $"Propuestas de stock — {mes}: {ordenados.Count} productos sin los propios (stock, venta en 24 meses, pendientes o compras abiertas)", resumida: false, p);
                HojaNuevos(libro.Worksheets.Add(HOJA_NUEVOS), ordenados.Where(r => r.Fase == FasesProducto.NUEVO).ToList(), mes);
                foreach (string hoja in new[] { NombreHojaTop(p.FilasTop), HOJA_TODOS, HOJA_NUEVOS })
                {
                    libro.Worksheet(hoja).Cell(2, 1).Value = nota;
                    libro.Worksheet(hoja).Cell(2, 1).Style.Font.Italic = true;
                }
                HojaPropios(libro.Worksheets.Add(HOJA_PROPIOS), propios, p, mes);
                HojaCalibracion(libro.Worksheets.Add(HOJA_CALIBRACION), resultado.Calibracion ?? new ResultadoCalibracion(), p, mes);

                using (MemoryStream ms = new MemoryStream())
                {
                    libro.SaveAs(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>NestoAPI#656: qué cuentan las columnas de lo no servido (fila 2 de Top, Todos y Nuevos).</summary>
        internal static string NotaPendientes(ParametrosTendencias p)
            => $"«{COLUMNA_PENDIENTE_VENCIDO}»: uds en líneas pendientes (−1) con la fecha de entrega antes de acabar el mes (sin las en curso ni las de " +
                $"entrega futura). «{COLUMNA_SERVIDO_CON_RETRASO}»: uds albaranadas en los 12 meses que salieron más de {p.DiasRetraso} días después de la " +
                "fecha de entrega. En los nuevos (fase A), el máximo suma el mayor de los dos (el retraso, por mes).";

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

        private static void HojaProductos(IXLWorksheet hoja, List<ResultadoTendencia> filas, string titulo, bool resumida, ParametrosTendencias p)
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
                ("Mín propuesto", r => r.StockMinimoPropuesto, null),
                (COLUMNA_SERVIDO_CON_RETRASO, r => r.ServidoConRetraso, null),
                ("Meses de venta del múltiplo", r => r.MesesVentaMultiplo, FORMATO_DECIMAL),
                (COLUMNA_MULTIPLO_GRANDE, r => MotorTendencias.EsLoteGrande(r, p) ? "Sí" : null, null)
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
                    (COLUMNA_PENDIENTE_VENCIDO, r => r.DemandaNoServida, null),
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
                (COLUMNA_PENDIENTE_VENCIDO, r => r.DemandaNoServida, null),
                (COLUMNA_SERVIDO_CON_RETRASO, r => r.ServidoConRetraso, null),
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

        /// <summary>NestoAPI#655: valor de stock (stock × precio medio, sin negativos) de un grupo de filas.</summary>
        internal static decimal ValorStock(IEnumerable<ResultadoTendencia> filas) => Math.Round(filas.Sum(r => Math.Max(0, r.Stock) * r.PrecioMedio), 2);

        /// <summary>NestoAPI#628: la línea del cuadre con lo que el motor deja fuera (MTP y MMP).</summary>
        public const string ETIQUETA_FUERA = "Fuera de las propuestas: materias primas (MTP) y muestras (MMP)";
        public const string ETIQUETA_ANALIZADO = "Analizado en las propuestas (todos, con propios)";
        public const string ETIQUETA_TOTAL_CUADRE = "Total";
        public const string ETIQUETA_INDICADORES = "Valor de stock según los indicadores del mes (IndicadoresStock, TOTAL)";
        public const string ETIQUETA_DIFERENCIA = "Diferencia";
        public const string TITULO_CUADRE = "Cuadre con los indicadores de stock del mes";
        public const string TITULO_FUERA_POR_FAMILIA = "Fuera de las propuestas por familia (materias primas y muestras)";

        private static void CabeceraTabla(IXLWorksheet hoja, int fila, IList<string> columnas)
        {
            for (int c = 0; c < columnas.Count; c++)
            {
                IXLCell celda = hoja.Cell(fila, c + 1);
                celda.Value = columnas[c];
                celda.Style.Font.Bold = true;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
                celda.Style.Alignment.WrapText = true;
            }
        }

        private static int Subtitulo(IXLWorksheet hoja, int fila, string texto)
        {
            hoja.Cell(fila, 1).Value = texto;
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            return fila + 1;
        }

        private static int Nota(IXLWorksheet hoja, int fila, string texto)
        {
            hoja.Cell(fila, 1).Value = texto;
            hoja.Cell(fila, 1).Style.Font.Italic = true;
            return fila + 1;
        }

        /// <summary>
        /// NestoAPI#628 (cuadre): lo analizado + lo que el motor deja fuera (MTP y MMP, con el mismo criterio de valoración que
        /// IndicadoresStock) frente al valor de stock de la fila TOTAL de los indicadores del mes. Devuelve la fila siguiente.
        /// </summary>
        private static int TablaCuadre(IXLWorksheet hoja, int fila, List<ResultadoTendencia> todos, IndicadoresDelMesPropuestas indicadores)
        {
            fila = Subtitulo(hoja, fila, TITULO_CUADRE);
            CabeceraTabla(hoja, fila, new[] { "Concepto", "Productos", "Valor de stock" });
            fila++;
            decimal analizado = ValorStock(todos);
            hoja.Cell(fila, 1).Value = ETIQUETA_ANALIZADO;
            hoja.Cell(fila, 2).Value = todos.Count;
            Valor(hoja.Cell(fila, 3), analizado, FORMATO_EUROS);
            fila++;

            List<StockFueraDeLaDemanda> fuera = indicadores?.FueraDeLaDemanda;
            decimal? valorFuera = fuera?.Sum(f => f.Valor);
            hoja.Cell(fila, 1).Value = ETIQUETA_FUERA;
            if (fuera != null)
            {
                hoja.Cell(fila, 2).Value = fuera.Sum(f => f.Productos);
                Valor(hoja.Cell(fila, 3), valorFuera.Value, FORMATO_EUROS);
            }
            else
            {
                hoja.Cell(fila, 3).Value = "sin datos";
            }
            fila++;

            decimal? total = valorFuera.HasValue ? analizado + valorFuera.Value : (decimal?)null;
            hoja.Cell(fila, 1).Value = ETIQUETA_TOTAL_CUADRE;
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            if (total.HasValue)
            {
                Valor(hoja.Cell(fila, 3), total.Value, FORMATO_EUROS);
                hoja.Cell(fila, 3).Style.Font.Bold = true;
            }
            fila++;

            decimal? segunIndicadores = indicadores?.Total?.ValorStock;
            hoja.Cell(fila, 1).Value = ETIQUETA_INDICADORES;
            if (segunIndicadores.HasValue)
            {
                Valor(hoja.Cell(fila, 3), segunIndicadores.Value, FORMATO_EUROS);
            }
            else
            {
                hoja.Cell(fila, 3).Value = "sin datos";
            }
            fila++;

            if (total.HasValue && segunIndicadores.HasValue)
            {
                hoja.Cell(fila, 1).Value = ETIQUETA_DIFERENCIA;
                Valor(hoja.Cell(fila, 3), total.Value - segunIndicadores.Value, FORMATO_EUROS);
                fila++;
                fila = Nota(hoja, fila, "Si la diferencia no es 0, suele ser stock negativo (aquí cuenta como 0 y en los indicadores resta) " +
                    "o productos con stock que el motor no analiza.");
            }
            if (indicadores == null)
            {
                fila = Nota(hoja, fila, "No se han leído los indicadores de stock del mes.");
            }
            else if (!string.IsNullOrWhiteSpace(indicadores.Aviso))
            {
                fila = Nota(hoja, fila, indicadores.Aviso);
            }
            return fila;
        }

        public const string TITULO_AGRUPACIONES = "Por tipo de exclusiva: propios, nacionales, Madrid, sin exclusiva y a extinguir";
        public const string ETIQUETA_TODAS_AGRUPACIONES = "Todos";

        /// <summary>El orden de las columnas «nº por acción»; las que no estén aquí, detrás por orden alfabético.</summary>
        private static readonly string[] ORDEN_ACCIONES =
        {
            AccionesPropuesta.AJUSTAR, AccionesPropuesta.MANTENER, AccionesPropuesta.ESTADO_1, AccionesPropuesta.PARAR_COMPRAS,
            AccionesPropuesta.LIQUIDAR, AccionesPropuesta.AJUSTAR_LOTE, AccionesPropuesta.POTENCIAR, AccionesPropuesta.CADUCA, AccionesPropuesta.NADA
        };

        /// <summary>
        /// NestoAPI#628 (Manuel): por agrupación de tipo de exclusiva (<see cref="AgrupacionesStock"/>), referencias, valor de stock y
        /// exceso de las propuestas, y el coste de ventas, la rotación y la cobertura de IndicadoresStock de esa agrupación (no una
        /// aproximación con el stock de fin de mes: el mismo cálculo que el total, con el stock medio de 12 meses).
        /// </summary>
        private static int TablaAgrupaciones(IXLWorksheet hoja, int fila, List<ResultadoTendencia> todos, IndicadoresDelMesPropuestas indicadores)
        {
            fila = Subtitulo(hoja, fila, TITULO_AGRUPACIONES);
            List<string> acciones = todos.Select(r => r.Accion).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct()
                .OrderBy(a => Array.IndexOf(ORDEN_ACCIONES, a) < 0 ? int.MaxValue : Array.IndexOf(ORDEN_ACCIONES, a))
                .ThenBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> cabecera = new List<string> { "Agrupación", "Referencias", "Valor de stock", "Coste de ventas 12 m", "Rotación (veces al año)",
                "Cobertura media (días)", "Exceso €" };
            cabecera.AddRange(acciones.Select(a => "Nº " + a));
            CabeceraTabla(hoja, fila, cabecera);
            fila++;

            bool faltanIndicadores = false;
            void Linea(string nombre, List<ResultadoTendencia> filas, FilaIndicadoresStock indicador)
            {
                hoja.Cell(fila, 1).Value = nombre;
                hoja.Cell(fila, 2).Value = filas.Count;
                Valor(hoja.Cell(fila, 3), ValorStock(filas), FORMATO_EUROS);
                if (indicador != null)
                {
                    Valor(hoja.Cell(fila, 4), indicador.CosteVentas12Meses, FORMATO_EUROS);
                    Valor(hoja.Cell(fila, 5), indicador.Rotacion, FORMATO_RATIO);
                    Valor(hoja.Cell(fila, 6), indicador.CoberturaMediaDias, FORMATO_DECIMAL);
                }
                else
                {
                    faltanIndicadores = true;
                }
                Valor(hoja.Cell(fila, 7), filas.Sum(r => r.ExcesoValorado), FORMATO_EUROS);
                for (int i = 0; i < acciones.Count; i++)
                {
                    hoja.Cell(fila, 8 + i).Value = filas.Count(r => r.Accion == acciones[i]);
                }
                fila++;
            }

            Dictionary<string, List<ResultadoTendencia>> porAgrupacion = todos.GroupBy(r => r.Agrupacion).ToDictionary(g => g.Key, g => g.ToList());
            foreach (string codigo in AgrupacionesStock.TODAS)
            {
                FilaIndicadoresStock indicador = indicadores?.Fila(AgrupacionesStock.Almacen(codigo));
                if (!porAgrupacion.TryGetValue(codigo, out List<ResultadoTendencia> filas) && indicador == null)
                {
                    continue;
                }
                Linea(AgrupacionesStock.Nombre(codigo), filas ?? new List<ResultadoTendencia>(), indicador);
            }
            Linea(ETIQUETA_TODAS_AGRUPACIONES, todos, indicadores?.Total);
            hoja.Range(fila - 1, 1, fila - 1, cabecera.Count).Style.Font.Bold = true;

            fila = Nota(hoja, fila, "Referencias, valor de stock, exceso y acciones: los de las propuestas (sin materias primas ni muestras). Coste de ventas, " +
                "rotación (coste de ventas de 12 meses ÷ stock medio de 12 meses) y cobertura: los de los indicadores de stock del mes de cada agrupación.");
            if (faltanIndicadores && indicadores != null)
            {
                fila = Nota(hoja, fila, "Los indicadores de stock de este mes no tienen alguna agrupación: hay que recalcularlos (POST api/IndicadoresStock/Reconstruir).");
            }
            return fila;
        }

        /// <summary>NestoAPI#628: el desglose por familia de lo que el motor deja fuera (para analizarlo cuando toque).</summary>
        private static int TablaFueraPorFamilia(IXLWorksheet hoja, int fila, IndicadoresDelMesPropuestas indicadores)
        {
            fila = Subtitulo(hoja, fila, TITULO_FUERA_POR_FAMILIA);
            List<StockFueraDeLaDemanda> fuera = indicadores?.FueraDeLaDemanda;
            if (fuera == null)
            {
                return Nota(hoja, fila, "Sin datos.");
            }
            CabeceraTabla(hoja, fila, new[] { "Partida", "Familia", "Nombre de la familia", "Productos", "Unidades", "Valor de stock" });
            fila++;
            foreach (StockFueraDeLaDemanda f in fuera
                .OrderBy(f => f.Partida == StockFueraDeLaDemanda.MATERIAS_PRIMAS ? 0 : 1)
                .ThenByDescending(f => f.Valor)
                .ThenBy(f => f.Familia, StringComparer.OrdinalIgnoreCase))
            {
                hoja.Cell(fila, 1).Value = f.Partida == StockFueraDeLaDemanda.MATERIAS_PRIMAS ? "Materias primas (MTP)" : "Muestras (MMP)";
                Valor(hoja.Cell(fila, 2), f.Familia);
                Valor(hoja.Cell(fila, 3), f.NombreFamilia);
                hoja.Cell(fila, 4).Value = f.Productos;
                hoja.Cell(fila, 5).Value = f.Unidades;
                Valor(hoja.Cell(fila, 6), f.Valor, FORMATO_EUROS);
                fila++;
            }
            return fila;
        }

        /// <summary>
        /// NestoAPI#655: valor de stock y exceso con y sin propios, y los propios por acción. NestoAPI#628: el cuadre con los
        /// indicadores del mes y el desglose por familia de lo que queda fuera (MTP y MMP).
        /// </summary>
        private static void HojaResumen(IXLWorksheet hoja, List<ResultadoTendencia> todos, List<ResultadoTendencia> sinPropios,
            List<ResultadoTendencia> propios, string mes, IndicadoresDelMesPropuestas indicadores)
        {
            Titulo(hoja, $"Propuestas de stock — {mes}: resumen con y sin propios (familias de fabricación propia)");
            const int FILA_CABECERA = 3;
            Cabecera(hoja, FILA_CABECERA, new[] { "Productos", "Filas", "Valor de stock", "Exceso (uds)", "Exceso €" });
            int fila = FILA_CABECERA + 1;
            foreach (var (nombre, filas) in new[] { ("Todos (con propios)", todos), ("Sin propios", sinPropios), ("Solo propios", propios) })
            {
                hoja.Cell(fila, 1).Value = nombre;
                hoja.Cell(fila, 2).Value = filas.Count;
                Valor(hoja.Cell(fila, 3), ValorStock(filas), FORMATO_EUROS);
                hoja.Cell(fila, 4).Value = filas.Sum(r => r.ExcesoUnidades);
                Valor(hoja.Cell(fila, 5), filas.Sum(r => r.ExcesoValorado), FORMATO_EUROS);
                fila++;
            }

            fila += 2;
            fila = TablaCuadre(hoja, fila, todos, indicadores);

            fila += 2;
            fila = TablaAgrupaciones(hoja, fila, todos, indicadores);

            fila += 2;
            hoja.Cell(fila, 1).Value = "Propios por acción (nunca Outlet ni liquidar)";
            hoja.Cell(fila, 1).Style.Font.Bold = true;
            fila++;
            string[] cabecera = { "Acción", "Filas", "Valor de stock", "Exceso (uds)", "Exceso €" };
            for (int c = 0; c < cabecera.Length; c++)
            {
                IXLCell celda = hoja.Cell(fila, c + 1);
                celda.Value = cabecera[c];
                celda.Style.Font.Bold = true;
                celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            }
            fila++;
            foreach (IGrouping<string, ResultadoTendencia> grupo in propios.GroupBy(r => r.Accion).OrderByDescending(g => g.Sum(r => r.ExcesoValorado)))
            {
                hoja.Cell(fila, 1).Value = grupo.Key;
                hoja.Cell(fila, 2).Value = grupo.Count();
                Valor(hoja.Cell(fila, 3), ValorStock(grupo), FORMATO_EUROS);
                hoja.Cell(fila, 4).Value = grupo.Sum(r => r.ExcesoUnidades);
                Valor(hoja.Cell(fila, 5), grupo.Sum(r => r.ExcesoValorado), FORMATO_EUROS);
                fila++;
            }

            fila += 2;
            _ = TablaFueraPorFamilia(hoja, fila, indicadores);

            hoja.Column(1).Width = 62;
            for (int c = 2; c <= 16; c++)
            {
                hoja.Column(c).Width = 18;
            }
        }

        /// <summary>NestoAPI#655: los propios con lo que mira la fórmula P (lote, meses de venta, tope de vida útil).</summary>
        private static void HojaPropios(IXLWorksheet hoja, List<ResultadoTendencia> filas, ParametrosTendencias p, string mes)
        {
            Titulo(hoja, $"Propios (fabricación propia) — {mes}: {filas.Count} productos; lote = múltiplos de ALG, " +
                $"{p.DiasSeguridadPropios} días de seguridad y tope de vida útil por grupo ({p.TextoVidaUtil()})");
            decimal? Meses(decimal unidades, decimal venta) => venta > 0 ? Math.Round(unidades / venta, 1) : (decimal?)null;
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
                ("Valor de stock", r => Math.Round(Math.Max(0, r.Stock) * r.PrecioMedio, 2), FORMATO_EUROS),
                ("Venta de referencia (uds/mes)", r => r.VentaMensualReferencia, FORMATO_DECIMAL),
                ("Meses de venta en stock", r => Meses(r.Stock, r.VentaMensualReferencia), FORMATO_DECIMAL),
                ("Días en servir", r => r.DiasEnServir, null),
                ("Días de seguridad", r => r.DiasSeguridad, null),
                ("Lote mínimo", r => r.Multiplos, null),
                ("Meses de venta del lote", r => Meses(r.Multiplos, r.VentaMensualReferencia), FORMATO_DECIMAL),
                ("Tope vida útil (uds)", r => MotorTendencias.TopePropios(r.VentaMensualReferencia, p.VidaUtilMeses(r.Grupo)), null),
                ("Grupo", r => r.Grupo, null),
                ("Vida útil", r => p.VidaUtilMeses(r.Grupo).HasValue ? $"{p.VidaUtilMeses(r.Grupo)} meses" : "No caduca", null),
                ("Exceso (uds)", r => r.ExcesoUnidades, null),
                ("Exceso €", r => r.ExcesoValorado, FORMATO_EUROS),
                ("Acción", r => r.Accion, null),
                ("Máx actual", r => r.StockMaximoActual, null),
                ("Mín actual", r => r.StockMinimoActual, null),
                ("Máx propuesto", r => r.StockMaximoPropuesto, null),
                ("Mín propuesto", r => r.StockMinimoPropuesto, null),
                ("Pedido compra abierto", r => r.PedidoCompraAbierto, null),
                ("Explicación", r => r.Explicacion, null)
            };
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

        private static void HojaCalibracion(IXLWorksheet hoja, ResultadoCalibracion calibracion, ParametrosTendencias p, string mes)
        {
            Titulo(hoja, $"Calibración — {mes}: cómo estaban 6, 9 y 12 meses antes los que acabaron mal, frente a los vivos");
            hoja.Cell(2, 1).Value = "Umbrales en uso: PROVISIONALES hasta revisar esta hoja (se cambian en appSettings PropuestasStock:<Nombre>).";
            hoja.Cell(2, 1).Style.Font.Italic = true;
            hoja.Cell(3, 1).Value = "Mes del evento: entrada en EXT; último mes con venta en estado 4 (no hay fecha del cambio de estado; sin venta en los últimos 36 meses no entra) y en «sin venta 12 meses»; mes de referencia en los vivos.";
            hoja.Cell(3, 1).Style.Font.Italic = true;

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
            List<string> cabDetalle = new List<string> { "Producto", "Nombre", "Grupo", "Mes del evento (estado 4: último con venta)" };
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
