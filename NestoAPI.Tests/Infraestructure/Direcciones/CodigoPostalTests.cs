using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.CTT;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Infraestructure.PedidosVenta;
using CP = NestoAPI.Infraestructure.Direcciones.CodigoPostal;

namespace NestoAPI.Tests.Infraestructure.Direcciones
{
    /// <summary>
    /// NestoAPI#596: un solo objeto entiende el código postal, lo teclee el usuario como lo teclee.
    /// Caso que lo destapa: envío 249519 a Vila do Conde con «4480 670», que CTT rechazaba.
    /// </summary>
    [TestClass]
    public class CodigoPostalTests
    {
        [DataTestMethod]
        [DataRow("4480-670")]
        [DataRow("4480 670")]
        [DataRow("4480 670 ")] // char de la BD rellenado con blancos
        [DataRow("4480670")]
        [DataRow("  4480   670  ")]
        [DataRow("4480 - 670")]
        public void Normalizar_PortugalEnCualquierFormato_DaElCanonicoConGuion(string texto)
        {
            Assert.AreEqual("4480-670", CP.Normalizar(texto, "PT"));
            Assert.AreEqual("4480-670", CP.Normalizar(texto, null), "Sin país se detecta por el formato");
            Assert.AreEqual("4480-670", CP.Normalizar(texto, "ES"), "7 cifras nunca son España");
            Assert.IsTrue(CP.EsPortugues(texto, null));
            Assert.IsTrue(CP.EsPortugues(texto, "PT"));
            Assert.IsTrue(CP.TieneFormatoPortugues(texto));
            Assert.AreEqual("4480-670", CP.ParaCTT(texto, "PT"));
        }

        [TestMethod]
        public void Normalizar_PortugalSoloCuatroCifras_SeQuedaIgual()
        {
            Assert.AreEqual("4480", CP.Normalizar("4480 ", "PT"));
            Assert.IsTrue(CP.EsPortugues("4480", "PT"));
            Assert.IsTrue(CP.TieneFormatoPortugues("4480"), "Regla antigua de los perfiles sin país");
            Assert.IsFalse(CP.EsPortugues("4480", null), "Sin país, 4 cifras son ambiguas: no se da por portugués");
        }

        [DataTestMethod]
        [DataRow("8850", "ES")]
        [DataRow("8850", null)]
        [DataRow("08850", "ES")]
        [DataRow(" 08850 ", null)]
        public void Normalizar_EspanaSinElCero_LoRellena(string texto, string pais)
        {
            Assert.AreEqual("08850", CP.Normalizar(texto, pais));
            Assert.IsTrue(CP.EsEspanol(texto, pais));
            Assert.AreEqual("08850", CP.ParaCTT(texto, pais));
        }

        [TestMethod]
        public void Normalizar_OtroPais_RecortaYMayusculas()
        {
            Assert.AreEqual("SW1A 1AA", CP.Normalizar(" sw1a 1aa ", "GB"));
            Assert.AreEqual("2000", CP.Normalizar("2000", "BE"), "4 cifras de otro país no se rellenan");
            Assert.IsFalse(CP.EsEspanol("28001", "FR"));
            Assert.IsFalse(CP.EsPortugues("4480-670", "FR"));
        }

        [TestMethod]
        public void Normalizar_NullYVacio_NoRevientan()
        {
            Assert.IsNull(CP.Normalizar(null, "PT"));
            Assert.AreEqual(string.Empty, CP.Normalizar("   ", null));
            Assert.AreEqual(string.Empty, CP.ParaCTT(null, null));
            Assert.AreEqual(string.Empty, CP.Digitos(null));
            Assert.IsFalse(CP.EsPortugues(null, null));
            Assert.IsFalse(CP.EsEspanol(null, null));
            Assert.IsFalse(CP.TieneFormatoPortugues(null));
        }

        [TestMethod]
        public void Digitos_SoloLasCifras()
        {
            Assert.AreEqual("4480670", CP.Digitos("4480-670 "));
        }

