using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Carlos, 28/09/26: le llega aviso de todo lo que hacen los usuarios en Novedades (comentarios,
    /// votos y sugerencias) por el programa donde se hace: Nesto → su campana; NestoApp → push.
    /// </summary>
    [TestClass]
    public class NovedadesActividadSupervisorTests
    {
        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private IServicioNotificacionesPush notificaciones;
        private ILectorParametrosUsuario lector;
        private NovedadesController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, Constantes.ParametrosUsuario.AVISAR_ACTIVIDAD_NOVEDADES_A))
                .Returns(null); // sin fila: Carlos
            A.CallTo(() => feedback.ExisteNovedad(A<int>._)).Returns(true);
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).Returns(60);
            A.CallTo(() => feedback.LeerAmbitoNovedad(350)).Returns("Nesto");
            A.CallTo(() => feedback.LeerTituloNovedad(350)).Returns("Cambiar el cliente de un pedido");
            A.CallTo(() => feedback.LeerMencionables(false)).Returns(new List<MencionableDTO>
            {
                new MencionableDTO { Nombre = "Carlos", Clave = "NUEVAVISION\\Carlos", Aplicacion = "Nesto" }
            });
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).Returns(401);
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Notificaciones = notificaciones,
                LectorParametros = lector
            };
        }

        private void ComoUsuarioDeNesto(string usuario)
        {
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, usuario),
                new Claim(ClaimTypes.Name, usuario),
                new Claim(ClaimTypes.AuthenticationMethod, "Windows")
            }, "Bearer"));
        }

        private void ComoUsuarioDeLaApp(string nombre)
        {
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "5f1c-guid"),
                new Claim(ClaimTypes.Name, nombre)
            }, "Bearer"));
        }

        [TestMethod]
        public async Task PostComentario_DesdeNesto_LlegaALaCampanaDeCarlos()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");

            _ = await controller.PostComentario(350, new NuevoComentarioNovedadDTO { Texto = "Me viene genial" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "Alfredo ha comentado «Cambiar el cliente de un pedido»"
                    && n.Cuerpo == "Me viene genial" && n.Datos["novedadId"] == "350" && n.Datos["comentarioId"] == "60")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.EnviarAUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostComentario_DesdeLaApp_LlegaPorPushAlMovilDeCarlos()
        {
            ComoUsuarioDeLaApp("Manuel");

            _ = await controller.PostComentario(350, new NuevoComentarioNovedadDTO { Texto = "No me sale el botón" });

            A.CallTo(() => notificaciones.EnviarAUsuario("Carlos", "NestoApp", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutVoto_Negativo_AvisaConElTituloDeLaNovedad()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\MariaJose");

            _ = await controller.PutVoto(350, new VotoNovedadDTO { Voto = -1 });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "MariaJose ha votado 👎 en Novedades"
                    && n.Cuerpo == "Cambiar el cliente de un pedido")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PutVoto_QuitarElVoto_NoAvisa()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\MariaJose");

            _ = await controller.PutVoto(350, new VotoNovedadDTO { Voto = 0 });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostSugerencia_Avisa()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");

            _ = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO { Texto = "Un botón para duplicar pedidos" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "Alfredo ha hecho una sugerencia" && n.Datos["novedadId"] == "401")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LoQueHaceElPropioCarlos_NoLeAvisa()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos");

            _ = await controller.PostComentario(350, new NuevoComentarioNovedadDTO { Texto = "Probando" });
            _ = await controller.PutVoto(350, new VotoNovedadDTO { Voto = 1 });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiYaLeMencionan_SoloLeLlegaUnAviso()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");

            _ = await controller.PostComentario(350, new NuevoComentarioNovedadDTO { Texto = "@Carlos, ¿lo miras?" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", A<string>._, A<NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ParametroACero_NoAvisaANadie()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, Constantes.ParametrosUsuario.AVISAR_ACTIVIDAD_NOVEDADES_A))
                .Returns("0");
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");

            _ = await controller.PostComentario(350, new NuevoComentarioNovedadDTO { Texto = "Me viene genial" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ParametroConOtroUsuario_LeAvisaAEl()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, Constantes.ParametrosUsuario.AVISAR_ACTIVIDAD_NOVEDADES_A))
                .Returns("NUEVAVISION\\Laura");
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");

            _ = await controller.PostComentario(350, new NuevoComentarioNovedadDTO { Texto = "Me viene genial" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Laura", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        // ---- Carlos, 30/09/26: lo que contesta el asistente también le llega al supervisor ----

        private void ComoElAsistente()
        {
            // El asistente contesta con el usuario de quien lo lanza (Dirección / Informática)
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "NUEVAVISION\\Carlos"),
                new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos"),
                new Claim(ClaimTypes.AuthenticationMethod, "Windows"),
                new Claim(ClaimTypes.Role, "NUEVAVISION\\Informatica")
            }, "Bearer"));
        }

        [TestMethod]
        public async Task PostComentarioAsistente_LaRespuestaLlegaTambienALaCampanaDeCarlos()
        {
            // Contestó a Laura (434) y a Enrique (435) y Carlos no recibió nada
            ComoElAsistente();
            A.CallTo(() => feedback.LeerMencionables(false)).Returns(new List<MencionableDTO>
            {
                new MencionableDTO { Nombre = "Laura", Clave = "NUEVAVISION\\Laura", Aplicacion = "Nesto" }
            });

            _ = await controller.PostComentarioAsistente(350, new NuevoComentarioAsistenteDTO { Texto = "Hola, @Laura. Lo hemos mirado." });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "Claude (asistente IA) ha contestado en Novedades"
                    && n.Cuerpo == "Hola, @Laura. Lo hemos mirado." && n.Datos["novedadId"] == "350" && n.Datos["comentarioId"] == "60")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Laura", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_SiYaLeLlegaComoMencion_NoSeLeAvisaDosVeces()
        {
            ComoElAsistente();

            _ = await controller.PostComentarioAsistente(350, new NuevoComentarioAsistenteDTO { Texto = "Hecho, @Carlos." });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_SiYaLeLlegaComoAutorContestado_NoSeLeAvisaDosVeces()
        {
            ComoElAsistente();
            A.CallTo(() => feedback.LeerAutores(A<IEnumerable<int>>._)).Returns(new List<AutorComentarioNovedad>
            {
                new AutorComentarioNovedad { Id = 9, NovedadId = 350, Usuario = "NUEVAVISION\\Carlos", NombreVisible = "Carlos", Cliente = "Nesto" }
            });

            _ = await controller.PostComentarioAsistente(350, new NuevoComentarioAsistenteDTO
            {
                Texto = "Anotado.",
                ComentariosContestados = new List<int> { 9 }
            });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_SinSupervisor_NoAvisaANadieMas()
        {
            ComoElAsistente();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, Constantes.ParametrosUsuario.AVISAR_ACTIVIDAD_NOVEDADES_A)).Returns("0");

            _ = await controller.PostComentarioAsistente(350, new NuevoComentarioAsistenteDTO { Texto = "Anotado." });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }
    }
}
