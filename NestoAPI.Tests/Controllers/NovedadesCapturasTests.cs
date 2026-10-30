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
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#754 (sugerencia 900): varias capturas por sugerencia / «Algo no funciona» y por comentario, guardadas en
    /// un contenedor privado de Azure Blob. Las de antes (una sola, en varbinary) se siguen leyendo, y los clientes que
    /// solo mandan ImagenBase64 siguen funcionando.
    /// </summary>
    [TestClass]
    public class NovedadesCapturasTests
    {
        private const int SUGERENCIA = 7;
        private const int COMENTARIO = 60;

        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private IServicioCapturasNovedades capturas;
        private IAlmacenCapturasNovedades almacen;
        private NovedadesController controller;
        private SugerenciaNovedadAGrabar sugerenciaGrabada;
        private ComentarioNovedadAGrabar comentarioGrabado;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            capturas = A.Fake<IServicioCapturasNovedades>();
            almacen = A.Fake<IAlmacenCapturasNovedades>();
            A.CallTo(() => almacen.Configurado).Returns(true);
            A.CallTo(() => almacen.Contenedor).Returns(AlmacenCapturasNovedadesAzure.CONTENEDOR);
            A.CallTo(() => capturas.Disponible()).Returns(true);
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._))
                .Invokes((SugerenciaNovedadAGrabar s) => sugerenciaGrabada = s)
                .Returns(SUGERENCIA);
            A.CallTo(() => feedback.ExisteNovedad(A<int>._)).Returns(true);
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._))
                .Invokes((ComentarioNovedadAGrabar c) => comentarioGrabado = c)
                .Returns(COMENTARIO);
            // Lo que se apunta en la tabla vuelve con su Id y su Orden
            A.CallTo(() => capturas.Crear(A<int>._, A<int?>._, A<IReadOnlyList<CapturaSubida>>._, A<string>._))
                .ReturnsLazily((int novedad, int? comentario, IReadOnlyList<CapturaSubida> subidas, string usuario) =>
                    subidas.Select((s, i) => new CapturaNovedadFila
                    {
                        Id = 100 + i,
                        NovedadId = novedad,
                        ComentarioId = comentario,
                        Ruta = s.Ruta,
                        Tipo = s.Tipo,
                        Bytes = s.Bytes,
                        Orden = i + 1
                    }).ToList());
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new HttpRequestMessage(),
                Capturas = capturas,
                AlmacenCapturas = almacen
            };
            ComoUsuarioDeNesto();
        }

        private void ComoUsuarioDeNesto(string usuario = "NUEVAVISION\\MariaJose", params string[] roles)
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

        /// <summary>Un PNG mínimo (la firma) y luego <paramref name="relleno"/> para que cada uno sea distinto.</summary>
        private static byte[] PngBytes(byte relleno, int extra = 10) =>
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Enumerable.Repeat(relleno, extra)).ToArray();

        private static string Png(byte relleno = 1, int extra = 10) => Convert.ToBase64String(PngBytes(relleno, extra));

        private static byte[] JpegBytes() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

        private void NoSeHaSubidoNada() =>
            A.CallTo(() => almacen.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();

        private void NoSeHaCreadoNada()
        {
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).MustNotHaveHappened();
            A.CallTo(() => capturas.Crear(A<int>._, A<int?>._, A<IReadOnlyList<CapturaSubida>>._, A<string>._)).MustNotHaveHappened();
        }

        // ---- Sugerencias con varias capturas ----

        [TestMethod]
        public async Task PostSugerencia_VariasCapturas_CadaUnaAlBlobYApuntadasEnOrden_SinTocarLaColumna()
        {
            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO
            {
                Texto = "Al guardar el pedido sale este error y luego este otro",
                ImagenesBase64 = new List<string> { Png(1), Png(2), Convert.ToBase64String(JpegBytes()) }
            }) as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => almacen.Subir(A<string>.That.EndsWith(".png"), A<byte[]>._, ReglasFeedbackNovedades.TIPO_PNG)).MustHaveHappenedTwiceExactly();
            A.CallTo(() => almacen.Subir(A<string>.That.EndsWith(".jpg"), A<byte[]>._, ReglasFeedbackNovedades.TIPO_JPEG)).MustHaveHappenedOnceExactly();
            A.CallTo(() => capturas.Crear(SUGERENCIA, null,
                A<IReadOnlyList<CapturaSubida>>.That.Matches(l => l.Count == 3 && l[2].Tipo == ReglasFeedbackNovedades.TIPO_JPEG),
                "NUEVAVISION\\MariaJose")).MustHaveHappenedOnceExactly();
            Assert.IsNull(sugerenciaGrabada.Imagen, "Las capturas nuevas no van a la columna");
            Assert.IsTrue(resultado.Content.TieneImagen);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, resultado.Content.Capturas.Select(c => c.Orden).ToList());
        }

        [TestMethod]
        public async Task PostSugerencia_ClienteViejoConImagenBase64_TambienVaAlBlob()
        {
            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO
            {
                Texto = "Una sola captura, como siempre",
                ImagenBase64 = "data:image/png;base64," + Png()
            }) as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => almacen.Subir(A<string>._, A<byte[]>._, ReglasFeedbackNovedades.TIPO_PNG)).MustHaveHappenedOnceExactly();
            Assert.IsNull(sugerenciaGrabada.Imagen);
            Assert.IsTrue(resultado.Content.TieneImagen);
            Assert.AreEqual(1, resultado.Content.Capturas.Count);
        }

        [TestMethod]
        public async Task PostSugerencia_LaPrimeraRepetidaEnImagenBase64_CuentaUnaVez()
        {
            string primera = Png(1);

            await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO
            {
                Texto = "Cliente nuevo que manda la primera también en ImagenBase64",
                ImagenBase64 = primera,
                ImagenesBase64 = new List<string> { primera, Png(2) }
            });

            A.CallTo(() => almacen.Subir(A<string>._, A<byte[]>._, A<string>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task PostSugerencia_MasDeDiezCapturas_BadRequestYNoSeSubeNada()
        {
            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO
            {
                Texto = "Muchas",
                ImagenesBase64 = Enumerable.Range(1, ReglasFeedbackNovedades.MAXIMO_CAPTURAS + 1).Select(i => Png((byte)i)).ToList()
            });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            NoSeHaSubidoNada();
            NoSeHaCreadoNada();
        }

        [TestMethod]
        public async Task PostSugerencia_UnaCapturaQueNoEsImagen_DiceCualYNoSeSubeNada()
        {
            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO
            {
                Texto = "La segunda está mal",
                ImagenesBase64 = new List<string> { Png(1), Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }) }
            }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.StartsWith(resultado.Message, "Captura 2:");
            NoSeHaSubidoNada();
        }

        // ---- Sin el script o sin almacenamiento: como antes ----

        [TestMethod]
        public async Task PostSugerencia_SinTabla_UnaCaptura_VaALaColumnaComoAntes()
        {
            A.CallTo(() => capturas.Disponible()).Returns(false);

            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO
            {
                Texto = "Sin el script",
                ImagenesBase64 = new List<string> { Png() }
            }) as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado);
            NoSeHaSubidoNada();
            CollectionAssert.AreEqual(PngBytes(1), sugerenciaGrabada.Imagen);
            Assert.AreEqual(ReglasFeedbackNovedades.TIPO_PNG, sugerenciaGrabada.ImagenTipo);
            Assert.IsTrue(resultado.Content.TieneImagen);
            Assert.AreEqual(0, resultado.Content.Capturas.Count);
        }

        [TestMethod]
        public async Task PostSugerencia_SinAlmacenamiento_VariasCapturas_BadRequestSinCrearNada()
        {
            A.CallTo(() => almacen.Configurado).Returns(false);

            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO
            {
                Texto = "Dos capturas sin blob",
                ImagenesBase64 = new List<string> { Png(1), Png(2) }
            }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(ReglasCapturasNovedades.ERROR_VARIAS_NO_DISPONIBLE, resultado.Message);
            NoSeHaCreadoNada();
        }

        [TestMethod]
        public async Task PostSugerencia_SinServicioDeCapturas_ComoAntes()
        {
            controller.Capturas = null;
            controller.AlmacenCapturas = null;

            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO { Texto = "Como siempre", ImagenBase64 = Png() });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<SugerenciaNovedadDTO>));
            Assert.IsNotNull(sugerenciaGrabada.Imagen);
        }

        // ---- Si Azure falla ----

        [TestMethod]
        public async Task PostSugerencia_FallaElBlobConUnaCaptura_VaALaColumnaYNoSePierde()
        {
            A.CallTo(() => almacen.Subir(A<string>._, A<byte[]>._, A<string>._)).Throws(new InvalidOperationException("Azure caído"));

            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO { Texto = "Azure caído", ImagenBase64 = Png() })
                as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsNotNull(sugerenciaGrabada.Imagen);
            Assert.IsTrue(resultado.Content.TieneImagen);
            A.CallTo(() => capturas.Crear(A<int>._, A<int?>._, A<IReadOnlyList<CapturaSubida>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostSugerencia_FallaElBlobConVarias_503SinCrearLaSugerencia()
        {
            A.CallTo(() => almacen.Subir(A<string>._, A<byte[]>._, A<string>._)).Throws(new InvalidOperationException("Azure caído"));

            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO
            {
                Texto = "Dos capturas con Azure caído",
                ImagenesBase64 = new List<string> { Png(1), Png(2) }
            }) as NegotiatedContentResult<HttpError>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.StatusCode);
            Assert.AreEqual(ReglasCapturasNovedades.ERROR_NO_SE_HAN_GUARDADO, resultado.Content.Message);
            NoSeHaCreadoNada();
        }

        [TestMethod]
        public async Task PostSugerencia_FallaAlApuntarLasCapturas_LaSugerenciaSeQuedaIgual()
        {
            A.CallTo(() => capturas.Crear(A<int>._, A<int?>._, A<IReadOnlyList<CapturaSubida>>._, A<string>._)).Throws(new Exception("BD"));

            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO { Texto = "Se apunta mal", ImagenBase64 = Png() })
                as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(SUGERENCIA, resultado.Content.Id);
            Assert.AreEqual(0, resultado.Content.Capturas.Count);
        }

        // ---- Comentarios ----

        [TestMethod]
        public async Task PostComentario_VariasCapturas_SeApuntanAlComentarioYASuNovedad()
        {
            var resultado = await controller.PostComentario(SUGERENCIA, new NuevoComentarioNovedadDTO
            {
                Texto = "Y aquí las otras dos pantallas",
                ImagenesBase64 = new List<string> { Png(1), Png(2) }
            }) as OkNegotiatedContentResult<ComentarioNovedadDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => capturas.Crear(SUGERENCIA, COMENTARIO, A<IReadOnlyList<CapturaSubida>>.That.Matches(l => l.Count == 2), "NUEVAVISION\\MariaJose"))
                .MustHaveHappenedOnceExactly();
            Assert.IsNull(comentarioGrabado.Imagen);
            Assert.IsTrue(resultado.Content.TieneImagen);
            Assert.AreEqual(2, resultado.Content.Capturas.Count);
        }

        [TestMethod]
        public async Task PostComentario_DeUnaNovedadQueNoExiste_NoSeSubeNada()
        {
            A.CallTo(() => feedback.ExisteNovedad(A<int>._)).Returns(false);

            var resultado = await controller.PostComentario(99, new NuevoComentarioNovedadDTO { Texto = "Hola", ImagenesBase64 = new List<string> { Png() } });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_ConCapturas_LasApuntaComoDelAsistente()
        {
            ComoInformatica();

            await controller.PostComentarioAsistente(SUGERENCIA, new NuevoComentarioAsistenteDTO
            {
                Texto = "Así queda",
                ImagenesBase64 = new List<string> { Png(1), Png(2) }
            });

            A.CallTo(() => capturas.Crear(SUGERENCIA, COMENTARIO, A<IReadOnlyList<CapturaSubida>>._, ReglasFeedbackNovedades.USUARIO_ASISTENTE))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void GetComentarios_TraeLasCapturasDeCadaUnoEnOrden()
        {
            A.CallTo(() => feedback.LeerComentarios(SUGERENCIA, A<string>._)).Returns(new List<ComentarioNovedadDTO>
            {
                new ComentarioNovedadDTO { Id = 60, NovedadId = SUGERENCIA, Texto = "Con dos" },
                new ComentarioNovedadDTO { Id = 61, NovedadId = SUGERENCIA, Texto = "De antes", TieneImagen = true },
                new ComentarioNovedadDTO { Id = 62, NovedadId = SUGERENCIA, Texto = "Sin nada" }
            });
            A.CallTo(() => capturas.LeerDeComentarios(A<IEnumerable<int>>._)).Returns(new List<CapturaNovedadFila>
            {
                new CapturaNovedadFila { Id = 6, NovedadId = SUGERENCIA, ComentarioId = 60, Orden = 2, Tipo = "image/png" },
                new CapturaNovedadFila { Id = 5, NovedadId = SUGERENCIA, ComentarioId = 60, Orden = 1, Tipo = "image/png" }
            });

            var comentarios = ((OkNegotiatedContentResult<List<ComentarioNovedadDTO>>)controller.GetComentarios(SUGERENCIA)).Content;

            CollectionAssert.AreEqual(new[] { 5, 6 }, comentarios[0].Capturas.Select(c => c.Id).ToList());
            Assert.IsTrue(comentarios[0].TieneImagen);
            Assert.IsTrue(comentarios[1].TieneImagen, "La de antes sigue diciendo que tiene imagen");
            Assert.AreEqual(0, comentarios[1].Capturas.Count);
            Assert.IsFalse(comentarios[2].TieneImagen);
        }

        [TestMethod]
        public void GetComentarios_SiFallaLeerLasCapturas_SalenSinEllas()
        {
            A.CallTo(() => feedback.LeerComentarios(SUGERENCIA, A<string>._)).Returns(new List<ComentarioNovedadDTO>
            {
                new ComentarioNovedadDTO { Id = 60, NovedadId = SUGERENCIA, Texto = "Hola" }
            });
            A.CallTo(() => capturas.LeerDeComentarios(A<IEnumerable<int>>._)).Throws(new Exception("Sin tabla"));

            var comentarios = ((OkNegotiatedContentResult<List<ComentarioNovedadDTO>>)controller.GetComentarios(SUGERENCIA)).Content;

            Assert.AreEqual(1, comentarios.Count);
            Assert.AreEqual(0, comentarios[0].Capturas.Count);
        }

        [TestMethod]
        public void GetSugerencias_TraeLasCapturasYMarcaTieneImagen()
        {
            A.CallTo(() => servicio.LeerSugerencias(A<bool>._)).Returns(new List<NovedadConSugerenciaFila>
            {
                new NovedadConSugerenciaFila { Id = SUGERENCIA, Fecha = DateTime.Today, Categoria = "Nuevo", Titulo = "Con capturas", Ambito = "Nesto", Estado = "Pendiente", SugeridaFecha = DateTime.Now }
            });
            A.CallTo(() => capturas.LeerDeNovedades(A<IEnumerable<int>>._)).Returns(new List<CapturaNovedadFila>
            {
                new CapturaNovedadFila { Id = 1, NovedadId = SUGERENCIA, Orden = 1, Tipo = "image/png" },
                new CapturaNovedadFila { Id = 2, NovedadId = SUGERENCIA, Orden = 2, Tipo = "image/png" }
            });

            var sugerencias = ((OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>)controller.GetSugerencias()).Content;

            Assert.AreEqual(2, sugerencias.Single().Capturas.Count);
            Assert.IsTrue(sugerencias.Single().TieneImagen);
        }

        [TestMethod]
        public void GetFeedback_TraeLasCapturasDeLosComentarios()
        {
            ComoInformatica();
            A.CallTo(() => feedback.LeerFeedback(A<DateTime>._, A<bool>._)).Returns(new FeedbackNovedadesDTO
            {
                Comentarios = new List<ComentarioFeedbackDTO> { new ComentarioFeedbackDTO { Id = 60, NovedadId = SUGERENCIA } }
            });
            A.CallTo(() => capturas.LeerDeComentarios(A<IEnumerable<int>>._)).Returns(new List<CapturaNovedadFila>
            {
                new CapturaNovedadFila { Id = 9, NovedadId = SUGERENCIA, ComentarioId = 60, Orden = 1 }
            });

            var resultado = ((OkNegotiatedContentResult<FeedbackNovedadesDTO>)controller.GetFeedback()).Content;

            Assert.AreEqual(9, resultado.Comentarios.Single().Capturas.Single().Id);
            Assert.IsTrue(resultado.Comentarios.Single().TieneImagen);
        }

        // ---- Servir las capturas ----

        [TestMethod]
        public async Task GetCaptura_LaBajaDelBlobConSuTipo()
        {
            A.CallTo(() => capturas.Leer(5)).Returns(new CapturaNovedadFila { Id = 5, NovedadId = SUGERENCIA, Ruta = "2026/10/x.jpg", Tipo = "image/jpeg" });
            A.CallTo(() => almacen.Descargar("2026/10/x.jpg")).Returns(JpegBytes());

            HttpResponseMessage respuesta = await controller.GetCaptura(5);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("image/jpeg", respuesta.Content.Headers.ContentType.MediaType);
            CollectionAssert.AreEqual(JpegBytes(), await respuesta.Content.ReadAsByteArrayAsync());
            Assert.IsTrue(respuesta.Headers.CacheControl.Private);
        }

        [TestMethod]
        public async Task GetCaptura_QueNoExiste_NotFound()
        {
            A.CallTo(() => capturas.Leer(5)).Returns(null);

            Assert.AreEqual(HttpStatusCode.NotFound, (await controller.GetCaptura(5)).StatusCode);
            A.CallTo(() => almacen.Descargar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetCaptura_DeUnaSugerenciaPrivada_SoloLaVenElAutorYDireccion()
        {
            A.CallTo(() => servicio.LeerPrivadas()).Returns(new List<SugerenciaPrivadaFila>
            {
                new SugerenciaPrivadaFila { Id = SUGERENCIA, SugeridaPor = "NUEVAVISION\\Manuel", SugeridaNombre = "Manuel" }
            });
            A.CallTo(() => capturas.Leer(5)).Returns(new CapturaNovedadFila { Id = 5, NovedadId = SUGERENCIA, ComentarioId = 60, Ruta = "r.png", Tipo = "image/png" });
            A.CallTo(() => almacen.Descargar("r.png")).Returns(PngBytes(1));
            controller.LectorMiembrosGrupo = grupo => new List<string>();

            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");
            Assert.AreEqual(HttpStatusCode.NotFound, (await controller.GetCaptura(5)).StatusCode);
            ComoUsuarioDeNesto("NUEVAVISION\\Manuel");
            Assert.AreEqual(HttpStatusCode.OK, (await controller.GetCaptura(5)).StatusCode);
        }

        [TestMethod]
        public async Task GetImagenNovedad_YaEnElBlob_SirveLaPrimeraCapturaALosClientesViejos()
        {
            A.CallTo(() => servicio.LeerImagen(SUGERENCIA)).Returns(null);
            A.CallTo(() => capturas.LeerDeNovedades(A<IEnumerable<int>>.That.Contains(SUGERENCIA))).Returns(new List<CapturaNovedadFila>
            {
                new CapturaNovedadFila { Id = 2, NovedadId = SUGERENCIA, Orden = 2, Ruta = "segunda.png", Tipo = "image/png" },
                new CapturaNovedadFila { Id = 1, NovedadId = SUGERENCIA, Orden = 1, Ruta = "primera.jpg", Tipo = "image/jpeg" }
            });
            A.CallTo(() => almacen.Descargar("primera.jpg")).Returns(JpegBytes());

            HttpResponseMessage respuesta = await controller.GetImagenNovedad(SUGERENCIA);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("image/jpeg", respuesta.Content.Headers.ContentType.MediaType);
        }

        [TestMethod]
        public async Task GetImagenNovedad_DeAntes_SeSigueLeyendoDeLaColumna()
        {
            A.CallTo(() => servicio.LeerImagen(SUGERENCIA)).Returns(new ImagenComentarioNovedad { Imagen = PngBytes(1), ImagenTipo = "image/png" });

            HttpResponseMessage respuesta = await controller.GetImagenNovedad(SUGERENCIA);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            A.CallTo(() => almacen.Descargar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetImagenComentario_YaEnElBlob_SirveLaPrimeraCaptura()
        {
            A.CallTo(() => feedback.LeerImagen(COMENTARIO)).Returns(null);
            A.CallTo(() => capturas.LeerDeComentarios(A<IEnumerable<int>>.That.Contains(COMENTARIO))).Returns(new List<CapturaNovedadFila>
            {
                new CapturaNovedadFila { Id = 1, NovedadId = SUGERENCIA, ComentarioId = COMENTARIO, Orden = 1, Ruta = "migradas/comentario-60.png", Tipo = "image/png" }
            });
            A.CallTo(() => almacen.Descargar("migradas/comentario-60.png")).Returns(PngBytes(1));

            Assert.AreEqual(HttpStatusCode.OK, (await controller.GetImagenComentario(COMENTARIO)).StatusCode);
        }

        // ---- Migración de las de antes ----

        private static ImagenPendienteMigrar Pendiente(string origen, int id, byte[] imagen) => new ImagenPendienteMigrar
        {
            Origen = origen,
            Id = id,
            NovedadId = origen == ImagenPendienteMigrar.ORIGEN_NOVEDAD ? id : SUGERENCIA,
            Usuario = "NUEVAVISION\\Alfredo",
            Imagen = imagen,
            ImagenTipo = "image/png"
        };

        private void ConMigracionPosible()
        {
            ComoInformatica();
            A.CallTo(() => almacen.ExisteContenedor()).Returns(true);
            A.CallTo(() => capturas.ContarPendientes()).Returns(new ResumenImagenesPendientes { Novedades = 1, Comentarios = 1, Bytes = 40 });
        }

        [TestMethod]
        public async Task PostMigrarCapturas_SinSerDireccionNiInformatica_Prohibido()
        {
            var resultado = await controller.PostMigrarCapturas(aplicar: true) as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado?.StatusCode);
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostMigrarCapturas_ModoMirar_NoTocaNada()
        {
            ConMigracionPosible();

            var resultado = (await controller.PostMigrarCapturas() as OkNegotiatedContentResult<MigracionCapturasDTO>).Content;

            Assert.IsFalse(resultado.Aplicar);
            Assert.AreEqual(true, resultado.ContenedorExiste);
            Assert.AreEqual(2, resultado.PendientesAntes.Novedades + resultado.PendientesAntes.Comentarios);
            A.CallTo(() => capturas.LeerPendientes(A<int>._)).MustNotHaveHappened();
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostMigrarCapturas_SinContenedor_LoDiceYNoSubeNada()
        {
            ConMigracionPosible();
            A.CallTo(() => almacen.ExisteContenedor()).Returns(false);

            var resultado = (await controller.PostMigrarCapturas(aplicar: true) as OkNegotiatedContentResult<MigracionCapturasDTO>).Content;

            Assert.AreEqual(false, resultado.ContenedorExiste);
            StringAssert.Contains(resultado.Mensaje, AlmacenCapturasNovedadesAzure.CONTENEDOR);
            NoSeHaSubidoNada();
        }

        [TestMethod]
        public async Task PostMigrarCapturas_Aplicar_PrimeroBlobComprobadoLuegoVaciarLaColumna()
        {
            ConMigracionPosible();
            byte[] deLaSugerencia = PngBytes(1);
            byte[] delComentario = JpegBytes();
            A.CallTo(() => capturas.LeerPendientes(25)).Returns(new List<ImagenPendienteMigrar>
            {
                Pendiente(ImagenPendienteMigrar.ORIGEN_NOVEDAD, 300, deLaSugerencia),
                Pendiente(ImagenPendienteMigrar.ORIGEN_COMENTARIO, 60, delComentario)
            });
            A.CallTo(() => almacen.Descargar("migradas/novedad-300.png")).Returns(deLaSugerencia);
            A.CallTo(() => almacen.Descargar("migradas/comentario-60.jpg")).Returns(delComentario);
            A.CallTo(() => capturas.RegistrarMigrada(A<ImagenPendienteMigrar>._, A<CapturaSubida>._)).Returns(true);

            var resultado = (await controller.PostMigrarCapturas(aplicar: true) as OkNegotiatedContentResult<MigracionCapturasDTO>).Content;

            Assert.AreEqual(2, resultado.Migradas.Count);
            Assert.AreEqual(0, resultado.Errores.Count);
            A.CallTo(() => almacen.Subir("migradas/novedad-300.png", deLaSugerencia, "image/png")).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => almacen.Descargar("migradas/novedad-300.png")).MustHaveHappened())
                .Then(A.CallTo(() => capturas.RegistrarMigrada(A<ImagenPendienteMigrar>.That.Matches(i => i.Id == 300),
                    A<CapturaSubida>.That.Matches(s => s.Ruta == "migradas/novedad-300.png" && s.Bytes == deLaSugerencia.Length))).MustHaveHappened());
            // El tipo sale de los bytes (JPEG), no del que estaba guardado (PNG)
            A.CallTo(() => almacen.Subir("migradas/comentario-60.jpg", delComentario, "image/jpeg")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostMigrarCapturas_LoDelBlobNoCoincide_NoSeVaciaLaColumna()
        {
            ConMigracionPosible();
            A.CallTo(() => capturas.LeerPendientes(A<int>._)).Returns(new List<ImagenPendienteMigrar>
            {
                Pendiente(ImagenPendienteMigrar.ORIGEN_NOVEDAD, 300, PngBytes(1))
            });
            A.CallTo(() => almacen.Descargar(A<string>._)).Returns(PngBytes(2));

            var resultado = (await controller.PostMigrarCapturas(aplicar: true) as OkNegotiatedContentResult<MigracionCapturasDTO>).Content;

            Assert.AreEqual(0, resultado.Migradas.Count);
            Assert.AreEqual(1, resultado.Errores.Count);
            A.CallTo(() => capturas.RegistrarMigrada(A<ImagenPendienteMigrar>._, A<CapturaSubida>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostMigrarCapturas_UnaFalla_SigueConLasDemas()
        {
            ConMigracionPosible();
            A.CallTo(() => capturas.LeerPendientes(A<int>._)).Returns(new List<ImagenPendienteMigrar>
            {
                Pendiente(ImagenPendienteMigrar.ORIGEN_NOVEDAD, 300, PngBytes(1)),
                Pendiente(ImagenPendienteMigrar.ORIGEN_NOVEDAD, 301, PngBytes(2))
            });
            A.CallTo(() => almacen.Subir("migradas/novedad-300.png", A<byte[]>._, A<string>._)).Throws(new InvalidOperationException("Azure"));
            A.CallTo(() => almacen.Descargar("migradas/novedad-301.png")).Returns(PngBytes(2));
            A.CallTo(() => capturas.RegistrarMigrada(A<ImagenPendienteMigrar>._, A<CapturaSubida>._)).Returns(true);

            var resultado = (await controller.PostMigrarCapturas(aplicar: true, maximo: 500) as OkNegotiatedContentResult<MigracionCapturasDTO>).Content;

            Assert.AreEqual(301, resultado.Migradas.Single().Id);
            StringAssert.Contains(resultado.Errores.Single(), "Novedad 300");
            A.CallTo(() => capturas.LeerPendientes(ReglasCapturasNovedades.MAXIMO_POR_PASADA)).MustHaveHappened();
        }
    }

    /// <summary>NestoAPI#754: reglas puras de las capturas.</summary>
    [TestClass]
    public class ReglasCapturasNovedadesTests
    {
        [TestMethod]
        public void Destino_SegunCuantasYSiHayBlob()
        {
            Assert.AreEqual(DestinoCapturasNovedad.Ninguna, ReglasCapturasNovedades.Destino(0, true));
            Assert.AreEqual(DestinoCapturasNovedad.Blob, ReglasCapturasNovedades.Destino(1, true));
            Assert.AreEqual(DestinoCapturasNovedad.Blob, ReglasCapturasNovedades.Destino(3, true));
            Assert.AreEqual(DestinoCapturasNovedad.Columna, ReglasCapturasNovedades.Destino(1, false));
            Assert.AreEqual(DestinoCapturasNovedad.NoDisponible, ReglasCapturasNovedades.Destino(2, false));
        }

        [TestMethod]
        public void RutaNueva_PorAnoYMesConGuidYExtension()
        {
            var guid = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

            Assert.AreEqual("2026/10/0f8fad5bd9cb469fa16570867728950e.png",
                ReglasCapturasNovedades.RutaNueva(new DateTime(2026, 10, 30), guid, "image/png"));
            Assert.AreEqual("2026/01/0f8fad5bd9cb469fa16570867728950e.jpg",
                ReglasCapturasNovedades.RutaNueva(new DateTime(2026, 1, 2), guid, "image/jpeg"));
        }

        [TestMethod]
        public void RutaMigrada_FijaParaQueLaMigracionSeaIdempotente()
        {
            Assert.AreEqual("migradas/novedad-7.png", ReglasCapturasNovedades.RutaMigrada("Novedad", 7, "image/png"));
            Assert.AreEqual("migradas/comentario-12.jpg", ReglasCapturasNovedades.RutaMigrada("Comentario", 12, "image/jpeg"));
        }

        [TestMethod]
        public void TipoDeImagenGuardada_MandanLosBytes()
        {
            Assert.AreEqual("image/jpeg", ReglasCapturasNovedades.TipoDeImagenGuardada(new byte[] { 0xFF, 0xD8, 0xFF, 0 }, "image/png"));
            Assert.AreEqual("image/png", ReglasCapturasNovedades.TipoDeImagenGuardada(new byte[] { 1, 2, 3, 4 }, null));
            Assert.AreEqual("image/gif", ReglasCapturasNovedades.TipoDeImagenGuardada(new byte[] { 1, 2, 3, 4 }, " image/gif "));
        }

        [TestMethod]
        public void PorPasada_PorDefecto25_EntreUnoYCien()
        {
            Assert.AreEqual(25, ReglasCapturasNovedades.PorPasada(null));
            Assert.AreEqual(1, ReglasCapturasNovedades.PorPasada(0));
            Assert.AreEqual(100, ReglasCapturasNovedades.PorPasada(1000));
            Assert.AreEqual(40, ReglasCapturasNovedades.PorPasada(40));
        }

        [TestMethod]
        public void Validar_ImagenBase64PrimeroYLuegoLaLista()
        {
            byte[] png = { 0x89, 0x50, 0x4E, 0x47, 9 };
            byte[] jpeg = { 0xFF, 0xD8, 0xFF, 9 };

            string error = ReglasFeedbackNovedades.Validar(new NuevoComentarioNovedadDTO
            {
                Texto = "Hola",
                ImagenBase64 = Convert.ToBase64String(jpeg),
                ImagenesBase64 = new List<string> { Convert.ToBase64String(png), "   ", null }
            }, out List<CapturaValidada> capturas);

            Assert.IsNull(error);
            CollectionAssert.AreEqual(new[] { "image/jpeg", "image/png" }, capturas.Select(c => c.Tipo).ToList());
        }
    }
}
