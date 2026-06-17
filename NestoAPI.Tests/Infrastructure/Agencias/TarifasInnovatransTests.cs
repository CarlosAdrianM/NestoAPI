using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Verifica el porte a NestoAPI de las tarifas Innovatrans (oferta Paquetería 2026). Los
    /// precios son ANTES de fuel (el 2,5% lo aplica el comparador desde AgenciasTransporte).
    /// Si cambia la oferta, actualizar estos valores junto con la tarifa.
    /// </summary>
    [TestClass]
    public class TarifasInnovatransTests
    {
        private static decimal Precio(ITarifaAgencia t, ZonasEnvioAgencia zona, decimal pesoMax)
            => t.CosteEnvio.Single(c => c.Zona == zona && c.PesoMaximo == pesoMax).Precio;

        [TestMethod]
        public void Economy_EstructuraYPreciosClave()
        {
            var t = new TarifaInnovatransEconomy();

            Assert.AreEqual(12, t.AgenciaId);
            Assert.AreEqual((byte)1, t.ServicioId);
            Assert.AreEqual("Economy", t.NombreServicio);
            Assert.AreEqual(0.17m, t.CosteKiloAdicional(ZonasEnvioAgencia.Provincial));
            Assert.AreEqual(0.44m, t.CosteKiloAdicional(ZonasEnvioAgencia.Peninsular));
            Assert.AreEqual(decimal.MaxValue, t.CosteKiloAdicional(ZonasEnvioAgencia.Portugal));

            Assert.AreEqual(3.94m, Precio(t, ZonasEnvioAgencia.Provincial, 2m));
            Assert.AreEqual(18.69m, Precio(t, ZonasEnvioAgencia.Provincial, 100m));
            Assert.AreEqual(4.53m, Precio(t, ZonasEnvioAgencia.Peninsular, 5m));
            Assert.AreEqual(43.12m, Precio(t, ZonasEnvioAgencia.Peninsular, 100m));
        }

        [TestMethod]
        public void Reembolso_5PorCiento_ConMinimoYMaximo()
        {
            var t = new TarifaInnovatransEconomy();

            Assert.AreEqual(5.00m, t.CosteReembolso(100m));   // 5% de 100
            Assert.AreEqual(4.03m, t.CosteReembolso(50m));    // 2,50 < mínimo 4,03
            Assert.AreEqual(300m, t.CosteReembolso(10000m));  // 500 > máximo 300
        }

        [TestMethod]
        public void Portugal_EstructuraYPreciosClave()
        {
            var t = new TarifaInnovatransPortugal();

            Assert.AreEqual(12, t.AgenciaId);
            Assert.AreEqual((byte)2, t.ServicioId);
            Assert.AreEqual("14H Portugal", t.NombreServicio);
            Assert.AreEqual(0.65m, t.CosteKiloAdicional(ZonasEnvioAgencia.Portugal));

            Assert.AreEqual(7.42m, Precio(t, ZonasEnvioAgencia.Portugal, 2m));
            Assert.AreEqual(32.61m, Precio(t, ZonasEnvioAgencia.Portugal, 50m));
        }

        [TestMethod]
        public void Maritimo_EstructuraYPreciosClave()
        {
            var t = new TarifaInnovatransMaritimo();

            Assert.AreEqual(12, t.AgenciaId);
            Assert.AreEqual((byte)3, t.ServicioId);
            Assert.AreEqual("Marítimo islas", t.NombreServicio);
            Assert.AreEqual(2.59m, t.CosteKiloAdicional(ZonasEnvioAgencia.CanariasMenores));

            Assert.AreEqual(8.47m, Precio(t, ZonasEnvioAgencia.BalearesMayores, 5m));
            // Canarias incorpora el despacho fijo (18,03 origen + 25 destino = 43,03).
            Assert.AreEqual(58.39m, Precio(t, ZonasEnvioAgencia.CanariasMayores, 10m));
            Assert.AreEqual(73.94m, Precio(t, ZonasEnvioAgencia.CanariasMenores, 10m));
        }
    }
}
