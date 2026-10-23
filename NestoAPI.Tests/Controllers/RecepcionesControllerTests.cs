using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    // NestoAPI#559/#553: las recepciones las usan Ariadna y Nesto (las tiendas, sus reposiciones): el permiso lo
    // decide cada tipo, no el filtro de escritura de Almacén del resto de api/Almacen.
    [TestClass]
    public class RecepcionesControllerTests
    {
        private IServicioRecepciones servicio;
        private RecepcionesController controller;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        [TestInitialize]
        public void Inicializar()
        {
            servicio = A.Fake<IServicioRecepciones>();
            controller = new RecepcionesController(servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("Lidia", "Tiendas")
            };
        }

        [TestMethod]
        public void NoLlevaElFiltroDeEscrituraDeAlmacen_PeroSiPideEstarIdentificado()
        {
            object[] atributos = typeof(RecepcionesController).GetCustomAttributes(true);

            Assert.IsFalse(atributos.OfType<EscrituraSoloAlmacenAttribute>().Any(),
                "Si no, una tienda no podría terminar la recepción de su reposición");
            Assert.IsTrue(atributos.OfType<AuthorizeAttribute>().Any());
        }

        [TestMethod]
        public async Task Terminar_SinPermisoParaEseTipo_403ConElMotivo()
        {
            A.CallTo(() => servicio.Terminar("COMP", "1", "ALG", "65", A<TerminarRecepcionDTO>.Ignored, A<IPrincipal>.Ignored, false))
                .Throws(new UnauthorizedAccessException("No tienes permiso para terminar recepciones de tipo COMP."));

            IHttpActionResult resultado = await controller.PostTerminar("COMP", "65", new TerminarRecepcionDTO());

            HttpResponseMessage respuesta = await resultado.ExecuteAsync(default);
            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "COMP");
        }

        [TestMethod]
        public async Task Terminar_PasaElUsuarioDelToken()
        {
            A.CallTo(() => servicio.Terminar("REPO", "1", "REI", "80841", A<TerminarRecepcionDTO>.Ignored, A<IPrincipal>.Ignored, false))
                .Returns(new ResultadoTerminarRecepcionDTO { Tipo = "REPO" });

            IHttpActionResult resultado = await controller.PostTerminar("REPO", "80841", new TerminarRecepcionDTO(), almacen: "rei");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoTerminarRecepcionDTO>));
            A.CallTo(() => servicio.Terminar("REPO", "1", "REI", "80841", A<TerminarRecepcionDTO>.Ignored,
                A<IPrincipal>.That.Matches(u => u.Identity.Name == "Lidia"), false)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Terminar_ConEnsayo_LoPasaAlServicio()
        {
            A.CallTo(() => servicio.Terminar("REPO", "1", "ALG", "80871", A<TerminarRecepcionDTO>.Ignored, A<IPrincipal>.Ignored, true))
                .Returns(new ResultadoTerminarRecepcionDTO { Tipo = "REPO", Ensayo = true });

            IHttpActionResult resultado = await controller.PostTerminar("REPO", "80871", new TerminarRecepcionDTO(), ensayo: true);

            Assert.IsTrue(((OkNegotiatedContentResult<ResultadoTerminarRecepcionDTO>)resultado).Content.Ensayo);
        }

        [TestMethod]
        public async Task Buscar_SinCodigo_400YConCodigoLoQueEncuentre()
        {
            Assert.IsInstanceOfType(await controller.GetBuscar(" "), typeof(BadRequestErrorMessageResult));

            A.CallTo(() => servicio.Buscar("1", "ALG", "8436620930427"))
                .Returns(new List<RecepcionPendienteDTO> { new RecepcionPendienteDTO { Tipo = "COMP", Documento = "65" } });

            IHttpActionResult resultado = await controller.GetBuscar(" 8436620930427 ");

            var ok = (OkNegotiatedContentResult<List<RecepcionPendienteDTO>>)resultado;
            Assert.AreEqual("65", ok.Content.Single().Documento);
        }

        [TestMethod]
        public async Task LeerEsperado_SinNadaPendiente_404()
        {
            A.CallTo(() => servicio.LeerEsperado("COMP", "1", "ALG", "65", A<IPrincipal>.Ignored)).Returns(Task.FromResult<RecepcionDTO>(null));

            IHttpActionResult resultado = await controller.GetRecepcion("COMP", "65");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task LeerEsperado_CompletaLaFichaDeCadaLinea()
        {
            IFichasProductoAlmacen fichas = A.Fake<IFichasProductoAlmacen>();
            var conFichas = new RecepcionesController(servicio, fichas)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("Lidia", "Tiendas")
            };
            var recepcion = new RecepcionDTO { Tipo = "COMP", Documento = "65", Lineas = new List<LineaRecepcionDTO> { new LineaRecepcionDTO { Producto = "37049" } } };
            A.CallTo(() => servicio.LeerEsperado("COMP", "1", "ALG", "65", A<IPrincipal>.Ignored)).Returns(recepcion);

            IHttpActionResult resultado = await conFichas.GetRecepcion("COMP", "65");

            Assert.AreSame(recepcion, ((OkNegotiatedContentResult<RecepcionDTO>)resultado).Content);
            A.CallTo(() => fichas.Completar("1", recepcion.Lineas)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Casar_SinNadaPendiente_404()
        {
            A.CallTo(() => servicio.Casar("COMP", "1", "ALG", "65", A<IEnumerable<LecturaRecepcionDTO>>.Ignored))
                .Returns(Task.FromResult<ResultadoCasarRecepcionDTO>(null));

            IHttpActionResult resultado = await controller.PostCasar("COMP", "65", new List<LecturaRecepcionDTO>());

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        // --- Ariadna#51 (corte 2): lo leído en curso, en el servidor ---

        private (RecepcionesController Controller, NestoAPI.Infraestructure.PreparacionAlmacen.IServicioLecturasRecepcion Lecturas,
            NestoAPI.Infraestructure.Notificaciones.IAvisosAlmacen Avisos) ConLecturas()
        {
            var lecturas = A.Fake<NestoAPI.Infraestructure.PreparacionAlmacen.IServicioLecturasRecepcion>();
            var avisos = A.Fake<NestoAPI.Infraestructure.Notificaciones.IAvisosAlmacen>();
            var conLecturas = new RecepcionesController(servicio, null, lecturas, avisos)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("NUEVAVISION\\Paloma", "Tiendas")
            };
            return (conLecturas, lecturas, avisos);
        }

        [TestMethod]
        public async Task Lecturas_SinTabla_404ConElMotivo()
        {
            var (conLecturas, lecturas, _) = ConLecturas();
            A.CallTo(() => lecturas.Leer("REPO", "1", "ALC", "81119", A<IPrincipal>.Ignored, A<string>.Ignored))
                .Returns(Task.FromResult<LecturasRecepcionEnCursoDTO>(null));

            HttpResponseMessage respuesta = await (await conLecturas.GetLecturas("REPO", "81119", "alc")).ExecuteAsync(default);

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "Ariadna51_RecepcionesLecturasEnCurso.sql");
        }

        [TestMethod]
        public async Task Lecturas_SinElServicio_404()
        {
            HttpResponseMessage get = await (await controller.GetLecturas("REPO", "81119", "ALC")).ExecuteAsync(default);
            HttpResponseMessage post = await (await controller.PostLecturas("REPO", "81119", new List<LecturaRecepcionEnCursoDTO>(), "ALC")).ExecuteAsync(default);
            HttpResponseMessage delete = await (await controller.DeleteLecturas("REPO", "81119", "ALC")).ExecuteAsync(default);

            Assert.AreEqual(HttpStatusCode.NotFound, get.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, post.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, delete.StatusCode);
        }

        [TestMethod]
        public async Task Lecturas_SinPermiso_403()
        {
            var (conLecturas, lecturas, avisos) = ConLecturas();
            A.CallTo(() => lecturas.Anotar("REPO", "1", "REI", "81119", A<IEnumerable<LecturaRecepcionEnCursoDTO>>.Ignored, A<IPrincipal>.Ignored))
                .Throws(new UnauthorizedAccessException("No tiene permiso para recibir reposiciones en REI."));

            HttpResponseMessage respuesta = await (await conLecturas.PostLecturas("REPO", "81119",
                new List<LecturaRecepcionEnCursoDTO> { new LecturaRecepcionEnCursoDTO { IdCliente = Guid.NewGuid(), Producto = "A", Cantidad = 1 } }, "REI"))
                .ExecuteAsync(default);

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            A.CallTo(() => avisos.HaCambiado(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Lecturas_Post_DevuelveLoSumadoYAvisaAlGrupoDeLaRecepcionMenosAlQueLoManda()
        {
            var (conLecturas, lecturas, avisos) = ConLecturas();
            var sumado = new LecturasRecepcionEnCursoDTO { IdRecepcion = Guid.NewGuid() };
            A.CallTo(() => lecturas.Anotar("REPO", "1", "ALC", "81119", A<IEnumerable<LecturaRecepcionEnCursoDTO>>.Ignored, A<IPrincipal>.Ignored)).Returns(sumado);
            conLecturas.Request.Headers.Add("X-Conexion-SignalR", "conexion-de-nesto");

            IHttpActionResult resultado = await conLecturas.PostLecturas("REPO", "81119",
                new List<LecturaRecepcionEnCursoDTO> { new LecturaRecepcionEnCursoDTO { IdCliente = Guid.NewGuid(), Producto = "A", Cantidad = 1, Dispositivo = "NESTO-RDS" } }, "alc");

            Assert.AreSame(sumado, ((OkNegotiatedContentResult<LecturasRecepcionEnCursoDTO>)resultado).Content);
            A.CallTo(() => avisos.HaCambiado("recepcion:REPO:81119:ALC", "NUEVAVISION\\Paloma", "conexion-de-nesto", "NESTO-RDS")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Lecturas_Post_ElDispositivoEsElDeLaCabeceraXDispositivo()
        {
            var (conLecturas, lecturas, avisos) = ConLecturas();
            A.CallTo(() => lecturas.Anotar("REPO", "1", "ALC", "81119", A<IEnumerable<LecturaRecepcionEnCursoDTO>>.Ignored, A<IPrincipal>.Ignored))
                .Returns(new LecturasRecepcionEnCursoDTO());
            conLecturas.Request.Headers.Add("X-Dispositivo", "PDA-03");

            _ = await conLecturas.PostLecturas("REPO", "81119",
                new List<LecturaRecepcionEnCursoDTO> { new LecturaRecepcionEnCursoDTO { IdCliente = Guid.NewGuid(), Producto = "A", Cantidad = 1 } }, "ALC");

            A.CallTo(() => lecturas.Anotar("REPO", "1", "ALC", "81119",
                A<IEnumerable<LecturaRecepcionEnCursoDTO>>.That.Matches(l => l.All(x => x.Dispositivo == "PDA-03")), A<IPrincipal>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.HaCambiado("recepcion:REPO:81119:ALC", A<string>.Ignored, null, "PDA-03")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Lecturas_Delete_DescartaYAvisa()
        {
            var (conLecturas, lecturas, avisos) = ConLecturas();
            var nuevo = new LecturasRecepcionEnCursoDTO { IdRecepcion = Guid.NewGuid() };
            A.CallTo(() => lecturas.Descartar("REPO", "1", "ALC", "81119", A<IPrincipal>.Ignored, "PDA-03")).Returns(nuevo);

            IHttpActionResult resultado = await conLecturas.DeleteLecturas("REPO", "81119", "ALC", dispositivo: "PDA-03");

            Assert.AreSame(nuevo, ((OkNegotiatedContentResult<LecturasRecepcionEnCursoDTO>)resultado).Content);
            A.CallTo(() => avisos.HaCambiado("recepcion:REPO:81119:ALC", A<string>.Ignored, null, "PDA-03")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Lecturas_Get_NoAvisa()
        {
            var (conLecturas, lecturas, avisos) = ConLecturas();
            A.CallTo(() => lecturas.Leer("REPO", "1", "ALC", "81119", A<IPrincipal>.Ignored, A<string>.Ignored)).Returns(new LecturasRecepcionEnCursoDTO());

            _ = await conLecturas.GetLecturas("REPO", "81119", "ALC");

            A.CallTo(() => avisos.HaCambiado(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_AvisaALosDemasEquipos_PeroNoEnUnEnsayoNiSiYaEstabaTerminada()
        {
            var (conLecturas, _, avisos) = ConLecturas();
            A.CallTo(() => servicio.Terminar("REPO", "1", "ALC", "81119", A<TerminarRecepcionDTO>.Ignored, A<IPrincipal>.Ignored, false))
                .Returns(new ResultadoTerminarRecepcionDTO { Tipo = "REPO" });
            A.CallTo(() => servicio.Terminar("REPO", "1", "ALC", "81119", A<TerminarRecepcionDTO>.Ignored, A<IPrincipal>.Ignored, true))
                .Returns(new ResultadoTerminarRecepcionDTO { Tipo = "REPO", Ensayo = true });

            _ = await conLecturas.PostTerminar("REPO", "81119", new TerminarRecepcionDTO { Dispositivo = "PDA-03" }, "ALC");
            _ = await conLecturas.PostTerminar("REPO", "81119", new TerminarRecepcionDTO { Dispositivo = "PDA-03" }, "ALC", ensayo: true);
            A.CallTo(() => servicio.Terminar("REPO", "1", "ALC", "81119", A<TerminarRecepcionDTO>.Ignored, A<IPrincipal>.Ignored, false))
                .Returns(new ResultadoTerminarRecepcionDTO { Tipo = "REPO", YaEstabaTerminada = true });
            _ = await conLecturas.PostTerminar("REPO", "81119", new TerminarRecepcionDTO { Dispositivo = "PDA-03" }, "ALC");

            A.CallTo(() => avisos.HaCambiado("recepcion:REPO:81119:ALC", A<string>.Ignored, A<string>.Ignored, "PDA-03")).MustHaveHappenedOnceExactly();
        }
    }
}
