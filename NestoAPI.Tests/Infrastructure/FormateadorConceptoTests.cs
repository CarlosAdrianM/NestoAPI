using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests de las reglas del concepto de los enlaces de pago (#295): detección del asunto
    /// genérico (solo válido cuando el enlace liquida efectos) y normalización de mayúsculas
    /// para que el extracto del cliente quede consistente.
    /// </summary>
    [TestClass]
    public class FormateadorConceptoTests
    {
        // ----- EsGenericoOVacio -----

        [TestMethod]
        public void EsGenericoOVacio_VacioNuloOEnBlanco_True()
        {
            Assert.IsTrue(FormateadorConcepto.EsGenericoOVacio(null));
            Assert.IsTrue(FormateadorConcepto.EsGenericoOVacio(""));
            Assert.IsTrue(FormateadorConcepto.EsGenericoOVacio("   "));
        }

        [TestMethod]
        public void EsGenericoOVacio_ElAsuntoPorDefecto_TrueSinImportarMayusculas()
        {
            Assert.IsTrue(FormateadorConcepto.EsGenericoOVacio("Enlace de pago a Nueva Visión"));
            Assert.IsTrue(FormateadorConcepto.EsGenericoOVacio("  enlace de pago a nueva visión  "));
            Assert.IsTrue(FormateadorConcepto.EsGenericoOVacio("ENLACE DE PAGO A NUEVA VISIÓN"));
        }

        [TestMethod]
        public void EsGenericoOVacio_ConceptoReal_False()
        {
            Assert.IsFalse(FormateadorConcepto.EsGenericoOVacio("Pago pedido 123456"));
            Assert.IsFalse(FormateadorConcepto.EsGenericoOVacio("Pago señal curso quiromasaje"));
        }

        // ----- Normalizar -----

        [TestMethod]
        public void Normalizar_TodoMayusculas_PasaATipoOracion()
        {
            Assert.AreEqual("Pago pedido 123456", FormateadorConcepto.Normalizar("PAGO PEDIDO 123456"));
            Assert.AreEqual("Pago señal curso quiromasaje", FormateadorConcepto.Normalizar("PAGO SEÑAL CURSO QUIROMASAJE"));
        }

        [TestMethod]
        public void Normalizar_TodoMinusculas_PasaATipoOracion()
        {
            Assert.AreEqual("Pago pedido 123456", FormateadorConcepto.Normalizar("pago pedido 123456"));
        }

        [TestMethod]
        public void Normalizar_PrimeraMinusculaRestoMayusculas_PasaATipoOracion()
        {
            // El caso "evidentemente mal" del enunciado (típico Bloq Mayús activado a destiempo).
            Assert.AreEqual("Pago pedido 123456", FormateadorConcepto.Normalizar("pAGO PEDIDO 123456"));
        }

        [TestMethod]
        public void Normalizar_MixtoRazonable_SeRespeta()
        {
            Assert.AreEqual("Pago señal Curso Quiromasaje", FormateadorConcepto.Normalizar("Pago señal Curso Quiromasaje"));
            Assert.AreEqual("Pago pedido PO-1234 de McArthur", FormateadorConcepto.Normalizar("Pago pedido PO-1234 de McArthur"));
        }

        [TestMethod]
        public void Normalizar_MixtoConPrimeraMinuscula_SoloCapitalizaLaPrimera()
        {
            Assert.AreEqual("Pago señal Curso", FormateadorConcepto.Normalizar("pago señal Curso"));
        }

        [TestMethod]
        public void Normalizar_SinLetrasVacioONulo_SeDevuelveTalCual()
        {
            Assert.IsNull(FormateadorConcepto.Normalizar(null));
            Assert.AreEqual("", FormateadorConcepto.Normalizar(""));
            Assert.AreEqual("123456", FormateadorConcepto.Normalizar("123456"));
        }

        [TestMethod]
        public void Normalizar_EmpiezaPorNumeros_CapitalizaLaPrimeraLetraQueEncuentre()
        {
            Assert.AreEqual("123 Pago pedido", FormateadorConcepto.Normalizar("123 PAGO PEDIDO"));
        }

        [TestMethod]
        public void Normalizar_RecortaEspacios()
        {
            Assert.AreEqual("Pago pedido 1", FormateadorConcepto.Normalizar("  pago pedido 1  "));
        }
    }
}
