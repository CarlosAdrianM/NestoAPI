using System.Collections.Generic;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#494 (regla de Carlos, 17/09/26): los retornos se subastan igual que los envíos. El
    /// comparador calcula qué agencia sale más barata para el retorno solo (recogida en el cliente o
    /// proveedor) o para el envío + retorno juntos. Una agencia sin precio de retorno informado no
    /// entra en esa subasta (como una tarifa que no cubre la zona).
    /// </summary>
    [TestClass]
    public class RetornosComparadorAgenciasTests
    {
        private const int GLS = 1;
        private const int CTT = 13;

        // GLS barata en envíos pero SIN precio de retorno (hoy no lo tenemos modelado).
        private static ITarifaAgencia TarifaSinRetorno(int agenciaId, decimal precio)
        {
            var t = A.Fake<ITarifaAgencia>();
            A.CallTo(() => t.AgenciaId).Returns(agenciaId);
            A.CallTo(() => t.ServicioId).Returns((byte)agenciaId);
            A.CallTo(() => t.CalcularCoste(A<string>._, A<string>._, A<decimal>._, A<decimal>._, A<decimal>._)).Returns(precio);
            return t;
        }

        private static ComparadorAgencias Comparador(params ITarifaAgencia[] tarifas)
        {
            var registro = A.Fake<IRegistroTarifas>();
            A.CallTo(() => registro.Todas()).Returns(tarifas);
            var fuel = A.Fake<IProveedorRecargoCombustible>();
            A.CallTo(() => fuel.RecargoCombustible(A<string>._, A<int>._)).Returns(0m);
            return new ComparadorAgencias(registro, fuel);
        }

        [TestMethod]
        public void TarifaCTT48h_ElRetornoCuestaLoMismoQueLaEmision_SinReembolso()
        {
            // Oferta CTT 2026, pág. 14: "misma tarifa que la de emisión para todos los servicios retorno".
            var ctt = new TarifaCTT48h();

            Assert.AreEqual(ctt.CalcularCoste("46001", "ES", 3m, 0m, 0.05m), ctt.CalcularCosteRetorno("46001", "ES", 3m, 0.05m));
            Assert.AreEqual(decimal.MaxValue, ctt.CalcularCosteRetorno("35001", "ES", 3m, 0m), "Canarias no se tarifica");
        }

        [TestMethod]
        public void ModoEnvio_NoCambia_GanaLaMasBarataAunqueNoTengaRetorno()
        {
            var comparador = Comparador(TarifaSinRetorno(GLS, 2m), new TarifaCTT48h());

            OpcionEnvioAgencia mejor = comparador.MasEconomica("1", "46001", 3m, 0m);

            Assert.AreEqual(GLS, mejor.AgenciaId);
        }

        [TestMethod]
        public void ModoRetorno_SoloCompitenLasAgenciasConPrecioDeRetorno()
        {
            var comparador = Comparador(TarifaSinRetorno(GLS, 2m), new TarifaCTT48h());

            OpcionEnvioAgencia mejor = comparador.MasEconomica("1", "46001", 3m, 0m, modo: ModoComparacionAgencia.Retorno);

            Assert.AreEqual(CTT, mejor.AgenciaId, "GLS es más barata pero no tiene precio de retorno: fuera de la subasta");
            Assert.AreEqual(new TarifaCTT48h().CalcularCosteRetorno("46001", "ES", 3m, 0m), mejor.Coste);
        }

        [TestMethod]
        public void ModoEnvioYRetorno_SumaLosDosCostesDeLaMismaAgencia()
        {
            var ctt = new TarifaCTT48h();
            var comparador = Comparador(ctt);

            OpcionEnvioAgencia mejor = comparador.MasEconomica("1", "46001", 3m, 50m, modo: ModoComparacionAgencia.EnvioYRetorno);

            Assert.AreEqual(ctt.CalcularCoste("46001", "ES", 3m, 50m, 0m) + ctt.CalcularCosteRetorno("46001", "ES", 3m, 0m), mejor.Coste,
                "Envío (con su reembolso) + retorno");
        }

        [TestMethod]
        public void ModoRetorno_SinNingunaAgenciaConPrecio_DevuelveNull()
        {
            var comparador = Comparador(TarifaSinRetorno(GLS, 2m));

            Assert.IsNull(comparador.MasEconomica("1", "46001", 3m, 0m, modo: ModoComparacionAgencia.Retorno));
            Assert.IsNull(comparador.MasEconomica("1", "46001", 3m, 0m, modo: ModoComparacionAgencia.EnvioYRetorno));
        }

        [TestMethod]
        public void ModoRetorno_EnCanarias_NoSaleCanteras()
        {
            // En envíos Canarias es siempre Canteras, pero Canteras no tiene precio de retorno.
            var comparador = Comparador(new TarifaCTT48h());

            Assert.AreEqual(11, comparador.MasEconomica("1", "35001", 3m, 0m).AgenciaId);
            Assert.IsNull(comparador.MasEconomica("1", "35001", 3m, 0m, modo: ModoComparacionAgencia.Retorno));
        }

        [TestMethod]
        public void ModoRetorno_ElFrenoPorZonasDeCTTTambienAplica()
        {
            var frenada = new TarifaConZonasActivas(new TarifaCTT48h(), ZonasActivasAgencia.Parsear("Provincial"));
            var comparador = Comparador(frenada);

            Assert.IsNotNull(comparador.MasEconomica("1", "28001", 3m, 0m, modo: ModoComparacionAgencia.Retorno), "Provincial activa");
            Assert.IsNull(comparador.MasEconomica("1", "46001", 3m, 0m, modo: ModoComparacionAgencia.Retorno), "Peninsular frenada");
        }

        [TestMethod]
        public void CosteDeAgencia_EnModoRetorno_DaElCosteDelRetorno()
        {
            var ctt = new TarifaCTT48h();
            var comparador = Comparador(TarifaSinRetorno(GLS, 2m), ctt);

            Assert.AreEqual(ctt.CalcularCosteRetorno("46001", "ES", 3m, 0m),
                comparador.CosteDeAgencia("1", "46001", 3m, 0m, CTT, modo: ModoComparacionAgencia.Retorno).Coste);
            Assert.IsNull(comparador.CosteDeAgencia("1", "46001", 3m, 0m, GLS, modo: ModoComparacionAgencia.Retorno));
        }
    }
}
