using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.CTT;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#505: el usuario puede forzar a mano el servicio URGENTE de CTT (24 h) en vez del 48 h
    /// por defecto. La tarifa nueva es «solo a petición»: el comparador no la propone nunca, pero sí
    /// la tarifica cuando se pide por su servicio (ImporteGasto), y la tramitación manda el código
    /// de 24 h de la zona (C24 / CBA24 / CCA24).
    /// </summary>
    [TestClass]
    public class ServicioUrgenteCTTTests
    {
        private readonly TarifaCTT24h _urgente = new TarifaCTT24h();
        private readonly TarifaCTT48h _normal = new TarifaCTT48h();

        // ---- Tarifa ----

        [TestMethod]
        public void Tarifa24h_EsDeCTT_Servicio24_YSoloAPeticion()
        {
            Assert.AreEqual(13, _urgente.AgenciaId);
            Assert.AreEqual((byte)24, _urgente.ServicioId);
            Assert.IsTrue(CapacidadesTarifa.EsSoloAPeticion(_urgente));
            Assert.IsFalse(CapacidadesTarifa.EsSoloAPeticion(_normal), "El 48 h lo sigue proponiendo el comparador");
        }

        [TestMethod]
        public void Tarifa24h_CubrePeninsulaPortugalYBaleares_NoCanarias()
        {
            foreach (string cp in new[] { "28001", "08001", "1000-001", "07001", "07820" })
            {
                Assert.AreNotEqual(decimal.MaxValue, _urgente.CalcularCoste(cp, "ES", 1m, 0m, 0m), "Debería cubrir " + cp);
            }
            Assert.AreEqual(decimal.MaxValue, _urgente.CalcularCoste("35001", "ES", 1m, 0m, 0m), "Canarias va por Canteras");
        }

        [TestMethod]
        public void Tarifa24h_NuncaEsMasBarataQueLa48h_EnNingunaZonaNiPeso()
        {
            // Invariante que protege al comparador aunque se cuele en un ranking: el urgente cuesta
            // igual o más que el normal en todas las zonas y pesos (si CTT cambia precios y esto
            // deja de cumplirse, hay que revisar la oferta).
            foreach (string cp in new[] { "28001", "08001", "1000-001", "07001", "07820" })
            {
                for (decimal peso = 0.5m; peso <= 25m; peso += 0.5m)
                {
                    decimal urgente = _urgente.CalcularCoste(cp, "ES", peso, 0m, 0.05m);
                    decimal normal = _normal.CalcularCoste(cp, "ES", peso, 0m, 0.05m);
                    Assert.IsTrue(urgente >= normal, $"CP {cp}, {peso} kg: 24h {urgente} < 48h {normal}");
                }
            }
        }

        // ---- Comparador ----

        private static ComparadorAgencias ComparadorCTT(IEnumerable<ITarifaAgencia> tarifas)
        {
            var registro = A.Fake<IRegistroTarifas>();
            A.CallTo(() => registro.Todas()).Returns(tarifas);
            var fuel = A.Fake<IProveedorRecargoCombustible>();
            A.CallTo(() => fuel.RecargoCombustible(A<string>._, A<int>._)).Returns(0m);
            return new ComparadorAgencias(registro, fuel);
        }

        [TestMethod]
        public void Comparador_ElUrgenteNoEntraEnElRanking_NiEnMasEconomica()
        {
            // El 24 h va PRIMERO en la lista: si el comparador no lo excluyera, en caso de empate saldría él.
            var comparador = ComparadorCTT(new ITarifaAgencia[] { _urgente, _normal });

            IReadOnlyList<OpcionEnvioAgencia> ranking = comparador.Ranking("1", "08001", 3m, 0m);
            OpcionEnvioAgencia mejor = comparador.MasEconomica("1", "08001", 3m, 0m);

            Assert.IsFalse(ranking.Any(o => o.ServicioId == 24), "El urgente no compite");
            Assert.AreEqual((byte)48, mejor.ServicioId);
        }

        [TestMethod]
        public void Comparador_CosteDeAgencia_SinServicio_DaEl48h_YConServicio24_DaElUrgente()
        {
            var comparador = ComparadorCTT(new ITarifaAgencia[] { _urgente, _normal });

            OpcionEnvioAgencia porDefecto = comparador.CosteDeAgencia("1", "08001", 3m, 0m, 13);
            OpcionEnvioAgencia forzado = comparador.CosteDeAgencia("1", "08001", 3m, 0m, 13, servicioId: 24);

            Assert.AreEqual((byte)48, porDefecto.ServicioId);
            Assert.AreEqual((byte)24, forzado.ServicioId);
            Assert.AreEqual(_urgente.CalcularCoste("08001", "ES", 3m, 0m, 0m), forzado.Coste, "ImporteGasto del urgente");
        }

        [TestMethod]
        public void Comparador_ElFrenoPorZonasNoTapaQueEsSoloAPeticion()
        {
            // En producción la tarifa va envuelta en el freno por zonas de CTT: debe seguir fuera del ranking.
            var decorada = new TarifaConZonasActivas(_urgente, ZonasActivasAgencia.Parsear("Provincial, Peninsular"));
            var comparador = ComparadorCTT(new ITarifaAgencia[] { decorada, new TarifaConZonasActivas(_normal, null) });

            Assert.IsTrue(CapacidadesTarifa.EsSoloAPeticion(decorada));
            Assert.IsFalse(comparador.Ranking("1", "08001", 3m, 0m).Any(o => o.ServicioId == 24));
            Assert.IsNull(comparador.CosteDeAgencia("1", "07001", 3m, 0m, 13, servicioId: 24), "Baleares fuera del freno");
        }

        [TestMethod]
        public void RegistroReal_IncluyeElUrgenteDeCTT()
        {
            Assert.IsTrue(new RegistroTarifas().Todas().Any(t => t.AgenciaId == 13 && t.ServicioId == 24));
        }

        // ---- Catálogo de servicios (GET api/Agencias/{numero}/Servicios) ----

        [TestMethod]
        public void CatalogoServicios_CTT_Tiene48hNormalY24hSoloAPeticion()
        {
            List<ServicioAgenciaDTO> servicios = AgenciasTarifasController.ServiciosDeAgencia(new RegistroTarifas(), 13);

            Assert.AreEqual(2, servicios.Count);
            Assert.IsFalse(servicios.Single(s => s.ServicioId == 48).SoloAPeticion);
            Assert.IsTrue(servicios.Single(s => s.ServicioId == 24).SoloAPeticion);
            Assert.AreEqual("CTT 24h", servicios.Single(s => s.ServicioId == 24).Nombre);
        }

        [TestMethod]
        public void CatalogoServicios_AgenciaSinTarifas_ListaVacia()
        {
            Assert.AreEqual(0, AgenciasTarifasController.ServiciosDeAgencia(new RegistroTarifas(), 11).Count);
        }

        // ---- Mapeador de servicio ----

        [TestMethod]
        public void Mapeador_Servicio0o48_EsEl48hDeLaZona()
        {
            // 0 es lo que guardaba Nesto antes (79 envíos en producción): sigue siendo el 48 h.
            Assert.AreEqual("C48", MapeadorTipoServicioCTT.TipoServicio(0, "28001"));
            Assert.AreEqual("C48", MapeadorTipoServicioCTT.TipoServicio(48, "46001"));
            Assert.AreEqual("CBA48", MapeadorTipoServicioCTT.TipoServicio(48, "07001"));
            Assert.AreEqual("CCA48", MapeadorTipoServicioCTT.TipoServicio(48, "35001"));
        }

        [TestMethod]
        public void Mapeador_Servicio24_EsEl24hDeLaZona()
        {
            Assert.AreEqual("C24", MapeadorTipoServicioCTT.TipoServicio(24, "28001"));
            Assert.AreEqual("C24", MapeadorTipoServicioCTT.TipoServicio(24, "1000-001"), "Portugal");
            Assert.AreEqual("CBA24", MapeadorTipoServicioCTT.TipoServicio(24, "07820"), "Baleares Express");
            Assert.AreEqual("CCA24", MapeadorTipoServicioCTT.TipoServicio(24, "38001"), "Canarias aéreo 24h");
        }

        [TestMethod]
        public void Mapeador_ServicioDeOtraAgencia_SeRechazaConMensajeClaro()
        {
            // 96 = BusinessParcel de GLS: si se colara en un envío de CTT, no se manda nada a CTT.
            var ex = Assert.ThrowsException<ArgumentException>(() => MapeadorTipoServicioCTT.TipoServicio(96, "28001"));
            StringAssert.Contains(ex.Message, "96");
        }

        [TestMethod]
        public void Mapeador_Urgente_ACeutaSeRechaza()
        {
            Assert.ThrowsException<ArgumentException>(() => MapeadorTipoServicioCTT.TipoServicio(24, "51001"));
        }
    }
}