        [DataTestMethod]
        [DataRow("351", "PT")]
        [DataRow("620", "PT")]
        [DataRow("PRT", "PT")]
        [DataRow("pt", "PT")]
        [DataRow("34", "ES")]
        [DataRow("724", "ES")]
        [DataRow(" es ", "ES")]
        [DataRow(null, "")]
        public void PaisIso_TraduceLoQueGuardaCadaTabla(string pais, string esperado)
        {
            Assert.AreEqual(esperado, CP.PaisIso(pais));
        }

        [TestMethod]
        public void PaisIso_Numerico_EspanaPortugalYElRestoTalCual()
        {
            Assert.AreEqual("PT", CP.PaisIso(351));
            Assert.AreEqual("ES", CP.PaisIso(34));
            Assert.AreEqual(string.Empty, CP.PaisIso(0), "Sin informar: se deduce del CP");
            Assert.AreEqual("32", CP.PaisIso(32), "Otro país: su CP no se rellena como si fuera español");
            Assert.AreEqual("2000", CP.Normalizar("2000", CP.PaisIso(32)));
        }

        // ---- Regresión: los parsers delegan y aceptan los cuatro formatos ----

        [TestMethod]
        public void CTT_TipoServicio_PortugalConEspacio_EsElServicioDePortugal()
        {
            // Envío 249519: lanzaba «el código postal '4480 670' no está en las zonas que tarificamos».
            Assert.AreEqual(MapeadorTipoServicioCTT.SERVICIO_48H, MapeadorTipoServicioCTT.TipoServicio(48, "4480 670"));
            Assert.AreEqual(MapeadorTipoServicioCTT.SERVICIO_48H, MapeadorTipoServicioCTT.TipoServicio(48, "4480670"));
            Assert.AreEqual(MapeadorTipoServicioCTT.SERVICIO_24H, MapeadorTipoServicioCTT.TipoServicio(24, "4480 670 "));
            Assert.AreEqual("PT", MapeadorTipoServicioCTT.PaisDesdeCodigoPostal("4480 670"));
        }

        [TestMethod]
        public void CTT_ElPaisDelEnvioManda()
        {
            Assert.AreEqual("PT", MapeadorTipoServicioCTT.PaisDesdeCodigoPostal("4480", "351"));
            Assert.AreEqual("ES", MapeadorTipoServicioCTT.PaisDesdeCodigoPostal("8850", "ES"));
            Assert.AreEqual(MapeadorTipoServicioCTT.SERVICIO_48H, MapeadorTipoServicioCTT.TipoServicio(48, "8850", "ES"), "Rellena el cero: 08850");
            Assert.AreEqual(MapeadorTipoServicioCTT.SERVICIO_BALEARES_ECONOMY, MapeadorTipoServicioCTT.TipoServicio(48, "7001", "ES"));
            Assert.AreEqual("PT", MapeadorTipoServicioCTT.PaisDesdeCodigoPostal("4480 670", "ES"), "7 cifras: Portugal aunque el envío diga España");
            Assert.ThrowsException<ArgumentException>(() => MapeadorTipoServicioCTT.TipoServicio(48, "75001", "FR"));
        }

        [TestMethod]
        public void GestorPortes_CanariasYBaleares_SobreElCanonico()
        {
            Assert.IsTrue(GestorPortes.EsCanarias("35001"));
            Assert.IsTrue(GestorPortes.EsCanarias(" 38001 "));
            Assert.IsTrue(GestorPortes.EsBaleares("7001"), "Sin el cero: 07001");
            Assert.IsFalse(GestorPortes.EsCanarias("3500-001"), "Viseu (Portugal) no es Canarias");
            Assert.IsFalse(GestorPortes.EsCanarias(null));
            Assert.IsFalse(GestorPortes.EsBaleares(""));
        }

        [TestMethod]
        public void CalculadoraZona_Portugal_SigueAceptandoLosTresFormatos()
        {
            Assert.AreEqual(ZonasEnvioAgencia.Portugal, CalculadoraZonaEnvio.CalcularZona("4480 670 "));
            Assert.AreEqual(ZonasEnvioAgencia.Portugal, CalculadoraZonaEnvio.CalcularZona("4480670"));
            Assert.AreEqual(ZonasEnvioAgencia.Portugal, CalculadoraZonaEnvio.CalcularZona("4480-670"));
        }
    }
}
