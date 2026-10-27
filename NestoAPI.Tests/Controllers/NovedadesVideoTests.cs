using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#711: un vídeo corto en la novedad, como mucho uno por versión y ámbito (lo hace cumplir el servidor).
    /// Suben y quitan solo Dirección / Informática; lo ven todos los que ven la novedad.
    /// </summary>
    [TestClass]
    public class NovedadesVideoTests
    {
        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private IServicioVideosNovedades videos;
        private IAlmacenVideosNovedades almacen;
        private NovedadesController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            videos = A.Fake<IServicioVideosNovedades>();
            almacen = A.Fake<IAlmacenVideosNovedades>();
            A.CallTo(() => almacen.Configurado).Returns(true);
            A.CallTo(() => videos.AdmiteVideos()).Returns(true);
            A.CallTo(() => videos.Leer(7)).Returns(new VideoNovedadFila { Id = 7, Version = "1.10.50.0", Ambito = "Nesto", Titulo = "Reposición", Publicada = true });
            A.CallTo(() => videos.GuardarVideo(A<int>._, A<string>._, A<int>._, A<string>._))
                .Returns(new ResultadoGuardarVideoNovedad { Guardado = true, Version = "1.10.50.0" });
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new HttpRequestMessage(),
                Videos = videos,
                AlmacenVideos = almacen
            };
            ComoUsuarioDeNesto();
        }

        private void ComoUsuarioDeNesto(string usuario = "NUEVAVISION\\Paloma", params string[] roles)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario),
                new Claim(ClaimTypes.Name, usuario),
                new Claim(ClaimTypes.AuthenticationMethod, "Windows")
            };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        }

        private void ComoInformatica() => ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");

        /// <summary>Un «MP4» mínimo: la caja ftyp al principio.</summary>
        private static byte[] Mp4(int longitud)
        {
            byte[] bytes = Enumerable.Repeat((byte)0x11, Math.Max(longitud, 12)).ToArray();
            bytes[0] = 0; bytes[1] = 0; bytes[2] = 0; bytes[3] = 0x18;
            bytes[4] = (byte)'f'; bytes[5] = (byte)'t'; bytes[6] = (byte)'y'; bytes[7] = (byte)'p';
            return bytes;
        }

        private void ConCuerpo(byte[] contenido, string tipo)
        {
            var cuerpo = new ByteArrayContent(contenido);
            if (tipo != null)
            {
                cuerpo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
            }
            controller.Request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/Novedades/7/Video") { Content = cuerpo };
        }

        private void ConMultipart(string nombre, string tipo, byte[] contenido)
        {
            var parte = new ByteArrayContent(contenido);
            if (tipo != null)
            {
                parte.Headers.ContentType = new MediaTypeHeaderValue(tipo);
            }
            var multipart = new MultipartFormDataContent { { parte, "fichero", nombre } };
            controller.Request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/Novedades/7/Video") { Content = multipart };
        }

        private static HttpStatusCode Estado(IHttpActionResult resultado)
        {
            switch (resultado)
            {
                case StatusCodeResult s: return s.StatusCode;
                case BadRequestErrorMessageResult _: return HttpStatusCode.BadRequest;
                case NotFoundResult _: return HttpStatusCode.NotFound;
                case NegotiatedContentResult<HttpError> e: return e.StatusCode;
                case OkNegotiatedContentResult<VideoNovedadDTO> _: return HttpStatusCode.OK;
                default: throw new AssertFailedException("Resultado inesperado: " + resultado?.GetType().Name);
            }
        }

        private void NoSeHaSubidoNada() =>
            A.CallTo(() => almacen.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();

        // ---- Subir: quién ----

        [TestMethod]
        public async Task PostVideo_SinSerDireccionNiInformatica_Prohibido()
        {
            ConCuerpo(Mp4(100), "video/mp4");

            IHttpActionResult resultado = await controller.PostVideo(7);

            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(resultado));
            NoSeHaSubidoNada();
            A.CallTo(() => videos.GuardarVideo(A<int>._, A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostVideo_CuerpoTalCual_SubeAlBlobYGuardaLaRuta()
        {
            ComoInformatica();
            ConCuerpo(Mp4(5000), "video/mp4");

            var resultado = await controller.PostVideo(7) as OkNegotiatedContentResult<VideoNovedadDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Content.TieneVideo);
            Assert.AreEqual(5000, resultado.Content.VideoTamano);
            A.CallTo(() => almacen.Subir(A<string>.That.Matches(r => r.StartsWith("novedades-videos/7/") && r.EndsWith(".mp4")),
                A<byte[]>.That.Matches(b => b.Length == 5000), "video/mp4")).MustHaveHappenedOnceExactly();
            A.CallTo(() => videos.GuardarVideo(7, A<string>.That.StartsWith("novedades-videos/7/"), 5000, "NUEVAVISION\\Carlos"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostVideo_Multipart_Direccion_Puede()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo", "NUEVAVISION\\Dirección");
            ConMultipart("Reposición.mp4", "video/mp4", Mp4(300));

            IHttpActionResult resultado = await controller.PostVideo(7);

            Assert.AreEqual(HttpStatusCode.OK, Estado(resultado));
            A.CallTo(() => videos.GuardarVideo(7, A<string>._, 300, A<string>._)).MustHaveHappenedOnceExactly();
        }

        // ---- Subir: tipo y tamaño ----

        [TestMethod]
        public async Task PostVideo_QueNoEsMp4_415()
        {
            ComoInformatica();
            ConMultipart("video.webm", "video/webm", Mp4(100));

            IHttpActionResult resultado = await controller.PostVideo(7);

            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, Estado(resultado));
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostVideo_DiceMp4PeroElContenidoNoLoEs_415()
        {
            ComoInformatica();
            ConCuerpo(Enumerable.Repeat((byte)0x25, 100).ToArray(), "video/mp4");

            IHttpActionResult resultado = await controller.PostVideo(7);

            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, Estado(resultado));
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostVideo_SinTipoConExtensionMp4_SeAcepta()
        {
            ComoInformatica();
            ConMultipart("demo.mp4", null, Mp4(100));

            IHttpActionResult resultado = await controller.PostVideo(7);

            Assert.AreEqual(HttpStatusCode.OK, Estado(resultado));
        }

        [TestMethod]
        public async Task PostVideo_JustoEnElLimite_SeAcepta()
        {
            ComoInformatica();
            ConCuerpo(Mp4(ReglasVideoNovedades.TAMANO_MAXIMO), "video/mp4");

            IHttpActionResult resultado = await controller.PostVideo(7);

            Assert.AreEqual(HttpStatusCode.OK, Estado(resultado));
        }

        [TestMethod]
        public async Task PostVideo_MasDe10MB_400()
        {
            ComoInformatica();
            ConCuerpo(Mp4(ReglasVideoNovedades.TAMANO_MAXIMO + 1), "video/mp4");

            var resultado = await controller.PostVideo(7) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "10 MB");
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostVideo_Vacio_400()
        {
            ComoInformatica();
            ConCuerpo(new byte[0], "video/mp4");

            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(await controller.PostVideo(7)));
            NoSeHaSubidoNada();
        }

        // ---- Subir: la novedad ----

        [TestMethod]
        public async Task PostVideo_NovedadQueNoExiste_404()
        {
            ComoInformatica();
            ConCuerpo(Mp4(100), "video/mp4");
            A.CallTo(() => videos.Leer(8)).Returns(null);

            Assert.AreEqual(HttpStatusCode.NotFound, Estado(await controller.PostVideo(8)));
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostVideo_SinVersion_400()
        {
            ComoInformatica();
            ConCuerpo(Mp4(100), "video/mp4");
            A.CallTo(() => videos.Leer(7)).Returns(new VideoNovedadFila { Id = 7, Version = null, Ambito = "Nesto", Publicada = true });

            var resultado = await controller.PostVideo(7) as BadRequestErrorMessageResult;

            Assert.AreEqual(ReglasVideoNovedades.MENSAJE_SIN_VERSION, resultado?.Message);
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostVideo_SinLaColumnaTodavia_400ConElMensaje()
        {
            ComoInformatica();
            ConCuerpo(Mp4(100), "video/mp4");
            A.CallTo(() => videos.AdmiteVideos()).Returns(false);

            var resultado = await controller.PostVideo(7) as BadRequestErrorMessageResult;

            Assert.AreEqual(ReglasVideoNovedades.MENSAJE_SIN_COLUMNA, resultado?.Message);
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostVideo_SinAlmacenamientoConfigurado_503()
        {
            ComoInformatica();
            ConCuerpo(Mp4(100), "video/mp4");
            A.CallTo(() => almacen.Configurado).Returns(false);

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, Estado(await controller.PostVideo(7)));
            NoSeHaSubidoNada();
        }

        // ---- Uno por versión ----

        [TestMethod]
        public async Task PostVideo_OtraNovedadDeLaVersionYaTieneVideo_409YSeBorraElBlobSubido()
        {
            ComoInformatica();
            ConCuerpo(Mp4(100), "video/mp4");
            A.CallTo(() => videos.GuardarVideo(A<int>._, A<string>._, A<int>._, A<string>._))
                .Returns(new ResultadoGuardarVideoNovedad { Guardado = false, OtraNovedadId = 782, OtroTitulo = "Terminar reposición", Version = "1.10.50.0" });

            var resultado = await controller.PostVideo(7) as NegotiatedContentResult<HttpError>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            string mensaje = resultado.Content.Message;
            StringAssert.Contains(mensaje, "1.10.50.0");
            StringAssert.Contains(mensaje, "782");
            StringAssert.Contains(mensaje, "«Terminar reposición»");
            StringAssert.Contains(mensaje, "un vídeo por versión");
            StringAssert.Contains(mensaje, "quítelo"); // de usted
            A.CallTo(() => almacen.Borrar(A<string>.That.StartsWith("novedades-videos/7/"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostVideo_SustituyeElDeLaMismaNovedad_BorraElBlobAnterior()
        {
            ComoInformatica();
            ConCuerpo(Mp4(100), "video/mp4");
            A.CallTo(() => videos.GuardarVideo(A<int>._, A<string>._, A<int>._, A<string>._))
                .Returns(new ResultadoGuardarVideoNovedad { Guardado = true, RutaAnterior = "novedades-videos/7/viejo.mp4" });

            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.PostVideo(7)));

            A.CallTo(() => almacen.Borrar("novedades-videos/7/viejo.mp4")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostVideo_FallaBorrarElAnterior_NoRompeLaSubida()
        {
            ComoInformatica();
            ConCuerpo(Mp4(100), "video/mp4");
            A.CallTo(() => videos.GuardarVideo(A<int>._, A<string>._, A<int>._, A<string>._))
                .Returns(new ResultadoGuardarVideoNovedad { Guardado = true, RutaAnterior = "novedades-videos/7/viejo.mp4" });
            A.CallTo(() => almacen.Borrar(A<string>._)).Throws(new InvalidOperationException("Azure caído"));

            Assert.AreEqual(HttpStatusCode.OK, Estado(await controller.PostVideo(7)));
        }

        // ---- Quitar ----

        [TestMethod]
        public async Task DeleteVideo_SinSerDireccionNiInformatica_Prohibido()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await controller.DeleteVideo(7)));
            A.CallTo(() => videos.QuitarVideo(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DeleteVideo_LoQuitaYBorraElBlob_204()
        {
            ComoInformatica();
            A.CallTo(() => videos.QuitarVideo(7, A<string>._)).Returns("novedades-videos/7/abc.mp4");

            Assert.AreEqual(HttpStatusCode.NoContent, Estado(await controller.DeleteVideo(7)));
            A.CallTo(() => almacen.Borrar("novedades-videos/7/abc.mp4")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeleteVideo_SinVideo_404()
        {
            ComoInformatica();
            A.CallTo(() => videos.QuitarVideo(7, A<string>._)).Returns(null);

            Assert.AreEqual(HttpStatusCode.NotFound, Estado(await controller.DeleteVideo(7)));
            A.CallTo(() => almacen.Borrar(A<string>._)).MustNotHaveHappened();
        }

        // ---- Ver ----

        private void ConVideoGuardado(byte[] contenido)
        {
            A.CallTo(() => videos.Leer(7)).Returns(new VideoNovedadFila { Id = 7, Version = "1.10.50.0", Ambito = "Nesto", Publicada = true, Video = "novedades-videos/7/abc.mp4" });
            A.CallTo(() => almacen.Descargar("novedades-videos/7/abc.mp4")).Returns(contenido);
        }

        [TestMethod]
        public async Task GetVideo_SinRange_200EnteroConTipoMp4YAcceptRanges()
        {
            ConVideoGuardado(Mp4(1000));

            HttpResponseMessage respuesta = await controller.GetVideo(7);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("video/mp4", respuesta.Content.Headers.ContentType.MediaType);
            Assert.AreEqual(1000, respuesta.Content.Headers.ContentLength);
            CollectionAssert.Contains(respuesta.Headers.AcceptRanges.ToList(), "bytes");
            Assert.AreEqual(1000, (await respuesta.Content.ReadAsByteArrayAsync()).Length);
        }

        [TestMethod]
        public async Task GetVideo_ConRange_206ConElTrozo()
        {
            byte[] contenido = Enumerable.Range(0, 1000).Select(i => (byte)(i % 256)).ToArray();
            ConVideoGuardado(contenido);
            controller.Request.Headers.Range = new RangeHeaderValue(100, 199);

            HttpResponseMessage respuesta = await controller.GetVideo(7);

            Assert.AreEqual(HttpStatusCode.PartialContent, respuesta.StatusCode);
            Assert.AreEqual(100, respuesta.Content.Headers.ContentRange.From);
            Assert.AreEqual(199, respuesta.Content.Headers.ContentRange.To);
            Assert.AreEqual(1000, respuesta.Content.Headers.ContentRange.Length);
            Assert.AreEqual(100, respuesta.Content.Headers.ContentLength);
            byte[] trozo = await respuesta.Content.ReadAsByteArrayAsync();
            Assert.AreEqual(100, trozo.Length);
            Assert.AreEqual((byte)100, trozo[0]);
            Assert.AreEqual((byte)199, trozo[99]);
        }

        [TestMethod]
        public async Task GetVideo_RangeMasAllaDelFinal_416()
        {
            ConVideoGuardado(Mp4(1000));
            controller.Request.Headers.Range = new RangeHeaderValue(5000, null);

            HttpResponseMessage respuesta = await controller.GetVideo(7);

            Assert.AreEqual(HttpStatusCode.RequestedRangeNotSatisfiable, respuesta.StatusCode);
            Assert.AreEqual(1000, respuesta.Content.Headers.ContentRange.Length);
        }

        [TestMethod]
        public async Task GetVideo_SinVideo_404()
        {
            HttpResponseMessage respuesta = await controller.GetVideo(7);

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            A.CallTo(() => almacen.Descargar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetVideo_NovedadNoPublicada_404()
        {
            A.CallTo(() => videos.Leer(7)).Returns(new VideoNovedadFila { Id = 7, Version = "1.10.50.0", Publicada = false, Video = "novedades-videos/7/abc.mp4" });

            Assert.AreEqual(HttpStatusCode.NotFound, (await controller.GetVideo(7)).StatusCode);
        }

        [TestMethod]
        public async Task GetVideo_PrivadaDeOtro_404()
        {
            ConVideoGuardado(Mp4(100));
            A.CallTo(() => servicio.LeerPrivadas()).Returns(new List<SugerenciaPrivadaFila>
            {
                new SugerenciaPrivadaFila { Id = 7, SugeridaPor = "NUEVAVISION\\Otro" }
            });

            Assert.AreEqual(HttpStatusCode.NotFound, (await controller.GetVideo(7)).StatusCode);
            A.CallTo(() => almacen.Descargar(A<string>._)).MustNotHaveHappened();
        }

        // ---- Reproducciones ----

        [TestMethod]
        public void PostReproduccion_LaApuntaConUsuarioYCliente_204()
        {
            IHttpActionResult resultado = controller.PostReproduccionVideo(7);

            Assert.AreEqual(HttpStatusCode.NoContent, Estado(resultado));
            A.CallTo(() => videos.RegistrarReproduccion(7, "NUEVAVISION\\Paloma", "Nesto")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PostReproduccion_SiFallaApuntarla_204Igual()
        {
            A.CallTo(() => videos.RegistrarReproduccion(A<int>._, A<string>._, A<string>._)).Throws(new Exception("Sin tabla"));

            Assert.AreEqual(HttpStatusCode.NoContent, Estado(controller.PostReproduccionVideo(7)));
        }

        // ---- TieneVideo en las listas ----

        [TestMethod]
        public void GetNovedades_TieneVideoSoloEnLaQueLoTiene()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                new NovedadDTO { Id = 7, Version = "1.10.50.0", Titulo = "Con vídeo", Ambito = "Nesto" },
                new NovedadDTO { Id = 8, Version = "1.10.50.0", Titulo = "Sin vídeo", Ambito = "Nesto" }
            });
            A.CallTo(() => videos.LeerConVideo()).Returns(new Dictionary<int, int?> { [7] = 2_100_000 });

            var resultado = controller.GetNovedades(todas: true) as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotNull(resultado);
            NovedadDTO con = resultado.Content.Single(n => n.Id == 7);
            NovedadDTO sin = resultado.Content.Single(n => n.Id == 8);
            Assert.IsTrue(con.TieneVideo);
            Assert.AreEqual(2_100_000, con.VideoTamano);
            Assert.IsFalse(sin.TieneVideo);
            Assert.IsNull(sin.VideoTamano);
        }

        [TestMethod]
        public void GetNovedades_SiFallaLeerLosVideos_SalenIgualSinVideo()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                new NovedadDTO { Id = 7, Version = "1.10.50.0", Titulo = "Con vídeo", Ambito = "Nesto" }
            });
            A.CallTo(() => videos.LeerConVideo()).Throws(new Exception("El nombre de columna 'Video' no es válido."));

            var resultado = controller.GetNovedades(todas: true) as OkNegotiatedContentResult<List<NovedadDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            Assert.IsFalse(resultado.Content[0].TieneVideo);
        }

        [TestMethod]
        public void NovedadConFeedbackDTO_Desde_CopiaElVideo()
        {
            var copia = NovedadConFeedbackDTO.Desde(new NovedadDTO { Id = 7, TieneVideo = true, VideoTamano = 123 });

            Assert.IsTrue(copia.TieneVideo);
            Assert.AreEqual(123, copia.VideoTamano);
        }

        // ---- Reglas puras ----

        [TestMethod]
        public void Validar_Mp4BienFormado_SinError()
        {
            Assert.IsNull(ReglasVideoNovedades.Validar("a.mp4", "video/mp4", Mp4(50)).Error);
        }

        [TestMethod]
        public void Validar_OctetStreamConExtensionQueNoEsMp4_TipoNoPermitido()
        {
            (string error, bool tipoNoPermitido) = ReglasVideoNovedades.Validar("a.mov", "application/octet-stream", Mp4(50));

            Assert.IsNotNull(error);
            Assert.IsTrue(tipoNoPermitido);
        }

        [TestMethod]
        public void RutaDelBlob_PrefijoNovedadYGuid()
        {
            var guid = new Guid("0123456789abcdef0123456789abcdef");

            Assert.AreEqual("novedades-videos/7/0123456789abcdef0123456789abcdef.mp4", ReglasVideoNovedades.RutaDelBlob(7, guid));
        }
    }
}
