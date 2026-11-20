using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>Carlos 20/11/26: los 4xx de las lecturas del repartidor se guardan (RepartoLecturasRechazadas) y se pueden consultar.</summary>
    [TestClass]
    public class LecturasRechazadasRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 20, 9, 43, 0);
        private IRepositorioLecturasRechazadas repositorio;
        private List<LecturaRechazadaReparto> guardadas;

        [TestInitialize]
        public void Setup()
        {
            guardadas = new List<LecturaRechazadaReparto>();
            repositorio = A.Fake<IRepositorioLecturasRechazadas>();
            A.CallTo(() => repositorio.ScriptAplicado()).Returns(true);
            A.CallTo(() => repositorio.Guardar(A<LecturaRechazadaReparto>._)).Invokes((LecturaRechazadaReparto l) => guardadas.Add(l)).Returns(Task.CompletedTask);
            ApuntadorLecturasRechazadas.Repositorio = () => repositorio;
            ApuntadorLecturasRechazadas.EnSegundoPlano = trabajo => trabajo().GetAwaiter().GetResult();
            ApuntadorLecturasRechazadas.ReiniciarAvisoParaTests();
        }

        [TestCleanup]
        public void Limpiar()
        {
            ApuntadorLecturasRechazadas.Repositorio = null;
            ApuntadorLecturasRechazadas.EnSegundoPlano = null;
        }

        // ================================================================ LA REGLA

        [TestMethod]
        public void Construir_409ConCodigo_ApuntaLoLeidoYElCodigo()
        {
            var args = new Dictionary<string, object> { ["peticion"] = new CargaBultoRepartoDTO { Codigo = "RP251169-1" } };
            object cuerpo = new { error = new { code = "EN_FURGONETA", message = "No se puede…" }, Message = "No se puede…", Codigo = "EN_FURGONETA" };

            LecturaRechazadaReparto l = ReglaLecturasRechazadas.Construir("Cargar", args, 409, cuerpo, @"NUEVAVISION\Andre", "PDA-3", "2.40", AHORA);

            Assert.AreEqual("RP251169-1", l.Codigo);
            Assert.AreEqual(409, l.Status);
            Assert.AreEqual("EN_FURGONETA", l.CodigoError);
            Assert.AreEqual("No se puede…", l.Mensaje);
            Assert.AreEqual("PDA-3", l.Dispositivo);
            Assert.AreEqual("2.40", l.Version);
            Assert.AreEqual(@"NUEVAVISION\Andre", l.Usuario);
            Assert.AreEqual("Cargar", l.Endpoint);
        }

        [TestMethod]
        public void Construir_404DeGetBulto_ElCodigoDeLaRuta()
        {
            LecturaRechazadaReparto l = ReglaLecturasRechazadas.Construir("GetBulto", new Dictionary<string, object> { ["codigo"] = " RP999-1 " }, 404,
                new { Message = "No existe el envío 999." }, "andre", null, null, AHORA);

            Assert.AreEqual("RP999-1", l.Codigo);
            Assert.AreEqual("No existe el envío 999.", l.Mensaje);
            Assert.IsNull(l.CodigoError);
        }

        [TestMethod]
        public void Construir_EntregarSinCodigo_ElEnvioDeLaRuta_YMensajeRecortado()
        {
            LecturaRechazadaReparto l = ReglaLecturasRechazadas.Construir("Entregar",
                new Dictionary<string, object> { ["envioId"] = 251169, ["peticion"] = new EntregaRepartoDTO() }, 400, new { Message = new string('x', 900) }, "andre", null, null, AHORA);

            Assert.AreEqual(251169, l.EnvioId);
            Assert.AreEqual("251169", l.Codigo);
            Assert.AreEqual(500, l.Mensaje.Length);
        }

        [TestMethod]
        public void Construir_NoSeApunta_2xx5xxYOtrosEndpoints()
        {
            Assert.IsNull(ReglaLecturasRechazadas.Construir("Cargar", null, 200, null, "a", null, null, AHORA));
            Assert.IsNull(ReglaLecturasRechazadas.Construir("Cargar", null, 500, null, "a", null, null, AHORA));
            Assert.IsNull(ReglaLecturasRechazadas.Construir("GetEnvios", null, 400, null, "a", null, null, AHORA));
            Assert.IsNotNull(ReglaLecturasRechazadas.Construir("NoRecogida", null, 403, "texto", "a", null, null, AHORA));
        }

        // ================================================================ el filtro (punto único)

        private static HttpActionExecutedContext Contexto(string accion, HttpStatusCode estado, object cuerpo, Dictionary<string, object> args)
        {
            var descriptor = A.Fake<HttpActionDescriptor>();
            A.CallTo(() => descriptor.ActionName).Returns(accion);
            var peticion = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/RepartoPropio/" + accion);
            peticion.Headers.Add("X-Ariadna-Version", "2.40");
            peticion.Headers.Add("X-Dispositivo", "PDA-3");
            var contexto = new HttpActionContext
            {
                ActionDescriptor = descriptor,
                ControllerContext = new HttpControllerContext
                {
                    Request = peticion,
                    RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"NUEVAVISION\Andre") }, "Bearer")) }
                }
            };
            foreach (KeyValuePair<string, object> a in args)
            {
                contexto.ActionArguments[a.Key] = a.Value;
            }
            var ejecutado = new HttpActionExecutedContext(contexto, null)
            {
                Response = peticion.CreateResponse(estado, cuerpo, new System.Net.Http.Formatting.JsonMediaTypeFormatter())
            };
            return ejecutado;
        }

        [TestMethod]
        public void Filtro_Un409DeCargar_SeGuardaConCabeceras()
        {
            new ApuntarLecturasRechazadasAttribute().OnActionExecuted(Contexto("Cargar", HttpStatusCode.Conflict,
                new { Message = "Esa parada la lleva Miguel", Codigo = "NO_CARGADA" }, new Dictionary<string, object> { ["peticion"] = new CargaBultoRepartoDTO { Codigo = "RP1-1" } }));

            LecturaRechazadaReparto l = guardadas[0];
            Assert.AreEqual("RP1-1", l.Codigo);
            Assert.AreEqual("NO_CARGADA", l.CodigoError);
            Assert.AreEqual("2.40", l.Version);
            Assert.AreEqual("PDA-3", l.Dispositivo);
        }

        [TestMethod]
        public void Filtro_Un200NoSeGuarda()
        {
            new ApuntarLecturasRechazadasAttribute().OnActionExecuted(Contexto("Cargar", HttpStatusCode.OK, new { }, new Dictionary<string, object>()));

            Assert.AreEqual(0, guardadas.Count);
        }

        [TestMethod]
        public void Filtro_SinLaTabla_NoGuardaNiRompe()
        {
            A.CallTo(() => repositorio.ScriptAplicado()).Returns(false);

            new ApuntarLecturasRechazadasAttribute().OnActionExecuted(Contexto("GetBulto", HttpStatusCode.NotFound,
                new { Message = "No existe" }, new Dictionary<string, object> { ["codigo"] = "RP1-1" }));

            A.CallTo(() => repositorio.Guardar(A<LecturaRechazadaReparto>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Filtro_SiFallaAlGuardar_NoRompe()
        {
            A.CallTo(() => repositorio.Guardar(A<LecturaRechazadaReparto>._)).Throws(new InvalidOperationException("caída"));

            new ApuntarLecturasRechazadasAttribute().OnActionExecuted(Contexto("GetBulto", HttpStatusCode.NotFound,
                new { Message = "No existe" }, new Dictionary<string, object> { ["codigo"] = "RP1-1" }));
        }

        [TestMethod]
        public void ElControladorLlevaElFiltro()
        {
            Assert.IsTrue(Attribute.IsDefined(typeof(RepartoPropioController), typeof(ApuntarLecturasRechazadasAttribute)));
        }

        // ================================================================ GET LecturasRechazadas

        private static RepartoPropioController Controller(IRepositorioLecturasRechazadas repo, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, @"NUEVAVISION\carlos") };
            foreach (string g in grupos)
            {
                claims.Add(new Claim(ClaimTypes.Role, g));
            }
            return new RepartoPropioController(A.Fake<NVEntities>(), new ServicioRepartoPropio(new ServicioRepartoPropioTests.RepositorioFalso(), () => AHORA))
            {
                LecturasRechazadas = repo,
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            };
        }

        [TestMethod]
        public async Task Get_Almacen_LasDevuelve()
        {
            var filas = new List<LecturaRechazadaReparto> { new LecturaRechazadaReparto { Id = 1, Codigo = "RP1-1", Status = 404 } };
            A.CallTo(() => repositorio.Leer(A<DateTime>._, "andre", 500)).Returns(Task.FromResult(filas));

            var resultado = await Controller(repositorio, Constantes.GruposSeguridad.ALMACEN).GetLecturasRechazadas(2, "andre")
                as OkNegotiatedContentResult<List<LecturaRechazadaReparto>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            A.CallTo(() => repositorio.Leer(DateTime.Today.AddDays(-1), "andre", 500)).MustHaveHappened();
        }

        [TestMethod]
        public async Task Get_SinGrupo_403_YSinTabla_503()
        {
            IHttpActionResult prohibido = await Controller(repositorio, Constantes.GruposSeguridad.ADMINISTRACION).GetLecturasRechazadas();
            Assert.AreEqual(HttpStatusCode.Forbidden, (HttpStatusCode)prohibido.GetType().GetProperty("StatusCode").GetValue(prohibido));

            A.CallTo(() => repositorio.ScriptAplicado()).Returns(false);
            IHttpActionResult sinTabla = await Controller(repositorio, Constantes.GruposSeguridad.DIRECCION).GetLecturasRechazadas();
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (HttpStatusCode)sinTabla.GetType().GetProperty("StatusCode").GetValue(sinTabla));
        }
    }
}
