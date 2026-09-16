using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#479: el nombre en MAYÚSCULAS de la ficha de Nesto viaja a la tienda en formato
    /// oración cuando no hay NombrePersonalizado. Los ejemplos son referencias reales de Mirplay.
    /// </summary>
    [TestClass]
    public class FormateadorNombreTiendaTests
    {
        [TestMethod]
        public void FormatoOracion_PrimeraLetraMayusculaYElRestoMinusculas()
        {
            Assert.AreEqual("Sillon de barbero Dave", FormateadorNombreTienda.FormatoOracion("SILLON DE BARBERO DAVE", "Dave"));
        }

        [TestMethod]
        public void FormatoOracion_ElModeloSeEscribeComoLoEscribeElFabricante_IncluidasSusSiglas()
        {
            Assert.AreEqual("Sillon de barbero Check GY", FormateadorNombreTienda.FormatoOracion("SILLON DE BARBERO CHECK GY", "Check GY"));
            Assert.AreEqual("Lavacabezas Noe B W", FormateadorNombreTienda.FormatoOracion("LAVACABEZAS NOE B W", "Noe B W"));
        }

        [TestMethod]
        public void FormatoOracion_SiElFabricanteEscribeElModeloEnMayusculas_SaleEnMayusculas()
        {
            // Fiel a la fuente: si se quiere "Oke" se corrige la referencia del proveedor, no el algoritmo.
            Assert.AreEqual("Tocador OKE 9 BR/W", FormateadorNombreTienda.FormatoOracion("TOCADOR OKE 9 BR/W", "OKE 9 BR/W"));
        }

        [TestMethod]
        public void FormatoOracion_LaReferenciaNoDistingueMayusculasAlCasar()
        {
            Assert.AreEqual("Complemento Ivar", FormateadorNombreTienda.FormatoOracion("COMPLEMENTO IVAR", "Ivar"));
        }

        [TestMethod]
        public void FormatoOracion_SinReferenciaDelProveedor_SoloPrimeraLetraYListaBlanca()
        {
            Assert.AreEqual("Lampara LED de 18 W", FormateadorNombreTienda.FormatoOracion("LAMPARA LED DE 18 W", null));
            Assert.AreEqual("Sillon de barbero dave", FormateadorNombreTienda.FormatoOracion("SILLON DE BARBERO DAVE", "  "));
        }

        [TestMethod]
        public void FormatoOracion_LaSiglaSoloCuentaComoTokenCompleto_LedsNoSeParte()
        {
            Assert.AreEqual("Tira de leds", FormateadorNombreTienda.FormatoOracion("TIRA DE LEDS", null));
        }

        [TestMethod]
        public void FormatoOracion_SiElNombreEmpiezaPorUnTokenFijo_SeRespeta()
        {
            Assert.AreEqual("LED frontal", FormateadorNombreTienda.FormatoOracion("LED FRONTAL", null));
            Assert.AreEqual("Dave sillon", FormateadorNombreTienda.FormatoOracion("DAVE SILLON", "Dave"));
        }

        [TestMethod]
        public void FormatoOracion_NoInventaTildesNiConservaEspaciosDobles()
        {
            Assert.AreEqual("Sillon de espera Thierry", FormateadorNombreTienda.FormatoOracion("  SILLON  DE ESPERA   THIERRY ", "Thierry"));
        }

        [TestMethod]
        public void FormatoOracion_NombreVacioONulo_SeDevuelveTalCual()
        {
            Assert.IsNull(FormateadorNombreTienda.FormatoOracion(null, "Dave"));
            Assert.AreEqual("", FormateadorNombreTienda.FormatoOracion("", "Dave"));
        }
    }
}
