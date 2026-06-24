using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Tarifas Innovatrans (oferta Paquetería 2026). Se prueban vía CalcularCoste(CP, país, peso,
    /// reembolso, fuel). El reembolso (5% min 4,03 / max 300) se verifica por diferencia de coste.
    /// </summary>
    [TestClass]
    public class TarifasInnovatransTests
    {
        private readonly TarifaInnovatransEconomy _eco = new TarifaInnovatransEconomy();
        private readonly TarifaInnovatransPortugal _pt = new TarifaInnovatransPortugal();
        private readonly TarifaInnovatransMaritimo _mar = new TarifaInnovatransMaritimo();

        [TestMethod]
        public void Economy_IdentidadYPreciosClave()
        {
            Assert.AreEqual(12, _eco.AgenciaId);
            Assert.AreEqual((byte)1, _eco.ServicioId);
            Assert.AreEqual("Economy", _eco.NombreServicio);

            Assert.AreEqual(3.94m, _eco.CalcularCoste("28001", "ES", 2m, 0m, 0m));    // Provincial 2 kg
            Assert.AreEqual(18.69m, _eco.CalcularCoste("28001", "ES", 100m, 0m, 0m)); // Provincial 100 kg
            Assert.AreEqual(4.53m, _eco.CalcularCoste("08001", "ES", 5m, 0m, 0m));    // Peninsular 5 kg
            Assert.AreEqual(43.12m, _eco.CalcularCoste("08001", "ES", 100m, 0m, 0m)); // Peninsular 100 kg
        }

        [TestMethod]
        public void Reembolso_5PorCiento_ConMinimoYMaximo()
        {
            decimal baseCoste = _eco.CalcularCoste("08001", "ES", 1m, 0m, 0m);
            Assert.AreEqual(5.00m, _eco.CalcularCoste("08001", "ES", 1m, 100m, 0m) - baseCoste);   // 5% de 100
            Assert.AreEqual(4.03m, _eco.CalcularCoste("08001", "ES", 1m, 50m, 0m) - baseCoste);    // mínimo 4,03
            Assert.AreEqual(300m, _eco.CalcularCoste("08001", "ES", 1m, 10000m, 0m) - baseCoste);  // máximo 300
        }

        [TestMethod]
        public void Portugal_PreciosClave()
        {
            Assert.AreEqual((byte)2, _pt.ServicioId);
            Assert.AreEqual("14H Portugal", _pt.NombreServicio);
            Assert.AreEqual(7.42m, _pt.CalcularCoste("1000-001", "PT", 2m, 0m, 0m));
            Assert.AreEqual(32.61m, _pt.CalcularCoste("1000-001", "PT", 50m, 0m, 0m));
        }

        [TestMethod]
        public void Maritimo_PreciosClave()
        {
            Assert.AreEqual((byte)3, _mar.ServicioId);
            Assert.AreEqual(8.47m, _mar.CalcularCoste("07001", "ES", 5m, 0m, 0m));    // Baleares Mayores
            // Canarias incorpora el despacho fijo (18,03 + 25 = 43,03).
            Assert.AreEqual(58.39m, _mar.CalcularCoste("35001", "ES", 10m, 0m, 0m));  // Canarias Mayores
            Assert.AreEqual(73.94m, _mar.CalcularCoste("38800", "ES", 10m, 0m, 0m));  // Canarias Menores
        }
    }
}
