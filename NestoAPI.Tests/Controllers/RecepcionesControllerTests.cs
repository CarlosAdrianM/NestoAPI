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
    }
}
