using System;
using System.Collections.Generic;
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
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#552 (fase 2): la API graba los envíos de GLS ella misma (detrás de Agencias:GlsTramitarPorApi). Nada sale
    /// hacia GLS: el cliente del WS es un fake. Las respuestas son de AgenciasLlamadasWeb (octubre de 2026).
    /// </summary>
    [TestClass]
    public class TramitadorGlsTests
    {
        internal const string RESPUESTA_OK = @"<?xml version=""1.0"" encoding=""utf-8""?><soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema""><soap:Body><GrabaServiciosResponse xmlns=""http://www.asmred.com/""><GrabaServiciosResult><Servicios xmlns=""""><Envio codbarras=""61197140249723"" uid=""ee988f72-d15a-410e-acd6-70aa2707553c"" codexp=""1365123979""><Resultado return=""0"" /><Errores /><Referencias><Referencia tipo=""0"">32624928379</Referencia><Referencia tipo=""C"">32624/928379</Referencia></Referencias><PlazaDestino><Codigo>850</Codigo><Nombre>SILLEDA</Nombre><Nemonico>G01</Nemonico><Responsable>X</Responsable><Telefono>910806229</Telefono><Fax></Fax><Mail>agencia.850@gls-spain.es</Mail><Direccion>Parque</Direccion><Poblacion>Silleda</Poblacion><Provincia>Pontevedra</Provincia><CodigoPostal>36540</CodigoPostal><Horario></Horario><Zona>0</Zona><TSP>SCQ</TSP></PlazaDestino></Envio></Servicios></GrabaServiciosResult></GrabaServiciosResponse></soap:Body></soap:Envelope>";

        internal static string Respuesta(string retorno, string error = null) =>
            @"<?xml version=""1.0"" encoding=""utf-8""?><soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body><GrabaServiciosResponse xmlns=""http://www.asmred.com/""><GrabaServiciosResult><Servicios xmlns=""""><Envio codbarras=""61197140249723"">" +
            $@"<Resultado return=""{retorno}"" />" +
            (error == null ? "<Errores />" : $"<Errores><Error>{error}</Error></Errores>") +
            "</Envio></Servicios></GrabaServiciosResult></GrabaServiciosResponse></soap:Body></soap:Envelope>";

        internal static EnviosAgencia Envio() => new EnviosAgencia
        {
            Numero = 249723, Empresa = "1  ", Agencia = 1, Cliente = "32624     ", Pedido = 928379, Fecha = new DateTime(2026, 10, 7),
            Servicio = 96, Horario = 18, Bultos = 2, Retorno = 0, Nombre = "CENTRO PRUEBA", Direccion = "C/ INVENTADA, 1",
            Poblacion = "MADRID", Provincia = "MADRID", Pais = 34, CodPostal = "28043", Telefono = "910000000", Movil = "",
            Email = "", Observaciones = "", Atencion = "", Reembolso = 0M, CodigoBarras = "61197140249723",
            Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO
        };

        internal static Empresa EmpresaNuevaVision() => new Empresa
        {
            Número = "1  ", Nombre = "Nueva Visión, S.A.", Dirección = "C/ Río Tiétar, 11 - Nave 22", Población = "ALGETE",
            Provincia = "Madrid", CodPostal = "28119", Teléfono = "916281914", Email = "nuevavision@nuevavision.es"
        };

        // ---------- Interpretar la respuesta ----------

        [TestMethod]
        public void Interpretar_RespuestaRealOk_ExitoConExpedicionYPlaza()
        {
            ResultadoGrabaServiciosGls r = RespuestaGrabaServiciosGls.Interpretar(RESPUESTA_OK);

            Assert.IsTrue(r.Exito);
            Assert.IsTrue(r.Aceptado);
            Assert.IsFalse(r.YaRegistrado);
            Assert.IsNull(r.Error);
            Assert.AreEqual("0", r.CodigoRetorno);
            Assert.AreEqual("1365123979", r.CodigoExpedicion);
            Assert.AreEqual("ee988f72-d15a-410e-acd6-70aa2707553c", r.Uid);
            Assert.AreEqual("G01", r.Plaza.Nemonico);
            Assert.AreEqual("SILLEDA", r.Plaza.Nombre);
            Assert.AreEqual("SCQ", r.Plaza.Tsp);
        }

        [TestMethod]
        public void Interpretar_YaExisteElCodigoDeBarras_SeDaPorBuenoComoEnNesto()
        {
            ResultadoGrabaServiciosGls r = RespuestaGrabaServiciosGls.Interpretar(Respuesta("-33", "Ya existe el codigo de barras de la expedicion"));

            Assert.IsFalse(r.Exito);
            Assert.IsTrue(r.YaRegistrado);
            Assert.IsTrue(r.Aceptado);
        }

        [TestMethod]
        public void Interpretar_Menos33SinTexto_TambienEsYaRegistrado()
        {
            ResultadoGrabaServiciosGls r = RespuestaGrabaServiciosGls.Interpretar(Respuesta("-33"));

            Assert.IsTrue(r.YaRegistrado);
            Assert.AreEqual("Ya existe el código de barras de la expedición", r.Error);
        }

        [TestMethod]
        public void Interpretar_RechazoSinTextoDeGls_UsaLaTablaDeAgenciaAsmYNoSeAcepta()
        {
            ResultadoGrabaServiciosGls r = RespuestaGrabaServiciosGls.Interpretar(Respuesta("-70"));

            Assert.IsFalse(r.Aceptado);
            Assert.AreEqual("-70", r.CodigoRetorno);
            Assert.AreEqual("Ya se ha enviado este pedido para esta fecha y cliente", r.Error);
        }

        [TestMethod]
        public void Interpretar_RechazoConTextoDeGls_UsaElTextoDeGls()
        {
            ResultadoGrabaServiciosGls r = RespuestaGrabaServiciosGls.Interpretar(Respuesta("-128", "Nombre destinatario corto"));

            Assert.IsFalse(r.Aceptado);
            Assert.AreEqual("Nombre destinatario corto", r.Error);
        }

        [TestMethod]
        public void EsYaRegistrado_ComoAgenciaAsm_SoloElMenos33()
        {
            Assert.IsTrue(RespuestaGrabaServiciosGls.EsYaRegistrado("Ya existe el código de barras de la expedición"));
            Assert.IsTrue(RespuestaGrabaServiciosGls.EsYaRegistrado("YA EXISTE EL CODIGO DE BARRAS"));
            Assert.IsTrue(RespuestaGrabaServiciosGls.EsYaRegistrado("Error -33"));
            Assert.IsFalse(RespuestaGrabaServiciosGls.EsYaRegistrado("Error -330"));
            Assert.IsFalse(RespuestaGrabaServiciosGls.EsYaRegistrado("Ya existe el albarán"), "Podría ser el -70: no se perdona");
            Assert.IsFalse(RespuestaGrabaServiciosGls.EsYaRegistrado(null));
        }

        [TestMethod]
        public void Interpretar_SoapFault_NoSeAceptaYDiceElMotivo()
        {
            const string fault = @"<?xml version=""1.0"" encoding=""utf-8""?><soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body><soap:Fault><faultcode>soap:Server</faultcode><faultstring>Server was unable to process request.</faultstring></soap:Fault></soap:Body></soap:Envelope>";

            ResultadoGrabaServiciosGls r = RespuestaGrabaServiciosGls.Interpretar(fault);

            Assert.IsFalse(r.Aceptado);
            StringAssert.Contains(r.Error, "Server was unable to process request.");
        }

        [TestMethod]
        public void Interpretar_VaciaOQueNoEsXml_NoSeAcepta()
        {
            Assert.IsFalse(RespuestaGrabaServiciosGls.Interpretar("").Aceptado);
            Assert.IsFalse(RespuestaGrabaServiciosGls.Interpretar("<html>Error").Aceptado);
            StringAssert.Contains(RespuestaGrabaServiciosGls.Interpretar(null).Error, "vacía");
        }

        // ---------- Tramitar ----------

        [TestMethod]
        public async Task TramitarAsync_MandaLaPeticionDeNestoConLaFechaDelDiaDeTramitacion()
        {
            var cliente = A.Fake<IClienteGrabaServiciosGls>();
            string mandado = null;
            A.CallTo(() => cliente.Url).Returns(PeticionGls.URL_GRABA_SERVICIOS);
            A.CallTo(() => cliente.EnviarAsync(A<string>._)).Invokes((string soap) => mandado = soap).Returns(Task.FromResult(RESPUESTA_OK));
            EnviosAgencia envio = Envio();

            TramitacionGls t = await new TramitadorGls(cliente).TramitarAsync(envio, EmpresaNuevaVision(), "AGENCIA", new DateTime(2026, 10, 16, 9, 30, 0));

            Assert.AreEqual(mandado, t.Peticion);
            Assert.AreEqual(RESPUESTA_OK, t.Respuesta);
            Assert.AreEqual(PeticionGls.URL_GRABA_SERVICIOS, t.Url);
            Assert.IsTrue(t.Resultado.Exito);
            Assert.IsNull(t.Fallo);
            XElement servicios = PeticionGls.ServiciosDeLaPeticion(mandado);
            Assert.AreEqual("16/10/26", servicios.Element(PeticionGls.Ns + "Envio").Element(PeticionGls.Ns + "Fecha").Value,
                "Decisión de Carlos: el día de tramitación, no el de creación del envío");
            // Todo lo demás, igual que lo que construye Nesto (la petición de la fase 1, con la fecha del envío).
            List<DiferenciaPeticionGls> diferencias = ComparadorPeticionesGls.Comparar(servicios, PeticionGls.ConstruirServicios(envio, EmpresaNuevaVision(), "AGENCIA"));
            Assert.AreEqual(ComparadorPeticionesGls.CAMPO_FECHA, diferencias.Single().Campo);
            Assert.AreEqual(new DateTime(2026, 10, 7), envio.Fecha, "El tramitador no toca el envío");
        }

        [TestMethod]
        public void ConstruirServicios_SinFecha_SigueMandandoLaDelEnvio()
        {
            XElement servicios = PeticionGls.ConstruirServicios(Envio(), EmpresaNuevaVision(), "AGENCIA");

            Assert.AreEqual("07/10/26", servicios.Element(PeticionGls.Ns + "Envio").Element(PeticionGls.Ns + "Fecha").Value);
        }

        [TestMethod]
        public async Task TramitarAsync_GlsNoResponde_DevuelveElFalloSinResultadoYConLaPeticion()
        {
            var cliente = A.Fake<IClienteGrabaServiciosGls>();
            var caida = new AgenciaRemotaException("GLS (GrabaServicios) respondió HTTP 503: x") { EsTransitoria = true };
            A.CallTo(() => cliente.EnviarAsync(A<string>._)).Throws(caida);

            TramitacionGls t = await new TramitadorGls(cliente).TramitarAsync(Envio(), EmpresaNuevaVision(), "AGENCIA", DateTime.Today);

            Assert.AreSame(caida, t.Fallo);
            Assert.IsNull(t.Resultado);
            Assert.AreEqual(string.Empty, t.Respuesta);
            Assert.IsNotNull(PeticionGls.ServiciosDeLaPeticion(t.Peticion), "La petición queda para auditarla");
        }

        [TestMethod]
        public void CalcularCodigoBarras_ComoAgenciaAsm()
        {
            EnviosAgencia envio = Envio();
            envio.Numero = 246221;
            Assert.AreEqual("61197140246221", TramitadorGls.CalcularCodigoBarras(envio, "6108352"), "BusinessParcel (96): su propio prefijo");
            envio.Servicio = 6;
            Assert.AreEqual("61083520246221", TramitadorGls.CalcularCodigoBarras(envio, " 6108352 "));
            Assert.IsNull(TramitadorGls.CalcularCodigoBarras(envio, null));
        }

        // ---------- Interruptor ----------

        [TestMethod]
        public void Interruptor_SoloTrueExplicitoEnciende()
        {
            Assert.IsTrue(InterruptorTramitacionGls.EstaActivo("true"));
            Assert.IsTrue(InterruptorTramitacionGls.EstaActivo(" True "));
            Assert.IsFalse(InterruptorTramitacionGls.EstaActivo(null));
            Assert.IsFalse(InterruptorTramitacionGls.EstaActivo(""));
            Assert.IsFalse(InterruptorTramitacionGls.EstaActivo("1"));
            Assert.IsFalse(InterruptorTramitacionGls.EstaActivo("false"));
        }

        [TestMethod]
        public void WebConfig_InterruptorDeGlsPorLaApi_ExisteYNaceApagado()
        {
            // bin\Debug -> NestoAPI.Tests -> raíz de la solución -> NestoAPI\Web.config
            string ruta = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\NestoAPI\Web.config"));
            Assert.IsTrue(File.Exists(ruta), $"No se encuentra el Web.config en {ruta}");
            var xml = new XmlDocument();
            xml.Load(ruta);
            XmlNode nodo = xml.SelectSingleNode("/configuration/appSettings/add[@key='" + InterruptorTramitacionGls.CLAVE + "']");

            Assert.IsNotNull(nodo, "Falta la clave del interruptor en Web.config");
            Assert.IsFalse(InterruptorTramitacionGls.EstaActivo(nodo.Attributes["value"].Value),
                "La tramitación de GLS por la API nace apagada (#552, fase 2); si se enciende, que sea a propósito y cambiando este test");
        }

        // ---------- Cliente HTTP (sin salir a la red) ----------

        private class ManejadorFalso : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public HttpRequestMessage Recibida { get; private set; }
            public string CuerpoRecibido { get; private set; }

            public ManejadorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) { _responder = responder; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Recibida = request;
                CuerpoRecibido = await request.Content.ReadAsStringAsync();
                return _responder(request);
            }
        }

        [TestMethod]
        public async Task Cliente_MandaElSoapConLaSoapActionDeGrabaServicios()
        {
            var manejador = new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(RESPUESTA_OK) });

            string respuesta = await new ClienteGrabaServiciosGls(new HttpClient(manejador)).EnviarAsync("<soap/>");

            Assert.AreEqual(RESPUESTA_OK, respuesta);
            Assert.AreEqual(HttpMethod.Post, manejador.Recibida.Method);
            Assert.AreEqual(PeticionGls.URL_GRABA_SERVICIOS, manejador.Recibida.RequestUri.ToString());
            Assert.AreEqual("\"http://www.asmred.com/GrabaServicios\"", manejador.Recibida.Headers.GetValues("SOAPAction").Single());
            Assert.AreEqual("text/xml", manejador.Recibida.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("<soap/>", manejador.CuerpoRecibido);
        }

        [TestMethod]
        public async Task Cliente_Http503SinXml_EsAgenciaQueNoResponde()
        {
            var manejador = new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Service Unavailable") });

            AgenciaRemotaException ex = await Assert.ThrowsExceptionAsync<AgenciaRemotaException>(
                () => new ClienteGrabaServiciosGls(new HttpClient(manejador)).EnviarAsync("<soap/>"));

            Assert.IsTrue(ex.EsTransitoria);
            Assert.IsTrue(FallosAgenciaRemota.NoResponde(ex));
        }

        [TestMethod]
        public async Task Cliente_SoapFaultConHttp500_SeDevuelveParaInterpretarlo()
        {
            const string fault = @"<?xml version=""1.0""?><soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/""><soap:Body><soap:Fault><faultstring>X</faultstring></soap:Fault></soap:Body></soap:Envelope>";
            var manejador = new ManejadorFalso(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(fault) });

            string respuesta = await new ClienteGrabaServiciosGls(new HttpClient(manejador)).EnviarAsync("<soap/>");

            Assert.AreEqual(fault, respuesta);
        }
    }
}
