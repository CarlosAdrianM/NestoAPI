using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Tarifa CTT 48h (agencia sombra). Tests price-independent: NO fijan los precios de los tramos
    /// de la oferta (una subida anual los rompería sin haber bug); verifican qué destinos cubre, que
    /// el fuel se aplica al porte y que el reembolso es el mínimo de 1,15€. Se prueban vía
    /// CalcularCoste(CP, país, peso, reembolso, fuel) con CP/país representativos de cada zona.
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
            // (CP, país) representativos de cada zona que CTT debe cubrir.
            foreach (var destino in new[]
            {
                ("28001", "ES"),    // Provincial
                ("08001", "ES"),    // Peninsular
                ("1000-001", "ES"), // Portugal
                ("07001", "ES"),    // Baleares Mayores
                ("07820", "ES")     // Baleares Menores
            })
            {
                decimal coste = _tarifa.CalcularCoste(destino.Item1, destino.Item2, 1m, 0m, 0m);
                Assert.AreNotEqual(decimal.MaxValue, coste, $"CTT debería cubrir el destino {destino.Item1}/{destino.Item2}.");
            }
        }

        [TestMethod]
        public void NoCubreCanariasNiExtranjero()
        {
            // Canarias la resuelve el comparador por Canteras; CTT no la modela.
            Assert.AreEqual(decimal.MaxValue, _tarifa.CalcularCoste("35001", "ES", 1m, 0m, 0m));
            Assert.AreEqual(decimal.MaxValue, _tarifa.CalcularCoste("00000", "DE", 1m, 0m, 0m));
        }

        [TestMethod]
        public void ElFuelSeAplicaAlPorte()
        {
            decimal sinFuel = _tarifa.CalcularCoste("08001", "ES", 1m, 0m, 0m);
            decimal conFuel = _tarifa.CalcularCoste("08001", "ES", 1m, 0m, 0.10m);

            Assert.AreEqual(sinFuel * 1.10m, conFuel);
        }

        [TestMethod]
        public void ReembolsoEsElMinimoDe115_SinFuel()
        {
            decimal sinReembolso = _tarifa.CalcularCoste("08001", "ES", 1m, 0m, 0m);

            // 0% del importe -> mínimo 1,15€, y al reembolso no se le aplica fuel (independiente del importe).
            Assert.AreEqual(1.15m, _tarifa.CalcularCoste("08001", "ES", 1m, 200m, 0m) - sinReembolso);
            Assert.AreEqual(1.15m, _tarifa.CalcularCoste("08001", "ES", 1m, 5m, 0m) - sinReembolso);
        }
    }
}
