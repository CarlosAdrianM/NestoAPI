using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Tests sintéticos de cada regla del SP de precios medios (Issue #547, §1). Fijan el comportamiento
    /// ACTUAL del SP, también el que parece un fallo (abs(), regla (e), albarán sin facturar como media anterior,
    /// primera compra que ignora el stock): cuando se decida arreglar uno, su test se cambia primero a rojo.
    /// </summary>
    [TestClass]
    public class CalculadoraPrecioMedioReglasTests
    {
        private static readonly DateTime Dia1 = new DateTime(2026, 1, 10, 10, 0, 0);
        private static readonly DateTime Dia2 = new DateTime(2026, 2, 10, 10, 0, 0);
        private static readonly DateTime Dia3 = new DateTime(2026, 3, 10, 10, 0, 0);
        private static readonly DateTime Dia4 = new DateTime(2026, 4, 10, 10, 0, 0);

        private static LineaCompraPrecioMedio Linea(int orden, DateTime? fecha, int? factura, int cantidad, decimal baseImponible,
            short estado = 4, int? albaran = null, decimal? coste = null)
        {
            return new LineaCompraPrecioMedio
            {
                Empresa = "1",
                NumeroOrden = orden,
                FechaAlbaran = fecha,
                NumeroFactura = factura,
                NumeroAlbaran = albaran ?? (fecha.HasValue ? orden : (int?)null),
                Cantidad = cantidad,
                BaseImponible = baseImponible,
                Estado = estado,
                Coste = coste
            };
        }

        private static HistorialPrecioMedio Historial(Dictionary<DateTime, int> stock, params LineaCompraPrecioMedio[] lineas)
        {
            return new HistorialPrecioMedio
            {
                Empresa = "1",
                EmpresaEspejo = "3",
                Producto = "12345",
                Compras = lineas.ToList(),
                StockHasta = f => stock.TryGetValue(f, out int s) ? s : 0
            };
        }

        #region Fórmula (§1.3.5)

        [TestMethod]
        public void ReglaA_PonderaElStockPrevioConLaMediaAnterior()
        {
            // stock previo 10 a 10 € + 5 uds por 60 € → 160 / 15 = 10,6666 (truncado)
            Assert.AreEqual(10.6666m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(15, 5, 60m, 10m, true));
        }

        [TestMethod]
        public void ReglaA_ElStockPrevioNegativoSePonderaComoPositivo_FalloConocidoDelAbs()
        {
            // stock 2 tras comprar 5 → previo −3, pero el SP usa |−3| = 3: (3 × 10 + 60) / 8 = 11,25.
            // (Lo razonable sería no ponderar un stock negativo; se replica tal cual, §1.5.1.)
            Assert.AreEqual(11.25m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(2, 5, 60m, 10m, true));
        }

        [TestMethod]
        public void ReglaA_UnAbonoConStockBajoPuedeDarMediaNegativa()
        {
            // 44904: abono de 4 a 3,00 con media 1,0909 y stock 6 → (10 × 1,0909 − 12) / 6 = −0,1818
            Assert.AreEqual(-0.1818m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(6, -4, -12m, 1.0909m, true));
        }

        [TestMethod]
        public void ReglaB_PrimeraCompraIgnoraElStockPrevio()
        {
            // 17877: stock previo 8 (sin compras anteriores) + 1 ud a 6,32 → 6,32, no (8 × 0 + 6,32) / 9
            Assert.AreEqual(6.32m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(9, 1, 6.32m, 0m, false));
        }

        [TestMethod]
        public void ReglaB_PrimeraCompraQueEsUnAbonoDividePorLaCantidadNegativa()
        {
            // 16137: −7 uds por −76,45 → 10,9214
            Assert.AreEqual(10.9214m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(9, -7, -76.45m, 0m, false));
        }

        [TestMethod]
        public void ReglaC_DenominadorCeroConservaLaMediaAnterior()
        {
            // 16137 el 26/08/04: abono de 4 que deja el stock a 0 → d = |0 + 4| − 4 = 0 → conserva 10,9912
            Assert.AreEqual(10.9912m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(0, -4, -47.73m, 10.9912m, true));
        }

        [TestMethod]
        public void ReglaC_DenominadorCeroSinMediaAnteriorDaCero()
        {
            Assert.AreEqual(0m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(0, -4, -47.73m, 0m, true));
        }

        [TestMethod]
        public void ReglaD_SinStockPrevioLaMediaEsElPrecioDeLaCompra()
        {
            // stock − cantidad = 0: la media anterior no pesa
            Assert.AreEqual(5m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(4, 4, 20m, 10m, true));
        }

        [TestMethod]
        public void ReglaE_CantidadCeroConStockCeroSumaLaBaseALaMedia_FalloConocido()
        {
            // §1.5.2: una línea de cantidad 0 con base 3 y stock 0 suma los 3 € enteros a la media
            Assert.AreEqual(13m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(0, 0, 3m, 10m, true));
        }

        [TestMethod]
        public void CantidadCeroConStockNoCambiaLaMedia()
        {
            // 34248: línea de cantidad 0 y base 0 con stock 6 → (6 × 3,675 + 0) / 6
            Assert.AreEqual(3.675m, CalculadoraPrecioMedio.CalcularNuevoPrecioMedio(6, 0, 0m, 3.675m, true));
        }

        #endregion

        #region Qué productos se procesan (§1.1)

        [TestMethod]
        public void SinLineasDeCompra_NoSeProcesa()
        {
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(new Dictionary<DateTime, int>()));

            Assert.IsFalse(resultado.Procesado);
            Assert.AreEqual(MotivoPrecioMedioNoProcesado.SinCompras, resultado.Motivo);
            Assert.IsNull(resultado.PrecioMedioFinal);
        }

        [TestMethod]
        public void SoloLineasPendientesSinFechaDeAlbaran_NoSeProcesa()
        {
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(new Dictionary<DateTime, int>(),
                Linea(1, null, null, 4, 40m, estado: 1)));

            Assert.AreEqual(MotivoPrecioMedioNoProcesado.SinCompras, resultado.Motivo);
        }

        [TestMethod]
        public void PrimeraFechaSinNingunaLineaFacturada_NoSeProcesaAunqueHayaComprasPosteriores()
        {
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 5 }, { Dia2, 10 } },
                Linea(1, Dia1, null, 5, 50m, estado: 2),
                Linea(2, Dia2, 100, 5, 60m)));

            Assert.IsFalse(resultado.Procesado);
            Assert.AreEqual(MotivoPrecioMedioNoProcesado.PrimeraCompraSinFacturar, resultado.Motivo);
        }

        [TestMethod]
        public void ProductoFicticio_NoSeProcesa()
        {
            HistorialPrecioMedio historial = Historial(new Dictionary<DateTime, int> { { Dia1, 5 } }, Linea(1, Dia1, 100, 5, 50m));
            historial.Ficticio = true;

            Assert.AreEqual(MotivoPrecioMedioNoProcesado.Ficticio, CalculadoraPrecioMedio.Calcular(historial).Motivo);
        }

        [TestMethod]
        public void MontajePosteriorALaUltimaRecepcion_NoSeProcesa()
        {
            HistorialPrecioMedio historial = Historial(new Dictionary<DateTime, int> { { Dia1, 5 } }, Linea(1, Dia1, 100, 5, 50m));
            historial.FechaUltimaRecepcion = Dia1;
            historial.FechaUltimoMontaje = Dia2;

            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(historial);

            Assert.IsFalse(resultado.Procesado);
            Assert.AreEqual(MotivoPrecioMedioNoProcesado.ExcluidoPorMontaje, resultado.Motivo);
            Assert.AreEqual(0, resultado.CostesPorLinea.Count);
        }

        [TestMethod]
        public void MontajeAnteriorALaUltimaRecepcion_SeProcesa()
        {
            HistorialPrecioMedio historial = Historial(new Dictionary<DateTime, int> { { Dia2, 5 } }, Linea(1, Dia2, 100, 5, 50m));
            historial.FechaUltimoMontaje = Dia1;
            historial.FechaUltimaRecepcion = Dia2;

            Assert.IsTrue(CalculadoraPrecioMedio.Calcular(historial).Procesado);
        }

        [TestMethod]
        public void MontajeSinNingunaRecepcion_SeComparaCon1990()
        {
            // #Montajes.Albaran tiene default('01/01/1990'): sin recepciones, un montaje posterior a 1990 excluye.
            HistorialPrecioMedio historial = Historial(new Dictionary<DateTime, int> { { Dia1, 5 } }, Linea(1, Dia1, 100, 5, 50m));
            historial.FechaUltimoMontaje = new DateTime(1990, 1, 2);
            Assert.AreEqual(MotivoPrecioMedioNoProcesado.ExcluidoPorMontaje, CalculadoraPrecioMedio.Calcular(historial).Motivo);

            historial.FechaUltimoMontaje = new DateTime(1989, 12, 31);
            Assert.IsTrue(CalculadoraPrecioMedio.Calcular(historial).Procesado);
        }

        [TestMethod]
        public void CodigoDeMasDeDiezCaracteres_NoSeProcesaYSeAvisa()
        {
            // prdLLamarActualizarPrecioMedioProducto recibe @Producto char(10): lo truncaría (§1.5.7).
            HistorialPrecioMedio historial = Historial(new Dictionary<DateTime, int> { { Dia1, 5 } }, Linea(1, Dia1, 100, 5, 50m));
            historial.Producto = "12345678901";

            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(historial);

            Assert.AreEqual(MotivoPrecioMedioNoProcesado.CodigoDemasiadoLargo, resultado.Motivo);
            Assert.IsTrue(resultado.Avisos.Any(a => a.Tipo == TipoAvisoPrecioMedio.CodigoDemasiadoLargo));
        }

        #endregion

        #region Recorrido de las compras (§1.2 y §1.3)

        [TestMethod]
        public void VariasCompras_EncadenaLaMediaYEscribeElCosteDeCadaLinea()
        {
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 10 }, { Dia2, 15 } },
                Linea(1, Dia1, 100, 10, 100m),   // primera: 10
                Linea(2, Dia2, 101, 5, 60m)));   // (10 × 10 + 60) / 15 = 10,6666

            Assert.AreEqual(10m, resultado.CostesPorLinea[1]);
            Assert.AreEqual(10.6666m, resultado.CostesPorLinea[2]);
            Assert.AreEqual(10.6666m, resultado.PrecioMedioFinal);
        }

        [TestMethod]
        public void DosLineasDeLaMismaFacturaYFecha_SeSumanYLlevanElMismoCoste()
        {
            // 45396: 7 + 8 uds de la misma factura a la misma hora
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 15 } },
                Linea(1, Dia1, 100, 7, 36.75m, albaran: 50),
                Linea(2, Dia1, 100, 8, 42m, albaran: 50)));

            Assert.AreEqual(5.25m, resultado.CostesPorLinea[1]);
            Assert.AreEqual(5.25m, resultado.CostesPorLinea[2]);
            Assert.AreEqual(1, resultado.Tramos.Count);
        }

        [TestMethod]
        public void MismaFacturaEnDosFechas_SeCalculaCadaFechaPorSeparado()
        {
            // 17877: compra de 7 y abono de 7 de la misma factura a distintas horas
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 8 }, { Dia2, 8 }, { Dia3, 1 } },
                Linea(1, Dia1, 99, 1, 6.32m),
                Linea(2, Dia2, 100, 7, 51.31m),
                Linea(3, Dia3, 100, -7, -51.31m)));

            Assert.AreEqual(7.2037m, resultado.CostesPorLinea[2]);
            Assert.AreEqual(6.3196m, resultado.CostesPorLinea[3]);
        }

        [TestMethod]
        public void StockSinApuntesALaFecha_CuentaComoCero()
        {
            // 34248: líneas fechadas a las 00:00:00 cuyo apunte real lleva hora → sum() NULL → 0
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 1 } },
                Linea(1, Dia1, 100, 1, 4.2m),
                Linea(2, Dia2, 101, 1, 0m)));    // stock 0: (|0 − 1| × 4,2 + 0) / 2 = 2,1

            Assert.AreEqual(2.1m, resultado.CostesPorLinea[2]);
        }

        [TestMethod]
        public void ElCosteDeEntradaDeLasLineasFacturadasNoInfluye()
        {
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 10 }, { Dia2, 15 } },
                Linea(1, Dia1, 100, 10, 100m, coste: 777m),
                Linea(2, Dia2, 101, 5, 60m, coste: 888m)));

            Assert.AreEqual(10.6666m, resultado.PrecioMedioFinal);
        }

        [TestMethod]
        public void AlbaranAnteriorSinFacturar_SuCosteSembradoHaceDeMediaAnterior_SeReplicaYSeAvisa()
        {
            // §1.5.4 (decisión 6 de Carlos: se replica). El albarán sin facturar del Dia2 lleva sembrado 20 €.
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 10 }, { Dia2, 15 }, { Dia3, 20 } },
                Linea(1, Dia1, 100, 10, 100m),
                Linea(2, Dia2, null, 5, 60m, estado: 2, coste: 20m),
                Linea(3, Dia3, 101, 5, 50m)));   // (15 × 20 + 50) / 20 = 17,5 (no 10 de la última facturada)

            Assert.AreEqual(17.5m, resultado.PrecioMedioFinal);
            Assert.IsFalse(resultado.CostesPorLinea.ContainsKey(2), "El albarán sin facturar no se reescribe");
            Assert.IsTrue(resultado.Avisos.Any(a => a.Tipo == TipoAvisoPrecioMedio.AlbaranAnteriorSinFacturar));
        }

        [TestMethod]
        public void AlbaranAnteriorSinCoste_CuentaComoCero()
        {
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 10 }, { Dia2, 15 }, { Dia3, 20 } },
                Linea(1, Dia1, 100, 10, 100m),
                Linea(2, Dia2, null, 5, 60m, estado: 2, coste: null),
                Linea(3, Dia3, 101, 5, 50m)));   // isnull(Coste, 0): (15 × 0 + 50) / 20 = 2,5

            Assert.AreEqual(2.5m, resultado.PrecioMedioFinal);
        }

        [TestMethod]
        public void AlbaranAnteriorConVariosCostes_UsaElDeLaLineaMasRecienteYAvisa()
        {
            // 34248: el albarán 82870 tiene dos líneas (misma factura, dos horas) con costes 2,10 y 3,85
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 1 }, { Dia2, 0 }, { Dia3, 6 }, { Dia4, 0 } },
                Linea(1, Dia1, 100, 1, 4.2m),
                Linea(2, Dia2, 101, 1, 0m, albaran: 500),     // 2,10
                Linea(3, Dia3, 101, 5, 21m, albaran: 500),    // (1 × 2,1 + 21) / 6 = 3,85
                Linea(4, Dia4, 102, 6, 21m)));                // (6 × 3,85 + 21) / 12 = 3,675

            Assert.AreEqual(3.675m, resultado.PrecioMedioFinal);
            Assert.IsTrue(resultado.Avisos.Any(a => a.Tipo == TipoAvisoPrecioMedio.AlbaranAnteriorConVariosCostes));
        }

        [TestMethod]
        public void DosFacturasALaMismaHora_SeDesempataPorNumeroDeFacturaYSeAvisa()
        {
            // El SP las procesa en orden indeterminado y la última pisa a la otra. Aquí: la de mayor nº, siempre.
            Dictionary<DateTime, int> stock = new Dictionary<DateTime, int> { { Dia1, 10 }, { Dia2, 20 }, { Dia3, 25 } };
            LineaCompraPrecioMedio[] lineas =
            {
                Linea(1, Dia1, 100, 10, 100m),                 // 10
                Linea(3, Dia2, 202, 5, 100m, albaran: 71),     // (15 × 10 + 100) / 20 = 12,5
                Linea(2, Dia2, 201, 5, 50m, albaran: 70),      // (15 × 10 + 50) / 20 = 10
                Linea(4, Dia3, 300, 5, 50m)                    // (20 × 12,5 + 50) / 25 = 12
            };

            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(stock, lineas));
            ResultadoPrecioMedio alReves = CalculadoraPrecioMedio.Calcular(Historial(stock, lineas.Reverse().ToArray()));

            Assert.AreEqual(10m, resultado.CostesPorLinea[2]);
            Assert.AreEqual(12.5m, resultado.CostesPorLinea[3]);
            Assert.AreEqual(12m, resultado.PrecioMedioFinal, "La media anterior de la compra siguiente es la de la factura 202");
            Assert.AreEqual(resultado.PrecioMedioFinal, alReves.PrecioMedioFinal, "Determinista: no depende del orden de entrada");
            Assert.IsTrue(resultado.Avisos.Any(a => a.Tipo == TipoAvisoPrecioMedio.EmpateFacturasMismaFecha));
            Assert.IsTrue(resultado.Avisos.Any(a => a.Tipo == TipoAvisoPrecioMedio.EmpateAlbaranAnterior));
        }

        [TestMethod]
        public void UltimaCompraDeLaMismaHoraQueOtra_LaMediaFinalEsLaDeLaFacturaMayor()
        {
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 10 }, { Dia2, 20 } },
                Linea(1, Dia1, 100, 10, 100m),
                Linea(2, Dia2, 202, 5, 100m),   // 12,5
                Linea(3, Dia2, 201, 5, 50m)));  // 10

            Assert.AreEqual(12.5m, resultado.PrecioMedioFinal);
        }

        [TestMethod]
        public void LineaConFacturaPeroNoFacturada_NoCalculaNada()
        {
            // El SP solo suma Estado = 4: una llamada sin líneas facturadas no hace nada.
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 10 }, { Dia2, 15 } },
                Linea(1, Dia1, 100, 10, 100m),
                Linea(2, Dia2, 101, 5, 60m, estado: 3)));

            Assert.AreEqual(10m, resultado.PrecioMedioFinal);
            Assert.IsFalse(resultado.CostesPorLinea.ContainsKey(2));
            Assert.AreEqual(1, resultado.Tramos.Count);
        }

        #endregion

        #region Tramos de ##global (§1.3.2-3)

        [TestMethod]
        public void Tramos_ElPrimeroDesdeElPrincipioYCadaUnoHastaLaSiguienteFechaFacturada()
        {
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 10 }, { Dia3, 15 }, { Dia4, 20 } },
                Linea(1, Dia1, 100, 10, 100m),
                Linea(2, Dia2, null, 5, 50m, estado: 2, coste: 10m),   // sin facturar: no corta los tramos
                Linea(3, Dia3, 101, 5, 50m),
                Linea(4, Dia4, 102, 5, 50m)));

            Assert.AreEqual(3, resultado.Tramos.Count);
            Assert.AreEqual(CalculadoraPrecioMedio.FechaPrincipioDeLosTiempos, resultado.Tramos[0].FechaDesde);
            Assert.AreEqual(Dia3, resultado.Tramos[0].FechaHasta, "Hasta la siguiente fecha FACTURADA, no la del albarán sin facturar");
            Assert.AreEqual(Dia3, resultado.Tramos[1].FechaDesde);
            Assert.AreEqual(Dia4, resultado.Tramos[1].FechaHasta);
            Assert.AreEqual(Dia4, resultado.Tramos[2].FechaDesde);
            Assert.AreEqual(CalculadoraPrecioMedio.FechaFinDeLosTiempos, resultado.Tramos[2].FechaHasta);
            Assert.AreEqual(resultado.PrecioMedioFinal, resultado.Tramos[2].PrecioMedio);
        }

        [TestMethod]
        public void Tramos_ElPrincipioYElFinSonLosDelSP()
        {
            // -53690 y 2958463 como datetime de SQL Server
            Assert.AreEqual(new DateTime(1900, 1, 1).AddDays(-53690), CalculadoraPrecioMedio.FechaPrincipioDeLosTiempos);
            Assert.AreEqual(new DateTime(1900, 1, 1).AddDays(2958463), CalculadoraPrecioMedio.FechaFinDeLosTiempos);
        }

        [TestMethod]
        public void Tramos_UnAlbaranAnteriorSinFacturarHaceQueLaPrimeraFacturaNoEmpieceEnElPrincipio()
        {
            // El SP busca el «albarán anterior» en cualquier estado; si lo hay, FechaDesde = fecha de la compra.
            ResultadoPrecioMedio resultado = CalculadoraPrecioMedio.Calcular(Historial(
                new Dictionary<DateTime, int> { { Dia1, 5 }, { Dia2, 10 } },
                Linea(1, Dia1, 100, 5, 50m),
                Linea(0, new DateTime(2025, 12, 1), 99, 5, 40m, estado: 2, coste: 8m)));
            // La primera fecha (01/12/25) tiene factura, pero la línea está en estado 2: no calcula; la del Dia1 sí.

            Assert.AreEqual(1, resultado.Tramos.Count);
            Assert.AreEqual(Dia1, resultado.Tramos[0].FechaDesde);
        }

        #endregion
    }
}
