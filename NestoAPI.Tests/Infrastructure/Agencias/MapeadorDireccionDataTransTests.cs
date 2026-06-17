using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Mapeo de provincia DataTrans (Innovatrans) desde el código postal, reglas del integrador:
    /// España = "0" + 2 primeros dígitos; Portugal = "6" + 4 primeros dígitos.
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
        [DataRow("1000-001", "PRT", "61000", DisplayName = "Lisboa con guion")]
        [DataRow("4000", "PRT", "64000", DisplayName = "Oporto 4 digitos")]
        public void ProvinciaDesdeCodigoPostal_Portugal_SeisMas4Digitos(string cp, string pais, string esperado)
        {
            Assert.AreEqual(esperado, MapeadorDireccionDataTrans.ProvinciaDesdeCodigoPostal(cp, pais));
        }

        [TestMethod]
        public void ProvinciaDesdeCodigoPostal_SinPais_AsumeEspana()
        {
            Assert.AreEqual("028", MapeadorDireccionDataTrans.ProvinciaDesdeCodigoPostal("28001", null));
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
