using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Verifica el porte a NestoAPI de las tarifas GLS/ASM 2026 (estructura + spot-check de precios).
    /// Si cambia la tarifa anual, actualizar estos valores junto con la tarifa.
    /// </summary>
    [TestClass]
    public class TarifasGLSTests
    {
        private static decimal Precio(ITarifaAgencia t, ZonasEnvioAgencia zona, decimal pesoMax)
            => t.CosteEnvio.Single(c => c.Zona == zona && c.PesoMaximo == pesoMax).Precio;

        [TestMethod]
        public void BusinessParcel_EstructuraYPreciosClave()
        {
            var t = new TarifaGLSBusinessParcel();

            Assert.AreEqual(1, t.AgenciaId);
            Assert.AreEqual((byte)96, t.ServicioId);
            Assert.AreEqual(1.80m, t.CosteReembolso(100m));
            Assert.AreEqual(0.41m, t.CosteKiloAdicional(ZonasEnvioAgencia.Peninsular));
            Assert.AreEqual(0.31m, t.CosteKiloAdicional(ZonasEnvioAgencia.Provincial));

            // Cubre Peninsular y Provincial con 5 tramos cada una.
            Assert.AreEqual(5, t.CosteEnvio.Count(c => c.Zona == ZonasEnvioAgencia.Peninsular));
            Assert.AreEqual(5, t.CosteEnvio.Count(c => c.Zona == ZonasEnvioAgencia.Provincial));

            // Spot-check (oferta 2026).
            Assert.AreEqual(3.66m, Precio(t, ZonasEnvioAgencia.Peninsular, 1m));
            Assert.AreEqual(4.19m, Precio(t, ZonasEnvioAgencia.Peninsular, 5m));
            Assert.AreEqual(3.10m, Precio(t, ZonasEnvioAgencia.Provincial, 1m));
            Assert.AreEqual(4.06m, Precio(t, ZonasEnvioAgencia.Provincial, 15m));
        }

        [TestMethod]
        public void InsularMaritimo_EstructuraYPreciosClave()
        {
            var t = new TarifaGLSBaleares();

            Assert.AreEqual(1, t.AgenciaId);
            Assert.AreEqual((byte)6, t.ServicioId);
            Assert.AreEqual(1.80m, t.CosteReembolso(100m));
            Assert.AreEqual(0.94m, t.CosteKiloAdicional(ZonasEnvioAgencia.BalearesMayores));
            Assert.AreEqual(1.50m, t.CosteKiloAdicional(ZonasEnvioAgencia.CanariasMenores));

            // Cubre las 4 zonas insulares.
            Assert.AreEqual(12.51m, Precio(t, ZonasEnvioAgencia.BalearesMayores, 5m));
            Assert.AreEqual(15.18m, Precio(t, ZonasEnvioAgencia.BalearesMenores, 5m));
            // Canarias incluye el DUA aproximado (12,94 + 20,85).
            Assert.AreEqual(33.79m, Precio(t, ZonasEnvioAgencia.CanariasMayores, 5m));
            Assert.AreEqual(35.63m, Precio(t, ZonasEnvioAgencia.CanariasMenores, 5m));
        }
    }
}
