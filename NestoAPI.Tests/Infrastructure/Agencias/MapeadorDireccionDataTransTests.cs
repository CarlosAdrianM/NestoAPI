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
        public void ProvinciaDesdeCodigoPostal_Portugal_CodigoFijo053(string cp, string pais)
        {
            // El integrador confirmó (22/06/26) que la provincia para Portugal es el código fijo "053"
            // (el CP comprimido "6"+4 dígitos va en codPostalDes, no aquí).
            Assert.AreEqual("053", MapeadorDireccionDataTrans.ProvinciaDesdeCodigoPostal(cp, pais));
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

        // El integrador (23/06/26): el país del WS (paisRem/paisDes) va SIEMPRE "ESP", incluso
        // Portugal (se canaliza vía España). Mandar "PRT" lo rechazaba con codError 402.
        [DataTestMethod]
        [DataRow("PRT", DisplayName = "Portugal viaja como ESP")]
        [DataRow("ESP", DisplayName = "España viaja como ESP")]
        [DataRow(null, DisplayName = "Null viaja como ESP")]
        public void PaisParaDataTrans_SiempreEsp(string paisInterno)
        {
            Assert.AreEqual("ESP", MapeadorDireccionDataTrans.PaisParaDataTrans(paisInterno));
        }

        // Población para Portugal: el WS canaliza por población y el texto debe cuadrar con el
        // catálogo de DTX (BuscarPoblacion 63830 -> ILHAVO). Quitamos tilde, mayúsculas y el sufijo.
        [DataTestMethod]
        [DataRow("ÍLHAVO-AVEIRO", "ILHAVO", DisplayName = "Tilde + sufijo de distrito")]
        [DataRow("ilhavo", "ILHAVO", DisplayName = "Minúsculas a mayúsculas")]
        [DataRow("LISBOA", "LISBOA", DisplayName = "Sin tilde ni separador, igual")]
        [DataRow("GAFANHA DA NAZARÉ", "GAFANHA DA NAZARE", DisplayName = "Espacios se conservan, tilde fuera")]
        [DataRow("ÍLHAVO, AVEIRO", "ILHAVO", DisplayName = "Separador coma")]
        public void PoblacionParaDataTrans_Portugal_Normaliza(string entrada, string esperado)
        {
            Assert.AreEqual(esperado, MapeadorDireccionDataTrans.PoblacionParaDataTrans(entrada, "PRT"));
        }

        [TestMethod]
        public void PoblacionParaDataTrans_Espana_NoToca()
        {
            // En España no se canaliza por población: la dejamos tal cual (tildes incluidas).
            Assert.AreEqual("ALCALÁ DE HENARES",
                MapeadorDireccionDataTrans.PoblacionParaDataTrans("ALCALÁ DE HENARES", "ESP"));
        }

        [DataTestMethod]
        [DataRow("PRT", true)]
        [DataRow("prt", true)]
        [DataRow(" PRT ", true)]
        [DataRow("ESP", false)]
        [DataRow(null, false)]
        public void EsPortugal_DetectaPais(string pais, bool esperado)
        {
            Assert.AreEqual(esperado, MapeadorDireccionDataTrans.EsPortugal(pais));
        }
    }
}
