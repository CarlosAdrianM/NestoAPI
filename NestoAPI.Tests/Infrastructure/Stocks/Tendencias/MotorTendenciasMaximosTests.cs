using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static NestoAPI.Tests.Infrastructure.Stocks.Tendencias.SeriesTendencia;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#632 (corte 4 de la épica #628): fórmula de máximos de los maduros ponderada hacia lo reciente, sin estacionalidad,
    /// con la de 6 meses si va a la baja y sin subir en declive; con el interruptor <see cref="ParametrosTendencias.FormulaMaduros"/>
    /// para volver a la del corte 2 (Ponderada) o a la de siempre (Clasica). Cada caso compara el valor antiguo con el nuevo.
    /// </summary>
    [TestClass]
    public class MotorTendenciasMaximosTests
    {
        private static readonly ParametrosTendencias NUEVA = new ParametrosTendencias();
        private static readonly ParametrosTendencias CORTE2 = new ParametrosTendencias { FormulaMaduros = FormulasMaduros.PONDERADA };
        private static readonly ParametrosTendencias CLASICA = new ParametrosTendencias { FormulaMaduros = FormulasMaduros.CLASICA };

        private static ResultadoTendencia Calcular(DatosProductoTendencia d, ParametrosTendencias p, decimal[] indices = null)
            => MotorTendencias.Calcular(d, indices, MesReferencia, p);

        /// <summary>Cinco años a 30/mes y junio, julio y agosto a 60 (producto de verano). Septiembre es el mes de referencia.</summary>
        private static int[] Verano()
        {
            int[] serie = new int[N];
            for (int i = 0; i < N; i++)
            {
                int mes = CalculadoraSeries.MesDelAnio(MesReferencia, FIN, i);
                serie[i] = mes >= 5 && mes <= 7 ? 60 : 30;
            }
            return serie;
        }

        /// <summary>Año a año 400, 300, 200 y 150 al mes, y los últimos 12 meses cayendo de 120 a 10 (como en MotorTendenciasTests).</summary>
        private static int[] DecliveConfirmado()
        {
            int[] serie = new int[N];
            int[] porAnio = { 400, 300, 200, 150 };
            for (int i = 0; i < 48; i++)
            {
                serie[i] = porAnio[i / 12];
            }
            for (int k = 0; k < 12; k++)
            {
                serie[48 + k] = 120 - 10 * k;
            }
            return serie;
        }

        /// <summary>
        /// Cuatro años a 100/mes; en los últimos 12, seis a 100, tres a 40 y tres a 60 (rebota algo): baja, pero al 75 % del pico,
        /// así que es un bajón puntual y no un declive.
        /// </summary>
        private static int[] BajonConRebote()
        {
            int[] serie = Constante(100);
            int[] ultimos = { 100, 100, 100, 100, 100, 100, 40, 40, 40, 60, 60, 60 };
            Array.Copy(ultimos, 0, serie, N - 12, 12);
            return serie;
        }

        /// <summary>Cinco años a 400/mes y los últimos 12 meses: seis a 450 y seis a 330 (como el 17404 en 2026).</summary>
        private static int[] BajonPuntual17404()
        {
            int[] serie = Constante(400);
            for (int k = 0; k < 12; k++)
            {
                serie[FIN - 11 + k] = k < 6 ? 450 : 330;
            }
            return serie;
        }

        [TestMethod]
        public void MediaDesestacionalizada_DivideCadaMesPorSuIndice()
        {
            int[] serie = Verano();
            decimal[] indices = CalculadoraSeries.IndicesEstacionales(serie, FIN, MesReferencia, 120, 0);

            // julio y agosto (60 ÷ 1,6) y septiembre (30 ÷ 0,8): todos 37,5, la venta de verdad sin el verano
            Assert.AreEqual(37.5m, CalculadoraSeries.MediaDesestacionalizada(serie, FIN, 3, indices, MesReferencia));
            Assert.AreEqual(37.5m, CalculadoraSeries.MediaDesestacionalizada(serie, FIN, 12, indices, MesReferencia));
            // Sin índices, la media tal cual: (60 + 60 + 30) ÷ 3
            Assert.AreEqual(50m, CalculadoraSeries.MediaDesestacionalizada(serie, FIN, 3, null, MesReferencia));
        }

        [TestMethod]
        public void Estacional_TrasElVerano_LaBaseNoSeInflaConLosMesesAltos()
        {
            int[] serie = Verano();
            decimal[] indices = CalculadoraSeries.IndicesEstacionales(serie, FIN, MesReferencia, 120, 0);
            DatosProductoTendencia d = Producto(ventas: serie, stock: 60, diasEnServir: 10);

            ResultadoTendencia antes = Calcular(d, CORTE2, indices);
            ResultadoTendencia ahora = Calcular(d, NUEVA, indices);

            // Octubre (índice 0,8) vende 30. Antes: ponderada 46,25 (julio y agosto altos) × 0,8 = 37 uds/mes → 34/21.
            Assert.AreEqual(0.80m, ahora.FactorEstacional);
            Assert.AreEqual(34, antes.StockMaximoPropuesto);
            Assert.AreEqual(21, antes.StockMinimoPropuesto);
            // Ahora: 37,5 sin estacionalidad × 0,8 = 30 uds/mes, lo que de verdad se vende en octubre → 27/17.
            Assert.AreEqual(27, ahora.StockMaximoPropuesto);
            Assert.AreEqual(17, ahora.StockMinimoPropuesto);
            Assert.AreEqual(TendenciasProducto.ESTABLE, ahora.Tendencia);
            StringAssert.Contains(ahora.Explicacion, "Venta sin la estacionalidad de la familia: 3 meses 37,5, 6 meses 37,5, 12 meses 37,5 uds/mes");
            StringAssert.Contains(ahora.Explicacion, "× 0,80 estacional de los próximos meses = 30,0 uds/mes");
            Assert.AreEqual("ponderada sin estacionalidad", ahora.OrigenVentaBaseMaximos);
        }

        [TestMethod]
        public void Declive_ElMaximoNuncaSube()
        {
            // Declive confirmado con poca cobertura (30 uds, menos de un mes): no se liquida ni pasa a estado 1; se calcula el máximo.
            DatosProductoTendencia d = Producto(ventas: DecliveConfirmado(), stock: 30, diasEnServir: 10);
            d.StockMaximoAlg = 20;
            d.StockMinimoAlg = 10;

            ResultadoTendencia antes = Calcular(d, CORTE2);
            ResultadoTendencia ahora = Calcular(d, NUEVA);

            Assert.AreEqual(TendenciasProducto.DECLIVE, ahora.Tendencia);
            // Antes: ponderada 32,5 → máximo 30, mínimo 19: subía los 20/10 de hoy en un producto que se muere.
            Assert.AreEqual(30, antes.StockMaximoPropuesto);
            Assert.AreEqual(19, antes.StockMinimoPropuesto);
            Assert.AreEqual(AccionesPropuesta.AJUSTAR, antes.Accion);
            // Ahora: se queda en los de hoy.
            Assert.AreEqual(20, ahora.StockMaximoPropuesto);
            Assert.AreEqual(10, ahora.StockMinimoPropuesto);
            Assert.AreEqual(AccionesPropuesta.MANTENER, ahora.Accion);
            StringAssert.Contains(ahora.Explicacion, "máximo = 32,5 × (7 + 2 × 10) ÷ 30 = 30; en declive no sube: se queda en los 20/10 de hoy → máximo 20, mínimo 10");
            JObject maximos = (JObject)JObject.Parse(ahora.EntradasJson)["maximos"];
            Assert.AreEqual(30, (int)maximos["maximoSinCongelar"]);
            Assert.AreEqual(true, (bool)maximos["declive"]);
            StringAssert.Contains(ahora.OrigenVentaBaseMaximos, "declive: no sube");
        }

        [TestMethod]
        public void Declive_SiLaFormulaDaMenosQueHoy_Baja()
        {
            DatosProductoTendencia d = Producto(ventas: DecliveConfirmado(), stock: 30, diasEnServir: 10); // hoy 555/295

            ResultadoTendencia ahora = Calcular(d, NUEVA);

            Assert.AreEqual(30, ahora.StockMaximoPropuesto);
            Assert.AreEqual(19, ahora.StockMinimoPropuesto);
            Assert.IsFalse(ahora.Explicacion.Contains("no sube"), ahora.Explicacion);
        }

        [TestMethod]
        public void Declive_SinControlDeStockEnAlg_NoHayTope()
        {
            DatosProductoTendencia d = Producto(ventas: DecliveConfirmado(), stock: 30, diasEnServir: 10);
            d.StockMaximoAlg = null;
            d.StockMinimoAlg = null;

            ResultadoTendencia ahora = Calcular(d, NUEVA);

            Assert.AreEqual(30, ahora.StockMaximoPropuesto);
            Assert.AreEqual(19, ahora.StockMinimoPropuesto);
        }

        [TestMethod]
        public void ALaBaja_UsaLaDeSeisMesesSiEsMenorQueLaPonderada()
        {
            DatosProductoTendencia d = Producto(ventas: BajonConRebote(), stock: 100, diasEnServir: 10);

            ResultadoTendencia antes = Calcular(d, CORTE2);
            ResultadoTendencia ahora = Calcular(d, NUEVA);

            Assert.AreEqual(TendenciasProducto.BAJON_PUNTUAL, ahora.Tendencia);
            Assert.IsTrue(ahora.PendienteRelativa <= NUEVA.PendienteBaja, $"pendiente {ahora.PendienteRelativa}");
            // Antes: ponderada (3 × 60 + 2 × 50 + 75) ÷ 6 = 59,2 → 54/34. El rebote de los 3 últimos meses tiraba hacia arriba.
            Assert.AreEqual(54, antes.StockMaximoPropuesto);
            Assert.AreEqual(34, antes.StockMinimoPropuesto);
            // Ahora: va a la baja, se toma la de 6 meses (50) → 45/29.
            Assert.AreEqual(45, ahora.StockMaximoPropuesto);
            Assert.AreEqual(29, ahora.StockMinimoPropuesto);
            StringAssert.Contains(ahora.Explicacion, "se toma la de 6 meses, 50,0, que es menor");
            Assert.AreEqual(50m, ahora.VentaBaseMaximos);
            Assert.AreEqual("6 meses (a la baja)", ahora.OrigenVentaBaseMaximos);
        }

        [TestMethod]
        public void BajonPuntual17404_NoSeCongelaYBajaPoco()
        {
            DatosProductoTendencia d = Producto(ventas: BajonPuntual17404(), stock: 1000, diasEnServir: 10, multiplos: 144);

            ResultadoTendencia antes = Calcular(d, CORTE2);
            ResultadoTendencia ahora = Calcular(d, NUEVA);

            Assert.AreEqual(TendenciasProducto.BAJON_PUNTUAL, ahora.Tendencia);
            // Antes: ponderada (3 × 330 + 2 × 330 + 390) ÷ 6 = 340 → 306/193. Ahora: la de 6 meses, 330 → 297/187.
            Assert.AreEqual(306, antes.StockMaximoPropuesto);
            Assert.AreEqual(193, antes.StockMinimoPropuesto);
            Assert.AreEqual(297, ahora.StockMaximoPropuesto);
            Assert.AreEqual(187, ahora.StockMinimoPropuesto);
            Assert.AreNotEqual(AccionesPropuesta.LIQUIDAR, ahora.Accion);
            Assert.IsFalse(ahora.Explicacion.Contains("no sube"), ahora.Explicacion);
            StringAssert.Contains(ahora.Explicacion, "múltiplos de 144 (se respetan al pedir, no en el máximo)");
        }

        [TestMethod]
        public void Nuevo_NoCambiaConLaFormulaDeLosMaduros()
        {
            DatosProductoTendencia d = Producto("45958", ventas: Ultimos(10, 20, 30, 40, 50), stock: 40, diasEnServir: 15);
            d.FechaPrimeraVenta = new DateTime(2026, 5, 3);
            d.FechaPrimeraEntrada = new DateTime(2026, 5, 1);

            ResultadoTendencia ahora = Calcular(d, NUEVA);
            ResultadoTendencia antes = Calcular(d, CORTE2);

            Assert.AreEqual(FasesProducto.NUEVO, ahora.Fase);
            StringAssert.StartsWith(ahora.Formula, "A:");
            Assert.AreEqual(antes.StockMaximoPropuesto, ahora.StockMaximoPropuesto);
            Assert.AreEqual(antes.StockMinimoPropuesto, ahora.StockMinimoPropuesto);
            Assert.AreEqual(antes.Explicacion, ahora.Explicacion);
            Assert.AreEqual("A", (string)JObject.Parse(ahora.EntradasJson)["maximos"]["estrategia"]);
        }

        [TestMethod]
        public void Intermitente_NoCambiaConLaFormulaDeLosMaduros()
        {
            int[] pocas = new int[N];
            pocas[FIN] = 2;
            pocas[FIN - 4] = 3;
            pocas[FIN - 30] = 5;
            DatosProductoTendencia d = Producto(ventas: pocas, stock: 3);

            ResultadoTendencia ahora = Calcular(d, NUEVA);

            Assert.AreEqual(FasesProducto.INTERMITENTE, ahora.Fase);
            Assert.AreEqual("C: 0-2", ahora.Formula);
            Assert.AreEqual(1, ahora.StockMaximoPropuesto);
            Assert.AreEqual(Calcular(d, CORTE2).Explicacion, ahora.Explicacion);
        }

        [TestMethod]
        public void Interruptor_Ponderada_EsLaDelCorte2()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 90, diasEnServir: 10), CORTE2);

            Assert.AreEqual("B: ponderada × (7 + 2 × días)", r.Formula);
            StringAssert.Contains(r.Explicacion, "Maduro. 6 meses: 30,0 uds/mes, ponderada 30,0 × 1,00 estacional (familia sin 3 años)");
            Assert.AreEqual(27, r.StockMaximoPropuesto);
            Assert.AreEqual(17, r.StockMinimoPropuesto);
        }

        [TestMethod]
        public void Interruptor_Clasica_EsLaMediaDe24MesesDeSiempre()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: DecliveConfirmado(), stock: 30, diasEnServir: 10), CLASICA);

            // 24 meses: 12 a 150 (1.800) + los 12 que caen de 120 a 10 (780) = 2.580 → 107,5 uds/mes; ni estacionalidad ni tope
            Assert.AreEqual("B: media 24 m × (7 + 2 × días)", r.Formula);
            Assert.AreEqual(97, r.StockMaximoPropuesto);   // 107,5 ÷ 30 × 27 = 96,75
            Assert.AreEqual(61, r.StockMinimoPropuesto);   // 107,5 ÷ 30 × 17 = 60,9
            StringAssert.Contains(r.Explicacion, "Maduro (fórmula de siempre). Media de 24 meses: 2580 uds, 107,5 uds/mes");
        }

        [TestMethod]
        public void Interruptor_SeLeeDeAppSettingsYLoDesconocidoEsLaNueva()
        {
            Assert.AreEqual(FormulasMaduros.RECIENTE, new ParametrosTendencias().FormulaMaduros);
            Assert.AreEqual(FormulasMaduros.PONDERADA, ParametrosTendencias.Desde(c => c == "PropuestasStock:FormulaMaduros" ? " ponderada " : null).FormulaMaduros);
            Assert.AreEqual(FormulasMaduros.CLASICA, ParametrosTendencias.Desde(c => c == "PropuestasStock:FormulaMaduros" ? "Clásica" : null).FormulaMaduros);
            Assert.AreEqual(FormulasMaduros.RECIENTE, ParametrosTendencias.Desde(c => c == "PropuestasStock:FormulaMaduros" ? "otra" : null).FormulaMaduros);
            Assert.IsTrue(new ParametrosTendencias().Descripcion().Any(d => d.Nombre == nameof(ParametrosTendencias.FormulaMaduros) && d.Valor == FormulasMaduros.RECIENTE));
        }

        [TestMethod]
        public void Trazabilidad_LasEntradasDelMaximoVanAlJson()
        {
            ResultadoTendencia r = Calcular(Producto(ventas: Constante(30), stock: 90, diasEnServir: 10), NUEVA);

            Assert.AreEqual("B: reciente × (7 + 2 × días)", r.Formula);
            JObject maximos = (JObject)JObject.Parse(r.EntradasJson)["maximos"];
            Assert.AreEqual("B reciente", (string)maximos["estrategia"]);
            Assert.AreEqual(30m, (decimal)maximos["media3"]);
            Assert.AreEqual(30m, (decimal)maximos["ponderada"]);
            Assert.AreEqual(17, (int)maximos["diasMinimo"]);
            Assert.AreEqual(27, (int)maximos["diasMaximo"]);
            Assert.AreEqual(90, (int)maximos["coberturaObjetivoDias"]);
            Assert.AreEqual(27, (int)maximos["maximo"]);
            Assert.AreEqual(17, (int)maximos["minimo"]);
            Assert.AreEqual(30m, r.VentaBaseMaximos);
            Assert.AreEqual("ponderada", r.OrigenVentaBaseMaximos);
            // Una salida temprana (liquidar) no tiene cálculo de máximos
            ResultadoTendencia liquidar = Calcular(Producto(ventas: Constante(2), stock: 50, estado: 4), NUEVA);
            Assert.IsNull(liquidar.VentaBaseMaximos);
            Assert.AreEqual(JTokenType.Null, JObject.Parse(liquidar.EntradasJson)["maximos"].Type);
        }

        [TestMethod]
        public void Trazabilidad_EntradasAntiguasSinMaximos_NoFallan()
        {
            Assert.IsNull(new ResultadoTendencia { EntradasJson = "{\"tipoExclusiva\":\"NAC\"}" }.VentaBaseMaximos);
            Assert.IsNull(new ResultadoTendencia { EntradasJson = "{\"maximos\":null}" }.OrigenVentaBaseMaximos);
            Assert.IsNull(new ResultadoTendencia { EntradasJson = "no es json" }.VentaBaseMaximos);
            Assert.AreEqual("NAC", new ResultadoTendencia { EntradasJson = "{\"tipoExclusiva\":\" NAC \"}" }.TipoExclusiva);
        }

        [TestMethod]
        public void Excel_TodosLosProductos_LlevaLaVentaBaseYDeDondeSale()
        {
            ResultadoTendencia r = Calcular(Producto("17404", ventas: BajonPuntual17404(), stock: 1000, diasEnServir: 10), NUEVA);
            ResultadoPropuestasStock resultado = new ResultadoPropuestasStock
            {
                Mes = MesReferencia,
                Parametros = NUEVA,
                Productos = new List<ResultadoTendencia> { r }
            };

            byte[] excel = new GeneradorExcelPropuestasStock().Generar(resultado);

            Dictionary<string, string> fila = Fila(excel, "Todos los productos", 3, 4);
            Assert.AreEqual("17404", fila["Producto"]);
            Assert.AreEqual("330", fila[GeneradorExcelPropuestasStock.COLUMNA_VENTA_BASE_MAXIMOS]);
            Assert.AreEqual("6 meses (a la baja)", fila[GeneradorExcelPropuestasStock.COLUMNA_ORIGEN_VENTA_BASE]);
        }

        /// <summary>Los valores de la fila <paramref name="filaDatos"/> por el nombre de su columna en la fila <paramref name="filaCabecera"/>.</summary>
        private static Dictionary<string, string> Fila(byte[] excel, string hoja, int filaCabecera, int filaDatos)
        {
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(new MemoryStream(excel), false))
            {
                Sheet sheet = doc.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Single(h => h.Name.Value == hoja);
                WorksheetPart parte = (WorksheetPart)doc.WorkbookPart.GetPartById(sheet.Id.Value);
                List<SharedStringItem> compartidas = doc.WorkbookPart.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().ToList()
                    ?? new List<SharedStringItem>();
                string Texto(Cell c)
                {
                    if (c.DataType != null && c.DataType.Value == CellValues.SharedString)
                    {
                        return compartidas[int.Parse(c.CellValue.Text)].InnerText;
                    }
                    return c.DataType != null && c.DataType.Value == CellValues.InlineString ? c.InnerText : c.CellValue?.Text;
                }
                string Columna(Cell c) => new string(c.CellReference.Value.TakeWhile(char.IsLetter).ToArray());
                List<Row> filas = parte.Worksheet.Descendants<Row>().ToList();
                Dictionary<string, string> datos = filas.Single(f => f.RowIndex.Value == filaDatos).Elements<Cell>().ToDictionary(Columna, Texto);
                Dictionary<string, string> resultado = new Dictionary<string, string>();
                foreach (Cell cabecera in filas.Single(f => f.RowIndex.Value == filaCabecera).Elements<Cell>())
                {
                    string nombre = Texto(cabecera);
                    if (nombre != null && !resultado.ContainsKey(nombre))
                    {
                        resultado[nombre] = datos.TryGetValue(Columna(cabecera), out string valor) ? valor : null;
                    }
                }
                return resultado;
            }
        }
    }
}
