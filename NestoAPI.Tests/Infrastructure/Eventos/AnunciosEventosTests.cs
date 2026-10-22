using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
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
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#692 (Nesto#545, NestoApp#247): anunciar un evento completo en las apps. Push a todos los vendedores de NestoApp
    /// y a los clientes de TNV (presenciales, solo zona de Madrid; online, todos), respetando «Notificaciones que recibe»,
    /// nunca fuera de 9:00-21:00, una sola vez salvo «otra vez» con reclamo.
    /// </summary>
    [TestClass]
    public class AnunciosEventosTests
    {
        private static readonly DateTime JUEVES_10 = new DateTime(2026, 10, 22, 10, 0, 0);

        private IServicioEventos eventos;
        private IRepositorioAnunciosEventos anuncios;
        private IServicioNotificacionesPush push;
        private IRepositorioPreferenciasNotificaciones preferencias;
        private DateTime ahora;
        private EventoDTO evento;
        private List<ClienteAppTiendas> conApp;
        private HashSet<string> deBaja;
        private List<(int Id, DateTime Cuando)> programados;
        private List<(string Cliente, NotificacionPushDTO Notificacion)> enviadasClientes;

        [TestInitialize]
        public void Preparar()
        {
            ahora = JUEVES_10;
            evento = Completo();
            eventos = A.Fake<IServicioEventos>();
            A.CallTo(() => eventos.LeerEvento(12)).ReturnsLazily(() => Task.FromResult(ReglasAnuncioEvento.Completar(evento, ahora.Date)));
            anuncios = A.Fake<IRepositorioAnunciosEventos>();
            A.CallTo(() => anuncios.Registrar(A<int>._, A<DateTime>._, A<string>._, A<string>._, A<DateTime?>._, A<bool>._))
                .Returns(Task.FromResult<int?>(77));
            conApp = new List<ClienteAppTiendas>
            {
                new ClienteAppTiendas { Cliente = "15191", CodigosPostales = new List<string> { "28110" } },   // Algete
                new ClienteAppTiendas { Cliente = "20000", CodigosPostales = new List<string> { "19200" } },   // Guadalajara
                new ClienteAppTiendas { Cliente = "30000", CodigosPostales = new List<string> { "08001" } },   // Barcelona
                new ClienteAppTiendas { Cliente = "40000", CodigosPostales = new List<string> { "28001" } }    // Madrid, de baja
            };
            A.CallTo(() => anuncios.ClientesConAppTiendas("1")).ReturnsLazily(() => Task.FromResult(conApp));
            deBaja = new HashSet<string> { "40000" };
            preferencias = A.Fake<IRepositorioPreferenciasNotificaciones>();
            A.CallTo(() => preferencias.ClientesDeBaja("1", TiposNotificacionCliente.EVENTOS_Y_CURSOS))
                .ReturnsLazily(() => Task.FromResult<ISet<string>>(deBaja));
            push = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => push.EnviarATodosDeAplicacion(Constantes.Aplicaciones.NESTO_APP, A<NotificacionPushDTO>._)).Returns(Task.FromResult(5));
            enviadasClientes = new List<(string, NotificacionPushDTO)>();
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string e, string c, NotificacionPushDTO n) => enviadasClientes.Add((c, n)))
                .Returns(Task.FromResult(2));
            programados = new List<(int, DateTime)>();
        }

        private ServicioAnunciosEventos Servicio() =>
            new ServicioAnunciosEventos(eventos, anuncios, push, preferencias, () => ahora, (id, cuando) => programados.Add((id, cuando)));

        private static EventoDTO Completo() => new EventoDTO
        {
            Id = 12,
            Empresa = "1",
            Titulo = "Masterclass Cloasma",
            Fecha = new DateTime(2026, 10, 24),
            HoraInicio = "10:00",
            HoraFin = "13:00",
            Almacen = "REI",
            Formato = FormatosEvento.PRESENCIAL,
            Ponente = "Nacho",
            DescripcionLarga = "Protocolo de cabina para el cloasma.",
            Plazas = 20,
            PlazasOcupadas = 5,
            ImporteSenal = 100,
            ImporteSenalNoReembolsable = 60,
            ProductoCurso = "EVENTO00012",
            ImagenUrl = "https://api.nuevavision.es/api/Eventos/12/Imagen?v=1",
            Activo = true
        };

        // ---------------- Completo / qué falta ----------------

        [TestMethod]
        public void QueFalta_Completo_NadaYCompletoTrue()
        {
            EventoDTO dto = ReglasAnuncioEvento.Completar(Completo(), JUEVES_10.Date);
            Assert.IsTrue(dto.Completo);
            Assert.AreEqual(0, dto.QueFalta.Count);
        }

        [TestMethod]
        public void QueFalta_Incompleto_DiceQueFalta()
        {
            EventoDTO dto = Completo();
            dto.ImagenUrl = null;
            dto.Ponente = " ";
            dto.HoraFin = null;
            dto.Plazas = null;
            dto.ProductoCurso = null;

            ReglasAnuncioEvento.Completar(dto, JUEVES_10.Date);

            Assert.IsFalse(dto.Completo);
            CollectionAssert.AreEquivalent(new[] { "la imagen", "el ponente", "la hora de fin", "las plazas", "el producto del curso" }, dto.QueFalta);
        }

        [TestMethod]
        public void QueFalta_GratuitoNoNecesitaProducto_OnlineNecesitaEnlace_PasadoNoSeAnuncia()
        {
            EventoDTO gratuito = Completo();
            gratuito.Gratuito = true;
            gratuito.ProductoCurso = null;
            Assert.IsTrue(ReglasAnuncioEvento.Completar(gratuito, JUEVES_10.Date).Completo);

            EventoDTO online = Completo();
            online.Formato = FormatosEvento.ONLINE;
            online.Lugar = "Sala de Zoom";
            CollectionAssert.AreEqual(new[] { "el enlace de conexión en «Lugar»" }, ReglasAnuncioEvento.QueFalta(online, JUEVES_10.Date));
            online.Lugar = "https://zoom.us/j/123";
            Assert.AreEqual(0, ReglasAnuncioEvento.QueFalta(online, JUEVES_10.Date).Count);

            EventoDTO pasado = Completo();
            pasado.Fecha = new DateTime(2026, 10, 21);
            pasado.Activo = false;
            CollectionAssert.AreEquivalent(new[] { "una fecha que no haya pasado", "que el evento esté activo" },
                ReglasAnuncioEvento.QueFalta(pasado, JUEVES_10.Date));
        }

        [TestMethod]
        public async Task ServicioEventos_LeerEvento_TraeCompletoYAnunciado()
        {
            NVEntities db = A.Fake<NVEntities>();
            var entidad = new Evento
            {
                Id = 12, Empresa = "1  ", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 10, 24), ImporteSenal = 100,
                ImporteSenalNoReembolsable = 60, Activo = true, Almacen = "REI", HoraInicio = new TimeSpan(10, 0, 0),
                HoraFin = new TimeSpan(13, 0, 0), Ponente = "Nacho", Descripcion = "Protocolo", Plazas = 20, ProductoCurso = "EVENTO00012",
                Usuario = "Reina", FechaModificacion = JUEVES_10
            };
            A.CallTo(() => db.Eventos).Returns(Crear(new List<Evento> { entidad }));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(new List<EventoInscripcion>()));
            IRepositorioImagenesEventos imagenes = A.Fake<IRepositorioImagenesEventos>();
            A.CallTo(() => imagenes.LeerFechas(A<IEnumerable<int>>._)).Returns(Task.FromResult(new Dictionary<int, DateTime> { [12] = JUEVES_10 }));
            A.CallTo(() => anuncios.LeerAnunciados(A<IEnumerable<int>>._)).Returns(Task.FromResult(new Dictionary<int, AnuncioEventoLeido>
            {
                [12] = new AnuncioEventoLeido { EventoId = 12, Fecha = JUEVES_10.AddHours(-1), Usuario = "Carlos " }
            }));
            var servicio = new ServicioEventos(db, () => JUEVES_10, (e, u) => null, A.Fake<IRepositorioFacturacionCursos>(),
                A.Fake<IRepositorioCupones>(), t => t(), imagenes, null, anuncios);

            EventoDTO dto = await servicio.LeerEvento(12);

            Assert.IsTrue(dto.Completo, string.Join(", ", dto.QueFalta));
            Assert.AreEqual(JUEVES_10.AddHours(-1), dto.AnunciadoFecha);
            Assert.AreEqual("Carlos", dto.AnunciadoUsuario);

            A.CallTo(() => imagenes.LeerFechas(A<IEnumerable<int>>._)).Returns(Task.FromResult(new Dictionary<int, DateTime>()));
            dto = await servicio.LeerEvento(12);
            Assert.IsFalse(dto.Completo, "Sin imagen no está completo");
            CollectionAssert.AreEqual(new[] { "la imagen" }, dto.QueFalta);
        }

        // ---------------- Reclamos ----------------

        [TestMethod]
        public void Reclamos_PocasPlazasYDosDias_LosPrimeros()
        {
            evento.PlazasOcupadas = 18;
            List<string> reclamos = ReglasAnuncioEvento.Reclamos(evento, JUEVES_10.Date);

            Assert.AreEqual(ReglasAnuncioEvento.RECLAMO_ULTIMAS_PLAZAS, reclamos[0]);
            CollectionAssert.Contains(reclamos, "¡Solo quedan dos plazas!");
            CollectionAssert.Contains(reclamos, "¡Solo faltan dos días!");
            CollectionAssert.Contains(reclamos, ReglasAnuncioEvento.RECLAMO_A_TIEMPO);
            Assert.IsTrue(reclamos.All(r => r.Length <= ReglasAnuncioEvento.LONGITUD_RECLAMO));
        }

        [TestMethod]
        public void Reclamos_MuchasPlazas_SinUltimasPlazas()
        {
            List<string> reclamos = ReglasAnuncioEvento.Reclamos(evento, JUEVES_10.Date);
            CollectionAssert.DoesNotContain(reclamos, ReglasAnuncioEvento.RECLAMO_ULTIMAS_PLAZAS);
            evento.Fecha = JUEVES_10.Date.AddDays(1);
            CollectionAssert.Contains(ReglasAnuncioEvento.Reclamos(evento, JUEVES_10.Date), "¡Es mañana! Aún está a tiempo");
        }

        [TestMethod]
        public async Task Reclamos_DelServicio_SegunElEvento()
        {
            evento.PlazasOcupadas = 19;
            List<string> reclamos = await Servicio().Reclamos(12);
            Assert.AreEqual(ReglasAnuncioEvento.RECLAMO_ULTIMAS_PLAZAS, reclamos[0]);
            CollectionAssert.Contains(reclamos, "¡Solo queda una plaza!");
        }

        // ---------------- Franja horaria ----------------

        [TestMethod]
        public void Franja_De9A21_FueraSeVaALas9()
        {
            Assert.IsTrue(ReglasAnuncioEvento.DentroDeHorario(new DateTime(2026, 10, 22, 9, 0, 0)));
            Assert.IsTrue(ReglasAnuncioEvento.DentroDeHorario(new DateTime(2026, 10, 22, 20, 59, 0)));
            Assert.IsFalse(ReglasAnuncioEvento.DentroDeHorario(new DateTime(2026, 10, 22, 21, 0, 0)));
            Assert.IsFalse(ReglasAnuncioEvento.DentroDeHorario(new DateTime(2026, 10, 22, 8, 59, 0)));
            Assert.AreEqual(new DateTime(2026, 10, 22, 9, 0, 0), ReglasAnuncioEvento.ProximaFranja(new DateTime(2026, 10, 22, 7, 30, 0)));
            Assert.AreEqual(new DateTime(2026, 10, 23, 9, 0, 0), ReglasAnuncioEvento.ProximaFranja(new DateTime(2026, 10, 22, 22, 15, 0)));
        }

        [TestMethod]
        public async Task Anunciar_FueraDeHorario_SeProgramaALas9YNoMandaNada()
        {
            ahora = new DateTime(2026, 10, 22, 22, 15, 0);

            ResultadoAnuncioEventoDTO resultado = await Servicio().Anunciar(12, false, null, "carlos");

            Assert.IsTrue(resultado.Encolado);
            Assert.AreEqual(new DateTime(2026, 10, 23, 9, 0, 0), resultado.ProgramadoPara);
            Assert.AreEqual(1, programados.Count);
            Assert.AreEqual((77, new DateTime(2026, 10, 23, 9, 0, 0)), programados[0]);
            A.CallTo(() => anuncios.Registrar(12, ahora, "carlos", null, new DateTime(2026, 10, 23, 9, 0, 0), false)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.EnviarATodosDeAplicacion(A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            StringAssert.Contains(resultado.Mensaje, "mañana a las 9:00");
        }

        [TestMethod]
        public async Task EnviarProgramado_ALas9_MandaYRegistra()
        {
            ahora = new DateTime(2026, 10, 23, 9, 0, 5);
            A.CallTo(() => anuncios.Leer(77)).Returns(Task.FromResult(new AnuncioEventoRegistrado
            {
                Id = 77, EventoId = 12, Fecha = new DateTime(2026, 10, 22, 22, 15, 0), Usuario = "carlos",
                ProgramadoPara = new DateTime(2026, 10, 23, 9, 0, 0)
            }));

            ResultadoAnuncioEventoDTO resultado = await Servicio().EnviarProgramado(77);

            Assert.AreEqual(5 + 4, resultado.Dispositivos);
            A.CallTo(() => anuncios.MarcarEnviado(77, ahora, 5, 4, 2)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarProgramado_YaEnviadoOFueraDeHorario_NoManda()
        {
            A.CallTo(() => anuncios.Leer(77)).Returns(Task.FromResult(new AnuncioEventoRegistrado { Id = 77, EventoId = 12, EnviadoFecha = JUEVES_10 }));
            Assert.IsNull(await Servicio().EnviarProgramado(77));

            A.CallTo(() => anuncios.Leer(77)).Returns(Task.FromResult(new AnuncioEventoRegistrado { Id = 77, EventoId = 12 }));
            ahora = new DateTime(2026, 10, 23, 23, 0, 0);
            Assert.IsNull(await Servicio().EnviarProgramado(77));
            Assert.AreEqual((77, new DateTime(2026, 10, 24, 9, 0, 0)), programados.Single(), "Hangfire tarde: a la próxima franja");
            A.CallTo(() => push.EnviarATodosDeAplicacion(A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        // ---------------- Anunciar ----------------

        [TestMethod]
        public async Task Anunciar_Completo_MandaANestoAppYALaZonaDeMadrid()
        {
            ResultadoAnuncioEventoDTO resultado = await Servicio().Anunciar(12, false, null, "carlos");

            Assert.IsFalse(resultado.Encolado);
            Assert.AreEqual(5, resultado.DispositivosNestoApp);
            Assert.AreEqual(4, resultado.DispositivosTiendas, "Algete y Guadalajara, 2 dispositivos cada uno");
            Assert.AreEqual(9, resultado.Dispositivos);
            CollectionAssert.AreEquivalent(new[] { "15191", "20000" }, enviadasClientes.Select(e => e.Cliente).ToList(),
                "Presencial: ni Barcelona ni el que lo ha apagado");
            A.CallTo(() => anuncios.MarcarEnviado(77, ahora, 5, 4, 2)).MustHaveHappenedOnceExactly();
            Assert.AreEqual("Anunciado en 9 dispositivos (5 de NestoApp y 4 de la tienda online).", resultado.Mensaje);

            NotificacionPushDTO tnv = enviadasClientes[0].Notificacion;
            Assert.AreEqual("Nuevo evento: «Masterclass Cloasma»", tnv.Titulo);
            Assert.AreEqual("El sábado 24 de octubre a las 10:00 en Reina. Puede reservar su plaza en la app.", tnv.Cuerpo);
            Assert.AreEqual(ReglasAnuncioEvento.TIPO_NOTIFICACION, tnv.Datos["tipo"]);
            Assert.AreEqual(evento.ImagenUrl, tnv.Datos["imagenUrl"]);
            A.CallTo(() => push.EnviarATodosDeAplicacion(Constantes.Aplicaciones.NESTO_APP,
                A<NotificacionPushDTO>.That.Matches(n => n.Datos["tipo"] == "AnuncioEvento" && n.Datos["eventoId"] == "12")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Anunciar_Online_ATodosMenosLosDeBaja()
        {
            evento.Formato = FormatosEvento.ONLINE;
            evento.Lugar = "https://zoom.us/j/123";

            _ = await Servicio().Anunciar(12, false, null, "carlos");

            CollectionAssert.AreEquivalent(new[] { "15191", "20000", "30000" }, enviadasClientes.Select(e => e.Cliente).ToList());
        }

        [TestMethod]
        public void Destinatarios_ZonaYBajas()
        {
            Assert.IsTrue(ReglasAnuncioEvento.EnZonaMadrid("28110"));
            Assert.IsTrue(ReglasAnuncioEvento.EnZonaMadrid("45001"));
            Assert.IsTrue(ReglasAnuncioEvento.EnZonaMadrid("19001"));
            Assert.IsFalse(ReglasAnuncioEvento.EnZonaMadrid("08001"));
            Assert.IsFalse(ReglasAnuncioEvento.EnZonaMadrid("28"));
            Assert.IsFalse(ReglasAnuncioEvento.EnZonaMadrid(null));
            List<string> sinBajas = ReglasAnuncioEvento.Destinatarios(conApp, new HashSet<string>(), online: false);
            CollectionAssert.AreEquivalent(new[] { "15191", "20000", "40000" }, sinBajas);
        }

        [TestMethod]
        public async Task Anunciar_Incompleto_400ConQueFalta()
        {
            evento.ImagenUrl = null;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anunciar(12, false, null, "carlos"));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.AreEqual(ReglasAnuncioEvento.ERROR_INCOMPLETO, ex.GetErrorCode());
            CollectionAssert.AreEqual(new[] { "la imagen" }, (List<string>)ex.Context.AdditionalData["queFalta"]);
            A.CallTo(() => anuncios.Registrar(A<int>._, A<DateTime>._, A<string>._, A<string>._, A<DateTime?>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anunciar_YaAnunciado_409()
        {
            evento.AnunciadoFecha = new DateTime(2026, 10, 21, 10, 15, 0);
            evento.AnunciadoUsuario = "Reina";

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anunciar(12, false, null, "carlos"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual(ReglasAnuncioEvento.ERROR_YA_ANUNCIADO, ex.GetErrorCode());
            StringAssert.Contains(ex.Message, "21/10/2026 a las 10:15 por Reina");
            A.CallTo(() => push.EnviarATodosDeAplicacion(A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anunciar_OtroLoAcabaDeAnunciar_409()
        {
            A.CallTo(() => anuncios.Registrar(A<int>._, A<DateTime>._, A<string>._, A<string>._, A<DateTime?>._, false))
                .Returns(Task.FromResult<int?>(null));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anunciar(12, false, null, "carlos"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            A.CallTo(() => push.EnviarATodosDeAplicacion(A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anunciar_OtraVezConReclamo_ElReclamoEsElTitulo()
        {
            evento.AnunciadoFecha = new DateTime(2026, 10, 21, 10, 15, 0);

            ResultadoAnuncioEventoDTO resultado = await Servicio().Anunciar(12, true, " ¡Últimas plazas disponibles! ", "carlos");

            Assert.AreEqual("¡Últimas plazas disponibles!", resultado.Reclamo);
            Assert.AreEqual("¡Últimas plazas disponibles!", enviadasClientes[0].Notificacion.Titulo);
            StringAssert.StartsWith(enviadasClientes[0].Notificacion.Cuerpo, "«Masterclass Cloasma», el sábado 24 de octubre");
            A.CallTo(() => anuncios.Registrar(12, ahora, "carlos", "¡Últimas plazas disponibles!", null, true)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Anunciar_OtraVezSinReclamoOReclamoLargo_400()
        {
            evento.AnunciadoFecha = new DateTime(2026, 10, 21, 10, 15, 0);
            NestoBusinessException sinReclamo = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anunciar(12, true, " ", "carlos"));
            Assert.AreEqual(HttpStatusCode.BadRequest, sinReclamo.StatusCode);

            NestoBusinessException largo = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => Servicio().Anunciar(12, true, new string('x', 61), "carlos"));
            StringAssert.Contains(largo.Message, "60");
            A.CallTo(() => anuncios.Registrar(A<int>._, A<DateTime>._, A<string>._, A<string>._, A<DateTime?>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anunciar_NoExiste_404()
        {
            A.CallTo(() => eventos.LeerEvento(99)).Returns(Task.FromResult<EventoDTO>(null));
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anunciar(99, false, null, "carlos"));
            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        // ---------------- Controlador: permisos ----------------

        [TestMethod]
        public async Task Controlador_SinPermisoDeMantener_403YNoAnuncia()
        {
            IServicioEventos servicio = A.Fake<IServicioEventos>();
            A.CallTo(() => servicio.AlmacenDelEvento(12)).Returns(Task.FromResult("REI"));
            A.CallTo(() => servicio.AlmacenDelUsuario(A<System.Security.Principal.IPrincipal>._, A<string>._)).Returns("ALC");
            IServicioAnunciosEventos anunciador = A.Fake<IServicioAnunciosEventos>();
            var controlador = new EventosController(A.Fake<NVEntities>(), servicio, A.Fake<IServicioInscripcionesEventos>(), A.Fake<IServicioPropuestasSenales>())
            {
                Request = new HttpRequestMessage(),
                Configuration = new System.Web.Http.HttpConfiguration(),
                User = Usuario("Paloma", Constantes.GruposSeguridad.TIENDAS),
                Anuncios = anunciador
            };

            var resultado = await controlador.PostAnunciar(12, new PeticionAnuncioEventoDTO()) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => anunciador.Anunciar(A<int>._, A<bool>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_LaTiendaDelEvento_AnunciaConOtraVezYReclamo()
        {
            IServicioEventos servicio = A.Fake<IServicioEventos>();
            A.CallTo(() => servicio.AlmacenDelEvento(12)).Returns(Task.FromResult("REI"));
            A.CallTo(() => servicio.AlmacenDelUsuario(A<System.Security.Principal.IPrincipal>._, A<string>._)).Returns("REI");
            IServicioAnunciosEventos anunciador = A.Fake<IServicioAnunciosEventos>();
            var controlador = new EventosController(A.Fake<NVEntities>(), servicio, A.Fake<IServicioInscripcionesEventos>(), A.Fake<IServicioPropuestasSenales>())
            {
                Request = new HttpRequestMessage(),
                User = Usuario("Reina", Constantes.GruposSeguridad.TIENDAS),
                Anuncios = anunciador
            };

            var resultado = await controlador.PostAnunciar(12, new PeticionAnuncioEventoDTO { Reclamo = "¡Aún está a tiempo!" }, otraVez: true);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoAnuncioEventoDTO>));
            A.CallTo(() => anunciador.Anunciar(12, true, "¡Aún está a tiempo!", A<string>._)).MustHaveHappenedOnceExactly();
        }

        private static ClaimsPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
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
