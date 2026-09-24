using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#537: @menciones en Novedades. Uso principal: si la conversación de un compañero con el
    /// asistente se atasca, se menciona a @Carlos y le llega el aviso para que ayude.
    /// </summary>
    [TestClass]
    public class NovedadesMencionesTests
    {
        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private IServicioNotificacionesPush notificaciones;
        private NovedadesController controller;

        private static readonly List<MencionableDTO> USUARIOS_NESTO = new List<MencionableDTO>
        {
            new MencionableDTO { Nombre = "Carlos", Clave = "NUEVAVISION\\Carlos", Aplicacion = "Nesto" },
            new MencionableDTO { Nombre = "Alfredo", Clave = "NUEVAVISION\\Alfredo", Aplicacion = "Nesto" },
            new MencionableDTO { Nombre = "MariaJose", Clave = "NUEVAVISION\\MariaJose", Aplicacion = "Nesto" }
        };

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => feedback.ExisteNovedad(A<int>._)).Returns(true);
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).Returns(60);
            A.CallTo(() => feedback.LeerAmbitoNovedad(350)).Returns("Nesto");
            A.CallTo(() => feedback.LeerMencionables(false)).Returns(USUARIOS_NESTO);
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Notificaciones = notificaciones
            };
        }

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

        // ---- Reglas puras ----

        [TestMethod]
        public void Extraer_AlPrincipioEnMedioYSinTildes_PeroNoLosCorreos()
        {
            List<string> menciones = ReglasMenciones.Extraer("@Carlos, ¿lo miras? Dice @maríajose que sí. Escribid a pepe@nuevavision.es (@Carlos otra vez)");

            CollectionAssert.AreEqual(new[] { "CARLOS", "MARIAJOSE" }, menciones);
        }

        [TestMethod]
        public void Resolver_SinElAutorNiDesconocidosNiRepetidos()
        {
            List<MencionableDTO> resultado = ReglasMenciones.Resolver(new[] { "CARLOS", "ALFREDO", "NADIE" }, USUARIOS_NESTO, "nuevavision\\alfredo");

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Carlos" }, resultado.Select(r => r.Clave).ToArray(),
                "Alfredo se menciona a sí mismo: no; «Nadie» no existe: no");
        }

        [TestMethod]
        public void Resolver_NombreQueCasaConDos_NoAvisaANadie()
        {
            var dos = new List<MencionableDTO>
            {
                new MencionableDTO { Nombre = "Jesus", Clave = "A" },
                new MencionableDTO { Nombre = "Jesús", Clave = "B" }
            };

            Assert.AreEqual(0, ReglasMenciones.Resolver(new[] { "JESUS" }, dos, null).Count);
        }

        // ---- Controller ----

        [TestMethod]
        public async Task PostComentario_ConMencion_ElMencionadoDeNestoLoRecibeEnSuBuzon()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");

            _ = await controller.PostComentario(350, new NuevoComentarioNovedadDTO { Texto = "@Carlos, el asistente no me entiende" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "Alfredo te ha mencionado en Novedades"
                    && n.Datos["novedadId"] == "350" && n.Datos["comentarioId"] == "60")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentario_NovedadDeNestoApp_ElMencionadoRecibePush()
        {
            A.CallTo(() => feedback.LeerAmbitoNovedad(200)).Returns("NestoApp");
            A.CallTo(() => feedback.LeerMencionables(true)).Returns(new List<MencionableDTO>
            {
                new MencionableDTO { Nombre = "Carlos", Clave = "Carlos", Aplicacion = "NestoApp" }
            });
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "5f1c-guid"),
                new Claim(ClaimTypes.Name, "Manuel")
            }, "Bearer"));

            _ = await controller.PostComentario(200, new NuevoComentarioNovedadDTO { Texto = "@Carlos ¿esto es así?" });

            A.CallTo(() => notificaciones.EnviarAUsuario("Carlos", "NestoApp", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostComentario_SinMenciones_NoConsultaNadaNiAvisa()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");

            _ = await controller.PostComentario(350, new NuevoComentarioNovedadDTO { Texto = "Todo bien, gracias" });

            A.CallTo(() => feedback.LeerMencionables(A<bool>._)).MustNotHaveHappened();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_MencionaAlAutorContestado_SoloUnAviso()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            A.CallTo(() => feedback.LeerAutores(A<IEnumerable<int>>._)).Returns(new List<AutorComentarioNovedad>
            {
                new AutorComentarioNovedad { Id = 1, NovedadId = 350, Usuario = "NUEVAVISION\\Alfredo", NombreVisible = "Alfredo", Cliente = "Nesto" }
            });

            _ = await controller.PostComentarioAsistente(350, new NuevoComentarioAsistenteDTO
            {
                Texto = "@Alfredo, ya está. @Carlos, échale un ojo.",
                ComentariosContestados = new List<int> { 1 }
            });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostSugerencia_ConMencion_AvisaYLlevaALaSugerencia()
        {
            ComoUsuarioDeNesto(@"NUEVAVISION\Alfredo");
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._)).Returns(361);
            A.CallTo(() => feedback.LeerAmbitoNovedad(361)).Returns("Nesto");

            _ = await controller.PostSugerencia(new NuevoComentarioNovedadDTO { Texto = "Un botón para duplicar. @Carlos, ¿lo ves?" });

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Carlos", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Datos["novedadId"] == "361" && !n.Datos.ContainsKey("comentarioId"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void GetMencionables_SinAmbito_LosDeNesto()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");

            var resultado = controller.GetMencionables() as OkNegotiatedContentResult<List<MencionableDTO>>;

            Assert.AreEqual(3, resultado.Content.Count);
        }
    }
}
