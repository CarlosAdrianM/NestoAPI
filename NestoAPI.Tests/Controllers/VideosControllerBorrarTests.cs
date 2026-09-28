using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Videos;
using NestoAPI.Models.Videos;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#545: DELETE api/Videos/{id}. Pueden borrar TiendaOnline (quien lleva los vídeos),
    /// Dirección e Informática; forzar el borrado de un vídeo que no es duplicado, solo Dirección.
    /// </summary>
    [TestClass]
    public class VideosControllerBorrarTests
    {
        private IServicioVideos servicio;
        private VideosController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioVideos>();
            controller = new VideosController(servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
        }

        private void Usuario(params string[] grupos)
        {
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\laura"), grupos);
        }

        private void ServicioDevuelve(EstadoBorradoVideo estado, string mensaje = null)
        {
            A.CallTo(() => servicio.BorrarVideo(A<int>._, A<bool>._, A<bool>._, A<string>._))
                .Returns(Task.FromResult(new ResultadoBorradoVideo
                {
                    Estado = estado,
                    Mensaje = mensaje,
                    Borrado = estado == EstadoBorradoVideo.Borrado ? new VideoBorradoDTO { Id = 1981, VideoId = "abc123", ProductosBorrados = 2 } : null
                }));
        }

        private static async Task<HttpStatusCode> Codigo(IHttpActionResult resultado)
        {
            HttpResponseMessage respuesta = await resultado.ExecuteAsync(CancellationToken.None);
            return respuesta.StatusCode;
        }

        [TestMethod]
        public async Task DeleteVideo_SinGrupoAutorizado_Prohibido()
        {
            Usuario("Almacén");
            ServicioDevuelve(EstadoBorradoVideo.Borrado);

            IHttpActionResult resultado = await controller.DeleteVideo(1981);

            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(resultado));
            A.CallTo(() => servicio.BorrarVideo(A<int>._, A<bool>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DeleteVideo_TiendaOnline_BorraElDuplicado()
        {
            Usuario("NUEVAVISION\\TiendaOnline");
            ServicioDevuelve(EstadoBorradoVideo.Borrado, "borrado");

            IHttpActionResult resultado = await controller.DeleteVideo(1981);

            var ok = resultado as OkNegotiatedContentResult<VideoBorradoDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(1981, ok.Content.Id);
            // TiendaOnline no es Dirección: no puede forzar.
            A.CallTo(() => servicio.BorrarVideo(1981, false, false, "NUEVAVISION\\laura")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PuedeBorrarVideos_DireccionEInformaticaTambien()
        {
            Assert.IsTrue(VideosController.PuedeBorrarVideos(new GenericPrincipal(new GenericIdentity("x"), new[] { "Dirección" })));
            Assert.IsTrue(VideosController.PuedeBorrarVideos(new GenericPrincipal(new GenericIdentity("x"), new[] { "NUEVAVISION\\Informatica" })));
            Assert.IsTrue(VideosController.PuedeBorrarVideos(new GenericPrincipal(new GenericIdentity("x"), new[] { "TiendaOnline" })));
            Assert.IsFalse(VideosController.PuedeBorrarVideos(new GenericPrincipal(new GenericIdentity("x"), new[] { "Tiendas" })));
            Assert.IsFalse(VideosController.PuedeBorrarVideos(null));
        }

        [TestMethod]
        public async Task DeleteVideo_Direccion_PasaQuePuedeForzar()
        {
            Usuario("Dirección");
            ServicioDevuelve(EstadoBorradoVideo.Borrado, "borrado");

            _ = await controller.DeleteVideo(1990, forzar: true);

            A.CallTo(() => servicio.BorrarVideo(1990, true, true, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeleteVideo_NoExiste_404()
        {
            Usuario("TiendaOnline");
            ServicioDevuelve(EstadoBorradoVideo.NoExiste);

            Assert.IsInstanceOfType(await controller.DeleteVideo(5000), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task DeleteVideo_NoEsDuplicado_400ConElMensaje()
        {
            Usuario("TiendaOnline");
            ServicioDevuelve(EstadoBorradoVideo.NoEsDuplicado, GestorBorradoVideos.MENSAJE_NO_ES_DUPLICADO);

            var bad = await controller.DeleteVideo(1990) as BadRequestErrorMessageResult;

            Assert.IsNotNull(bad);
            Assert.AreEqual(GestorBorradoVideos.MENSAJE_NO_ES_DUPLICADO, bad.Message);
        }

        [TestMethod]
        public async Task DeleteVideo_ForzarSinSerDireccion_Prohibido()
        {
            Usuario("TiendaOnline");
            ServicioDevuelve(EstadoBorradoVideo.ForzarNoPermitido, GestorBorradoVideos.MENSAJE_FORZAR_NO_PERMITIDO);

            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controller.DeleteVideo(1990, forzar: true)));
        }

        // ---- Carlos 28/09/26: baja desde la ventana Vídeos ----

        [TestMethod]
        public async Task DarDeBaja_SinGrupoAutorizado_Prohibido()
        {
            Usuario("Almacén");

            IHttpActionResult resultado = await controller.DarDeBajaVideo(1990);

            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(resultado));
            A.CallTo(() => servicio.DarDeBajaVideo(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DarDeBaja_TiendaOnline_Ok()
        {
            Usuario("NUEVAVISION\\TiendaOnline");
            A.CallTo(() => servicio.DarDeBajaVideo(1990, A<string>._))
                .Returns(Task.FromResult(new ResultadoBajaVideo { Estado = EstadoBajaVideo.DadoDeBaja, Mensaje = "hecho" }));

            IHttpActionResult resultado = await controller.DarDeBajaVideo(1990);

            Assert.AreEqual(HttpStatusCode.OK, await Codigo(resultado));
        }

        [TestMethod]
        public async Task DarDeBaja_YaEstabaDeBaja_BadRequest()
        {
            Usuario("NUEVAVISION\\TiendaOnline");
            A.CallTo(() => servicio.DarDeBajaVideo(1990, A<string>._))
                .Returns(Task.FromResult(new ResultadoBajaVideo { Estado = EstadoBajaVideo.YaEstabaDeBaja, Mensaje = "ya" }));

            IHttpActionResult resultado = await controller.DarDeBajaVideo(1990);

            Assert.AreEqual(HttpStatusCode.BadRequest, await Codigo(resultado));
        }

        [TestMethod]
        public async Task DeleteVideo_ActivoConDuplicadosDeBaja_BadRequest()
        {
            Usuario("NUEVAVISION\\TiendaOnline");
            ServicioDevuelve(EstadoBorradoVideo.DuplicadosDeBaja, GestorBorradoVideos.MENSAJE_DUPLICADOS_DE_BAJA);

            IHttpActionResult resultado = await controller.DeleteVideo(1981);

            Assert.AreEqual(HttpStatusCode.BadRequest, await Codigo(resultado));
        }
    }
}
