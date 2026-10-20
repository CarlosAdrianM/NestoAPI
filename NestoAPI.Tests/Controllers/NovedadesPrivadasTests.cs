using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#650: sugerencias privadas. Solo las ven su autor y Dirección / Informática; para los demás no
    /// existen (listas, buscador, comentarios, imágenes, adjuntos y avisos). No admiten «Me gusta» y van detrás.
    /// </summary>
    [TestClass]
    public class NovedadesPrivadasTests
    {
        private const int PRIVADA = 700;
        private const int PUBLICA = 701;
        private const string AUTOR = "NUEVAVISION\\Manuel";

        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private IServicioAdjuntosNovedades adjuntos;
        private IServicioNotificacionesPush notificaciones;
        private ILectorParametrosUsuario lector;
        private NovedadesController controller;
        private List<string> direccion;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            adjuntos = A.Fake<IServicioAdjuntosNovedades>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            lector = A.Fake<ILectorParametrosUsuario>();
            direccion = new List<string> { "Carlos" };
            A.CallTo(() => servicio.LeerPrivadas()).Returns(new List<SugerenciaPrivadaFila>
            {
                new SugerenciaPrivadaFila { Id = PRIVADA, SugeridaPor = AUTOR, SugeridaNombre = "Manuel" }
            });
            A.CallTo(() => servicio.AdmitePrivadas()).Returns(true);
            A.CallTo(() => servicio.LeerSugerencias(A<bool>._)).Returns(new List<NovedadConSugerenciaFila>
            {
                Sugerencia(PRIVADA, AUTOR),
                Sugerencia(PUBLICA, "NUEVAVISION\\Alfredo")
            });
            A.CallTo(() => feedback.ExisteNovedad(A<int>._)).Returns(true);
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).Returns(60);
            A.CallTo(() => feedback.LeerAmbitoNovedad(A<int>._)).Returns("Nesto");
            A.CallTo(() => feedback.LeerMencionables(false)).Returns(new List<MencionableDTO>
            {
                new MencionableDTO { Nombre = "Carlos", Clave = "NUEVAVISION\\Carlos", Aplicacion = "Nesto" },
                new MencionableDTO { Nombre = "Alfredo", Clave = "NUEVAVISION\\Alfredo", Aplicacion = "Nesto" },
                new MencionableDTO { Nombre = "Manuel", Clave = "NUEVAVISION\\Manuel", Aplicacion = "Nesto" }
            });
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, Constantes.ParametrosUsuario.AVISAR_ACTIVIDAD_NOVEDADES_A))
                .Returns(null); // sin fila: Carlos
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new HttpRequestMessage(),
                Adjuntos = adjuntos,
                Notificaciones = notificaciones,
                LectorParametros = lector,
                LectorMiembrosGrupo = grupo => grupo == Constantes.GruposSeguridad.DIRECCION ? direccion : new List<string>()
            };
        }

        private static NovedadConSugerenciaFila Sugerencia(int id, string autor, string version = null) => new NovedadConSugerenciaFila
        {
            Id = id,
            Version = version,
            Fecha = DateTime.Today,
            Categoria = "Nuevo",
            Titulo = "Sugerencia " + id,
            Ambito = "Nesto",
            TextoOriginal = "Texto de la sugerencia",
            Estado = ReglasSugerenciasNovedades.ESTADO_PENDIENTE,
            SugeridaPor = autor,
            SugeridaNombre = autor.Substring(autor.IndexOf('\\') + 1),
            SugeridaFecha = DateTime.Today
        };

        private void ComoUsuarioDeNesto(string usuario, params string[] roles)
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

        private void ComoOtro() => ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");
        private void ComoAutor() => ComoUsuarioDeNesto(AUTOR);
        private void ComoInformatica() => ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");

        private List<SugerenciaNovedadDTO> Sugerencias() =>
            ((OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>)controller.GetSugerencias()).Content;

        // ---- Listas ----

        [TestMethod]
        public void GetSugerencias_OtroUsuario_NoVeLaPrivada()
        {
            ComoOtro();

            CollectionAssert.AreEqual(new[] { PUBLICA }, Sugerencias().Select(s => s.Id).ToArray());
        }

        [TestMethod]
        public void GetSugerencias_SinUsuario_NoVeLaPrivada()
        {
            CollectionAssert.AreEqual(new[] { PUBLICA }, Sugerencias().Select(s => s.Id).ToArray());
        }

        [TestMethod]
        public void GetSugerencias_SuAutor_LaVeMarcadaComoPrivada()
        {
            ComoAutor();

            SugerenciaNovedadDTO privada = Sugerencias().Single(s => s.Id == PRIVADA);
            Assert.IsTrue(privada.Privada);
        }

        [TestMethod]
        public void GetSugerencias_Informatica_LaVeYVaDetrasDeLasPublicasAunqueEstasTenganVotosNegativos()
        {
            ComoInformatica();
            A.CallTo(() => feedback.LeerResumen(A<IEnumerable<int>>._, A<string>._)).Returns(new List<ResumenFeedbackNovedad>
            {
                new ResumenFeedbackNovedad { NovedadId = PRIVADA, Positivos = 3 },
                new ResumenFeedbackNovedad { NovedadId = PUBLICA, Negativos = 2 }
            });

            List<SugerenciaNovedadDTO> sugerencias = Sugerencias();

            CollectionAssert.AreEqual(new[] { PUBLICA, PRIVADA }, sugerencias.Select(s => s.Id).ToArray());
            Assert.IsFalse(sugerencias[0].Privada);
            Assert.IsTrue(sugerencias[1].Privada);
        }

        [TestMethod]
        public void GetSugerencias_SinColumnaPrivada_SalenTodasComoSiempre()
        {
            A.CallTo(() => servicio.LeerPrivadas()).Returns(new List<SugerenciaPrivadaFila>());
            ComoOtro();

            List<SugerenciaNovedadDTO> sugerencias = Sugerencias();

            Assert.AreEqual(2, sugerencias.Count);
            Assert.IsTrue(sugerencias.All(s => !s.Privada));
        }

        [TestMethod]
        public void GetSugerencias_SiNoSePuedenLeerLasPrivadas_CadaUnoSoloVeLasSuyas()
        {
            A.CallTo(() => servicio.LeerPrivadas()).Throws(new InvalidOperationException("Timeout"));
            ComoAutor();

            CollectionAssert.AreEqual(new[] { PRIVADA }, Sugerencias().Select(s => s.Id).ToArray(),
                "Se cierra por seguridad: la de Alfredo podría ser privada");
        }

        [TestMethod]
        public void GetSugerencias_SiNoSePuedenLeerLasPrivadas_InformaticaLasVeTodas()
        {
            A.CallTo(() => servicio.LeerPrivadas()).Throws(new InvalidOperationException("Timeout"));
            ComoInformatica();

            Assert.AreEqual(2, Sugerencias().Count);
        }

        [TestMethod]
        public void GetBuscar_OtroUsuario_NoEncuentraLaPrivada()
        {
            A.CallTo(() => servicio.Buscar(A<IReadOnlyList<string>>._)).Returns(new List<NovedadConSugerenciaFila>
            {
                Sugerencia(PRIVADA, AUTOR),
                Sugerencia(PUBLICA, "NUEVAVISION\\Alfredo")
            });
            ComoOtro();

            var resultado = (OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>)controller.GetBuscar("sugerencia");

            CollectionAssert.AreEqual(new[] { PUBLICA }, resultado.Content.Select(s => s.Id).ToArray());
        }

        [TestMethod]
        public void GetBuscar_SuAutor_LaEncuentraMarcadaComoPrivada()
        {
            A.CallTo(() => servicio.Buscar(A<IReadOnlyList<string>>._)).Returns(new List<NovedadConSugerenciaFila> { Sugerencia(PRIVADA, AUTOR) });
            ComoAutor();

            var resultado = (OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>)controller.GetBuscar("sugerencia");

            Assert.IsTrue(resultado.Content.Single().Privada);
        }

        [TestMethod]
        public void GetNovedades_UnaPrivadaImplementada_SoloLaVenSuAutorYDireccion()
        {
            A.CallTo(() => servicio.LeerNovedadesPublicadas()).Returns(new List<NovedadDTO>
            {
                new NovedadDTO { Id = PRIVADA, Version = "1.10.45.0", Ambito = "Nesto", Titulo = "La de Manuel" },
                new NovedadDTO { Id = PUBLICA, Version = "1.10.45.0", Ambito = "Nesto", Titulo = "Para todos" }
            });

            ComoOtro();
            var otro = (OkNegotiatedContentResult<List<NovedadDTO>>)controller.GetNovedades(todas: true);
            ComoAutor();
            var autor = (OkNegotiatedContentResult<List<NovedadDTO>>)controller.GetNovedades(todas: true);

            CollectionAssert.AreEqual(new[] { PUBLICA }, otro.Content.Select(n => n.Id).ToArray());
            CollectionAssert.AreEquivalent(new[] { PRIVADA, PUBLICA }, autor.Content.Select(n => n.Id).ToArray());
        }

        // ---- Por id ----

        [TestMethod]
        public void GetComentarios_OtroUsuario_NoExiste()
        {
            ComoOtro();

            Assert.IsInstanceOfType(controller.GetComentarios(PRIVADA), typeof(NotFoundResult));
            A.CallTo(() => feedback.LeerComentarios(PRIVADA, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetComentarios_SuAutorEInformatica_LosLeen()
        {
            ComoAutor();
            Assert.IsInstanceOfType(controller.GetComentarios(PRIVADA), typeof(OkNegotiatedContentResult<List<ComentarioNovedadDTO>>));
            ComoInformatica();
            Assert.IsInstanceOfType(controller.GetComentarios(PRIVADA), typeof(OkNegotiatedContentResult<List<ComentarioNovedadDTO>>));
        }

        [TestMethod]
        public void GetComentarios_DeUnaPublica_ComoSiempre()
        {
            ComoOtro();

            Assert.IsInstanceOfType(controller.GetComentarios(PUBLICA), typeof(OkNegotiatedContentResult<List<ComentarioNovedadDTO>>));
        }

        [TestMethod]
        public void GetComentarios_SiNoSePuedenLeerLasPrivadas_NoSeSirvenAQuienNoRevisa()
        {
            A.CallTo(() => servicio.LeerPrivadas()).Throws(new InvalidOperationException("Timeout"));
            ComoOtro();

            Assert.IsInstanceOfType(controller.GetComentarios(PUBLICA), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task PostComentario_OtroUsuario_NoExiste()
        {
            ComoOtro();

            var resultado = await controller.PostComentario(PRIVADA, new NuevoComentarioNovedadDTO { Texto = "Yo también" });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetImagenNovedad_OtroUsuario_NoExiste()
        {
            A.CallTo(() => servicio.LeerImagen(PRIVADA)).Returns(new ImagenComentarioNovedad { Imagen = new byte[] { 1 }, ImagenTipo = "image/png" });
            ComoOtro();

            Assert.AreEqual(HttpStatusCode.NotFound, controller.GetImagenNovedad(PRIVADA).StatusCode);
            ComoAutor();
            Assert.AreEqual(HttpStatusCode.OK, controller.GetImagenNovedad(PRIVADA).StatusCode);
        }

        [TestMethod]
        public void GetImagenComentario_DeUnaPrivada_OtroUsuarioNoLaVe()
        {
            A.CallTo(() => feedback.LeerImagen(60)).Returns(new ImagenComentarioNovedad { Imagen = new byte[] { 1 }, ImagenTipo = "image/png" });
            A.CallTo(() => feedback.LeerAutores(A<IEnumerable<int>>._)).Returns(new List<AutorComentarioNovedad>
            {
                new AutorComentarioNovedad { Id = 60, NovedadId = PRIVADA, Usuario = AUTOR }
            });
            ComoOtro();

            Assert.AreEqual(HttpStatusCode.NotFound, controller.GetImagenComentario(60).StatusCode);
            ComoAutor();
            Assert.AreEqual(HttpStatusCode.OK, controller.GetImagenComentario(60).StatusCode);
        }

        [TestMethod]
        public void GetImagenComentario_SinPrivadas_NoConsultaDeQueNovedadEs()
        {
            A.CallTo(() => servicio.LeerPrivadas()).Returns(new List<SugerenciaPrivadaFila>());
            A.CallTo(() => feedback.LeerImagen(60)).Returns(new ImagenComentarioNovedad { Imagen = new byte[] { 1 }, ImagenTipo = "image/png" });
            ComoOtro();

            Assert.AreEqual(HttpStatusCode.OK, controller.GetImagenComentario(60).StatusCode);
            A.CallTo(() => feedback.LeerAutores(A<IEnumerable<int>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetAdjuntos_DeUnaPrivada_OtroUsuarioNoExiste()
        {
            ComoOtro();

            Assert.IsInstanceOfType(controller.GetAdjuntos(PRIVADA), typeof(NotFoundResult));
        }

        [TestMethod]
        public void GetAdjunto_DeUnaPrivada_OtroUsuarioNoLoDescarga()
        {
            A.CallTo(() => adjuntos.Leer(5)).Returns(new AdjuntoNovedadContenido { NovedadId = PRIVADA, Nombre = "a.pdf", Tipo = "application/pdf", Contenido = new byte[] { 1 } });
            ComoOtro();

            Assert.AreEqual(HttpStatusCode.NotFound, controller.GetAdjunto(5).StatusCode);
            ComoAutor();
            Assert.AreEqual(HttpStatusCode.OK, controller.GetAdjunto(5).StatusCode);
        }

        // ---- Votos ----

        [TestMethod]
        public async Task PutVoto_UnaPrivada_NoAdmiteMeGusta()
        {
            ComoAutor();

            var resultado = await controller.PutVoto(PRIVADA, new VotoNovedadDTO { Voto = 1 });

            Assert.AreEqual(PrivacidadNovedades.ERROR_VOTO_PRIVADA, ((BadRequestErrorMessageResult)resultado).Message);
            A.CallTo(() => feedback.Votar(A<int>._, A<string>._, A<string>._, A<short>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutVoto_UnaPrivada_SePuedeQuitarElVotoDeCuandoEraPublica()
        {
            ComoAutor();

            var resultado = await controller.PutVoto(PRIVADA, new VotoNovedadDTO { Voto = 0 });

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            A.CallTo(() => feedback.Votar(PRIVADA, AUTOR, A<string>._, (short)0)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PutVoto_UnaPrivada_OtroUsuarioNoExiste()
        {
            ComoOtro();

            Assert.IsInstanceOfType(await controller.PutVoto(PRIVADA, new VotoNovedadDTO { Voto = 1 }), typeof(NotFoundResult));
        }

        // ---- Crear y cambiar ----

        [TestMethod]
        public async Task PostSugerencia_Privada_SeGrabaPrivadaYLoDevuelve()
        {
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).Returns(800);
            ComoAutor();

            var resultado = (OkNegotiatedContentResult<SugerenciaNovedadDTO>)await controller.PostSugerencia(
                new NuevaSugerenciaNovedadDTO { Texto = "Que los pedidos de compra avisen", Privada = true });

            Assert.IsTrue(resultado.Content.Privada);
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>.That.Matches(s => s.Privada))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostSugerencia_SinPrivada_EsPublicaComoSiempreYNiSeMiraLaColumna()
        {
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).Returns(800);
            ComoAutor();

            var resultado = (OkNegotiatedContentResult<SugerenciaNovedadDTO>)await controller.PostSugerencia(
                new NuevaSugerenciaNovedadDTO { Texto = "Un botón" });

            Assert.IsFalse(resultado.Content.Privada);
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>.That.Matches(s => !s.Privada))).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicio.AdmitePrivadas()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostSugerencia_PrivadaSinLaColumna_NoSeCreaYSeDicePorQue()
        {
            A.CallTo(() => servicio.AdmitePrivadas()).Returns(false);
            ComoAutor();

            var resultado = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO { Texto = "Algo privado", Privada = true });

            Assert.AreEqual(PrivacidadNovedades.ERROR_SIN_COLUMNA, ((BadRequestErrorMessageResult)resultado).Message);
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PutSugerencia_InformaticaLaPasaAPublica()
        {
            A.CallTo(() => servicio.ActualizarSugerencia(PRIVADA, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).Returns(true);
            ComoInformatica();

            var resultado = controller.PutSugerencia(PRIVADA, new ActualizarSugerenciaNovedadDTO { Privada = false });

            Assert.AreEqual(HttpStatusCode.NoContent, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => servicio.GuardarPrivada(PRIVADA, false, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PutSugerencia_SinPrivada_NoTocaLaPrivacidad()
        {
            A.CallTo(() => servicio.ActualizarSugerencia(PUBLICA, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).Returns(true);
            ComoInformatica();

            _ = controller.PutSugerencia(PUBLICA, new ActualizarSugerenciaNovedadDTO { Estado = "Aceptada" });

            A.CallTo(() => servicio.GuardarPrivada(A<int>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PutSugerencia_APrivadaSinLaColumna_Error()
        {
            A.CallTo(() => servicio.AdmitePrivadas()).Returns(false);
            ComoInformatica();

            var resultado = controller.PutSugerencia(PUBLICA, new ActualizarSugerenciaNovedadDTO { Privada = true });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.ActualizarSugerencia(A<int>._, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PutSugerencia_ElAutorNoPuedeCambiarla()
        {
            ComoAutor();

            var resultado = controller.PutSugerencia(PRIVADA, new ActualizarSugerenciaNovedadDTO { Privada = false });

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
        }

        // ---- Avisos ----

        [TestMethod]
        public async Task PostComentario_EnUnaPrivada_LaMencionSoloAvisaAQuienPuedeVerla()
        {
            ComoAutor();

            _ = await controller.PostComentario(PRIVADA, new NuevoComentarioNovedadDTO { Texto = "@Alfredo y @Carlos, mirad esto" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo.Contains("mencionado")))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentario_EnUnaPublica_LaMencionAvisaComoSiempre()
        {
            ComoAutor();

            _ = await controller.PostComentario(PUBLICA, new NuevoComentarioNovedadDTO { Texto = "@Alfredo, mira esto" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentario_DeDireccionEnUnaPrivada_LaMencionAlAutorLeLlega()
        {
            ComoInformatica();

            _ = await controller.PostComentario(PRIVADA, new NuevoComentarioNovedadDTO { Texto = "@Manuel, ¿lo quiere así?" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Manuel", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostSugerencia_Privada_LaMencionAQuienNoPuedeVerlaNoAvisa()
        {
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).Returns(800);
            ComoAutor();

            _ = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO { Texto = "Como dice @Alfredo, falta un botón", Privada = true });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostSugerencia_Privada_AlSupervisorDeDireccionLeLlegaElAviso()
        {
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).Returns(800);
            ComoAutor();

            _ = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO { Texto = "Falta un botón", Privada = true });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentario_EnUnaPrivada_ElSupervisorQueNoEsDeDireccionNoSeEntera()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, Constantes.ParametrosUsuario.AVISAR_ACTIVIDAD_NOVEDADES_A))
                .Returns("NUEVAVISION\\Alfredo");
            ComoAutor();

            _ = await controller.PostComentario(PRIVADA, new NuevoComentarioNovedadDTO { Texto = "Otra cosa más" });
            _ = await controller.PostComentario(PUBLICA, new NuevoComentarioNovedadDTO { Texto = "En la pública sí" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Datos["novedadId"] == PRIVADA.ToString()))).MustNotHaveHappened();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Datos["novedadId"] == PUBLICA.ToString()))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_EnUnaPrivada_NoContestaAQuienYaNoPuedeVerla()
        {
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).Returns(61);
            A.CallTo(() => feedback.LeerAutores(A<IEnumerable<int>>._)).Returns(new List<AutorComentarioNovedad>
            {
                new AutorComentarioNovedad { Id = 50, NovedadId = PRIVADA, Usuario = "NUEVAVISION\\Alfredo", NombreVisible = "Alfredo", Cliente = ReglasFeedbackNovedades.CLIENTE_NESTO },
                new AutorComentarioNovedad { Id = 51, NovedadId = PRIVADA, Usuario = AUTOR, NombreVisible = "Manuel", Cliente = ReglasFeedbackNovedades.CLIENTE_NESTO }
            });
            ComoInformatica();

            _ = await controller.PostComentarioAsistente(PRIVADA, new NuevoComentarioAsistenteDTO { Texto = "Ya está hecho", ComentariosContestados = new List<int> { 50, 51 } });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(AUTOR, "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        // ---- Reglas puras ----

        [TestMethod]
        public void PuedeRecibirAviso_PorClaveConDominioPorNombreDeLaAppYPorGrupo()
        {
            PrivacidadNovedades privacidad = PrivacidadNovedades.Desde(new[]
            {
                new SugerenciaPrivadaFila { Id = 1, SugeridaPor = "5f1c-guid", SugeridaNombre = "manuel" }
            }, null, false);

            Assert.IsTrue(privacidad.PuedeRecibirAviso(1, "manuel", null), "El UserName de la app (el nombre visible)");
            Assert.IsTrue(privacidad.PuedeRecibirAviso(1, "NUEVAVISION\\Carlos", () => new[] { "carlos" }), "De Dirección");
            Assert.IsFalse(privacidad.PuedeRecibirAviso(1, "NUEVAVISION\\Alfredo", () => new[] { "carlos" }));
            Assert.IsTrue(privacidad.PuedeRecibirAviso(2, "NUEVAVISION\\Alfredo", null), "La 2 es pública");
        }
    }
}
