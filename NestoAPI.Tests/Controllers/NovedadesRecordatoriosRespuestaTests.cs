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
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Recordatorios de respuesta (Carlos, 16/10/26): el asistente pregunta, el autor no contesta → dos recordatorios
    /// cada 2 días laborables y, a la tercera, Cerrada (visible para el autor, que la reabre comentando).
    /// </summary>
    [TestClass]
    public class NovedadesRecordatoriosRespuestaTests
    {
        // Semana del 19/10/26: lunes 19 … viernes 23; sábado 24, domingo 25; lunes 26.
        private static readonly DateTime LUNES = new DateTime(2026, 10, 19, 10, 15, 0);
        private static readonly Func<DateTime, bool> TODOS_LABORABLES = d => true;

        private IServicioNovedades servicio;
        private IServicioFeedbackNovedades feedback;
        private IServicioNotificacionesPush notificaciones;
        private ILectorParametrosUsuario lector;
        private IRepositorioEsperaRespuestaNovedades espera;
        private NovedadesController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            feedback = A.Fake<IServicioFeedbackNovedades>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            lector = A.Fake<ILectorParametrosUsuario>();
            espera = A.Fake<IRepositorioEsperaRespuestaNovedades>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, Constantes.ParametrosUsuario.AVISAR_ACTIVIDAD_NOVEDADES_A)).Returns(null);
            A.CallTo(() => feedback.ExisteNovedad(A<int>._)).Returns(true);
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).Returns(80);
            A.CallTo(() => feedback.LeerTituloNovedad(584)).Returns("Avisar al comercial del pago");
            controller = new NovedadesController(servicio, feedback)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Notificaciones = notificaciones,
                LectorParametros = lector,
                EsperaRespuesta = espera
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

        private static EsperaRespuestaNovedad Espera(string estado = "Pendiente", DateTime? desde = null, byte recordatorios = 0,
            DateTime? ultimo = null, string ambito = "Nesto", string categoria = "Nuevo", string esperandoDe = null) => new EsperaRespuestaNovedad
            {
                Id = 584,
                Estado = estado,
                Categoria = categoria,
                Ambito = ambito,
                Titulo = "Avisar al comercial del pago",
                SugeridaPor = "NUEVAVISION\\MariaJose",
                SugeridaNombre = "MariaJose",
                EsperandoRespuestaDesde = desde,
                RecordatoriosRespuesta = recordatorios,
                UltimoRecordatorioRespuesta = ultimo,
                EsperandoRespuestaDe = esperandoDe
            };

        private RecordatoriosRespuestaNovedades Motor(DateTime ahora, bool activo = true, Func<DateTime, bool> esLaborable = null) =>
            new RecordatoriosRespuestaNovedades(espera, feedback, notificaciones, () => ahora, esLaborable ?? TODOS_LABORABLES, () => activo);

        // ---- Marcar la espera (POST del asistente) ----

        [TestMethod]
        public async Task PostComentarioAsistente_ConEsperaRespuesta_MarcaLaEspera()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");

            _ = await controller.PostComentarioAsistente(584, new NuevoComentarioAsistenteDTO { Texto = "¿Qué correo es?", EsperaRespuesta = true });

            A.CallTo(() => espera.MarcarEspera(584, A<DateTime>._, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_SinEsperaRespuesta_NoTocaLaEspera()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");

            _ = await controller.PostComentarioAsistente(584, new NuevoComentarioAsistenteDTO { Texto = "Ya está corregido." });

            A.CallTo(() => espera.MarcarEspera(A<int>._, A<DateTime>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => espera.BorrarEspera(A<int>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_SiFallaLaEspera_ElComentarioSeGrabaIgual()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            A.CallTo(() => espera.MarcarEspera(A<int>._, A<DateTime>._, A<string>._)).Throws(new InvalidOperationException("columna inexistente"));

            var resultado = await controller.PostComentarioAsistente(584, new NuevoComentarioAsistenteDTO { Texto = "¿Qué correo es?", EsperaRespuesta = true });

            Assert.IsInstanceOfType(resultado, typeof(System.Web.Http.Results.OkNegotiatedContentResult<ComentarioNovedadDTO>));
        }

        // ---- Quién quita la espera ----

        [TestMethod]
        public async Task PostComentario_ElAutorContesta_SeBorraLaEspera()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\MariaJose");
            A.CallTo(() => espera.Leer(584)).Returns(Espera(desde: LUNES, recordatorios: 1));

            _ = await controller.PostComentario(584, new NuevoComentarioNovedadDTO { Texto = "Es el enlace de pago" });

            A.CallTo(() => espera.BorrarEspera(584, false, "NUEVAVISION\\MariaJose")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentario_OtroUsuarioComenta_NoSeBorraLaEspera()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");
            A.CallTo(() => espera.Leer(584)).Returns(Espera(desde: LUNES));

            _ = await controller.PostComentario(584, new NuevoComentarioNovedadDTO { Texto = "A mí también me pasa" });

            A.CallTo(() => espera.BorrarEspera(A<int>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostComentario_ElAutorComentaEnUnaCerrada_SeReabreYSeAvisaAlSupervisor()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\MariaJose");
            A.CallTo(() => espera.Leer(584)).Returns(Espera(estado: "Cerrada"));

            _ = await controller.PostComentario(584, new NuevoComentarioNovedadDTO { Texto = "Sí, la sigo necesitando" });

            A.CallTo(() => espera.BorrarEspera(584, true, "NUEVAVISION\\MariaJose")).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "MariaJose ha reabierto «Avisar al comercial del pago»")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentario_ElAutorSinEsperaNiCerrada_NoTocaNada()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\MariaJose");
            A.CallTo(() => espera.Leer(584)).Returns(Espera());

            _ = await controller.PostComentario(584, new NuevoComentarioNovedadDTO { Texto = "Otra cosa" });

            A.CallTo(() => espera.BorrarEspera(A<int>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        // ---- Días laborables ----

        [TestMethod]
        public void Decidir_PreguntaElLunes_ElMartesNadaYElMiercolesRecordatorio()
        {
            EsperaRespuestaNovedad e = Espera(desde: LUNES);

            Assert.AreEqual(AccionEsperaRespuesta.Nada, ReglasEsperaRespuestaNovedades.Decidir(e, LUNES.Date.AddDays(1).AddHours(9), TODOS_LABORABLES));
            Assert.AreEqual(AccionEsperaRespuesta.Recordatorio, ReglasEsperaRespuestaNovedades.Decidir(e, LUNES.Date.AddDays(2).AddHours(9), TODOS_LABORABLES));
        }

        [TestMethod]
        public void Decidir_LosFinesDeSemanaNoCuentan()
        {
            EsperaRespuestaNovedad e = Espera(desde: new DateTime(2026, 10, 22, 17, 0, 0)); // jueves

            Assert.AreEqual(AccionEsperaRespuesta.Nada, ReglasEsperaRespuestaNovedades.Decidir(e, new DateTime(2026, 10, 23, 9, 0, 0), TODOS_LABORABLES), "viernes: 1");
            Assert.AreEqual(AccionEsperaRespuesta.Nada, ReglasEsperaRespuestaNovedades.Decidir(e, new DateTime(2026, 10, 25, 9, 0, 0), TODOS_LABORABLES), "domingo: sigue 1");
            Assert.AreEqual(AccionEsperaRespuesta.Recordatorio, ReglasEsperaRespuestaNovedades.Decidir(e, new DateTime(2026, 10, 26, 9, 0, 0), TODOS_LABORABLES), "lunes: 2");
        }

        [TestMethod]
        public void Decidir_LosFestivosNoCuentan()
        {
            EsperaRespuestaNovedad e = Espera(desde: LUNES);
            Func<DateTime, bool> miercolesFestivo = d => d.Date != new DateTime(2026, 10, 21);

            Assert.AreEqual(AccionEsperaRespuesta.Nada, ReglasEsperaRespuestaNovedades.Decidir(e, new DateTime(2026, 10, 21, 9, 0, 0), miercolesFestivo));
            Assert.AreEqual(AccionEsperaRespuesta.Recordatorio, ReglasEsperaRespuestaNovedades.Decidir(e, new DateTime(2026, 10, 22, 9, 0, 0), miercolesFestivo));
        }

        [TestMethod]
        public void Decidir_ElSegundoRecordatorioCuentaDesdeElPrimero()
        {
            DateTime primero = new DateTime(2026, 10, 21, 9, 0, 0); // miércoles
            EsperaRespuestaNovedad e = Espera(desde: LUNES, recordatorios: 1, ultimo: primero);

            Assert.AreEqual(AccionEsperaRespuesta.Nada, ReglasEsperaRespuestaNovedades.Decidir(e, new DateTime(2026, 10, 22, 9, 0, 0), TODOS_LABORABLES));
            Assert.AreEqual(AccionEsperaRespuesta.Recordatorio, ReglasEsperaRespuestaNovedades.Decidir(e, new DateTime(2026, 10, 23, 9, 0, 0), TODOS_LABORABLES));
        }

        [TestMethod]
        public void Decidir_YaImplementadaODescartada_Nada()
        {
            Assert.AreEqual(AccionEsperaRespuesta.Nada, ReglasEsperaRespuestaNovedades.Decidir(Espera(estado: "Descartada", desde: LUNES), LUNES.AddDays(9), TODOS_LABORABLES));
            EsperaRespuestaNovedad conVersion = Espera(desde: LUNES);
            conVersion.Version = "1.10.43.0";
            Assert.AreEqual(AccionEsperaRespuesta.Nada, ReglasEsperaRespuestaNovedades.Decidir(conVersion, LUNES.AddDays(9), TODOS_LABORABLES));
        }

        // ---- El job ----

        [TestMethod]
        public async Task Ejecutar_PrimerRecordatorio_ComentaConLaMencionAvisaAlAutorYLoApunta()
        {
            DateTime miercoles = new DateTime(2026, 10, 21, 9, 0, 0);
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad> { Espera(desde: LUNES) });
            ComentarioNovedadAGrabar grabado = null;
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).Invokes((ComentarioNovedadAGrabar c) => grabado = c).Returns(81);

            List<ResultadoRecordatorioRespuestaDTO> resultados = await Motor(miercoles).Ejecutar(ensayo: false);

            Assert.AreEqual(1, resultados.Count);
            Assert.AreEqual(1, resultados[0].Recordatorio);
            Assert.AreEqual("Claude", grabado.Usuario);
            Assert.AreEqual("Claude (asistente IA)", grabado.NombreVisible);
            Assert.AreEqual("Hola, @MariaJose. Le recordamos que estamos esperando su respuesta a la pregunta de arriba para poder seguir con esto.", grabado.Texto);
            A.CallTo(() => feedback.MarcarRevisado(81)).MustHaveHappenedOnceExactly();
            A.CallTo(() => espera.GuardarRecordatorio(584, 1, miercoles)).MustHaveHappenedOnceExactly();
            A.CallTo(() => espera.Cerrar(A<int>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\MariaJose", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Datos["novedadId"] == "584" && n.Datos["comentarioId"] == "81")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Ejecutar_SegundoRecordatorio_AvisaDeQueSeCerrara()
        {
            DateTime viernes = new DateTime(2026, 10, 23, 9, 0, 0);
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad>
            {
                Espera(desde: LUNES, recordatorios: 1, ultimo: new DateTime(2026, 10, 21, 9, 0, 0))
            });

            List<ResultadoRecordatorioRespuestaDTO> resultados = await Motor(viernes).Ejecutar(ensayo: false);

            Assert.AreEqual(2, resultados.Single().Recordatorio);
            StringAssert.Contains(resultados[0].Texto, "Seguimos esperando su respuesta");
            A.CallTo(() => espera.GuardarRecordatorio(584, 2, viernes)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Ejecutar_TrasElSegundoSinRespuesta_LaCierra()
        {
            DateTime martes = new DateTime(2026, 10, 27, 9, 0, 0);
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad>
            {
                Espera(desde: LUNES, recordatorios: 2, ultimo: new DateTime(2026, 10, 23, 9, 0, 0), categoria: "Incidencia")
            });
            ComentarioNovedadAGrabar grabado = null;
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).Invokes((ComentarioNovedadAGrabar c) => grabado = c).Returns(82);

            List<ResultadoRecordatorioRespuestaDTO> resultados = await Motor(martes).Ejecutar(ensayo: false);

            Assert.AreEqual("Cerrar", resultados.Single().Accion);
            Assert.AreEqual("Hola, @MariaJose. Damos esta incidencia por cerrada al no tener respuesta; si la necesita, escriba aquí y se reabre.", grabado.Texto);
            A.CallTo(() => espera.Cerrar(584, martes)).MustHaveHappenedOnceExactly();
            A.CallTo(() => espera.GuardarRecordatorio(A<int>._, A<byte>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ejecutar_ElSabadoTrasPreguntarElJueves_NoRecuerda()
        {
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad> { Espera(desde: new DateTime(2026, 10, 22, 17, 0, 0)) });

            List<ResultadoRecordatorioRespuestaDTO> resultados = await Motor(new DateTime(2026, 10, 24, 9, 0, 0)).Ejecutar(ensayo: false);

            Assert.AreEqual(0, resultados.Count);
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ejecutar_InterruptorApagado_NoHaceNada()
        {
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad> { Espera(desde: LUNES) });

            List<ResultadoRecordatorioRespuestaDTO> resultados = await Motor(LUNES.AddDays(3), activo: false).Ejecutar(ensayo: false);

            Assert.AreEqual(0, resultados.Count);
            A.CallTo(() => espera.LeerEsperando()).MustNotHaveHappened();
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ejecutar_Ensayo_DiceLoQueHariaSinTocarNada()
        {
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad> { Espera(desde: LUNES) });

            List<ResultadoRecordatorioRespuestaDTO> resultados = await Motor(LUNES.AddDays(3), activo: false).Ejecutar(ensayo: true);

            Assert.AreEqual(1, resultados.Count);
            Assert.IsTrue(resultados[0].Ensayo);
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).MustNotHaveHappened();
            A.CallTo(() => espera.GuardarRecordatorio(A<int>._, A<byte>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ejecutar_SugerenciaDeNestoApp_LlegaPorPushAlUsuarioDeLaApp()
        {
            EsperaRespuestaNovedad e = Espera(desde: LUNES, ambito: "NestoApp");
            e.SugeridaPor = "5e63cedf-guid";
            e.SugeridaNombre = "Marta";
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad> { e });

            _ = await Motor(LUNES.AddDays(2)).Ejecutar(ensayo: false);

            A.CallTo(() => notificaciones.EnviarAUsuario("Marta", "NestoApp", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        // ---- NestoAPI#783: la pregunta va a un @mencionado ----

        private const string ESPERANDO_A_SANTIAGO = "Nesto|NUEVAVISION\\Santiago|Santiago";

        private void ConMencionables(params string[] nombres)
        {
            A.CallTo(() => feedback.LeerAmbitoNovedad(584)).Returns("Nesto");
            A.CallTo(() => feedback.LeerMencionables(false)).Returns(nombres
                .Select(n => new MencionableDTO { Nombre = n, Clave = "NUEVAVISION\\" + n, Aplicacion = "Nesto" }).ToList());
        }

        [TestMethod]
        public async Task PostComentarioAsistente_ConMencion_EsperaAlMencionado()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            ConMencionables("Santiago", "MariaJose", "Alfredo");

            _ = await controller.PostComentarioAsistente(584, new NuevoComentarioAsistenteDTO
            {
                Texto = "@Santiago, ¿puede contar el 46048?",
                EsperaRespuesta = true
            });

            A.CallTo(() => espera.MarcarEspera(584, A<DateTime>._, ESPERANDO_A_SANTIAGO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_ConVariasMenciones_EsperaATodos()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            ConMencionables("Santiago", "Alfredo");

            _ = await controller.PostComentarioAsistente(584, new NuevoComentarioAsistenteDTO
            {
                Texto = "@Santiago o @Alfredo, ¿pueden contarlo?",
                EsperaRespuesta = true
            });

            A.CallTo(() => espera.MarcarEspera(584, A<DateTime>._, ESPERANDO_A_SANTIAGO + ";Nesto|NUEVAVISION\\Alfredo|Alfredo")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentarioAsistente_MencionQueNoExiste_EsperaAlAutor()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            ConMencionables("Santiago");

            _ = await controller.PostComentarioAsistente(584, new NuevoComentarioAsistenteDTO { Texto = "@Fulanito, ¿qué correo es?", EsperaRespuesta = true });

            A.CallTo(() => espera.MarcarEspera(584, A<DateTime>._, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Ejecutar_ConMencion_ElRecordatorioVaAlMencionadoYNoAlAutor()
        {
            DateTime miercoles = new DateTime(2026, 10, 21, 9, 0, 0);
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad> { Espera(desde: LUNES, esperandoDe: ESPERANDO_A_SANTIAGO) });
            ComentarioNovedadAGrabar grabado = null;
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).Invokes((ComentarioNovedadAGrabar c) => grabado = c).Returns(81);

            List<ResultadoRecordatorioRespuestaDTO> resultados = await Motor(miercoles).Ejecutar(ensayo: false);

            Assert.AreEqual("Hola, @Santiago. Le recordamos que estamos esperando su respuesta a la pregunta de arriba para poder seguir con esto.", grabado.Texto);
            Assert.AreEqual("Santiago", resultados.Single().Destinatarios);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Santiago", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Datos["novedadId"] == "584" && n.Datos["comentarioId"] == "81")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\MariaJose", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => espera.GuardarRecordatorio(584, 1, miercoles)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Ejecutar_ConMencion_ElCierreTambienVaAlMencionado()
        {
            DateTime martes = new DateTime(2026, 10, 27, 9, 0, 0);
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad>
            {
                Espera(desde: LUNES, recordatorios: 2, ultimo: new DateTime(2026, 10, 23, 9, 0, 0), esperandoDe: ESPERANDO_A_SANTIAGO + ";Nesto|NUEVAVISION\\Alfredo|Alfredo")
            });
            ComentarioNovedadAGrabar grabado = null;
            A.CallTo(() => feedback.CrearComentario(A<ComentarioNovedadAGrabar>._)).Invokes((ComentarioNovedadAGrabar c) => grabado = c).Returns(82);

            _ = await Motor(martes).Ejecutar(ensayo: false);

            Assert.AreEqual("Hola, @Santiago y @Alfredo. Damos esta sugerencia por cerrada al no tener respuesta; si la necesita, escriba aquí y se reabre.", grabado.Texto);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Santiago", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Alfredo", "Nesto", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\MariaJose", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => espera.Cerrar(584, martes)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Ejecutar_MencionadoDeNestoApp_LlegaPorPushASuUsuario()
        {
            A.CallTo(() => espera.LeerEsperando()).Returns(new List<EsperaRespuestaNovedad>
            {
                Espera(desde: LUNES, ambito: "NestoApp", esperandoDe: "NestoApp|Marta|Marta")
            });

            _ = await Motor(LUNES.AddDays(2)).Ejecutar(ensayo: false);

            A.CallTo(() => notificaciones.EnviarAUsuario("Marta", "NestoApp", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostComentario_ContestaElMencionado_SeBorraLaEspera()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Santiago");
            A.CallTo(() => espera.Leer(584)).Returns(Espera(desde: LUNES, recordatorios: 1, esperandoDe: ESPERANDO_A_SANTIAGO));

            _ = await controller.PostComentario(584, new NuevoComentarioNovedadDTO { Texto = "Hay 12" });

            A.CallTo(() => espera.BorrarEspera(584, false, "NUEVAVISION\\Santiago")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostComentario_ComentaElAutorSinSerElEsperado_SigueEsperando()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\MariaJose");
            A.CallTo(() => espera.Leer(584)).Returns(Espera(desde: LUNES, esperandoDe: ESPERANDO_A_SANTIAGO));

            _ = await controller.PostComentario(584, new NuevoComentarioNovedadDTO { Texto = "Santiago, mírelo cuando pueda" });

            A.CallTo(() => espera.BorrarEspera(A<int>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void AlComentar_MencionadoDeLaApp_CuentaPorSuNombreDeUsuario()
        {
            EsperaRespuestaNovedad e = Espera(desde: LUNES, ambito: "NestoApp", esperandoDe: "NestoApp|Marta|Marta");

            Assert.IsTrue(ReglasEsperaRespuestaNovedades.AlComentar(e, "5e63cedf-guid", "Marta").BorrarEspera);
            Assert.IsFalse(ReglasEsperaRespuestaNovedades.AlComentar(e, "otro-guid", "Pepe").BorrarEspera);
        }

        [TestMethod]
        public void AlComentar_Cerrada_LaReabreCualquieraQueComenteMenosElAsistente()
        {
            // Carlos 09/11/26: «Cerrada» = contestada, nada más que hacer; si alguien escribe, vuelve a Pendiente.
            EsperaRespuestaNovedad cerrada = Espera(estado: "Cerrada", esperandoDe: ESPERANDO_A_SANTIAGO);

            Assert.IsTrue(ReglasEsperaRespuestaNovedades.AlComentar(cerrada, "NUEVAVISION\\Santiago").Reabrir);
            Assert.IsTrue(ReglasEsperaRespuestaNovedades.AlComentar(cerrada, "NUEVAVISION\\MariaJose").Reabrir);
            Assert.IsTrue(ReglasEsperaRespuestaNovedades.AlComentar(cerrada, "NUEVAVISION\\Alfredo").Reabrir);
            Assert.IsTrue(ReglasEsperaRespuestaNovedades.AlComentar(cerrada, "NUEVAVISION\\Alfredo").BorrarEspera);
            Assert.IsFalse(ReglasEsperaRespuestaNovedades.AlComentar(cerrada, ReglasFeedbackNovedades.USUARIO_ASISTENTE).Reabrir, "el asistente no la reabre");
        }

        [TestMethod]
        public async Task PostComentario_OtroCompaneroComentaEnUnaCerrada_SeReabre()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");
            A.CallTo(() => espera.Leer(584)).Returns(Espera(estado: "Cerrada"));

            _ = await controller.PostComentario(584, new NuevoComentarioNovedadDTO { Texto = "A mí también me hace falta" });

            A.CallTo(() => espera.BorrarEspera(584, true, "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void SerializarEsperados_IdaYVuelta()
        {
            var esperados = new List<MencionableDTO>
            {
                new MencionableDTO { Aplicacion = "Nesto", Clave = "NUEVAVISION\\Santiago", Nombre = "Santiago" },
                new MencionableDTO { Aplicacion = "NestoApp", Clave = "Marta", Nombre = "Marta" }
            };

            string guardado = ReglasEsperaRespuestaNovedades.SerializarEsperados(esperados);
            List<MencionableDTO> leidos = ReglasEsperaRespuestaNovedades.Esperados(new EsperaRespuestaNovedad { EsperandoRespuestaDe = guardado });

            Assert.AreEqual(2, leidos.Count);
            Assert.AreEqual("NUEVAVISION\\Santiago", leidos[0].Clave);
            Assert.AreEqual("NestoApp", leidos[1].Aplicacion);
            Assert.IsNull(ReglasEsperaRespuestaNovedades.SerializarEsperados(new List<MencionableDTO>()));
        }

        // ---- Estado Cerrada ----

        private static NovedadConSugerenciaFila CerradaEl(DateTime cierre) => new NovedadConSugerenciaFila
        {
            Id = 584,
            Ambito = "Nesto",
            Titulo = "Avisar al comercial del pago",
            Estado = "Cerrada",
            SugeridaPor = "NUEVAVISION\\MariaJose",
            SugeridaFecha = cierre.AddDays(-30),
            FechaModificacion = cierre
        };

        [TestMethod]
        public void SeVeEnLaLista_Cerrada_SuAutorLaVeCatorceDiasDesdeElCierre()
        {
            DateTime cierre = new DateTime(2026, 11, 9, 9, 0, 0);
            NovedadConSugerenciaFila cerrada = CerradaEl(cierre);

            Assert.IsTrue(ReglasSugerenciasNovedades.SeVeEnLaLista(cerrada, "NUEVAVISION\\MariaJose", cierre.AddDays(13)));
            Assert.IsTrue(ReglasSugerenciasNovedades.SeVeEnLaLista(cerrada, "NUEVAVISION\\MariaJose", cierre.AddDays(14)));
            Assert.IsFalse(ReglasSugerenciasNovedades.SeVeEnLaLista(cerrada, "NUEVAVISION\\MariaJose", cierre.AddDays(14).AddMinutes(1)));
        }

        [TestMethod]
        public void SeVeEnLaLista_Cerrada_LosDemasDejanDeVerlaYaDireccionLaVeCatorceDias()
        {
            DateTime cierre = new DateTime(2026, 11, 9, 9, 0, 0);
            NovedadConSugerenciaFila cerrada = CerradaEl(cierre);

            Assert.IsFalse(ReglasSugerenciasNovedades.SeVeEnLaLista(cerrada, "NUEVAVISION\\Alfredo", cierre.AddMinutes(1)));
            Assert.IsFalse(ReglasSugerenciasNovedades.SeVeEnLaLista(cerrada, null, cierre.AddMinutes(1)));
            Assert.IsTrue(ReglasSugerenciasNovedades.SeVeEnLaLista(cerrada, "NUEVAVISION\\Carlos", cierre.AddDays(10), veCerradasDeTodos: true));
            Assert.IsFalse(ReglasSugerenciasNovedades.SeVeEnLaLista(cerrada, "NUEVAVISION\\Carlos", cierre.AddDays(15), veCerradasDeTodos: true));
        }

        [TestMethod]
        public void EstaAbierta_LaCerradaNoEstaAbierta()
        {
            Assert.IsFalse(ReglasSugerenciasNovedades.EstaAbierta("Cerrada"));
        }

        [TestMethod]
        public void GetSugerencias_Cerrada_LaVenSuAutorYDireccionPeroNoLosDemas()
        {
            NovedadConSugerenciaFila cerrada = CerradaEl(DateTime.Now.AddDays(-3));
            NovedadConSugerenciaFila cerradaAntigua = CerradaEl(DateTime.Now.AddDays(-20));
            cerradaAntigua.Id = 585;
            A.CallTo(() => servicio.LeerSugerencias(true)).Returns(new List<NovedadConSugerenciaFila> { cerrada, cerradaAntigua });

            ComoUsuarioDeNesto("NUEVAVISION\\MariaJose");
            var delAutor = controller.GetSugerencias() as System.Web.Http.Results.OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>;
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección");
            var deDireccion = controller.GetSugerencias() as System.Web.Http.Results.OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>;
            ComoUsuarioDeNesto("NUEVAVISION\\Alfredo");
            var deOtro = controller.GetSugerencias() as System.Web.Http.Results.OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>;

            CollectionAssert.AreEqual(new[] { 584 }, delAutor.Content.Select(s => s.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 584 }, deDireccion.Content.Select(s => s.Id).ToArray());
            Assert.AreEqual(0, deOtro.Content.Count);
        }

        [TestMethod]
        public void PutSugerencia_EstadoCerrada_SeGrabaComoCerrada()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            ActualizarSugerenciaNovedadDTO grabado = null;
            A.CallTo(() => servicio.ActualizarSugerencia(584, A<ActualizarSugerenciaNovedadDTO>._, A<string>._))
                .Invokes((int id, ActualizarSugerenciaNovedadDTO c, string u) => grabado = c).Returns(true);

            var resultado = controller.PutSugerencia(584, new ActualizarSugerenciaNovedadDTO { Estado = "cerrada" });

            Assert.AreEqual(System.Net.HttpStatusCode.NoContent, ((System.Web.Http.Results.StatusCodeResult)resultado).StatusCode);
            Assert.AreEqual("Cerrada", grabado.Estado);
        }

        [TestMethod]
        public void Normalizar_AceptaElEstadoCerrada()
        {
            var cambios = new ActualizarSugerenciaNovedadDTO { Estado = "cerrada" };

            Assert.IsNull(ReglasSugerenciasNovedades.Normalizar(cambios));
            Assert.AreEqual("Cerrada", cambios.Estado);
        }
    }
}
