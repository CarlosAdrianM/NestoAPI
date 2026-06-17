using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// CalculadoraCosteEnvio: porte del cálculo de coste de Nesto + el recargo de combustible
    /// por agencia aplicado al porte. Se usa GLS BusinessParcel (Peninsular) como tarifa de prueba.
    /// </summary>
    [TestClass]
    public class CalculadoraCosteEnvioTests
    {
        private readonly TarifaGLSBusinessParcel _gls = new TarifaGLSBusinessParcel();

        [TestMethod]
        public void CalcularCoste_SinFuelNiReembolso_DevuelvePrecioDelTramo()
        {
            // Peninsular 3 kg -> tramo "hasta 3 kg" = 3,86. Sin fuel ni reembolso.
            decimal coste = CalculadoraCosteEnvio.CalcularCoste(
                _gls, ZonasEnvioAgencia.Peninsular, peso: 3m, reembolso: 0m, recargoCombustible: 0m);

            Assert.AreEqual(3.86m, coste);
        }

        [TestMethod]
        public void CalcularCoste_ConFuel_MultiplicaElPorte()
        {
            // 3,86 * (1 + 0,1055) = 4,26723.
            decimal coste = CalculadoraCosteEnvio.CalcularCoste(
                _gls, ZonasEnvioAgencia.Peninsular, peso: 3m, reembolso: 0m, recargoCombustible: 0.1055m);

            Assert.AreEqual(3.86m * 1.1055m, coste);
        }

        [TestMethod]
        public void CalcularCoste_ConReembolso_NoAplicaFuelAlReembolso()
        {
            // Porte con fuel + comisión de reembolso (1,80) SIN fuel.
            decimal coste = CalculadoraCosteEnvio.CalcularCoste(
                _gls, ZonasEnvioAgencia.Peninsular, peso: 3m, reembolso: 50m, recargoCombustible: 0.1055m);

            Assert.AreEqual((3.86m * 1.1055m) + 1.80m, coste);
        }

        [TestMethod]
        public void CalcularCoste_PesoPorEncimaDelUltimoTramo_UsaKiloAdicional()
        {
            // Peninsular 20 kg: último tramo (15 kg = 6,07) + 5 kg * 0,41 = 8,12. Con fuel 0.
            decimal coste = CalculadoraCosteEnvio.CalcularCoste(
                _gls, ZonasEnvioAgencia.Peninsular, peso: 20m, reembolso: 0m, recargoCombustible: 0m);

            Assert.AreEqual(6.07m + (5m * 0.41m), coste);
        }

        [TestMethod]
        public void CalcularCoste_ZonaNoCubierta_DevuelveMaxValue()
        {
            // BusinessParcel no cubre Canarias.
            decimal coste = CalculadoraCosteEnvio.CalcularCoste(
                _gls, ZonasEnvioAgencia.CanariasMayores, peso: 3m, reembolso: 0m, recargoCombustible: 0m);

            Assert.AreEqual(decimal.MaxValue, coste);
        }
    }
}
