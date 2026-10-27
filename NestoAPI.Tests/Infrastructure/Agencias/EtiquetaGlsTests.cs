using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#552 (fase 4): la etiqueta de GLS montada en el servidor, la misma que AgenciaASM.imprimirEtiqueta, sin grabar el
    /// envío en GLS. Todo con fakes: nada sale hacia GLS.
    /// </summary>
    [TestClass]
    public class EtiquetaGlsTests
    {
        internal const string RESPUESTA_TSP = @"<?xml version=""1.0"" encoding=""utf-8""?><soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body><GetTSPResponse xmlns=""http://www.asmred.com/""><GetTSPResult><CodTSP>MAD-28</CodTSP></GetTSPResult></GetTSPResponse></soap:Body></soap:Envelope>";

        internal static EnviosAgencia EnvioConPlaza()
        {
            EnviosAgencia envio = TramitadorGlsTests.Envio();
            envio.Nemonico = "M28";
            envio.NombrePlaza = "MADRID SUR";
            return envio;
        }

        // ---------- La ZPL ----------

        [TestMethod]
        public void ConstruirZpl_UnaEtiquetaPorBultoConElCodigoDeBarrasYElBulto()
        {
            string zpl = EtiquetaGls.ConstruirZpl(EnvioConPlaza(), "MAD-28");

            Assert.AreEqual(2, zpl.Split(new[] { "^XA" }, StringSplitOptions.None).Length - 1);
            StringAssert.Contains(zpl, "^FO10,300^B2B,200,Y,N,Y^FD61197140249723001^FS");
            StringAssert.Contains(zpl, "^FO10,300^B2B,200,Y,N,Y^FD61197140249723002^FS");
            StringAssert.Contains(zpl, "^FO200,50^AUN,50,50^FD61197140249723^FS");
            StringAssert.Contains(zpl, "^FO600,770^AUN,50,50^FD2/2^FS");
        }

        [TestMethod]
        public void ConstruirZpl_LlevaLaPlazaElTspElServicioYElDestinoComoNesto()
        {
            string zpl = EtiquetaGls.ConstruirZpl(EnvioConPlaza(), "MAD-28");

            StringAssert.Contains(zpl, "^FO300,530^AVN,200,200^FDM28^FS");
            StringAssert.Contains(zpl, "^FO300,720^AUN,50,50^FDMADRID SUR^FS");
            StringAssert.Contains(zpl, "^FO90,230^AUN,50,50^FDMAD-28^FS");
            StringAssert.Contains(zpl, "^FO300,510^ATN,10,10^FDBusinessParcel (18)^FS");
            StringAssert.Contains(zpl, "^FO200,130^ARN,1,1^FDCENTRO PRUEBA^FS");
            StringAssert.Contains(zpl, "^FO200,190^ARN,1,1^FD28043. MADRID^FS");
            StringAssert.Contains(zpl, "^FO10,160^ARN,1,1^FD07/10/2026^FS");
            StringAssert.Contains(zpl, "^FO110,850^ASN,1,1^FD32624928379^FS");
            StringAssert.Contains(zpl, "^FO110,890^ASN,1,1^FD32624/928379^FS");
            StringAssert.Contains(zpl, "^FO300,270^APN,1,1^FDNUEVA VISIÓN, S.A.^FS");
        }

        [TestMethod]
        public void ConstruirZpl_ServicioQueNoConoce_SaleConSuNumero()
        {
            EnviosAgencia envio = EnvioConPlaza();
            envio.Servicio = 37;

            StringAssert.Contains(EtiquetaGls.ConstruirZpl(envio, "X"), "^FDServicio 37 (18)^FS");
        }

        [TestMethod]
        public void ConstruirZpl_UnDatoConCaracteresDeZpl_NoRompeLaEtiqueta()
        {
            EnviosAgencia envio = EnvioConPlaza();
            envio.Observaciones = "Llamar^antes~";

            StringAssert.Contains(EtiquetaGls.ConstruirZpl(envio, "X"), "^FDLlamar antes^FS");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ConstruirZpl_SinCodigoDeBarras_Lanza()
        {
            EnviosAgencia envio = EnvioConPlaza();
            envio.CodigoBarras = " ";

            EtiquetaGls.ConstruirZpl(envio, "X");
        }

        [TestMethod]
        public void EnBase64_EnAnsi1252ParaLaZebra()
        {
            string base64 = EtiquetaGls.EnBase64("^FDNUEVA VISIÓN^FS");

            Assert.AreEqual("^FDNUEVA VISIÓN^FS", Encoding.GetEncoding(1252).GetString(Convert.FromBase64String(base64)));
            Assert.AreEqual(18, Convert.FromBase64String(base64).Length, "Un byte por carácter: la Ó en 1252, no en UTF-8");
        }

        // ---------- El TSP ----------

        [TestMethod]
        public void InterpretarTsp_DevuelveElCodTsp()
        {
            Assert.AreEqual("MAD-28", RespuestaTspGls.Interpretar(RESPUESTA_TSP));
        }

        [TestMethod]
        public void InterpretarTsp_ComoNesto_SinCodigoNA_SinXmlERROR()
        {
            Assert.AreEqual("N/A", RespuestaTspGls.Interpretar(@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body/></soap:Envelope>"));
            Assert.AreEqual("ERROR", RespuestaTspGls.Interpretar("esto no es xml"));
        }

        [TestMethod]
        public void SoapTsp_OrigenAlgeteYDestinoDelEnvio()
        {
            string soap = ClienteTspGls.ConstruirSoap(EnvioConPlaza());

            StringAssert.Contains(soap, "<CodPaisOrg>34</CodPaisOrg><CPOrg>28119</CPOrg><CodPaisDst>34</CodPaisDst><CPDst>28043</CPDst><CodServicio>96</CodServicio><CodHorario>18</CodHorario>");
        }

        // ---------- El generador ----------

        [TestMethod]
        public async Task Generar_ConPlazaEnElEnvio_NoPideLaPlazaYSoloConsultaElTsp()
        {
            IClienteTspGls tsp = A.Fake<IClienteTspGls>();
            A.CallTo(() => tsp.EnviarAsync(A<string>._)).Returns(Task.FromResult(RESPUESTA_TSP));
            IPlazaYCodigoBarrasAgencia plaza = A.Fake<IPlazaYCodigoBarrasAgencia>();

            EtiquetaGlsGenerada etiqueta = await new GeneradorEtiquetaGls(tsp, plaza).GenerarAsync(EnvioConPlaza(), "6108352");

            Assert.AreEqual("MAD-28", etiqueta.CodigoTsp);
            Assert.IsFalse(etiqueta.PlazaConsultada);
            StringAssert.Contains(etiqueta.Zpl, "^FDM28^FS");
            A.CallTo(() => plaza.CalcularPlazaAsync(A<string>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => tsp.EnviarAsync(A<string>.That.Contains("<GetTSP "))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Generar_SinPlaza_LaPideYLaDejaEnElEnvio()
        {
            IClienteTspGls tsp = A.Fake<IClienteTspGls>();
            A.CallTo(() => tsp.EnviarAsync(A<string>._)).Returns(Task.FromResult(RESPUESTA_TSP));
            IPlazaYCodigoBarrasAgencia plaza = A.Fake<IPlazaYCodigoBarrasAgencia>();
            A.CallTo(() => plaza.CalcularPlazaAsync("28043", 34)).Returns(Task.FromResult(new PlazaAgencia { Nemonico = "G01", Nombre = "SILLEDA", Telefono = "910806229" }));
            EnviosAgencia envio = TramitadorGlsTests.Envio();

            EtiquetaGlsGenerada etiqueta = await new GeneradorEtiquetaGls(tsp, plaza).GenerarAsync(envio, "6108352");

            Assert.IsTrue(etiqueta.PlazaConsultada);
            Assert.AreEqual("G01", envio.Nemonico);
            Assert.AreEqual("SILLEDA", envio.NombrePlaza);
            StringAssert.Contains(etiqueta.Zpl, "^FDG01^FS");
        }

        [TestMethod]
        public async Task Generar_SinCodigoDeBarras_LoCalculaComoAgenciaAsm()
        {
            IClienteTspGls tsp = A.Fake<IClienteTspGls>();
            A.CallTo(() => tsp.EnviarAsync(A<string>._)).Returns(Task.FromResult(RESPUESTA_TSP));
            EnviosAgencia envio = EnvioConPlaza();
            envio.CodigoBarras = null;
            envio.Servicio = 6;
            envio.Horario = 10;

            EtiquetaGlsGenerada etiqueta = await new GeneradorEtiquetaGls(tsp, null).GenerarAsync(envio, "6108352");

            Assert.AreEqual("61083520249723", envio.CodigoBarras);
            StringAssert.Contains(etiqueta.Zpl, "^FD61083520249723001^FS");
        }

        [TestMethod]
        public async Task Generar_GlsNoRespondeAlTsp_NoHayEtiquetaComoEnNesto()
        {
            IClienteTspGls tsp = A.Fake<IClienteTspGls>();
            A.CallTo(() => tsp.EnviarAsync(A<string>._)).Throws(new AgenciaRemotaException("GLS (GetTSP): la petición agotó el tiempo de espera.") { EsTransitoria = true });

            await Assert.ThrowsExceptionAsync<AgenciaRemotaException>(() => new GeneradorEtiquetaGls(tsp, null).GenerarAsync(EnvioConPlaza(), null));
        }

        [DataTestMethod]
        [DataRow(@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body/></soap:Envelope>", DisplayName = "Sin CodTSP (N/A)")]
        [DataRow("esto no es xml", DisplayName = "XML roto (ERROR)")]
        [DataRow("", DisplayName = "Vacía (Error)")]
        public async Task Generar_GlsContestaSinTsp_NoHayEtiqueta_ElTspEsObligatorio(string respuesta)
        {
            // Decisión de Carlos (27/10/26): sin código de ruta GLS no entrega el paquete; nunca se imprime sin él (Nesto ponía «N/A»).
            IClienteTspGls tsp = A.Fake<IClienteTspGls>();
            A.CallTo(() => tsp.EnviarAsync(A<string>._)).Returns(Task.FromResult(respuesta));

            AgenciaRemotaException ex = await Assert.ThrowsExceptionAsync<AgenciaRemotaException>(() => new GeneradorEtiquetaGls(tsp, null).GenerarAsync(EnvioConPlaza(), null));
            StringAssert.Contains(ex.Message, "código de ruta (TSP)");
        }

        [DataTestMethod]
        [DataRow("MAD-28", true)]
        [DataRow("SCQ", true)]
        [DataRow("N/A", false)]
        [DataRow("ERROR", false)]
        [DataRow("Error", false)]
        [DataRow("  ", false)]
        [DataRow(null, false)]
        public void EsCodigoValido_SoloUnCodigoDeVerdad(string tsp, bool esperado)
        {
            Assert.AreEqual(esperado, RespuestaTspGls.EsCodigoValido(tsp));
        }
    }
}
