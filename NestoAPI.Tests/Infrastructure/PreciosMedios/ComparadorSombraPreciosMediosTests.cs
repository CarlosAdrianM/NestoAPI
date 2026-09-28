using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Issue #547, corte (b): comparación SIN tolerancia (al diezmilésimo) entre lo calculado y lo que dejó el SP.
    /// </summary>
    [TestClass]
    public class ComparadorSombraPreciosMediosTests
    {
        private static readonly DateTime F1 = new DateTime(2025, 1, 10, 10, 0, 0);
        private static readonly DateTime F2 = new DateTime(2025, 2, 10, 12, 30, 0);
        private static readonly DateTime CORTE = new DateTime(2026, 9, 27, 0, 30, 0);

        /// <summary>Dos compras: la 1 (F1) a 10,0000 y la 2 (F2) a 12,5000; tramos [1753, F2) = 10 y [F2, 9999) = 12,5.</summary>
        private static ResultadoPrecioMedio Calculado()
        {
            ResultadoPrecioMedio r = new ResultadoPrecioMedio { Empresa = "1", Producto = "P", Procesado = true, PrecioMedioFinal = 12.5m };
            r.CostesPorLinea[1] = 10m;
            r.CostesPorLinea[2] = 12.5m;
            r.Tramos.Add(new TramoPrecioMedio { FechaDesde = CalculadoraPrecioMedio.FechaPrincipioDeLosTiempos, FechaHasta = F2, PrecioMedio = 10m, NumeroFactura = 1, FechaAlbaran = F1 });
            r.Tramos.Add(new TramoPrecioMedio { FechaDesde = F2, FechaHasta = CalculadoraPrecioMedio.FechaFinDeLosTiempos, PrecioMedio = 12.5m, NumeroFactura = 2, FechaAlbaran = F2 });
            return r;
        }

        private static EstadoActualPrecioMedio Bd(decimal? media = 12.5m, decimal coste1 = 10m, decimal coste2 = 12.5m)
        {
            return new EstadoActualPrecioMedio
            {
                PrecioMedio = media,
                Compras = new List<LineaCompraBDPrecioMedio>
                {
                    new LineaCompraBDPrecioMedio { NumeroOrden = 2, FechaAlbaran = F2, Coste = coste2, Estado = 4 },
                    new LineaCompraBDPrecioMedio { NumeroOrden = 1, FechaAlbaran = F1, Coste = coste1, Estado = 4 }
                }
            };
        }

        [TestMethod]
        public void Comparar_TodoIgual_NoHayDiferencia()
        {
            DiferenciaPrecioMedio d = ComparadorSombraPreciosMedios.Comparar(Calculado(), Bd(), CORTE);

            Assert.IsFalse(d.HayDiferencia);
            Assert.IsTrue(d.MediaComparada);
            Assert.AreEqual(2, d.LineasComparadas);
            Assert.AreEqual(0, d.LineasDistintas);
            Assert.IsNull(d.PrimeraLineaDistinta);
            Assert.IsNull(d.VentasComparadas, "sin muestreo de ventas no se comparan");
        }

        [TestMethod]
        public void Comparar_UnDiezmilesimoEnLaMedia_EsDiferencia()
        {
            DiferenciaPrecioMedio d = ComparadorSombraPreciosMedios.Comparar(Calculado(), Bd(media: 12.5001m), CORTE);

            Assert.IsTrue(d.HayDiferencia);
            Assert.IsTrue(d.MediaDistinta);
            Assert.AreEqual(12.5001m, d.PrecioMedioBD);
            Assert.AreEqual(12.5m, d.PrecioMedioCalculado);
        }

        [TestMethod]
        public void Comparar_MediaNulaEnLaBD_EsDiferencia()
        {
            Assert.IsTrue(ComparadorSombraPreciosMedios.Comparar(Calculado(), Bd(media: null), CORTE).MediaDistinta);
        }

        [TestMethod]
        public void Comparar_LineasDistintas_SeñalaLaPrimeraPorFechaDeAlbaran()
        {
            DiferenciaPrecioMedio d = ComparadorSombraPreciosMedios.Comparar(Calculado(), Bd(coste1: 10.0001m, coste2: 12m), CORTE);

            Assert.IsTrue(d.HayDiferencia);
            Assert.IsFalse(d.MediaDistinta);
            Assert.AreEqual(2, d.LineasDistintas);
            Assert.AreEqual(1, d.PrimeraLineaDistinta, "la de F1 va antes aunque venga después en la lista de la BD");
            DetalleDiferenciaPrecioMedio detalle = d.Detalles.First();
            Assert.AreEqual(ComparadorSombraPreciosMedios.TABLA_COMPRA, detalle.Tabla);
            Assert.AreEqual(10.0001m, detalle.CosteBD);
            Assert.AreEqual(10m, detalle.CosteCalculado);
        }

        [TestMethod]
        public void Comparar_LineaQueNoEstaEnLaBD_EsDistinta()
        {
            EstadoActualPrecioMedio bd = Bd();
            bd.Compras.RemoveAll(l => l.NumeroOrden == 2);
            Assert.AreEqual(1, ComparadorSombraPreciosMedios.Comparar(Calculado(), bd, CORTE).LineasDistintas);
        }

        [TestMethod]
        public void Comparar_ProductoNoProcesado_NoSeCompara()
        {
            ResultadoPrecioMedio noProcesado = new ResultadoPrecioMedio { Procesado = false, Motivo = MotivoPrecioMedioNoProcesado.ExcluidoPorMontaje };

            DiferenciaPrecioMedio d = ComparadorSombraPreciosMedios.Comparar(noProcesado, Bd(media: 32m), CORTE);

            Assert.IsFalse(d.MediaComparada);
            Assert.IsFalse(d.HayDiferencia);
            Assert.AreEqual(32m, d.PrecioMedioBD);
        }

        [TestMethod]
        public void Comparar_Ventas_AlbaranesPorTramoYPendientesConLaMediaFinal()
        {
            EstadoActualPrecioMedio bd = Bd();
            bd.Ventas = new List<LineaVentaPrecioMedio>
            {
                Venta(1, 2, F1.AddDays(5), 10m),          // tramo 1
                Venta(2, 4, F2, 12.5m),                   // tramo 2 (desde incluido)
                Venta(3, 4, F2.AddDays(-1), 10m),         // tramo 1 (hasta excluido)
                Venta(4, -1, null, 12.5m),                // pendiente → media final
                Venta(5, 1, null, 12.5m),                 // en curso → media final
                Venta(6, 0, null, 99m),                   // estado 0: el SP no lo toca
                Venta(7, 2, null, 99m),                   // albarán sin fecha: el SP no lo toca
                Venta(8, -3, null, 99m)                   // presupuesto: no se toca
            };

            DiferenciaPrecioMedio d = ComparadorSombraPreciosMedios.Comparar(Calculado(), bd, CORTE);

            Assert.AreEqual(5, d.VentasComparadas);
            Assert.AreEqual(0, d.VentasDistintas);
            Assert.IsFalse(d.HayDiferencia);
        }

        [TestMethod]
        public void Comparar_VentaConCosteDeOtroTramo_EsDiferencia()
        {
            EstadoActualPrecioMedio bd = Bd();
            bd.Ventas = new List<LineaVentaPrecioMedio> { Venta(1, 2, F1.AddDays(5), 12.5m), Venta(2, 1, null, 10m) };

            DiferenciaPrecioMedio d = ComparadorSombraPreciosMedios.Comparar(Calculado(), bd, CORTE);

            Assert.IsTrue(d.HayDiferencia);
            Assert.AreEqual(2, d.VentasDistintas);
            Assert.AreEqual(ComparadorSombraPreciosMedios.TABLA_VENTA, d.Detalles.First().Tabla);
            Assert.AreEqual(10m, d.Detalles.First().CosteCalculado);
        }

        [TestMethod]
        public void Comparar_VentaModificadaDespuesDelCorte_NoSeCompara()
        {
            EstadoActualPrecioMedio bd = Bd();
            bd.Ventas = new List<LineaVentaPrecioMedio> { Venta(1, 1, null, 7m, modificada: CORTE.AddHours(8)) };

            DiferenciaPrecioMedio d = ComparadorSombraPreciosMedios.Comparar(Calculado(), bd, CORTE);

            Assert.AreEqual(0, d.VentasComparadas);
            Assert.AreEqual(1, d.VentasPosterioresAlCorte);
            Assert.IsFalse(d.HayDiferencia);
        }

        [TestMethod]
        public void Comparar_VentaEnUnEmpateDeTramos_CualquieraDeLosPreciosVale()
        {
            // Dos facturas a la misma hora: dos tramos idénticos con precios distintos; el UPDATE del SP coge uno cualquiera.
            ResultadoPrecioMedio calculado = Calculado();
            calculado.Tramos.Add(new TramoPrecioMedio { FechaDesde = F2, FechaHasta = CalculadoraPrecioMedio.FechaFinDeLosTiempos, PrecioMedio = 11m, NumeroFactura = 3, FechaAlbaran = F2 });
            EstadoActualPrecioMedio bd = Bd();
            bd.Ventas = new List<LineaVentaPrecioMedio> { Venta(1, 2, F2.AddDays(1), 12.5m), Venta(2, 2, F2.AddDays(2), 11m), Venta(3, 2, F2.AddDays(3), 5m) };

            DiferenciaPrecioMedio d = ComparadorSombraPreciosMedios.Comparar(calculado, bd, CORTE);

            Assert.AreEqual(3, d.VentasConEmpateDeTramos);
            Assert.AreEqual(1, d.VentasDistintas);
            Assert.AreEqual(11m, d.Detalles.Single().CosteCalculado, "el esperado es el del último tramo insertado (factura mayor)");
        }

        [TestMethod]
        public void Comparar_LimitaLosDetalles()
        {
            ResultadoPrecioMedio calculado = Calculado();
            EstadoActualPrecioMedio bd = Bd();
            bd.Ventas = Enumerable.Range(1, 80).Select(i => Venta(i, 1, null, 1m)).ToList();

            DiferenciaPrecioMedio d = ComparadorSombraPreciosMedios.Comparar(calculado, bd, CORTE);

            Assert.AreEqual(80, d.VentasDistintas);
            Assert.AreEqual(DiferenciaPrecioMedio.MAXIMO_DETALLES, d.Detalles.Count);
        }

        private static LineaVentaPrecioMedio Venta(int orden, short estado, DateTime? fechaAlbaran, decimal coste, DateTime? modificada = null)
        {
            return new LineaVentaPrecioMedio
            {
                Empresa = "1",
                Numero = 1000 + orden,
                NumeroOrden = orden,
                Estado = estado,
                FechaAlbaran = fechaAlbaran,
                Coste = coste,
                FechaModificacion = modificada ?? CORTE.AddDays(-10)
            };
        }
    }
}
