using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Tarifa CTT 48h (agencia sombra). Tests price-independent: NO fijan los precios de los tramos
    /// de la oferta (una subida anual los rompería sin haber bug); verifican qué zonas cubre, que el
    /// fuel se aplica al porte y que el reembolso es el mínimo de 1,15€.
    /// </summary>
    [TestClass]
    public class TarifasCTTTests
    {
        private readonly TarifaCTT48h _tarifa = new TarifaCTT48h();

        [TestMethod]
        public void Identidad_EsLaAgencia13Sombra()
        {
            Assert.AreEqual(13, _tarifa.AgenciaId);
            Assert.AreEqual((byte)48, _tarifa.ServicioId);
        }

        [TestMethod]
        public void CubreLasZonasPeninsularesPortugalYBaleares()
        {
            foreach (var zona in new[]
            {
                ZonasEnvioAgencia.Provincial, ZonasEnvioAgencia.Peninsular, ZonasEnvioAgencia.Portugal,
                ZonasEnvioAgencia.BalearesMayores, ZonasEnvioAgencia.BalearesMenores
            })
            {
                decimal coste = CalculadoraCosteEnvio.CalcularCoste(_tarifa, zona, peso: 1m, reembolso: 0m, recargoCombustible: 0m);
                Assert.AreNotEqual(decimal.MaxValue, coste, $"CTT debería cubrir la zona {zona}.");
            }
        }

        [TestMethod]
        public void NoCubreCanariasNiExtranjero()
        {
            // Canarias la resuelve el comparador por Canteras; CTT no la modela.
            Assert.AreEqual(decimal.MaxValue,
                CalculadoraCosteEnvio.CalcularCoste(_tarifa, ZonasEnvioAgencia.CanariasMayores, 1m, 0m, 0m));
            Assert.AreEqual(decimal.MaxValue,
                CalculadoraCosteEnvio.CalcularCoste(_tarifa, ZonasEnvioAgencia.Extranjero, 1m, 0m, 0m));
        }

        [TestMethod]
        public void ElFuelSeAplicaAlPorte()
        {
            decimal sinFuel = CalculadoraCosteEnvio.CalcularCoste(_tarifa, ZonasEnvioAgencia.Peninsular, 1m, 0m, 0m);
            decimal conFuel = CalculadoraCosteEnvio.CalcularCoste(_tarifa, ZonasEnvioAgencia.Peninsular, 1m, 0m, 0.10m);

            Assert.AreEqual(sinFuel * 1.10m, conFuel);
        }

        [TestMethod]
        public void ReembolsoEsElMinimoDe115_SinFuel()
        {
            decimal sinReembolso = CalculadoraCosteEnvio.CalcularCoste(_tarifa, ZonasEnvioAgencia.Peninsular, 1m, 0m, 0m);
            decimal conReembolso = CalculadoraCosteEnvio.CalcularCoste(_tarifa, ZonasEnvioAgencia.Peninsular, 1m, 200m, 0m);

            // 0% del importe -> mínimo 1,15€, y al reembolso no se le aplica fuel.
            Assert.AreEqual(1.15m, conReembolso - sinReembolso);
            Assert.AreEqual(1.15m, _tarifa.CosteReembolso(200m));
            Assert.AreEqual(1.15m, _tarifa.CosteReembolso(5m));
        }
    }
}
