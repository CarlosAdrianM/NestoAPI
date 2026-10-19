using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#552 (fase 3): anular en GLS un envío ya grabado (operación Anula del WS b2b). Nada sale hacia GLS: el
    /// transporte es un fake o un HttpMessageHandler falso.
    /// </summary>
    [TestClass]
    public class AnulacionGlsTests
    {
        private const string ALBARAN = "61197140249723";
        private const string UID_AGENCIA = "9ABAEE33";

        internal static string RespuestaAnula(string retorno, string resultado = null, string error = null) =>
            @"<?xml version=""1.0"" encoding=""utf-8""?><soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body><AnulaResponse xmlns=""http://www.asmred.com/""><AnulaResult><Servicios xmlns=""""><Envio codbarras=""61197140249723"">" +
            (resultado == null ? $@"<Resultado return=""{retorno}"" />" : $@"<Resultado return=""{retorno}"" result=""{resultado}"" />") +
            (error == null ? string.Empty : $"<Errores><Error>{error}</Error></Errores>") +
            "</Envio></Servicios></AnulaResult></AnulaResponse></soap:Body></soap:Envelope>";

        // ---------- La petición ----------

        [TestMethod]
        public void Peticion_EsUnAnulaConLaCuentaYElCodigoDeBarrasEnElMismoSobreQueGrabaServicios()
        {
            string soap = PeticionGls.ConstruirSoap(PeticionGls.ConstruirAnulacion(" " + ALBARAN + " ", UID_AGENCIA), PeticionGls.OPERACION_ANULA);

            XDocument documento = XDocument.Parse(soap);
            XNamespace soapNs = "http://schemas.xmlsoap.org/soap/envelope/";
            XElement anula = documento.Root.Element(soapNs + "Body").Elements().Single();
            Assert.AreEqual(PeticionGls.Ns + "Anula", anula.Name);
            XElement servicios = anula.Element(PeticionGls.Ns + "docIn").Element(PeticionGls.Ns + "Servicios");
            Assert.AreEqual(UID_AGENCIA, servicios.Attribute("uidcliente").Value);
            XElement envio = servicios.Elements().Single();
            Assert.AreEqual(PeticionGls.Ns + "Envio", envio.Name);
            Assert.AreEqual(ALBARAN, envio.Attribute("codbarras").Value, "Sin espacios: es lo que identifica el envío en GLS");
            Assert.IsFalse(envio.HasElements, "Para anular solo hace falta el código de barras");
        }

        [TestMethod]
        public void Peticion_GrabaServiciosSigueIgual()
        {
            XElement servicios = PeticionGls.ConstruirServicios(TramitadorGlsTests.Envio(), TramitadorGlsTests.EmpresaNuevaVision(), UID_AGENCIA);

            Assert.AreEqual(PeticionGls.ConstruirSoap(servicios, PeticionGls.OPERACION_GRABA_SERVICIOS), PeticionGls.ConstruirSoap(servicios));
            StringAssert.Contains(PeticionGls.ConstruirSoap(servicios), "<GrabaServicios xmlns=\"http://www.asmred.com/\"><docIn>");
        }

        [TestMethod]
        public void Sombra_NoConfundeUnaAnulacionConUnaPeticionDeGrabar()
        {
            string soap = PeticionGls.ConstruirSoap(PeticionGls.ConstruirAnulacion(ALBARAN, UID_AGENCIA), PeticionGls.OPERACION_ANULA);

            Assert.IsNull(PeticionGls.ServiciosDeLaPeticion(soap));
            Assert.IsNotNull(PeticionGls.ServiciosDeLaPeticion(PeticionGls.ConstruirSoap(
                PeticionGls.ConstruirServicios(TramitadorGlsTests.Envio(), TramitadorGlsTests.EmpresaNuevaVision(), UID_AGENCIA))));
        }

        // ---------- La cuenta del envío (la misma que al grabar) ----------

        private static Func<string, Task<string>> UidConEnBD(EnviosAgencia envio, AgenciaTransporte agencia)
        {
            var db = A.Fake<NVEntities>();
            var envios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            var agencias = A.Fake<DbSet<AgenciaTransporte>>(o => o.Implements<IQueryable<AgenciaTransporte>>().Implements<IDbAsyncEnumerable<AgenciaTransporte>>());
            ConfigurarFakeDbSet(envios, (envio == null ? new EnviosAgencia[0] : new[] { envio }).AsQueryable());
            ConfigurarFakeDbSet(agencias, new[] { agencia }.AsQueryable());
            A.CallTo(() => envios.AsNoTracking()).Returns(envios);
            A.CallTo(() => agencias.AsNoTracking()).Returns(agencias);
            A.CallTo(() => db.EnviosAgencias).Returns(envios);
            A.CallTo(() => db.AgenciasTransportes).Returns(agencias);
            return AnulacionGls.UidDesdeLaBD(db);
        }

        private static AgenciaTransporte AgenciaGls() => new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_GLS, Nombre = "ASM", Identificador = UID_AGENCIA };

        [TestMethod]
        public async Task Uid_BusinessParcelVaConSuPropiaCuentaComoAlGrabar()
        {
            EnviosAgencia envio = TramitadorGlsTests.Envio(); // servicio 96

            string uid = await UidConEnBD(envio, AgenciaGls())(ALBARAN);

            Assert.AreEqual(PeticionGls.IDENTIFICADOR_BUSINESSPARCEL, uid);
            Assert.AreEqual(PeticionGls.Identificador(envio, UID_AGENCIA), uid);
        }

        [TestMethod]
        public async Task Uid_OtrosServiciosVanConLaCuentaDeLaAgencia()
        {
            EnviosAgencia envio = TramitadorGlsTests.Envio();
            envio.Servicio = 6;

            Assert.AreEqual(UID_AGENCIA, await UidConEnBD(envio, AgenciaGls())(ALBARAN));
        }

        [TestMethod]
        public async Task Uid_SinEnvioDeGlsConEseCodigo_Null()
        {
            EnviosAgencia deOtraAgencia = TramitadorGlsTests.Envio();
            deOtraAgencia.Agencia = Constantes.Agencias.AGENCIA_CTT;

            Assert.IsNull(await UidConEnBD(deOtraAgencia, AgenciaGls())(ALBARAN));
            Assert.IsNull(await UidConEnBD(null, AgenciaGls())(ALBARAN));
        }

        // ---------- Interpretar la respuesta ----------

        [TestMethod]
        public void Interpretar_ReturnCero_Exito()
        {
            ResultadoAnulaGls r = RespuestaAnulaGls.Interpretar(RespuestaAnula("0"));

            Assert.IsTrue(r.Exito);
            Assert.AreEqual("0", r.CodigoRetorno);
            Assert.IsNull(r.Error);
        }

        [TestMethod]
        public void Interpretar_OtroCodigo_FalloConElTextoDeGlsTalCual()
        {
            ResultadoAnulaGls conResult = RespuestaAnulaGls.Interpretar(RespuestaAnula("-1", resultado: "Expedición ya en reparto"));
            ResultadoAnulaGls conErrores = RespuestaAnulaGls.Interpretar(RespuestaAnula("-2", error: "No existe el envío"));

            Assert.IsFalse(conResult.Exito);
            Assert.AreEqual("-1", conResult.CodigoRetorno);
            Assert.AreEqual("Expedición ya en reparto", conResult.Error);
            Assert.IsFalse(conErrores.Exito);
            Assert.AreEqual("No existe el envío", conErrores.Error);
        }

        [TestMethod]
        public void Interpretar_OtroCodigoSinTexto_FalloSinInventarseElMotivo()
        {
            ResultadoAnulaGls r = RespuestaAnulaGls.Interpretar(RespuestaAnula("-33"));

            Assert.IsFalse(r.Exito);
            Assert.AreEqual("-33", r.CodigoRetorno);
            Assert.IsNull(r.Error, "Los códigos de Anula no son los de GrabaServicios: no se traduce con aquella tabla");
        }

        [TestMethod]
        public void Interpretar_RespuestasRotas_Fallo()
        {
            Assert.IsFalse(RespuestaAnulaGls.Interpretar(string.Empty).Exito);
            Assert.IsFalse(RespuestaAnulaGls.Interpretar("<no es xml").Exito);
            ResultadoAnulaGls fault = RespuestaAnulaGls.Interpretar(@"<?xml version=""1.0""?><soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body><soap:Fault><faultstring>Server was unable to process request.</faultstring></soap:Fault></soap:Body></soap:Envelope>");
            Assert.IsFalse(fault.Exito);
            StringAssert.Contains(fault.Error, "Server was unable to process request.");
            ResultadoAnulaGls deGrabar = RespuestaAnulaGls.Interpretar(TramitadorGlsTests.RESPUESTA_OK);
            Assert.IsFalse(deGrabar.Exito, "Una respuesta de GrabaServicios no es una anulación");
            StringAssert.Contains(deGrabar.Error, "AnulaResult");
        }

        // ---------- La estrategia ----------

        private static (AnulacionGls Anulacion, IClienteSoapGls Cliente) Anulacion(string uid = UID_AGENCIA)
        {
            var cliente = A.Fake<IClienteSoapGls>();
            A.CallTo(() => cliente.Url).Returns(PeticionGls.URL_ANULA);
            return (new AnulacionGls(cliente, _ => Task.FromResult(uid)), cliente);
        }

        [TestMethod]
        public async Task Anular_GlsAnula_ExitoYQuedaElIntercambio()
        {
            var (anulacion, cliente) = Anulacion();
            string mandado = null;
            A.CallTo(() => cliente.EnviarAsync(A<string>._)).Invokes((string s) => mandado = s).Returns(Task.FromResult(RespuestaAnula("0")));

            ResultadoOperacionRemota r = await anulacion.AnularAsync(ALBARAN + "  ");

            Assert.IsTrue(r.Exito);
            Assert.AreEqual(PeticionGls.ConstruirSoap(PeticionGls.ConstruirAnulacion(ALBARAN, UID_AGENCIA), PeticionGls.OPERACION_ANULA), mandado);
            IntercambioRemoto intercambio = anulacion.Intercambios.Single();
            Assert.AreEqual(PeticionGls.OPERACION_ANULA, intercambio.Operacion);
            Assert.AreEqual(PeticionGls.URL_ANULA, intercambio.Url);
            Assert.AreEqual(mandado, intercambio.Peticion);
        }

        [TestMethod]
        public async Task Anular_GlsNoAnula_FalloConSuMotivoDeUsted()
        {
            var (anulacion, cliente) = Anulacion();
            A.CallTo(() => cliente.EnviarAsync(A<string>._)).Returns(Task.FromResult(RespuestaAnula("-1", resultado: "Expedición ya en reparto.")));

            ResultadoOperacionRemota r = await anulacion.AnularAsync(ALBARAN);

            Assert.IsFalse(r.Exito);
            Assert.AreEqual($"GLS no ha anulado el envío con albarán {ALBARAN}: Expedición ya en reparto. El envío sigue como estaba. " +
                "Si ya no se puede anular, pídaselo a GLS por correo o por teléfono.", r.Error);
        }

        [TestMethod]
        public void MensajeNoAnulado_SinTextoDeGls_DiceElCodigo()
        {
            Assert.AreEqual($"GLS no ha anulado el envío con albarán {ALBARAN}: GLS ha contestado con el código -7 sin decir el motivo. " +
                "El envío sigue como estaba. Si ya no se puede anular, pídaselo a GLS por correo o por teléfono.",
                AnulacionGls.MensajeNoAnulado(ALBARAN, new ResultadoAnulaGls { CodigoRetorno = "-7" }));
        }

        [TestMethod]
        public async Task Anular_SinCuentaDelEnvio_NoLlamaAGls()
        {
            var (anulacion, cliente) = Anulacion(uid: null);

            ResultadoOperacionRemota r = await anulacion.AnularAsync(ALBARAN);

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "no se sabe con qué cuenta de GLS se grabó");
            A.CallTo(() => cliente.EnviarAsync(A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, anulacion.Intercambios.Count);
        }

        [TestMethod]
        public async Task Anular_GlsNoResponde_LanzaYLaPeticionQuedaRegistrada()
        {
            var (anulacion, cliente) = Anulacion();
            A.CallTo(() => cliente.EnviarAsync(A<string>._))
                .ThrowsAsync(new AgenciaRemotaException("GLS (Anula): la petición agotó el tiempo de espera.") { EsTransitoria = true });

            var ex = await Assert.ThrowsExceptionAsync<AgenciaRemotaException>(() => anulacion.AnularAsync(ALBARAN));

            Assert.IsTrue(FallosAgenciaRemota.NoResponde(ex));
            StringAssert.Contains(anulacion.Intercambios.Single().Peticion, "<Anula xmlns=\"http://www.asmred.com/\">");
        }

        // ---------- El transporte (sin salir a la red) ----------

        private class ManejadorFalso : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public HttpRequestMessage Recibida { get; private set; }

            public ManejadorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) { _responder = responder; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Recibida = request;
                return Task.FromResult(_responder(request));
            }
        }

        [TestMethod]
        public async Task Cliente_MandaElSoapConLaSoapActionDeAnula()
        {
            var manejador = new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(RespuestaAnula("0")) });
            var cliente = new ClienteSoapGls(PeticionGls.OPERACION_ANULA, PeticionGls.URL_ANULA, new HttpClient(manejador));

            string respuesta = await cliente.EnviarAsync("<soap/>");

            Assert.AreEqual(RespuestaAnula("0"), respuesta);
            Assert.AreEqual("https://www.asmred.com/WebSrvs/b2b.asmx?op=Anula", manejador.Recibida.RequestUri.ToString());
            Assert.AreEqual("\"http://www.asmred.com/Anula\"", manejador.Recibida.Headers.GetValues("SOAPAction").Single());
        }

        [TestMethod]
        public async Task Cliente_GrabaServiciosSigueConSuSoapAction()
        {
            var manejador = new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TramitadorGlsTests.RESPUESTA_OK) });

            _ = await new ClienteGrabaServiciosGls(new HttpClient(manejador)).EnviarAsync("<soap/>");

            Assert.AreEqual(PeticionGls.URL_GRABA_SERVICIOS, manejador.Recibida.RequestUri.ToString());
            Assert.AreEqual("\"http://www.asmred.com/GrabaServicios\"", manejador.Recibida.Headers.GetValues("SOAPAction").Single());
        }

        [TestMethod]
        public async Task Cliente_ServidorCaidoSinXml_TransitoriaConLaOperacion()
        {
            var manejador = new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Service Unavailable") });
            var cliente = new ClienteSoapGls(PeticionGls.OPERACION_ANULA, PeticionGls.URL_ANULA, new HttpClient(manejador));

            var ex = await Assert.ThrowsExceptionAsync<AgenciaRemotaException>(() => cliente.EnviarAsync("<soap/>"));

            Assert.IsTrue(ex.EsTransitoria);
            StringAssert.StartsWith(ex.Message, "GLS (Anula) respondió HTTP 503");
        }

        // ---------- Interruptor, perfil y fábrica ----------

        [TestMethod]
        public void Perfil_InterruptorApagado_GlsNoAnulaPorLaApi()
        {
            var perfil = new PerfilAgenciaGls { AnularPorApi = () => false };

            Assert.IsNull(perfil.CrearAnulacion(A.Fake<NVEntities>()));
        }

        [TestMethod]
        public void Perfil_InterruptorEncendido_LaAnulacionDeGls()
        {
            var perfil = new PerfilAgenciaGls { AnularPorApi = () => true };

            Assert.IsInstanceOfType(perfil.CrearAnulacion(A.Fake<NVEntities>()), typeof(AnulacionGls));
        }

        [TestMethod]
        public void Perfil_PorDefectoLeeElInterruptorDelConfig_Apagado()
        {
            // El app.config de las pruebas no tiene la clave: apagado, como en producción mientras no se encienda.
            Assert.IsNull(new PerfilAgenciaGls().CrearAnulacion(A.Fake<NVEntities>()));
        }

        private static IFabricaAgenciasRemotas Fabrica(params IPerfilAgencia[] perfiles)
        {
            var gate = A.Fake<IGateAgenciasActivas>();
            A.CallTo(() => gate.EstaActiva(A<int>._)).Returns(true);
            return new FabricaAgenciasRemotas(A.Fake<NVEntities>(), new RegistroAgencias(perfiles, gate));
        }

        [TestMethod]
        public void Fabrica_AgenciaConGestionRemotaCompleta_AnulaConSuPropiaEstrategia()
        {
            var agencia = A.Fake<IAgenciaRemota>();
            var perfil = A.Fake<IPerfilConGestionRemota>();
            A.CallTo(() => perfil.AgenciaId).Returns(Constantes.Agencias.AGENCIA_CTT);
            A.CallTo(() => perfil.CrearGestionRemota(A<NVEntities>._)).Returns(agencia);

            Assert.AreSame(agencia, Fabrica(perfil).CrearAnulacion(Constantes.Agencias.AGENCIA_CTT));
        }

        [TestMethod]
        public void Fabrica_Gls_SoloConElInterruptorEncendido()
        {
            Assert.IsNull(Fabrica(new PerfilAgenciaGls { AnularPorApi = () => false }).CrearAnulacion(Constantes.Agencias.AGENCIA_GLS));
            Assert.IsInstanceOfType(Fabrica(new PerfilAgenciaGls { AnularPorApi = () => true }).CrearAnulacion(Constantes.Agencias.AGENCIA_GLS), typeof(AnulacionGls));
            Assert.IsNull(Fabrica(new PerfilAgenciaGls { AnularPorApi = () => true }).Crear(Constantes.Agencias.AGENCIA_GLS),
                "Anular no convierte a GLS en agencia con gestión remota completa");
        }

        [TestMethod]
        public void Fabrica_AgenciaSinPerfilNiCapacidad_Null()
        {
            Assert.IsNull(Fabrica().CrearAnulacion(Constantes.Agencias.AGENCIA_CANTERAS));
        }

        [TestMethod]
        public void WebConfig_InterruptorDeAnularGlsPorLaApi_ExisteYNaceApagado()
        {
            // bin\Debug -> NestoAPI.Tests -> raíz de la solución -> NestoAPI\Web.config
            string ruta = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\NestoAPI\Web.config"));
            Assert.IsTrue(File.Exists(ruta), $"No se encuentra el Web.config en {ruta}");
            var xml = new XmlDocument();
            xml.Load(ruta);
            XmlNode nodo = xml.SelectSingleNode("/configuration/appSettings/add[@key='" + InterruptorAnulacionGls.CLAVE + "']");

            Assert.IsNotNull(nodo, "Falta la clave del interruptor en Web.config");
            Assert.IsFalse(InterruptorTramitacionGls.EstaActivo(nodo.Attributes["value"].Value),
                "La anulación de GLS por la API nace apagada (#552, fase 3): Anula no se ha probado contra GLS. Si se enciende, que sea a propósito y cambiando este test");
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
