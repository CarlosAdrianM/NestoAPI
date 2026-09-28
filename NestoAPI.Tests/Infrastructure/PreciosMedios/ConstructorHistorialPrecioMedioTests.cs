using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>
    /// Issue #547, corte (b): de lo leído de la BD al historial de la calculadora (stock a una fecha, regla de
    /// montajes y facturas posteriores a la pasada del SP).
    /// </summary>
    [TestClass]
    public class ConstructorHistorialPrecioMedioTests
    {
        private static readonly DateTime F1 = new DateTime(2025, 1, 10, 10, 0, 0);
        private static readonly DateTime F2 = new DateTime(2025, 2, 10, 12, 30, 0);
        private static readonly DateTime F3 = new DateTime(2025, 3, 10, 9, 15, 0);

        [TestMethod]
        public void StockHasta_SinMovimientos_EsCero()
        {
            Func<DateTime, int> stock = ConstructorHistorialPrecioMedio.CrearStockHasta(new List<MovimientoStockPrecioMedio>());
            Assert.AreEqual(0, stock(F1));
        }

        [TestMethod]
        public void StockHasta_SumaHastaLaFechaIncluidaConHora()
        {
            Func<DateTime, int> stock = ConstructorHistorialPrecioMedio.CrearStockHasta(new List<MovimientoStockPrecioMedio>
            {
                new MovimientoStockPrecioMedio { Fecha = F2, Cantidad = -3 },
                new MovimientoStockPrecioMedio { Fecha = F1, Cantidad = 10 },
                new MovimientoStockPrecioMedio { Fecha = F3, Cantidad = 5 }
            });

            Assert.AreEqual(0, stock(F1.AddSeconds(-1)), "antes del primer apunte no hay filas: 0");
            Assert.AreEqual(10, stock(F1), "<=: incluye el apunte del mismo instante");
            Assert.AreEqual(10, stock(F2.AddMilliseconds(-3)));
            Assert.AreEqual(7, stock(F2));
            Assert.AreEqual(12, stock(F3));
            Assert.AreEqual(12, stock(new DateTime(2030, 1, 1)));
        }

        [TestMethod]
        public void StockHasta_ApuntesDelMismoInstanteSeSuman()
        {
            Func<DateTime, int> stock = ConstructorHistorialPrecioMedio.CrearStockHasta(new List<MovimientoStockPrecioMedio>
            {
                new MovimientoStockPrecioMedio { Fecha = F1, Cantidad = 10 },
                new MovimientoStockPrecioMedio { Fecha = F1, Cantidad = -4 }
            });
            Assert.AreEqual(6, stock(F1));
        }

        [TestMethod]
        public void Construir_FechasDeMontajeYRecepcionSonLasUltimas()
        {
            DatosProductoPrecioMedio datos = Datos();
            datos.Movimientos = new List<MovimientoStockPrecioMedio>
            {
                new MovimientoStockPrecioMedio { Fecha = F1, Cantidad = 10, EsRecepcion = true },
                new MovimientoStockPrecioMedio { Fecha = F2, Cantidad = 2, EsMontaje = true },
                new MovimientoStockPrecioMedio { Fecha = F3, Cantidad = 1, EsRecepcion = true }
            };

            HistorialPrecioMedio historial = ConstructorHistorialPrecioMedio.Construir(datos);

            Assert.AreEqual(F2, historial.FechaUltimoMontaje);
            Assert.AreEqual(F3, historial.FechaUltimaRecepcion);
        }

        [TestMethod]
        public void Construir_SinMontajesNiRecepciones_FechasNulas()
        {
            HistorialPrecioMedio historial = ConstructorHistorialPrecioMedio.Construir(Datos());
            Assert.IsNull(historial.FechaUltimoMontaje);
            Assert.IsNull(historial.FechaUltimaRecepcion);
        }

        [TestMethod]
        public void Construir_CopiaLasLineasYConvierteLosNulos()
        {
            DatosProductoPrecioMedio datos = Datos();
            datos.Ficha = new FichaProductoPrecioMedio { PrecioMedio = 3m, Ficticio = true };
            datos.Compras.Add(new LineaCompraBDPrecioMedio { Empresa = "1  ", NumeroOrden = 7, Cantidad = null, BaseImponible = null, Estado = null });

            HistorialPrecioMedio historial = ConstructorHistorialPrecioMedio.Construir(datos);

            Assert.IsTrue(historial.Ficticio);
            Assert.AreEqual("41281", historial.Producto);
            Assert.AreEqual("3", historial.EmpresaEspejo);
            LineaCompraPrecioMedio linea = historial.Compras.Single();
            Assert.AreEqual("1", linea.Empresa);
            Assert.AreEqual(0, linea.Cantidad);
            Assert.AreEqual(0m, linea.BaseImponible);
            Assert.AreEqual((short)0, linea.Estado);
        }

        [TestMethod]
        public void Construir_ConCorte_LasFacturasPosterioresSeTratanComoAlbaranSinFacturar()
        {
            DateTime corte = new DateTime(2026, 9, 27, 0, 30, 0);
            DatosProductoPrecioMedio datos = Datos();
            datos.Compras.Add(Compra(1, 100, F1, estado: 4, modificacionFactura: corte.AddDays(-30)));
            datos.Compras.Add(Compra(2, 200, F2, estado: 4, modificacionFactura: corte.AddHours(9)));
            datos.Compras.Add(Compra(3, null, F3, estado: 2, modificacionFactura: null));

            HistorialPrecioMedio historialSP = ConstructorHistorialPrecioMedio.Construir(datos, corte, out int posteriores);
            HistorialPrecioMedio historialActual = ConstructorHistorialPrecioMedio.Construir(datos);

            Assert.AreEqual(1, posteriores);
            LineaCompraPrecioMedio desfacturada = historialSP.Compras.Single(l => l.NumeroOrden == 2);
            Assert.IsNull(desfacturada.NumeroFactura);
            Assert.AreEqual((short)2, desfacturada.Estado);
            Assert.AreEqual(100, historialSP.Compras.Single(l => l.NumeroOrden == 1).NumeroFactura, "las anteriores al corte no se tocan");
            Assert.AreEqual(200, historialActual.Compras.Single(l => l.NumeroOrden == 2).NumeroFactura, "sin corte, el historial es el actual");
            Assert.AreEqual((short)4, historialActual.Compras.Single(l => l.NumeroOrden == 2).Estado);
        }

        [TestMethod]
        public void EsFacturaPosteriorAlCorte_ElMismoInstanteCuentaComoPosterior()
        {
            DateTime corte = new DateTime(2026, 9, 27, 0, 30, 0);
            Assert.IsTrue(ConstructorHistorialPrecioMedio.EsFacturaPosteriorAlCorte(Compra(1, 5, F1, 4, corte), corte));
            Assert.IsFalse(ConstructorHistorialPrecioMedio.EsFacturaPosteriorAlCorte(Compra(1, 5, F1, 4, corte.AddSeconds(-1)), corte));
            Assert.IsFalse(ConstructorHistorialPrecioMedio.EsFacturaPosteriorAlCorte(Compra(1, 5, F1, 2, corte.AddDays(1)), corte),
                "una línea no facturada no es una factura posterior");
            Assert.IsFalse(ConstructorHistorialPrecioMedio.EsFacturaPosteriorAlCorte(Compra(1, 5, F1, 4, null), corte),
                "sin cabecera de factura no se sabe: se deja como está");
        }

        internal static DatosProductoPrecioMedio Datos()
        {
            return new DatosProductoPrecioMedio
            {
                Empresa = "1",
                EmpresaEspejo = "3",
                Producto = "41281",
                Ficha = new FichaProductoPrecioMedio { PrecioMedio = 0m, Ficticio = false }
            };
        }

        internal static LineaCompraBDPrecioMedio Compra(int orden, int? factura, DateTime fecha, short estado, DateTime? modificacionFactura,
            int cantidad = 10, decimal baseImponible = 100m, decimal coste = 0m, int? albaran = null)
        {
            return new LineaCompraBDPrecioMedio
            {
                Empresa = "1",
                NumeroOrden = orden,
                NumeroFactura = factura,
                FechaAlbaran = fecha,
                NumeroAlbaran = albaran ?? orden * 10,
                Cantidad = cantidad,
                BaseImponible = baseImponible,
                Coste = coste,
                Estado = estado,
                FechaModificacionFactura = modificacionFactura
            };
        }
    }
}
