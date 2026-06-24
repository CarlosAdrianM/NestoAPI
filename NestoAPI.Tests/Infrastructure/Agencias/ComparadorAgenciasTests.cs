using System.Collections.Generic;
using System.Linq;
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
        // Tarifa falsa que cubre UNA sola zona (resuelta a partir del CP, como las nacionales reales) y
        // cobra un precio fijo + fuel; MaxValue si el CP cae en otra zona. Reembolso no se modela.
        private static ITarifaAgencia TarifaFake(int agenciaId, ZonasEnvioAgencia zona, decimal precioHasta5kg)
        {
            var t = A.Fake<ITarifaAgencia>();
            A.CallTo(() => t.AgenciaId).Returns(agenciaId);
            A.CallTo(() => t.ServicioId).Returns((byte)agenciaId);
            A.CallTo(() => t.NombreServicio).Returns("Servicio " + agenciaId);
            A.CallTo(() => t.CalcularCoste(A<string>._, A<string>._, A<decimal>._, A<decimal>._, A<decimal>._))
                .ReturnsLazily((string cp, string pais, decimal peso, decimal reembolso, decimal fuel) =>
                    CalculadoraZonaEnvio.CalcularZona(cp) == zona
                        ? precioHasta5kg * (1m + fuel)
                        : decimal.MaxValue);
            return t;
        }

        private static ComparadorAgencias Comparador(IEnumerable<ITarifaAgencia> tarifas, IProveedorRecargoCombustible fuel,
            IEnumerable<int> sombra = null)
        {
            var registro = A.Fake<IRegistroTarifas>();
            A.CallTo(() => registro.Todas()).Returns(tarifas);
            return new ComparadorAgencias(registro, fuel, sombra);
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

        // Caso real del bug: GLS no tiene tarifa de Portugal (solo Provincial/Peninsular/Baleares);
        // Innovatrans sí (14H Portugal). Para un CP portugués debe ganar Innovatrans, NUNCA GLS.
        [TestMethod]
        public void MasEconomica_DestinoPortugal_ConTarifasReales_EligeInnovatransNoGLS()
        {
            var comparador = new ComparadorAgencias(new RegistroTarifas(), FuelCero(), agenciasSombra: new[] { 13 });

            var mejor = comparador.MasEconomica("1", "4590-704", peso: 1m, reembolso: 0m);

            Assert.IsNotNull(mejor, "Innovatrans cubre Portugal: debe haber opción");
            Assert.AreEqual(12, mejor.AgenciaId, "Para Portugal debe elegirse Innovatrans (12), no GLS (1)");
        }

        // GLS SÍ cubre Portugal (oferta ASM_2026.pdf: 13,28 € hasta 5 kg), aunque más cara que
        // Innovatrans (por eso MasEconomica elige Innovatrans, pero GLS es una opción válida/de respaldo).
        [TestMethod]
        public void CosteDeAgencia_GLS_DestinoPortugal_ConTarifasReales_DevuelveCosteGLS()
        {
            var comparador = new ComparadorAgencias(new RegistroTarifas(), FuelCero(), agenciasSombra: new[] { 13 });

            var coste = comparador.CosteDeAgencia("1", "4590-704", peso: 1m, reembolso: 0m, agenciaId: 1);

            Assert.IsNotNull(coste, "GLS cubre Portugal");
            Assert.AreEqual(1, coste.AgenciaId);
            Assert.AreEqual(13.28m, coste.Coste, "GLS Portugal hasta 5 kg = 13,28 € (sin fuel en el test)");
        }

        // Zona sin cobertura en ninguna agencia portada (Extranjero) -> null -> aguas arriba es error.
        [TestMethod]
        public void MasEconomica_DestinoExtranjero_ConTarifasReales_DevuelveNull()
        {
            var comparador = new ComparadorAgencias(new RegistroTarifas(), FuelCero(), agenciasSombra: new[] { 13 });

            var mejor = comparador.MasEconomica("1", "EXTER", peso: 1m, reembolso: 0m);

            Assert.IsNull(mejor);
        }

        [TestMethod]
        public void RegistroTarifasExistentes_SoloDevuelveLasAgenciasConFila()
        {
            // Numero 1 (GLS) existe; 12 (Innovatrans) no -> Innovatrans queda fuera de la comparación.
            var registro = new RegistroTarifasExistentes(new RegistroTarifas(), new[] { 1 });

            var ids = registro.Todas().Select(t => t.AgenciaId).Distinct().ToList();

            Assert.IsTrue(ids.Contains(1));
            Assert.IsFalse(ids.Contains(12), "Innovatrans (12) no tiene fila todavía: no se compara");
        }

        [TestMethod]
        public void MasEconomica_ExcluyeLasAgenciasSombra()
        {
            // La 2 (8) es la más barata, pero es SOMBRA -> MasEconomica devuelve la 1 (10).
            var tarifas = new[]
            {
                TarifaFake(1, ZonasEnvioAgencia.Peninsular, 10m),
                TarifaFake(2, ZonasEnvioAgencia.Peninsular, 8m)
            };
            var comparador = Comparador(tarifas, FuelCero(), sombra: new[] { 2 });

            var mejor = comparador.MasEconomica("1", "08001", peso: 3m, reembolso: 0m);

            Assert.AreEqual(1, mejor.AgenciaId, "La sombra (2) no debe auto-seleccionarse.");
            Assert.AreEqual(10m, mejor.Coste);
        }

        [TestMethod]
        public void Ranking_IncluyeLasSombraOrdenadasPorCoste()
        {
            // El ranking SÍ incluye la sombra (para medir cuánto ganaría): la 2 (8) va primera.
            var tarifas = new[]
            {
                TarifaFake(1, ZonasEnvioAgencia.Peninsular, 10m),
                TarifaFake(2, ZonasEnvioAgencia.Peninsular, 8m)
            };
            var comparador = Comparador(tarifas, FuelCero(), sombra: new[] { 2 });

            var ranking = comparador.Ranking("1", "08001", peso: 3m, reembolso: 0m);

            Assert.AreEqual(2, ranking.Count);
            Assert.AreEqual(2, ranking[0].AgenciaId, "La más barata (sombra) va primera en el ranking.");
            Assert.AreEqual(1, ranking[1].AgenciaId);
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
