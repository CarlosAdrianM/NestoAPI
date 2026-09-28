using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Tests de CARACTERIZACIÓN (Issue #547, corte a): los 18 productos reales del §3 del plan. La calculadora
    /// tiene que reproducir, al diezmilésimo, el Productos.PrecioMedio y el LinPedidoCmp.Coste de cada línea
    /// facturada que dejó el SP del domingo 27/09/26. Si el SP es raro (abs(), medias negativas...), el test
    /// fija lo raro: se arreglará después, cada fallo con su test en rojo primero.
    ///
    /// Las fixtures (JSON en Fixtures\) se regeneran con la consulta del §3 del plan, en solo lectura.
    /// OJO: representan el estado del 27/09; una compra facturada después cambia el coste real (ver 45396).
    /// </summary>
    [TestClass]
    public class CalculadoraPrecioMedioFixturesTests
    {
        private static void ComprobarQueCuadraConElSP(string producto)
        {
            FixturePrecioMedio fixture = FixturePrecioMedio.Cargar(producto);

            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(fixture.CrearHistorial());

            Assert.AreEqual(fixture.MotivoEsperado, resultado.Motivo, producto + ": " + fixture.Nota);
            if (fixture.MotivoEsperado != MotivoPrecioMedioNoProcesado.Ninguno)
            {
                Assert.IsFalse(resultado.Procesado, producto);
                Assert.IsNull(resultado.PrecioMedioFinal, producto + ": si no se procesa, se queda la media que tenga");
                Assert.AreEqual(0, resultado.CostesPorLinea.Count, producto + ": si no se procesa, no se toca ningún coste");
                Assert.AreEqual(0, resultado.Tramos.Count, producto);
                return;
            }

            Assert.IsTrue(resultado.Procesado, producto);
            Assert.AreEqual(fixture.PrecioMedioSP, resultado.PrecioMedioFinal, producto + ": Productos.PrecioMedio. " + fixture.Nota);

            List<string> distintas = new List<string>();
            foreach (LineaCompraPrecioMedio linea in fixture.Compras.Where(FixturePrecioMedio.EsLineaFacturada))
            {
                if (!resultado.CostesPorLinea.TryGetValue(linea.NumeroOrden, out decimal coste) || coste != linea.Coste)
                {
                    distintas.Add(string.Format("línea {0} ({1:dd/MM/yy HH:mm}): SP {2}, calculado {3}", linea.NumeroOrden,
                        linea.FechaAlbaran, linea.Coste, resultado.CostesPorLinea.ContainsKey(linea.NumeroOrden) ? coste.ToString() : "(sin calcular)"));
                }
            }
            Assert.AreEqual(0, distintas.Count, producto + ": LinPedidoCmp.Coste distinto en " + string.Join("; ", distintas));

            // Las líneas sin facturar no se reescriben (salvo que compartan factura y fecha con una facturada).
            foreach (LineaCompraPrecioMedio linea in fixture.Compras.Where(l => !l.NumeroFactura.HasValue))
            {
                Assert.IsFalse(resultado.CostesPorLinea.ContainsKey(linea.NumeroOrden), producto + ": línea sin facturar " + linea.NumeroOrden);
            }

            // Tramos: el primero desde el principio de los tiempos, el último hasta el fin y la media final.
            Assert.AreEqual(CalculadoraPrecioMedio.FechaPrincipioDeLosTiempos, resultado.Tramos.First().FechaDesde, producto);
            Assert.AreEqual(CalculadoraPrecioMedio.FechaFinDeLosTiempos, resultado.Tramos.Last().FechaHasta, producto);
            Assert.AreEqual(resultado.PrecioMedioFinal, resultado.Tramos.Last().PrecioMedio, producto);
        }

        [TestMethod]
        public void Producto45934_UnaSolaCompra_ReglaDePrimeraCompra()
        {
            ComprobarQueCuadraConElSP("45934");
        }

        [TestMethod]
        public void Producto38896_TreintaYCuatroComprasYTruncadoDeLaPrimera()
        {
            ComprobarQueCuadraConElSP("38896");
        }

        [TestMethod]
        public void Producto41281_PrimeraCompraACosteCeroAbonoYTruncado32_1662()
        {
            ComprobarQueCuadraConElSP("41281");
        }

        [TestMethod]
        public void Producto44904_MediaNegativaPorElAbsDelSP()
        {
            ComprobarQueCuadraConElSP("44904");

            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(FixturePrecioMedio.Cargar("44904").CrearHistorial());
            Assert.IsTrue(resultado.Avisos.Any(a => a.Tipo == TipoAvisoPrecioMedio.MediaNegativa));
        }

        [TestMethod]
        public void Producto34248_CantidadCeroStockNuloYAlbaranAnteriorConDosCostes()
        {
            ComprobarQueCuadraConElSP("34248");

            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(FixturePrecioMedio.Cargar("34248").CrearHistorial());
            Assert.IsTrue(resultado.Avisos.Any(a => a.Tipo == TipoAvisoPrecioMedio.AlbaranAnteriorConVariosCostes),
                "El albarán 82870 tiene líneas a 2,10 y 3,85: el SP cogió una cualquiera (3,85) y hay que avisarlo");
        }

        [TestMethod]
        public void Producto40985_DosFacturasALaMismaHoraYAbonos()
        {
            ComprobarQueCuadraConElSP("40985");

            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(FixturePrecioMedio.Cargar("40985").CrearHistorial());
            Assert.IsTrue(resultado.Avisos.Any(a => a.Tipo == TipoAvisoPrecioMedio.EmpateFacturasMismaFecha));
        }

        [TestMethod]
        public void Producto45396_DosLineasDeLaMismaFacturaSeSuman_EstadoDel27DeSeptiembre()
        {
            // La compra de 24 uds (base 125,92) se facturó el lunes 28/09, después del SP: en la fixture figura
            // como estaba el domingo (pendiente, sin albarán). Ver el test siguiente para lo que pasará.
            ComprobarQueCuadraConElSP("45396");
        }

        [TestMethod]
        public void Producto45396_ConLaCompraDelLunesFacturada_ElDomingoDara5_2478()
        {
            FixturePrecioMedio fixture = FixturePrecioMedio.Cargar("45396");
            HistorialPrecioMedio historial = fixture.CrearHistorial();
            LineaCompraPrecioMedio compraDelLunes = historial.Compras.Single(l => l.NumeroOrden == 141491851);
            compraDelLunes.Estado = 4;
            compraDelLunes.NumeroFactura = 88491;
            compraDelLunes.NumeroAlbaran = 125412;
            compraDelLunes.FechaAlbaran = new System.DateTime(2026, 9, 28, 8, 42, 22, 423);
            System.Func<System.DateTime, int> stockAnterior = historial.StockHasta;
            historial.StockHasta = f => f == compraDelLunes.FechaAlbaran.Value ? 37 : stockAnterior(f);

            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(historial);

            // (13 × 5,25 + 125,92) / 37 = 194,17 / 37 = 5,24783... → 5,2478 (truncado)
            Assert.AreEqual(5.2478m, resultado.PrecioMedioFinal);
            Assert.AreEqual(5.2478m, resultado.CostesPorLinea[141491851]);
        }

        [TestMethod]
        public void Producto17877_CompraEnLaEmpresaEspejoYAbonoTotalElMismoDia()
        {
            ComprobarQueCuadraConElSP("17877");
        }

        [TestMethod]
        public void Producto16137_PrimeraCompraEsUnAbonoYDenominadorCeroConservaLaMedia()
        {
            ComprobarQueCuadraConElSP("16137");
        }

        [TestMethod]
        public void Producto45188_AbonoEnOtraFechaYAlbaranSinFacturarPosterior()
        {
            ComprobarQueCuadraConElSP("45188");
        }

        [TestMethod]
        public void Producto44243_LineaPendienteSinFechaDeAlbaranSeIgnora()
        {
            ComprobarQueCuadraConElSP("44243");
        }

        [TestMethod]
        public void Producto43859_CompraACosteCeroHundeLaMediaYSeRecupera()
        {
            ComprobarQueCuadraConElSP("43859");
        }

        [TestMethod]
        public void Producto45039_KitConComprasPropiasSeTrataComoProductoNormal()
        {
            ComprobarQueCuadraConElSP("45039");
        }

        [TestMethod]
        public void Producto34929_ExcluidoPorMontajesNoSeProcesa()
        {
            ComprobarQueCuadraConElSP("34929");
        }

        [TestMethod]
        public void Producto37688_Empresa4SinEspejoDistinto()
        {
            ComprobarQueCuadraConElSP("37688");
        }

        [TestMethod]
        public void Producto40179_Empresa5()
        {
            ComprobarQueCuadraConElSP("40179");
        }

        [TestMethod]
        public void Producto45668_KitSinComprasNoSeProcesa()
        {
            ComprobarQueCuadraConElSP("45668");
        }

        [TestMethod]
        public void Producto47074_SinComprasNoSeProcesa()
        {
            ComprobarQueCuadraConElSP("47074");
        }
    }
}
