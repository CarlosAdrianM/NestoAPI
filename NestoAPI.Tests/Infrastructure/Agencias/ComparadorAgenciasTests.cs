using System.Collections.Generic;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// ComparadorAgencias: elige la agencia+servicio más barata para un pedido, teniendo en
    /// cuenta el recargo de combustible por agencia. Se usan tarifas/fuel falsos (FakeItEasy)
    /// para testear la lógica de comparación, no precios reales.
    /// </summary>
    [TestClass]
    public class ComparadorAgenciasTests
    {
        private static ITarifaAgencia TarifaFake(int agenciaId, ZonasEnvioAgencia zona, decimal precioHasta5kg)
        {
            var t = A.Fake<ITarifaAgencia>();
            A.CallTo(() => t.AgenciaId).Returns(agenciaId);
            A.CallTo(() => t.ServicioId).Returns((byte)agenciaId);
            A.CallTo(() => t.NombreServicio).Returns("Servicio " + agenciaId);
            A.CallTo(() => t.CosteEnvio).Returns(new List<TramoCosteEnvio>
            {
                new TramoCosteEnvio(5m, zona, precioHasta5kg)
            });
            A.CallTo(() => t.CosteKiloAdicional(A<ZonasEnvioAgencia>._)).Returns(1m);
            A.CallTo(() => t.CosteReembolso(A<decimal>._)).Returns(0m);
            return t;
        }

        private static ComparadorAgencias Comparador(IEnumerable<ITarifaAgencia> tarifas, IProveedorRecargoCombustible fuel)
        {
            var registro = A.Fake<IRegistroTarifas>();
            A.CallTo(() => registro.Todas()).Returns(tarifas);
            return new ComparadorAgencias(registro, fuel);
        }

        private static IProveedorRecargoCombustible FuelCero()
        {
            var fuel = A.Fake<IProveedorRecargoCombustible>();
            A.CallTo(() => fuel.RecargoCombustible(A<string>._, A<int>._)).Returns(0m);
            return fuel;
        }

        [TestMethod]
        public void MasEconomica_DosAgencias_DevuelveLaMasBarata()
        {
            var tarifas = new[]
            {
                TarifaFake(1, ZonasEnvioAgencia.Peninsular, 10m),
                TarifaFake(2, ZonasEnvioAgencia.Peninsular, 8m)
            };
            var comparador = Comparador(tarifas, FuelCero());

            var mejor = comparador.MasEconomica("1", "08001", peso: 3m, reembolso: 0m);

            Assert.IsNotNull(mejor);
            Assert.AreEqual(2, mejor.AgenciaId);
            Assert.AreEqual(8m, mejor.Coste);
        }

        [TestMethod]
        public void MasEconomica_ElFuelPuedeCambiarLaEleccion()
        {
            // Sin fuel, la agencia 1 (8) es más barata que la 2 (10). Pero la 1 tiene un fuel
            // altísimo (50%) -> 8*1,5 = 12 > 10, así que gana la 2.
            var tarifas = new[]
            {
                TarifaFake(1, ZonasEnvioAgencia.Peninsular, 8m),
                TarifaFake(2, ZonasEnvioAgencia.Peninsular, 10m)
            };
            var fuel = A.Fake<IProveedorRecargoCombustible>();
            A.CallTo(() => fuel.RecargoCombustible(A<string>._, 1)).Returns(0.5m);
            A.CallTo(() => fuel.RecargoCombustible(A<string>._, 2)).Returns(0m);
            var comparador = Comparador(tarifas, fuel);

            var mejor = comparador.MasEconomica("1", "08001", peso: 3m, reembolso: 0m);

            Assert.AreEqual(2, mejor.AgenciaId);
            Assert.AreEqual(10m, mejor.Coste);
        }

        [TestMethod]
        public void MasEconomica_IgnoraAgenciasQueNoCubrenLaZona()
        {
            // Una sí cubre Peninsular, la otra solo Canarias.
            var tarifas = new[]
            {
                TarifaFake(1, ZonasEnvioAgencia.Peninsular, 10m),
                TarifaFake(2, ZonasEnvioAgencia.CanariasMayores, 5m)
            };
            var comparador = Comparador(tarifas, FuelCero());

            var mejor = comparador.MasEconomica("1", "08001", peso: 3m, reembolso: 0m);

            Assert.AreEqual(1, mejor.AgenciaId);
        }

        [TestMethod]
        public void MasEconomica_SiNingunaCubreLaZona_DevuelveNull()
        {
            var tarifas = new[] { TarifaFake(1, ZonasEnvioAgencia.CanariasMayores, 5m) };
            var comparador = Comparador(tarifas, FuelCero());

            var mejor = comparador.MasEconomica("1", "08001", peso: 3m, reembolso: 0m);

            Assert.IsNull(mejor);
        }

        [TestMethod]
        public void MasEconomica_Canarias_SiempreVaPorCanterasSinCompararPrecio()
        {
            // Aunque haya una tarifa baratísima que cubra Canarias, Canarias siempre va por
            // Canteras (juega en otro nivel, no depende del precio).
            var tarifas = new[] { TarifaFake(1, ZonasEnvioAgencia.CanariasMayores, 1m) };
            var comparador = Comparador(tarifas, FuelCero());

            var mejor = comparador.MasEconomica("1", "35001", peso: 3m, reembolso: 0m);

            Assert.IsNotNull(mejor);
            Assert.AreEqual(11, mejor.AgenciaId);
            Assert.AreEqual("Canteras", mejor.NombreServicio);
        }
    }
}
