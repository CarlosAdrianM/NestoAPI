using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// CalculadoraCosteEnvio: helper de porte por tramos + fuel + reembolso (sin conocer agencias).
    /// Se le pasan tramos y coste por kilo adicional explícitos.
    /// </summary>
    [TestClass]
    public class CalculadoraCosteEnvioTests
    {
        private static readonly IReadOnlyList<TramoCosteEnvio> Tramos = new List<TramoCosteEnvio>
        {
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.Peninsular, 3.66m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.Peninsular, 3.86m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Peninsular, 4.19m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Peninsular, 4.71m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Peninsular, 6.07m)
        };

        private static decimal KiloAdicional(ZonasEnvioAgencia zona)
            => zona == ZonasEnvioAgencia.Peninsular ? 0.41m : decimal.MaxValue;

        private static decimal Calcular(decimal peso, decimal fuel, decimal costeReembolso,
            ZonasEnvioAgencia zona = ZonasEnvioAgencia.Peninsular)
            => CalculadoraCosteEnvio.CalcularCoste(Tramos, KiloAdicional, zona, peso, fuel, costeReembolso);

        [TestMethod]
        public void CalcularCoste_SinFuelNiReembolso_DevuelvePrecioDelTramo()
        {
            Assert.AreEqual(3.86m, Calcular(peso: 3m, fuel: 0m, costeReembolso: 0m)); // tramo "hasta 3 kg"
        }

        [TestMethod]
        public void CalcularCoste_ConFuel_MultiplicaElPorte()
        {
            Assert.AreEqual(3.86m * 1.1055m, Calcular(peso: 3m, fuel: 0.1055m, costeReembolso: 0m));
        }

        [TestMethod]
        public void CalcularCoste_ConReembolso_NoAplicaFuelAlReembolso()
        {
            Assert.AreEqual((3.86m * 1.1055m) + 1.80m, Calcular(peso: 3m, fuel: 0.1055m, costeReembolso: 1.80m));
        }

        [TestMethod]
        public void CalcularCoste_PesoPorEncimaDelUltimoTramo_UsaKiloAdicional()
        {
            // 15 kg = 6,07; 20 kg = 6,07 + 5*0,41.
            Assert.AreEqual(6.07m + (5m * 0.41m), Calcular(peso: 20m, fuel: 0m, costeReembolso: 0m));
        }

        [TestMethod]
        public void CalcularCoste_ZonaSinTramos_DevuelveMaxValue()
        {
            Assert.AreEqual(decimal.MaxValue, Calcular(peso: 3m, fuel: 0m, costeReembolso: 0m, zona: ZonasEnvioAgencia.CanariasMayores));
        }
    }
}
