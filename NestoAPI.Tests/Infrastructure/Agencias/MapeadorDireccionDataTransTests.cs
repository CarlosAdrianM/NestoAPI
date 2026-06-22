using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Mapeo a DataTrans (Innovatrans) desde el código postal, reglas del integrador (22/06/26):
    /// - Provincia: España = "0" + 2 primeros dígitos; Portugal = pendiente (vacío de momento).
    /// - codPostalDes: España = el CP tal cual; Portugal = "6" + 4 primeros dígitos (1000-001 -> 61000).
    /// </summary>
    [TestClass]
    public class MapeadorDireccionDataTransTests
    {
        [DataTestMethod]
        [DataRow("28001", "ESP", "028", DisplayName = "Madrid")]
        [DataRow("08001", "ESP", "008", DisplayName = "Barcelona (cero a la izquierda)")]
        [DataRow("35001", "ESP", "035", DisplayName = "Las Palmas (Canarias)")]
        [DataRow("07001", "ESP", "007", DisplayName = "Baleares")]
        [DataRow("51001", "ESP", "051", DisplayName = "Ceuta")]
        [DataRow("52001", "ESP", "052", DisplayName = "Melilla")]
        public void ProvinciaDesdeCodigoPostal_Espana_ZeroMas2Digitos(string cp, string pais, string esperado)
        {
            Assert.AreEqual(esperado, MapeadorDireccionDataTrans.ProvinciaDesdeCodigoPostal(cp, pais));
        }

        [DataTestMethod]
        [DataRow("1000-001", "PRT", DisplayName = "Lisboa con guion")]
        [DataRow("4000", "PRT", DisplayName = "Oporto 4 digitos")]
        public void ProvinciaDesdeCodigoPostal_Portugal_VacioPendienteDeConfirmar(string cp, string pais)
        {
            // El integrador aclaró que el "6"+4 dígitos va en codPostalDes, NO en provincia, y quedó
            // en confirmar qué valor lleva provincia para Portugal. Hasta entonces, vacío.
            Assert.AreEqual(string.Empty, MapeadorDireccionDataTrans.ProvinciaDesdeCodigoPostal(cp, pais));
        }

        [TestMethod]
        public void ProvinciaDesdeCodigoPostal_SinPais_AsumeEspana()
        {
            Assert.AreEqual("028", MapeadorDireccionDataTrans.ProvinciaDesdeCodigoPostal("28001", null));
        }

        [DataTestMethod]
        [DataRow("1000-001", "61000", DisplayName = "Lisboa con guion")]
        [DataRow("1000 001", "61000", DisplayName = "Con espacio")]
        [DataRow("1000001", "61000", DisplayName = "Junto")]
        [DataRow(" 1000-001 ", "61000", DisplayName = "Con espacios alrededor")]
        [DataRow("4000", "64000", DisplayName = "Oporto 4 dígitos")]
        public void CodigoPostalDestino_Portugal_SeisMas4Digitos(string cp, string esperado)
        {
            Assert.AreEqual(esperado, MapeadorDireccionDataTrans.CodigoPostalDestino(cp, "PRT"));
        }

        [DataTestMethod]
        [DataRow("28001", DisplayName = "CP español de 5 dígitos")]
        [DataRow("08001", DisplayName = "Barcelona")]
        public void CodigoPostalDestino_Espana_DevuelveElCpTalCual(string cp)
        {
            Assert.AreEqual(cp, MapeadorDireccionDataTrans.CodigoPostalDestino(cp, "ESP"));
        }

        [DataTestMethod]
        [DataRow("ABC", DisplayName = "No reconocible no se toca")]
        [DataRow("12", DisplayName = "Menos de 4 dígitos no se toca")]
        public void CodigoPostalDestino_Portugal_SinSuficientesDigitos_DevuelveIgualTrim(string cp)
        {
            Assert.AreEqual(cp.Trim(), MapeadorDireccionDataTrans.CodigoPostalDestino(cp, "PRT"));
        }

        [DataTestMethod]
        [DataRow("", "ESP")]
        [DataRow(null, "ESP")]
        [DataRow("1", "ESP")]
        public void ProvinciaDesdeCodigoPostal_CpInvalido_DevuelveVacio(string cp, string pais)
        {
            Assert.AreEqual("", MapeadorDireccionDataTrans.ProvinciaDesdeCodigoPostal(cp, pais));
        }
    }
}
