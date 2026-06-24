using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Tarifas GLS/ASM (oferta 2026). Se prueban vía CalcularCoste(CP, país, peso, reembolso, fuel)
    /// con CP representativos de cada zona (sin fuel ni reembolso = precio del tramo).
    /// </summary>
    [TestClass]
    public class TarifasGLSTests
    {
        private readonly TarifaGLSBusinessParcel _bp = new TarifaGLSBusinessParcel();
        private readonly TarifaGLSBaleares _bal = new TarifaGLSBaleares();

        [TestMethod]
        public void BusinessParcel_IdentidadYPreciosClave()
        {
            Assert.AreEqual(1, _bp.AgenciaId);
            Assert.AreEqual((byte)96, _bp.ServicioId);

            // Provincial (28xxx): 1 kg = 3,10; 15 kg = 4,06.
            Assert.AreEqual(3.10m, _bp.CalcularCoste("28001", "ES", 1m, 0m, 0m));
            Assert.AreEqual(4.06m, _bp.CalcularCoste("28001", "ES", 15m, 0m, 0m));
            // Peninsular (08xxx): 1 kg = 3,66; 5 kg = 4,19.
            Assert.AreEqual(3.66m, _bp.CalcularCoste("08001", "ES", 1m, 0m, 0m));
            Assert.AreEqual(4.19m, _bp.CalcularCoste("08001", "ES", 5m, 0m, 0m));
            // GLS Portugal (CP portugués): 5 kg = 13,28; 10 kg = 14,76.
            Assert.AreEqual(13.28m, _bp.CalcularCoste("1000-001", "ES", 5m, 0m, 0m));
            Assert.AreEqual(14.76m, _bp.CalcularCoste("1000-001", "ES", 10m, 0m, 0m));
        }

        [TestMethod]
        public void BusinessParcel_Reembolso180_SinFuel()
        {
            decimal sinReembolso = _bp.CalcularCoste("08001", "ES", 1m, 0m, 0m);
            decimal conReembolso = _bp.CalcularCoste("08001", "ES", 1m, 50m, 0m);
            Assert.AreEqual(1.80m, conReembolso - sinReembolso);
        }

        [TestMethod]
        public void BusinessParcel_NoCubreExtranjero()
        {
            Assert.AreEqual(decimal.MaxValue, _bp.CalcularCoste("00000", "DE", 1m, 0m, 0m));
        }

        [TestMethod]
        public void InsularMaritimo_IdentidadYPreciosClave()
        {
            Assert.AreEqual(1, _bal.AgenciaId);
            Assert.AreEqual((byte)6, _bal.ServicioId);

            Assert.AreEqual(12.51m, _bal.CalcularCoste("07001", "ES", 5m, 0m, 0m)); // Baleares Mayores
            Assert.AreEqual(15.18m, _bal.CalcularCoste("07820", "ES", 5m, 0m, 0m)); // Baleares Menores
            // Canarias incluye el DUA aproximado (12,94 + 20,85 = 33,79).
            Assert.AreEqual(33.79m, _bal.CalcularCoste("35001", "ES", 5m, 0m, 0m));
        }
    }
}
