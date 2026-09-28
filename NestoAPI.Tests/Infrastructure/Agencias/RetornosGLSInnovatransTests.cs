using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#494 (Carlos 28/09/26): GLS e Innovatrans entran en la subasta de retornos con el precio de
    /// sus ofertas 2026. El retorno cuesta un envío; la recogida SUELTA tiene sus límites (GLS: canon de
    /// 1,23 € en capitales y sin precio en pueblos; Innovatrans: solo Madrid). La vuelta en la misma
    /// entrega («Recoger producto») es siempre un envío más.
    /// </summary>
    [TestClass]
    public class RetornosGLSInnovatransTests
    {
        private readonly TarifaGLSBusinessParcel gls = new TarifaGLSBusinessParcel();
        private readonly TarifaInnovatransEconomy innovatrans = new TarifaInnovatransEconomy();

        private static ComparadorAgencias Comparador(params ITarifaAgencia[] tarifas)
        {
            var registro = A.Fake<IRegistroTarifas>();
            A.CallTo(() => registro.Todas()).Returns(tarifas);
            var fuel = A.Fake<IProveedorRecargoCombustible>();
            A.CallTo(() => fuel.RecargoCombustible(A<string>._, A<int>._)).Returns(0m);
            return new ComparadorAgencias(registro, fuel);
        }

        // ---- GLS ----

        [TestMethod]
        public void GLS_RecogidaEnMadrid_CuestaUnEnvioSinReembolso()
        {
            Assert.AreEqual(gls.CalcularCoste("28670", "ES", 3m, 0m, 0.1m), gls.CalcularCosteRetorno("28670", "ES", 3m, 0.1m));
        }

        [TestMethod]
        public void GLS_RecogidaEnCapitalDeProvincia_EnvioMasCanonDe123()
        {
            Assert.AreEqual(gls.CalcularCoste("46001", "ES", 3m, 0m, 0.1m) + 1.23m, gls.CalcularCosteRetorno("46001", "ES", 3m, 0.1m));
        }

        [TestMethod]
        public void GLS_RecogidaEnPueblo_SinPrecio()
        {
            // Los km a la capital (0,62 €/km ida y vuelta) no se pueden calcular desde el CP
            Assert.AreEqual(decimal.MaxValue, gls.CalcularCosteRetorno("46100", "ES", 3m, 0.1m));
        }

        [TestMethod]
        public void GLS_RecogidaEnPortugal_SinPrecio()
        {
            Assert.AreEqual(decimal.MaxValue, gls.CalcularCosteRetorno("1000-001", "PT", 3m, 0.1m));
        }

        [TestMethod]
        public void GLS_VueltaEnLaMismaEntrega_EsUnEnvioMasTambienEnPueblos()
        {
            // «En los servicios con retorno el puente de vuelta se facturará como nuevo envío»
            Assert.AreEqual(gls.CalcularCoste("46100", "ES", 3m, 0m, 0.1m), gls.CalcularCosteVueltaEnEntrega("46100", "ES", 3m, 0.1m));
        }

        [TestMethod]
        public void GLS_EnvioYRetornoEnPueblo_SigueEnLaSubasta()
        {
            var comparador = Comparador(gls);

            OpcionEnvioAgencia mejor = comparador.MasEconomica("1", "46100", 3m, 0m, modo: ModoComparacionAgencia.EnvioYRetorno);

            Assert.IsNotNull(mejor, "El «Recoger producto» de GLS en un pueblo tiene precio");
            Assert.AreEqual(2 * gls.CalcularCoste("46100", "ES", 3m, 0m, 0m), mejor.Coste);
        }

        // ---- Innovatrans ----

        [TestMethod]
        public void Innovatrans_FueraDeLaSubastaDeRetornos_MientrasDataTransNoRecibaElRetorno()
        {
            // 28/09/26: el alta en DataTrans no manda el retorno; si el comparador la eligiera para «Recoger
            // producto», la recogida no se pediría.
            Assert.AreEqual(decimal.MaxValue, CapacidadesTarifa.CosteRetorno(innovatrans, "28670", "ES", 3m, 0m));
            Assert.AreEqual(decimal.MaxValue, CapacidadesTarifa.Coste(innovatrans, ModoComparacionAgencia.EnvioYRetorno, "28670", "ES", 3m, 0m, 0m));
        }

        [TestMethod]
        public void Innovatrans_RecogidaEnMadrid_CuestaUnEnvio()
        {
            Assert.AreEqual(innovatrans.CalcularCoste("28670", "ES", 3m, 0m, 0.025m), innovatrans.CalcularCosteRetorno("28670", "ES", 3m, 0.025m));
        }

        [TestMethod]
        public void Innovatrans_RecogidaFueraDeMadrid_SinPrecio()
        {
            // «Para recogidas resto de Península, sujetas a oferta presentada o presupuesto previo»
            Assert.AreEqual(decimal.MaxValue, innovatrans.CalcularCosteRetorno("46001", "ES", 3m, 0.025m));
        }

        [TestMethod]
        public void Innovatrans_VueltaEnLaMismaEntrega_MismoPrecioQueElServicio()
        {
            Assert.AreEqual(innovatrans.CalcularCoste("46001", "ES", 3m, 0m, 0.025m), innovatrans.CalcularCosteVueltaEnEntrega("46001", "ES", 3m, 0.025m));
        }

        // ---- Subasta ----

        [TestMethod]
        public void ModoRetorno_FueraDeMadridEnCapital_CompitenCTTYGLS_NoInnovatrans()
        {
            var comparador = Comparador(gls, innovatrans, new TarifaCTT48h());

            Assert.IsNotNull(comparador.CosteDeAgencia("1", "46001", 3m, 0m, 1, modo: ModoComparacionAgencia.Retorno), "GLS en capital");
            Assert.IsNotNull(comparador.CosteDeAgencia("1", "46001", 3m, 0m, 13, modo: ModoComparacionAgencia.Retorno), "CTT");
            Assert.IsNull(comparador.CosteDeAgencia("1", "46001", 3m, 0m, 12, modo: ModoComparacionAgencia.Retorno), "Innovatrans fuera de Madrid");
        }

        [TestMethod]
        public void CTT_NoCambia_VueltaEnEntregaIgualQueElRetorno()
        {
            var ctt = new TarifaCTT48h();

            Assert.AreEqual(ctt.CalcularCosteRetorno("46100", "ES", 3m, 0m),
                CapacidadesTarifa.CosteVueltaEnEntrega(ctt, "46100", "ES", 3m, 0m));
        }

        [TestMethod]
        public void ReglaCapital_TerceraCifraCero()
        {
            Assert.IsTrue(ReglasRetornoAgencias.EsCapitalDeProvincia("08001", "ES"));
            Assert.IsTrue(ReglasRetornoAgencias.EsCapitalDeProvincia("07001", ""));
            Assert.IsFalse(ReglasRetornoAgencias.EsCapitalDeProvincia("08400", "ES"));
            Assert.IsFalse(ReglasRetornoAgencias.EsCapitalDeProvincia("46001", "PT"));
            Assert.IsFalse(ReglasRetornoAgencias.EsCapitalDeProvincia(null, "ES"));
        }
    }
}
