using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#875 (Novedades 1193 y 1251, Carlos 17/11/26): el enlace de la conexión online en su propio campo, quién lo ve, el
    /// recordatorio con ese enlace, la inscripción tardía y el correo «nuevo enlace».
    /// </summary>
    [TestClass]
    public class EnlaceOnlineEventosTests
    {
        // Miércoles 18/11/26 a las 11:00 (hora de Madrid)
        private static readonly DateTime AHORA = new DateTime(2026, 11, 18, 11, 0, 0);
        private const string TEAMS = "https://teams.microsoft.com/l/meetup-join/abc";
        private const string ZOOM_EN_LUGAR = "https://zoom.us/j/123";

        private static Evento Online(DateTime fecha, string lugar = null, string formato = FormatosEvento.ONLINE) => new Evento
        {
            Id = 7, Empresa = "1", Titulo = "Webinar Peeling", Fecha = fecha, Activo = true, Almacen = "REI", Formato = formato,
            HoraInicio = new TimeSpan(17, 0, 0), HoraFin = new TimeSpan(18, 30, 0), Lugar = lugar
        };

        private static EventoInscripcion Inscripcion(int id = 1, EstadoInscripcionEvento estado = EstadoInscripcionEvento.Pagada,
            DateTime? recordatorio = null, string respuesta = null) => new EventoInscripcion
            {
                Id = id, EventoId = 7, Empresa = "1", Cliente = "15191", Contacto = "0", Estado = estado.ToString(),
                RecordatorioEnviado = recordatorio, AsistenciaRespuesta = respuesta
            };

        // ---------------- El enlace ----------------

        [TestMethod]
        public void Normalizar_VacioLoQuita_SinHttpOLargo400DeUsted()
        {
            Assert.IsNull(ReglasEnlaceOnlineEvento.Normalizar("   "));
            Assert.AreEqual(TEAMS, ReglasEnlaceOnlineEvento.Normalizar("  " + TEAMS + " "));
            var sinHttp = Assert.ThrowsException<NestoBusinessException>(() => ReglasEnlaceOnlineEvento.Normalizar("teams.microsoft.com/l/x"));
            StringAssert.Contains(sinHttp.Message, "dirección web completa");
            _ = Assert.ThrowsException<NestoBusinessException>(() => ReglasEnlaceOnlineEvento.Normalizar("https://zoom.us/j/1 y la sala"));
            _ = Assert.ThrowsException<NestoBusinessException>(() => ReglasEnlaceOnlineEvento.Normalizar("https://x.es/" + new string('a', 500)));
        }

        [TestMethod]
        public void DelEvento_ElPropioManda_SinElDeLugar()
        {
            Assert.AreEqual(TEAMS, ReglasEnlaceOnlineEvento.DelEvento(TEAMS, "Sala " + ZOOM_EN_LUGAR));
            Assert.AreEqual(ZOOM_EN_LUGAR, ReglasEnlaceOnlineEvento.DelEvento(null, "Sala " + ZOOM_EN_LUGAR));
            Assert.AreEqual(ZOOM_EN_LUGAR, ReglasEnlaceOnlineEvento.DelEvento("  ", ZOOM_EN_LUGAR));
            Assert.IsNull(ReglasEnlaceOnlineEvento.DelEvento(null, "Tienda de Reina"));
        }

        [TestMethod]
        public void ParaInscripcion_OnlineTodas_MixtoSoloLasOnline_PresencialNinguna()
        {
            Assert.AreEqual(TEAMS, ReglasEnlaceOnlineEvento.ParaInscripcion(Online(AHORA.Date), TEAMS, null));
            Evento mixto = Online(AHORA.Date, "C/ Mayor 1", FormatosEvento.MIXTO);
            Assert.AreEqual(TEAMS, ReglasEnlaceOnlineEvento.ParaInscripcion(mixto, TEAMS, "Online"));
            Assert.IsNull(ReglasEnlaceOnlineEvento.ParaInscripcion(mixto, TEAMS, "Presencial"));
            Assert.IsNull(ReglasEnlaceOnlineEvento.ParaInscripcion(mixto, TEAMS, null));
            Assert.IsNull(ReglasEnlaceOnlineEvento.ParaInscripcion(Online(AHORA.Date, formato: FormatosEvento.PRESENCIAL), TEAMS, null));
        }

        // ---------------- Inscripción tardía (decisión 4) ----------------

        [TestMethod]
        public void AlInscribirse_RecordatorioYaSalido_SeLeMandaYa()
        {
            Assert.AreEqual(AccionEnlaceOnline.Recordatorio,
                ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(2)), Inscripcion(), null, TEAMS, AHORA), "El de dentro de 2 días ya tuvo su recordatorio");
            Assert.AreEqual(AccionEnlaceOnline.Recordatorio,
                ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date), Inscripcion(), null, TEAMS, AHORA), "Hoy, antes de que empiece");
            Assert.AreEqual(AccionEnlaceOnline.Recordatorio,
                ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(1), ZOOM_EN_LUGAR), Inscripcion(), null, null, AHORA), "Sin enlace propio, el de Lugar");
        }

        [TestMethod]
        public void AlInscribirse_SinEnlace_CampanaAQuienMantieneElEvento()
        {
            Assert.AreEqual(AccionEnlaceOnline.FaltaEnlace,
                ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(1), "Online"), Inscripcion(), null, null, AHORA));
        }

        [TestMethod]
        public void AlInscribirse_LoQueNoToca()
        {
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(3)), Inscripcion(), null, TEAMS, AHORA),
                "Más adelante: le llegará el recordatorio de siempre");
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(1)), Inscripcion(recordatorio: AHORA.AddDays(-1)), null, TEAMS, AHORA),
                "Ya lo tiene");
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(1)), Inscripcion(estado: EstadoInscripcionEvento.Reservada), null, TEAMS, AHORA),
                "Una reserva sin pagar no recibe el recordatorio (le llegará al pagarla)");
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(1)), Inscripcion(respuesta: "NoPodraIr"), null, TEAMS, AHORA),
                "Dijo que no podrá ir");
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(1), formato: FormatosEvento.MIXTO), Inscripcion(), "Presencial", TEAMS, AHORA),
                "En un mixto, la presencial no lleva enlace");
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(-1)), Inscripcion(), null, TEAMS, AHORA),
                "Evento pasado");
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date), Inscripcion(), null, TEAMS, AHORA.Date.AddHours(19)),
                "Hoy, ya terminado");
            Evento inactivo = Online(AHORA.Date.AddDays(1));
            inactivo.Activo = false;
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlInscribirse(inactivo, Inscripcion(), null, TEAMS, AHORA));
        }

        [TestMethod]
        public void AlInscribirse_MixtoQuePasaAOnline_SeLeManda()
        {
            Assert.AreEqual(AccionEnlaceOnline.Recordatorio,
                ReglasEnlaceOnlineEvento.AlInscribirse(Online(AHORA.Date.AddDays(1), formato: FormatosEvento.MIXTO), Inscripcion(), "Online", TEAMS, AHORA));
        }

        // ---------------- Enlace cambiado (decisión 5) ----------------

        [TestMethod]
        public void AlCambiarEnlace_YaAvisadas_NuevoEnlace_Esperando_Recordatorio_Lejos_Nada()
        {
            Evento manana = Online(AHORA.Date.AddDays(1));
            Assert.AreEqual(AccionEnlaceOnline.NuevoEnlace, ReglasEnlaceOnlineEvento.AlCambiarEnlace(manana, Inscripcion(recordatorio: AHORA.AddDays(-1)), null, TEAMS, AHORA));
            Assert.AreEqual(AccionEnlaceOnline.Recordatorio, ReglasEnlaceOnlineEvento.AlCambiarEnlace(manana, Inscripcion(), null, TEAMS, AHORA));
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlCambiarEnlace(Online(AHORA.Date.AddDays(10)), Inscripcion(), null, TEAMS, AHORA),
                "Aún no le toca: el recordatorio de siempre ya lleva el enlace nuevo");
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlCambiarEnlace(manana, Inscripcion(recordatorio: AHORA, estado: EstadoInscripcionEvento.Anulada), null, TEAMS, AHORA));
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlCambiarEnlace(Online(AHORA.Date.AddDays(-2)), Inscripcion(recordatorio: AHORA.AddDays(-4)), null, TEAMS, AHORA));
            Assert.AreEqual(AccionEnlaceOnline.Nada, ReglasEnlaceOnlineEvento.AlCambiarEnlace(manana, Inscripcion(recordatorio: AHORA), null, null, AHORA), "Quitado: nada");
        }

        [TestMethod]
        public void HaCambiado()
        {
            Assert.IsTrue(ReglasEnlaceOnlineEvento.HaCambiado(null, TEAMS));
            Assert.IsTrue(ReglasEnlaceOnlineEvento.HaCambiado(ZOOM_EN_LUGAR, TEAMS));
            Assert.IsFalse(ReglasEnlaceOnlineEvento.HaCambiado(TEAMS, " " + TEAMS));
        }

        [TestMethod]
        public void CorreoNuevoEnlace_DeUstedConElEnlace()
        {
            using (MailMessage correo = TextosEnlaceOnlineEvento.CorreoNuevoEnlace(Online(AHORA.Date.AddDays(1)), TEAMS, "Luna", "luna@example.com", habiaEnlace: true))
            {
                Assert.AreEqual("Nuevo enlace para conectarse a «Webinar Peeling»", correo.Subject);
                Assert.AreEqual("nuevavision@nuevavision.es", correo.From.Address);
                StringAssert.Contains(correo.Body, TEAMS);
                StringAssert.Contains(correo.Body, "Use este, por favor");
                StringAssert.Contains(correo.Body, "El enlace anterior ya no vale.");
            }
            using (MailMessage primero = TextosEnlaceOnlineEvento.CorreoNuevoEnlace(Online(AHORA.Date.AddDays(1)), TEAMS, null, "luna@example.com", habiaEnlace: false))
            {
                Assert.AreEqual("Enlace para conectarse a «Webinar Peeling»", primero.Subject);
                Assert.IsFalse(primero.Body.Contains("ya no vale"));
            }
        }

        [TestMethod]
        public void NotificacionFaltaEnlace_DeUstedDiceDondePonerlo()
        {
            NotificacionPushDTO n = TextosEnlaceOnlineEvento.NotificacionFaltaEnlace(Online(AHORA.Date.AddDays(1)), 3);
            Assert.AreEqual(TextosEnlaceOnlineEvento.TIPO_FALTA_ENLACE, n.Tipo);
            StringAssert.Contains(n.Cuerpo, "hay 3 inscripciones online esperando");
            StringAssert.Contains(n.Cuerpo, "Póngalo en «Enlace de la conexión online»");
            Assert.AreEqual("7", n.Datos["eventoId"]);
        }

        [TestMethod]
        public void DestinatariosEvento_LosDelGrupoConEsaTienda()
        {
            CollectionAssert.AreEqual(new[] { "Reina" },
                DestinatariosEvento.Cruzar(new[] { "Reina", "Paloma", "Pilar" }, new[] { "Reina", "NUEVAVISION\\otra" }));
        }

        // ---------------- Recordatorio con el enlace propio (decisión 3) ----------------

        [TestMethod]
        public void Recordatorio_ElEnlacePropioMandaSobreElDeLugar()
        {
            Evento evento = Online(AHORA.Date.AddDays(2), ZOOM_EN_LUGAR);
            EventoInscripcion inscripcion = Inscripcion();
            inscripcion.ClaveAsistencia = Guid.NewGuid();
            using (MailMessage correo = RecordatorioEventosJobsService.Correo(evento, inscripcion, "Luna", "luna@example.com", null, TEAMS))
            {
                StringAssert.Contains(correo.Body, TEAMS);
                Assert.IsFalse(correo.Body.Contains(ZOOM_EN_LUGAR));
            }
            Assert.AreEqual(TEAMS, RecordatorioEventosJobsService.Notificacion(evento, inscripcion, null, TEAMS).Datos["enlace"]);
            using (MailMessage comoAntes = RecordatorioEventosJobsService.Correo(evento, inscripcion, "Luna", "luna@example.com"))
            {
                StringAssert.Contains(comoAntes.Body, ZOOM_EN_LUGAR, "Sin enlace propio, el de Lugar, como antes");
            }
        }

        [TestMethod]
        public async Task Recordatorio_OnlineSinEnlace_SaleIgualYCampanaAQuienLoMantiene()
        {
            Evento evento = Online(AHORA.Date.AddDays(2), "Sala virtual");
            EventoInscripcion inscripcion = Inscripcion();
            inscripcion.ClaveAsistencia = Guid.NewGuid();
            var campanas = new List<NotificacionPushDTO>();
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            var deps = new DependenciasRecordatorioEventos
            {
                Encendido = () => true,
                Ahora = () => AHORA,
                Pendientes = d => Task.FromResult(new List<KeyValuePair<Evento, List<EventoInscripcion>>>
                {
                    new KeyValuePair<Evento, List<EventoInscripcion>>(evento, new List<EventoInscripcion> { inscripcion })
                }),
                MarcarEnviados = l => Task.CompletedTask,
                CorreoCliente = (e, c, k) => Task.FromResult("luna@example.com"),
                NombreCliente = (e, c, k) => Task.FromResult("Luna"),
                Push = A.Fake<IServicioNotificacionesPush>(),
                Correo = correo,
                EnlacesOnline = ids => Task.FromResult(new Dictionary<int, string>()),
                AvisarFaltaEnlace = (e, n) => { campanas.Add(n); return Task.CompletedTask; }
            };

            ResultadoRecordatorioEventos r = await RecordatorioEventosJobsService.Procesar(deps);

            Assert.AreEqual(1, r.Correos);
            Assert.AreEqual(1, campanas.Count);
            StringAssert.Contains(campanas[0].Cuerpo, "hay una inscripción online esperando");
        }

        // ---------------- Núcleo de los envíos ----------------

        private sealed class Envios
        {
            public readonly List<MailMessage> Correos = new List<MailMessage>();
            public readonly List<EventoInscripcion> Marcadas = new List<EventoInscripcion>();
            public readonly List<NotificacionPushDTO> Campanas = new List<NotificacionPushDTO>();
            public bool Encendido = true;

            public DependenciasEnlaceOnlineEventos Deps(List<KeyValuePair<EventoInscripcion, string>> inscripciones)
            {
                IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
                A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) => { Correos.Add(m); return true; });
                return new DependenciasEnlaceOnlineEventos
                {
                    Ahora = () => AHORA,
                    RecordatorioEncendido = () => Encendido,
                    Recordatorio = new DependenciasRecordatorioEventos
                    {
                        CorreoCliente = (e, c, k) => Task.FromResult(c + "@example.com"),
                        NombreCliente = (e, c, k) => Task.FromResult("Cliente " + c),
                        Push = A.Fake<IServicioNotificacionesPush>(),
                        Correo = correo
                    },
                    Inscripciones = id => Task.FromResult(inscripciones),
                    PonerClaves = l => { foreach (EventoInscripcion i in l.Where(x => x.ClaveAsistencia == null)) { i.ClaveAsistencia = Guid.NewGuid(); } return Task.CompletedTask; },
                    MarcarEnviados = l => { Marcadas.AddRange(l); return Task.CompletedTask; },
                    AvisarTienda = (e, n) => { Campanas.Add(n); return Task.CompletedTask; }
                };
            }
        }

        [TestMethod]
        public async Task EnlaceCambiado_NuevoEnlaceALasAvisadasYRecordatorioALasQueEsperaban()
        {
            EventoInscripcion avisada = Inscripcion(1, recordatorio: AHORA.AddDays(-1));
            avisada.Cliente = "avisada";
            EventoInscripcion esperando = Inscripcion(2);
            esperando.Cliente = "esperando";
            EventoInscripcion presencial = Inscripcion(3, recordatorio: AHORA.AddDays(-1));
            presencial.Cliente = "presencial";
            var envios = new Envios();

            ResultadoEnlaceOnlineEventos r = await EnlaceOnlineEventosJobs.EnlaceCambiado(envios.Deps(new List<KeyValuePair<EventoInscripcion, string>>
            {
                new KeyValuePair<EventoInscripcion, string>(avisada, "Online"),
                new KeyValuePair<EventoInscripcion, string>(esperando, "Online"),
                new KeyValuePair<EventoInscripcion, string>(presencial, "Presencial")
            }), Online(AHORA.Date.AddDays(1), "C/ Mayor 1", FormatosEvento.MIXTO), TEAMS, habiaEnlace: true);

            Assert.AreEqual(1, r.NuevosEnlaces);
            Assert.AreEqual(1, r.Recordatorios);
            Assert.AreEqual("Nuevo enlace para conectarse a «Webinar Peeling»", envios.Correos.Single(c => c.To[0].Address == "avisada@example.com").Subject);
            StringAssert.Contains(envios.Correos.Single(c => c.To[0].Address == "esperando@example.com").Body, TEAMS);
            Assert.IsFalse(envios.Correos.Any(c => c.To[0].Address == "presencial@example.com"));
            CollectionAssert.AreEqual(new[] { 2 }, envios.Marcadas.Select(i => i.Id).ToArray(), "Solo se marca el recordatorio, no el correo corto");
        }

        [TestMethod]
        public async Task AlInscribirse_ConEnlace_RecordatorioYSeMarca_SinEnlace_Campana_Apagado_Nada()
        {
            var envios = new Envios();
            EventoInscripcion nueva = Inscripcion();
            Assert.AreEqual(AccionEnlaceOnline.Recordatorio,
                await EnlaceOnlineEventosJobs.AlInscribirse(envios.Deps(null), Online(AHORA.Date.AddDays(1)), nueva, null, TEAMS));
            Assert.AreEqual(1, envios.Correos.Count);
            StringAssert.Contains(envios.Correos[0].Body, TEAMS);
            Assert.IsTrue(nueva.ClaveAsistencia.HasValue, "Lleva los botones de confirmar");
            CollectionAssert.AreEqual(new[] { 1 }, envios.Marcadas.Select(i => i.Id).ToArray());

            var sinEnlace = new Envios();
            Assert.AreEqual(AccionEnlaceOnline.FaltaEnlace,
                await EnlaceOnlineEventosJobs.AlInscribirse(sinEnlace.Deps(null), Online(AHORA.Date.AddDays(1), "Online"), Inscripcion(), null, null));
            Assert.AreEqual(0, sinEnlace.Correos.Count);
            Assert.AreEqual(1, sinEnlace.Campanas.Count);

            var apagado = new Envios { Encendido = false };
            Assert.AreEqual(AccionEnlaceOnline.Nada,
                await EnlaceOnlineEventosJobs.AlInscribirse(apagado.Deps(null), Online(AHORA.Date.AddDays(1)), Inscripcion(), null, TEAMS));
            Assert.AreEqual(0, apagado.Correos.Count);
        }

        // ---------------- Quién lo ve (decisión 2) ----------------

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        [TestMethod]
        public void PuedeVerEnlaceOnline_TiendasAdministracionDireccionEInformatica()
        {
            Assert.IsTrue(PermisosEventos.PuedeVerEnlaceOnline(Usuario("NUEVAVISION\\Reina", "Tiendas")));
            Assert.IsTrue(PermisosEventos.PuedeVerEnlaceOnline(Usuario("NUEVAVISION\\Pilar", "Administración")));
            Assert.IsTrue(PermisosEventos.PuedeVerEnlaceOnline(Usuario("NUEVAVISION\\Carlos", "Dirección")));
            Assert.IsTrue(PermisosEventos.PuedeVerEnlaceOnline(Usuario("NUEVAVISION\\Informatica", "Informática")));
            Assert.IsFalse(PermisosEventos.PuedeVerEnlaceOnline(Usuario("NUEVAVISION\\Laura", "TiendaOnline")));
            Assert.IsFalse(PermisosEventos.PuedeVerEnlaceOnline(Usuario("NUEVAVISION\\Jesus", "Ventas")));
            Assert.IsFalse(PermisosEventos.PuedeVerEnlaceOnline(new ClaimsPrincipal(new ClaimsIdentity(new[]
                { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", "15191"), new Claim(ClaimTypes.Role, "NUEVAVISION\\Tiendas") }, "prueba"))),
                "Nunca el cliente de la tienda online");
            Assert.IsFalse(PermisosEventos.PuedeVerEnlaceOnline(null));
        }

        private NVEntities db;
        private List<Evento> eventos;
        private IRepositorioEnlaceOnlineEventos repositorio;
        private Dictionary<int, string> guardados;
        private List<KeyValuePair<int, bool>> avisos;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            eventos = new List<Evento>
            {
                new Evento { Id = 7, Empresa = "1", Titulo = "Webinar Peeling", Fecha = AHORA.Date.AddDays(1), ImporteSenal = 0, ImporteSenalNoReembolsable = 0,
                    Activo = true, Almacen = "REI", Formato = FormatosEvento.ONLINE, Lugar = ZOOM_EN_LUGAR, Usuario = "Reina", FechaModificacion = AHORA }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(new List<EventoInscripcion>()));
            A.CallTo(() => db.Productos).Returns(Crear(new List<Producto>()));
            guardados = new Dictionary<int, string> { [7] = TEAMS };
            repositorio = A.Fake<IRepositorioEnlaceOnlineEventos>();
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>._)).ReturnsLazily(() => Task.FromResult(new Dictionary<int, string>(guardados)));
            A.CallTo(() => repositorio.Guardar(A<int>._, A<string>._)).ReturnsLazily((int id, string enlace) =>
            {
                if (enlace == null) { _ = guardados.Remove(id); } else { guardados[id] = enlace; }
                return Task.FromResult(true);
            });
            avisos = new List<KeyValuePair<int, bool>>();
        }

        private ServicioEventos Servicio() => new ServicioEventos(db, () => AHORA, (e, u) => u == "Reina" ? "REI" : null,
            A.Fake<IRepositorioFacturacionCursos>(), A.Fake<IRepositorioCupones>(), t => t())
        {
            EnlacesOnline = repositorio,
            AlCambiarEnlaceOnline = (id, habia) => avisos.Add(new KeyValuePair<int, bool>(id, habia))
        };

        private EventosController Controlador(IPrincipal usuario) =>
            new EventosController(db, Servicio()) { Request = new HttpRequestMessage(), User = usuario };

        [TestMethod]
        public async Task GetEventos_SoloQuienPuedeVeElEnlace()
        {
            var tienda = await Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas")).GetEventos() as OkNegotiatedContentResult<List<EventoDTO>>;
            var vendedor = await Controlador(Usuario("NUEVAVISION\\Jesus", "Ventas")).GetEventos() as OkNegotiatedContentResult<List<EventoDTO>>;
            var cliente = await Controlador(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", "15191") }, "prueba")))
                .GetEventos() as OkNegotiatedContentResult<List<EventoDTO>>;

            Assert.AreEqual(TEAMS, tienda.Content.Single().EnlaceOnline);
            Assert.IsNull(vendedor.Content.Single().EnlaceOnline);
            Assert.IsNull(cliente.Content.Single().EnlaceOnline);
            StringAssert.Contains(Newtonsoft.Json.JsonConvert.SerializeObject(cliente.Content), "\"EnlaceOnline\":null", "Aditivo: el campo va, vacío");
        }

        [TestMethod]
        public async Task PutEvento_CambiaElEnlace_AvisaQueHabiaUno()
        {
            var resultado = await Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas")).PutEvento(7, new EventoDTO
            { HoraInicio = "07:00",
                Titulo = "Webinar Peeling", Fecha = AHORA.Date.AddDays(1), ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Almacen = "REI",
                Formato = FormatosEvento.ONLINE, EnlaceOnline = "https://meet.google.com/xyz"
            }) as OkNegotiatedContentResult<EventoDTO>;

            Assert.AreEqual("https://meet.google.com/xyz", guardados[7]);
            Assert.AreEqual("https://meet.google.com/xyz", resultado.Content.EnlaceOnline);
            CollectionAssert.AreEqual(new[] { new KeyValuePair<int, bool>(7, true) }, avisos);
        }

        [TestMethod]
        public async Task PutEvento_SinElCampo_NoLoTocaNiAvisa()
        {
            _ = await Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas")).PutEvento(7, new EventoDTO
            { HoraInicio = "07:00",
                Titulo = "Webinar Peeling (2.ª)", Fecha = AHORA.Date.AddDays(1), ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Almacen = "REI"
            });

            Assert.AreEqual(TEAMS, guardados[7]);
            Assert.AreEqual(0, avisos.Count);
            A.CallTo(() => repositorio.Guardar(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutEvento_PrimerEnlace_AvisaQueNoHabia()
        {
            guardados.Clear();
            eventos[0].Lugar = "Sala virtual";

            _ = await Controlador(Usuario("NUEVAVISION\\Carlos", "Dirección")).PutEvento(7, new EventoDTO
            { HoraInicio = "07:00",
                Titulo = "Webinar Peeling", Fecha = AHORA.Date.AddDays(1), ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Almacen = "REI",
                EnlaceOnline = TEAMS
            });

            CollectionAssert.AreEqual(new[] { new KeyValuePair<int, bool>(7, false) }, avisos);
        }

        [TestMethod]
        public async Task PutEvento_EnlaceNoValido_400SinGuardar()
        {
            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas")).PutEvento(7, new EventoDTO
            { HoraInicio = "07:00",
                Titulo = "Webinar Peeling", Fecha = AHORA.Date.AddDays(1), ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Almacen = "REI",
                EnlaceOnline = "pregunte en tienda"
            }));
            Assert.AreEqual(TEAMS, guardados[7]);
        }

        [TestMethod]
        public async Task SinElScript_PonerloDiceQueFaltaElScript()
        {
            A.CallTo(() => repositorio.Guardar(A<int>._, A<string>._)).Returns(Task.FromResult(false));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas")).PutEvento(7, new EventoDTO
            { HoraInicio = "07:00",
                Titulo = "Webinar Peeling", Fecha = AHORA.Date.AddDays(1), ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Almacen = "REI",
                EnlaceOnline = "https://meet.google.com/xyz"
            }));
            Assert.AreEqual(ServicioEventos.MENSAJE_FALTA_SCRIPT_ENLACE, ex.Message);
        }

        [TestMethod]
        public void OcultarEnlaceOnline_QuienNoPuedeNoLoCambia()
        {
            var dto = new EventoDTO { HoraInicio = "07:00", EnlaceOnline = TEAMS };
            Assert.IsTrue(dto.Trae(nameof(EventoDTO.EnlaceOnline)));
            dto.OcultarEnlaceOnline();
            Assert.IsFalse(dto.Trae(nameof(EventoDTO.EnlaceOnline)));
            Assert.IsNull(dto.EnlaceOnline);
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.ToList().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.ToList().GetEnumerator());
            return fake;
        }
    }
}
