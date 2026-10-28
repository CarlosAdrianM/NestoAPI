using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;
using NestoAPI.Tests.Infrastructure.Agencias;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#552 (fase 3): POST api/EnviosAgencias/{id}/Anular con un envío de GLS. El núcleo es el mismo que con CTT e
    /// Innovatrans (En curso con albarán, reembolso sin pagar, GLS primero y BD después, auditoría); lo de GLS es la
    /// estrategia, que solo existe con Agencias:GlsAnularPorApi encendido. Nada sale hacia GLS.
    /// </summary>
    [TestClass]
    public class AnularEnvioGlsTests
    {
        private const string ALBARAN = "61197140249723";

        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<AgenciaLlamadaWeb> fakeLlamadas;
        private IFabricaAgenciasRemotas fakeFabrica;
        private IClienteSoapGls fakeGls;
        private PerfilAgenciaGls perfil;
        private EnviosAgenciasController controller;
        private readonly List<AgenciaLlamadaWeb> auditorias = new List<AgenciaLlamadaWeb>();

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

            fakeGls = A.Fake<IClienteSoapGls>();
            A.CallTo(() => fakeGls.Url).Returns(PeticionGls.URL_ANULA);
            GlsResponde(AnulacionGlsTests.RespuestaAnula("0"));

            // La fábrica hace lo que la real: GLS no tiene gestión remota completa, y su anulación la da su perfil, que mira
            // el interruptor. Aquí la cuenta del envío no se busca en la BD.
            perfil = new PerfilAgenciaGls { AnularPorApi = () => true };
            fakeFabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fakeFabrica.Crear(A<int>._)).Returns(null);
            A.CallTo(() => fakeFabrica.CrearAnulacion(Constantes.Agencias.AGENCIA_GLS)).ReturnsLazily(() =>
                perfil.AnularPorApi() ? new AnulacionGls(fakeGls, _ => Task.FromResult(PeticionGls.IDENTIFICADOR_BUSINESSPARCEL)) : null);

            controller = new EnviosAgenciasController(db, fakeFabrica)
            {
                MotivoPedidoRetenido = (e, p) => Task.FromResult<string>(null), // NestoAPI#698: sin BD en los tests
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/249723/Anular") }
            };
        }

        private void GlsResponde(string respuesta) => A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).Returns(Task.FromResult(respuesta));

        private EnviosAgencia ConEnvio(Action<EnviosAgencia> cambiar = null)
        {
            EnviosAgencia envio = TramitadorGlsTests.Envio();
            envio.AgenciasTransporte = new AgenciaTransporte { Numero = envio.Agencia, Nombre = "ASM" };
            cambiar?.Invoke(envio);
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
            return envio;
        }

        [TestMethod]
        public async Task InterruptorApagado_GlsSigueDandoElBadRequestDeSiempreYNoLlamaAGls()
        {
            perfil.AnularPorApi = () => false;
            EnviosAgencia envio = ConEnvio();

            var resultado = await controller.AnularEnvio(envio.Numero) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("La agencia 1 no tiene gestión remota en el servidor.", resultado.Message);
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(ALBARAN, envio.CodigoBarras);
            Assert.AreEqual(0, auditorias.Count);
        }

        [TestMethod]
        public async Task Exito_AnuladoEnGlsVuelveAEtiquetaPendienteYQuedaAuditado()
        {
            EnviosAgencia envio = ConEnvio();

            var resultado = await controller.AnularEnvio(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EnvioAgenciaDTO>));
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_PENDIENTE, envio.Estado);
            Assert.AreEqual(string.Empty, envio.CodigoBarras);
            AgenciaLlamadaWeb auditoria = auditorias.Single();
            Assert.IsTrue(auditoria.Exito);
            Assert.AreEqual("ASM", auditoria.Agencia, "Como las llamadas de Nesto a GLS");
            Assert.AreEqual(PeticionGls.URL_ANULA, auditoria.UrlLlamada);
            StringAssert.StartsWith(auditoria.CuerpoLlamada, "Anular envío 249723");
            StringAssert.Contains(auditoria.CuerpoLlamada, $"<Envio codbarras=\"{ALBARAN}\" />");
            Assert.IsNull(PeticionGls.ServiciosDeLaPeticion(auditoria.CuerpoLlamada), "La sombra de GLS no la toma por una grabación");
        }

        [TestMethod]
        public async Task GlsNoLoAnula_502ConSuMotivoDeUstedYBdIntacta()
        {
            EnviosAgencia envio = ConEnvio();
            GlsResponde(AnulacionGlsTests.RespuestaAnula("-1", resultado: "Expedición ya en reparto"));

            var resultado = await controller.AnularEnvio(envio.Numero) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.StatusCode);
            Assert.AreEqual($"GLS no ha anulado el envío con albarán {ALBARAN}: Expedición ya en reparto. El envío sigue como estaba. " +
                "Si ya no se puede anular, pídaselo a GLS por correo o por teléfono.", resultado.Content);
            Assert.AreEqual(ALBARAN, envio.CodigoBarras);
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_EN_CURSO, envio.Estado);
            Assert.IsFalse(auditorias.Single().Exito);
        }

        [TestMethod]
        public async Task GlsNoResponde_502AgenciaNoRespondeQueProponeOtraAgenciaYBdIntacta()
        {
            EnviosAgencia envio = ConEnvio();
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._))
                .ThrowsAsync(new AgenciaRemotaException("GLS (Anula) respondió HTTP 503: Service Unavailable") { EsTransitoria = true });

            var resultado = await controller.AnularEnvio(envio.Numero) as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.Response.StatusCode);
            var cuerpo = Newtonsoft.Json.Linq.JObject.Parse(await resultado.Response.Content.ReadAsStringAsync());
            Assert.AreEqual(EnviosAgenciasController.CODIGO_AGENCIA_NO_RESPONDE, (string)cuerpo["Codigo"]);
            Assert.AreEqual(FallosAgenciaRemota.MensajeNoResponde("GLS"), (string)cuerpo["Message"]);
            StringAssert.Contains((string)cuerpo["Message"], "sáquelo por otra agencia", "No «sáquelo por GLS», que es la que no responde");
            Assert.AreEqual(ALBARAN, envio.CodigoBarras);
            AgenciaLlamadaWeb auditoria = auditorias.Single();
            Assert.IsFalse(auditoria.Exito);
            StringAssert.Contains(auditoria.CuerpoLlamada, "<Anula xmlns=\"http://www.asmred.com/\">", "La petición queda aunque GLS no conteste");
        }

        [TestMethod]
        public async Task EnvioYaTramitado_BadRequestSinLlamarAGls()
        {
            // Un GLS tramitado tiene el reembolso contabilizado: deshacerlo es una decisión pendiente (fuera de la fase 3).
            EnviosAgencia envio = ConEnvio(e => e.Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO);

            var resultado = await controller.AnularEnvio(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
        }

        [TestMethod]
        public async Task ReembolsoYaPagado_BadRequestSinLlamarAGls()
        {
            EnviosAgencia envio = ConEnvio(e => e.FechaPagoReembolso = new DateTime(2026, 10, 10));

            var resultado = await controller.AnularEnvio(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Modificar_GlsSigueSinModificarPorLaApi()
        {
            // Fase 3: modificar en GLS queda fuera (GLS no tiene operación de modificar y Nesto nunca lo ha hecho).
            EnviosAgencia envio = ConEnvio();

            var resultado = await controller.ModificarEnvio(envio.Numero, new ModificarEnvioAgenciaDTO { CodigoPostal = "28001" });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
            Assert.AreEqual("28043", envio.CodPostal);
        }
    }
}
