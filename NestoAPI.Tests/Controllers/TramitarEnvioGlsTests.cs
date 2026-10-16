using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http.Results;
using System.Xml.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Models;
using NestoAPI.Tests.Infrastructure.Agencias;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#552 (fase 2): POST api/EnviosAgencias/{id}/Tramitar con un envío de GLS. Con el interruptor apagado, el 400 de
    /// siempre; encendido, la API lo graba en GLS (fake: nada sale hacia GLS) y deja la llamada en AgenciasLlamadasWeb como Nesto.
    /// </summary>
    [TestClass]
    public class TramitarEnvioGlsTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 16);

        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<AgenciaLlamadaWeb> fakeLlamadas;
        private IFabricaAgenciasRemotas fakeFabrica;
        private IClienteGrabaServiciosGls fakeGls;
        private EnviosAgenciasController controller;
        private readonly List<AgenciaLlamadaWeb> auditorias = new List<AgenciaLlamadaWeb>();
        private string soapMandado;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.Ignored))
                .Invokes((AgenciaLlamadaWeb l) => auditorias.Add(l))
                .ReturnsLazily((AgenciaLlamadaWeb l) => l);

            fakeFabrica = A.Fake<IFabricaAgenciasRemotas>();
            fakeGls = A.Fake<IClienteGrabaServiciosGls>();
            A.CallTo(() => fakeGls.Url).Returns(PeticionGls.URL_GRABA_SERVICIOS);
            GlsResponde(TramitadorGlsTests.RESPUESTA_OK);

            controller = new EnviosAgenciasController(db, fakeFabrica)
            {
                GlsTramitarPorApi = () => true,
                ClienteGls = fakeGls,
                HoyGls = () => HOY,
                CargarDatosGls = e => Task.FromResult((TramitadorGlsTests.EmpresaNuevaVision(),
                    new AgenciaTransporte { Numero = 1, Nombre = "ASM", Identificador = "9ABAEE33", PrefijoCodigoBarras = "6108352" })),
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/1/Tramitar") }
            };
        }

        private void GlsResponde(string respuesta)
            => A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).Invokes((string soap) => soapMandado = soap).Returns(Task.FromResult(respuesta));

        private EnviosAgencia ConEnvio(Action<EnviosAgencia> cambiar = null)
        {
            EnviosAgencia envio = TramitadorGlsTests.Envio();
            cambiar?.Invoke(envio);
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
            return envio;
        }

        [TestMethod]
        public async Task InterruptorApagado_GlsSigueDandoElBadRequestDeSiempreYNoLlamaAGls()
        {
            controller.GlsTramitarPorApi = () => false;
            EnviosAgencia envio = ConEnvio();
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_GLS)).Returns(null);

            var resultado = await controller.TramitarEnvio(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(System.Web.Http.Results.BadRequestErrorMessageResult));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, auditorias.Count);
        }

        [TestMethod]
        public async Task InterruptorEncendido_OtrasAgenciasNoCambian()
        {
            EnviosAgencia envio = ConEnvio(e => e.Agencia = Constantes.Agencias.AGENCIA_CANTERAS);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_CANTERAS)).Returns(null);

            var resultado = await controller.TramitarEnvio(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(System.Web.Http.Results.BadRequestErrorMessageResult));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Exito_DevuelveCodigoDeBarrasYExpedicionSinCerrarElEnvio()
        {
            EnviosAgencia envio = ConEnvio();

            var resultado = await controller.TramitarEnvio(envio.Numero) as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("61197140249723", resultado.Content.Albaran);
            Assert.AreEqual("1365123979", resultado.Content.Expedicion);
            Assert.AreEqual(2, resultado.Content.Bultos);
            Assert.IsFalse(resultado.Content.Reimpresion);
            Assert.IsNull(resultado.Content.EtiquetaContenido, "GrabaServicios no devuelve etiqueta: la sigue montando Nesto");
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_EN_CURSO, envio.Estado, "Cerrarlo es ConfirmarTramitacion, el segundo paso");
            Assert.AreEqual(new DateTime(2026, 10, 7), envio.Fecha);
        }

        [TestMethod]
        public async Task Exito_MandaElDiaDeTramitacionYElUidDeLaCuenta()
        {
            EnviosAgencia envio = ConEnvio(e => e.Servicio = 6);
            envio.Horario = 10;

            _ = await controller.TramitarEnvio(envio.Numero);

            XElement servicios = PeticionGls.ServiciosDeLaPeticion(soapMandado);
            Assert.AreEqual("16/10/26", servicios.Element(PeticionGls.Ns + "Envio").Element(PeticionGls.Ns + "Fecha").Value);
            Assert.AreEqual("9ABAEE33", servicios.Attribute("uidcliente").Value, "Fuera de BusinessParcel, el Identificador de la agencia");
        }

        [TestMethod]
        public async Task Exito_AuditaComoNestoParaQueLaSombraLaSigaLeyendo()
        {
            EnviosAgencia envio = ConEnvio();

            _ = await controller.TramitarEnvio(envio.Numero);

            AgenciaLlamadaWeb llamada = auditorias.Single();
            Assert.AreEqual(SombraPeticionesGls.AGENCIA_LLAMADAS, llamada.Agencia);
            Assert.IsTrue(llamada.Exito);
            Assert.AreEqual("OK", llamada.TextoRespuestaError);
            Assert.AreEqual(PeticionGls.URL_GRABA_SERVICIOS, llamada.UrlLlamada);
            Assert.AreEqual(soapMandado, llamada.CuerpoLlamada);
            Assert.AreEqual(TramitadorGlsTests.RESPUESTA_OK, llamada.CuerpoRespuesta);

            // La sombra la lee y, con el envío aún sin cerrar (fecha de creación), no da falso aviso por la fecha.
            ResultadoSombraGls sombra = SombraPeticionesGls.Comparar(
                new[] { new PeticionGlsGuardada { Llamada = 1, Fecha = HOY.AddHours(10), Cuerpo = llamada.CuerpoLlamada } },
                codigo => new EnvioParaPeticionGls { Envio = envio, Empresa = TramitadorGlsTests.EmpresaNuevaVision(), IdentificadorAgencia = "9ABAEE33" });
            Assert.AreEqual(1, sombra.Iguales);
            Assert.AreEqual(1, sombra.FechasReescritasAlTramitar);
        }

        [TestMethod]
        public async Task YaRegistradoEnGls_SeDaPorBuenoYQuedaAuditadoComoEnNesto()
        {
            GlsResponde(TramitadorGlsTests.Respuesta("-33", "Ya existe el codigo de barras de la expedicion"));
            EnviosAgencia envio = ConEnvio();

            var resultado = await controller.TramitarEnvio(envio.Numero) as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;

            Assert.IsNotNull(resultado, "Repetir la tramitación es inocuo: GLS ya lo tiene");
            Assert.IsTrue(resultado.Content.Reimpresion);
            Assert.IsFalse(auditorias.Single().Exito, "Nesto guarda el -33 con Exito = 0 (la sombra solo mira las de éxito)");
            StringAssert.Contains(auditorias.Single().TextoRespuestaError, "Ya existe");
        }

        [TestMethod]
        public async Task GlsRechaza_502ConElMotivoDeGlsYAuditado()
        {
            GlsResponde(TramitadorGlsTests.Respuesta("-70"));
            EnviosAgencia envio = ConEnvio();

            var resultado = await controller.TramitarEnvio(envio.Numero) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.StatusCode);
            Assert.AreEqual("GLS no ha aceptado el envío 249723: Ya se ha enviado este pedido para esta fecha y cliente", resultado.Content);
            Assert.IsFalse(auditorias.Single().Exito);
            Assert.AreEqual("Ya se ha enviado este pedido para esta fecha y cliente", auditorias.Single().TextoRespuestaError);
        }

        [TestMethod]
        public async Task GlsNoResponde_502ConMensajeClaroYCodigoYAuditaLaPeticion()
        {
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._))
                .Throws(new AgenciaRemotaException("GLS (GrabaServicios) respondió HTTP 503: caído") { EsTransitoria = true });
            EnviosAgencia envio = ConEnvio();

            var resultado = await controller.TramitarEnvio(envio.Numero) as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.Response.StatusCode);
            var cuerpo = Newtonsoft.Json.Linq.JObject.Parse(await resultado.Response.Content.ReadAsStringAsync());
            Assert.AreEqual(EnviosAgenciasController.CODIGO_AGENCIA_NO_RESPONDE, (string)cuerpo["Codigo"]);
            Assert.AreEqual("GLS no responde ahora mismo (error de su servidor). Vuelve a intentarlo en unos minutos o, si es urgente, sácalo por otra agencia.",
                (string)cuerpo["Message"]);
            AgenciaLlamadaWeb llamada = auditorias.Single();
            Assert.IsFalse(llamada.Exito);
            Assert.IsNotNull(PeticionGls.ServiciosDeLaPeticion(llamada.CuerpoLlamada));
            Assert.AreEqual(string.Empty, llamada.CuerpoRespuesta);
        }

        [TestMethod]
        public async Task EnvioYaTramitado_409SinLlamarAGls()
        {
            EnviosAgencia envio = ConEnvio(e => e.Estado = Constantes.Agencias.ESTADO_TRAMITADO);

            var resultado = await controller.TramitarEnvio(envio.Numero) as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ServicioQueNoEsDeGls_400SinLlamarAGls()
        {
            EnviosAgencia envio = ConEnvio(e => { e.Servicio = 48; e.Horario = 0; });

            var resultado = await controller.TramitarEnvio(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(System.Web.Http.Results.BadRequestErrorMessageResult));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinCodigoDeBarras_LoCalculaComoAgenciaAsmYLoGuardaAntesDeLlamar()
        {
            EnviosAgencia envio = ConEnvio(e => e.CodigoBarras = null);

            var resultado = await controller.TramitarEnvio(envio.Numero) as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;

            Assert.AreEqual("61197140249723", envio.CodigoBarras);
            Assert.AreEqual("61197140249723", resultado.Content.Albaran);
            StringAssert.Contains(soapMandado, "codbarras=\"61197140249723\"");
        }

        [TestMethod]
        public async Task ReembolsoCentinela_ViajaACero()
        {
            EnviosAgencia envio = ConEnvio(e => e.Reembolso = -1M);

            _ = await controller.TramitarEnvio(envio.Numero);

            XElement servicios = PeticionGls.ServiciosDeLaPeticion(soapMandado);
            Assert.AreEqual(0M, decimal.Parse(servicios.Descendants(PeticionGls.Ns + "Reembolso").Single().Value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
