using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>NestoAPI#591: eventos, marcar/quitar señales, la lista por estado, los permisos y el correo del día del evento.</summary>
    [TestClass]
    public class ServicioEventosTests
    {
        // Miércoles
        private static readonly DateTime HOY = new DateTime(2026, 10, 14, 9, 0, 0);

        private NVEntities db;
        private DbSet<Evento> fakeEventos;
        private DbSet<EventoSenal> fakeSenales;
        private DbSet<ExtractoCliente> fakeExtracto;
        private DbSet<Cliente> fakeClientes;
        private List<Evento> eventos;
        private List<EventoSenal> senales;
        private List<ExtractoCliente> extracto;
        private List<Cliente> clientes;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            eventos = new List<Evento>();
            senales = new List<EventoSenal>();
            extracto = new List<ExtractoCliente>();
            clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0", Nombre = "CENTRO DE ESTÉTICA LUNA " },
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "1", Nombre = "LUNA (ALCALÁ)" },
                new Cliente { Empresa = "1", Nº_Cliente = "20000", Contacto = "0", Nombre = "OTRA" }
            };
            fakeEventos = Crear(eventos);
            fakeSenales = Crear(senales);
            fakeExtracto = Crear(extracto);
            fakeClientes = Crear(clientes);
            A.CallTo(() => db.Eventos).Returns(fakeEventos);
            A.CallTo(() => db.EventosSenales).Returns(fakeSenales);
            A.CallTo(() => db.ExtractosCliente).Returns(fakeExtracto);
            A.CallTo(() => db.Clientes).Returns(fakeClientes);
            inscripciones = new List<EventoInscripcion>();
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(inscripciones));
            rectificaciones = new List<LinFacturaVtaRectificacion>();
            A.CallTo(() => db.LinFacturaVtaRectificaciones).Returns(Crear(rectificaciones));
            A.CallTo(() => db.Productos).Returns(Crear(new List<Producto> { new Producto { Empresa = "1", Número = "CURSO01", Grupo = "CUR" } }));
        }

        private List<EventoInscripcion> inscripciones;
        private List<LinFacturaVtaRectificacion> rectificaciones;

        /// <summary>NestoAPI#611: Reina mantiene los eventos de REI y Paloma los de ALC (su AlmacénPedidoVta).</summary>
        private static string AlmacenDe(string empresa, string usuario) =>
            usuario == "Reina" ? "REI" : usuario == "Paloma" ? "ALC" : null;

        private readonly IRepositorioFacturacionCursos facturacionCursos = A.Fake<IRepositorioFacturacionCursos>();
        private readonly IRepositorioCupones cupones = A.Fake<IRepositorioCupones>();

        private int transacciones;

        private ServicioEventos Servicio() => new ServicioEventos(db, () => HOY, AlmacenDe, facturacionCursos, cupones,
            trabajo => { transacciones++; return trabajo(); });

        private static Evento Evento(int id, string titulo, DateTime fecha, bool activo = true) =>
            new Evento { Id = id, Empresa = "1", Titulo = titulo, Fecha = fecha, ImporteSenal = 50, Activo = activo, Usuario = "Laura", FechaModificacion = HOY, Almacen = "REI" };

        private static ExtractoCliente Apunte(int orden, string cliente, string contacto, decimal importe, decimal pendiente) =>
            new ExtractoCliente
            {
                Empresa = "1",
                Nº_Orden = orden,
                Número = cliente,
                Contacto = contacto,
                Fecha = new DateTime(2026, 10, 6),
                TipoApunte = "3",
                Nº_Documento = "A CUENTA",
                Concepto = "S/Pago a cuenta curso 13/10",
                Importe = importe,
                ImportePdte = pendiente
            };

        private static EventoSenal Senal(int id, int eventoId, int orden, string cliente = "15191", string contacto = "0") =>
            new EventoSenal { Id = id, EventoId = eventoId, Empresa = "1", Cliente = cliente, Contacto = contacto, NumOrdenExtracto = orden, Importe = 50, Usuario = "NUEVAVISION\\Admin", FechaModificacion = HOY.AddDays(-8) };

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private EventosController Controlador(IPrincipal usuario) =>
            new EventosController(db, Servicio()) { Request = new HttpRequestMessage(), User = usuario };

        // ---------------- Eventos ----------------

        [TestMethod]
        public async Task LeerEventos_FuturosPrimeroYSoloActivos()
        {
            eventos.Add(Evento(1, "Pasado", new DateTime(2026, 9, 1)));
            eventos.Add(Evento(2, "Lejano", new DateTime(2026, 12, 1)));
            eventos.Add(Evento(3, "Hoy", HOY.Date));
            eventos.Add(Evento(4, "Desactivado", new DateTime(2026, 11, 1), activo: false));

            List<EventoDTO> lista = await Servicio().LeerEventos("1", true);

            CollectionAssert.AreEqual(new[] { "Hoy", "Lejano", "Pasado" }, lista.Select(e => e.Titulo).ToArray());
            Assert.AreEqual(4, (await Servicio().LeerEventos("1", false)).Count);
        }

        [TestMethod]
        public async Task CrearEvento_GuardaConElUsuarioDeAuditoria()
        {
            EventoDTO creado = await Servicio().CrearEvento(new EventoDTO
            {
                Titulo = "  Masterclass Cloasma ",
                Fecha = new DateTime(2026, 10, 20, 17, 0, 0),
                ImporteSenal = 50,
                Activo = true,
                Almacen = "rei",
                ProductoCurso = " CURSO01 ",
                Plazas = 12,
                Lugar = "Tienda de Reina"
            }, "NUEVAVISION\\Laura");

            A.CallTo(() => fakeEventos.Add(A<Evento>.That.Matches(e =>
                e.Titulo == "Masterclass Cloasma" && e.Fecha == new DateTime(2026, 10, 20) && e.Empresa == "1" && e.Usuario == "NUEVAVISION\\Laura"
                && e.Almacen == "REI" && e.ProductoCurso == "CURSO01" && e.Plazas == 12 && e.Lugar == "Tienda de Reina")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.AreEqual("Masterclass Cloasma", creado.Titulo);
            Assert.AreEqual("REI", creado.Almacen);
        }

        [TestMethod]
        public async Task CrearEvento_SinTienda_400SinGuardar()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CrearEvento(new EventoDTO { Titulo = "X", Fecha = HOY, ImporteSenal = 50, Almacen = "ALG" }, "x"));
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CrearEvento_ProductoQueNoExiste_400SinGuardar()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CrearEvento(new EventoDTO { Titulo = "X", Fecha = HOY, ImporteSenal = 50, Almacen = "REI", ProductoCurso = "NOEXISTE" }, "x"));
            StringAssert.Contains(ex.Message, "NOEXISTE");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ModificarEvento_MenosPlazasQueInscritos_400()
        {
            eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 20)));
            inscripciones.Add(new EventoInscripcion { Id = 1, EventoId = 1, Empresa = "1", Cliente = "15191", Contacto = "0", Estado = "Pagada", Origen = "Nesto" });
            inscripciones.Add(new EventoInscripcion { Id = 2, EventoId = 1, Empresa = "1", Cliente = "20000", Contacto = "0", Estado = "Reservada", Origen = "Nesto" });
            inscripciones.Add(new EventoInscripcion { Id = 3, EventoId = 1, Empresa = "1", Cliente = "30000", Contacto = "0", Estado = "Caducada", Origen = "Nesto" });

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().ModificarEvento(1,
                new EventoDTO { Titulo = "Masterclass", Fecha = new DateTime(2026, 10, 20), ImporteSenal = 50, Almacen = "REI", Plazas = 1 }, "x"));
            Assert.AreEqual(2, (await Servicio().LeerEventos("1", true)).Single().PlazasOcupadas);
        }

        [TestMethod]
        public async Task CrearEvento_SinTitulo_400SinGuardar()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CrearEvento(new EventoDTO { Titulo = " ", Fecha = HOY, ImporteSenal = 50 }, "x"));
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostEvento_SinSerDeLaTienda_403()
        {
            var resultado = await Controlador(Usuario("NUEVAVISION\\Vendedor", "Ventas"))
                .PostEvento(new EventoDTO { Titulo = "X", Fecha = HOY, ImporteSenal = 50, Almacen = "REI" }) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostEvento_AdministracionConTienda_Crea()
        {
            var resultado = await Controlador(Usuario("NUEVAVISION\\Admin", "Administración"))
                .PostEvento(new EventoDTO { Titulo = "X", Fecha = HOY, ImporteSenal = 50, Almacen = "REI" });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EventoDTO>));
        }

        // ---------------- NestoAPI#611: compatibilidad con el Nesto de #591 (en producción) ----------------

        /// <summary>
        /// El cuerpo EXACTO que manda hoy Nesto (JsonConvert.SerializeObject de su EventoModel): sin los campos nuevos y con una
        /// «Descripcion» calculada (Titulo + fecha) que no es la descripción del evento.
        /// </summary>
        private static EventoDTO CuerpoNesto591(int id, string titulo, string fecha) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<EventoDTO>(
                "{\"Id\":" + id + ",\"Empresa\":\"1\",\"Titulo\":\"" + titulo + "\",\"Fecha\":\"" + fecha + "T00:00:00\"," +
                "\"ImporteSenal\":50.0,\"Activo\":true,\"Usuario\":null,\"FechaModificacion\":null," +
                "\"Descripcion\":\"" + titulo + " 20/10/2026\"}");

        /// <summary>
        /// Carlos (22/10/26): todo evento, también los online, tiene tienda (REI o ALC). Antes el cuerpo del Nesto de #591 (sin
        /// Almacen) creaba eventos sin tienda; ahora es un 400 de usted y no se guarda nada.
        /// </summary>
        [TestMethod]
        public async Task Nesto591_PostSinTienda_400DeUstedSinGuardar()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Controlador(Usuario("NUEVAVISION\\Laura", "TiendaOnline"))
                .PostEvento(CuerpoNesto591(0, "Masterclass Cloasma", "2026-10-20")));

            Assert.AreEqual(ServicioEventos.MENSAJE_FALTA_TIENDA, ex.Message);
            A.CallTo(() => fakeEventos.Add(A<Evento>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Nesto591_PutDeUnoSinTienda_400HastaQueSeLePongaTienda()
        {
            Evento evento = Evento(1, "Viejo", new DateTime(2026, 10, 20));
            evento.Almacen = null;
            eventos.Add(evento);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Controlador(Usuario("NUEVAVISION\\Laura", "TiendaOnline")).PutEvento(1, CuerpoNesto591(1, "Nuevo", "2026-10-21")));
            Assert.AreEqual(ServicioEventos.MENSAJE_FALTA_TIENDA, ex.Message);
            Assert.AreEqual("Viejo", evento.Titulo);

            EventoDTO conTienda = CuerpoNesto591(1, "Nuevo", "2026-10-21");
            conTienda.Almacen = "REI";
            var resultado = await Controlador(Usuario("NUEVAVISION\\Carlos", "Dirección")).PutEvento(1, conTienda);
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EventoDTO>));
            Assert.AreEqual("REI", evento.Almacen);
        }

        [TestMethod]
        public async Task Nesto591_PutComoEnProduccion_ConservaLosCamposNuevos()
        {
            Evento evento = Evento(1, "Viejo", new DateTime(2026, 10, 20));
            evento.ProductoCurso = "CURSO01";
            evento.Plazas = 12;
            evento.Lugar = "Tienda de Reina";
            evento.Descripcion = "Dos horas de práctica con modelo.";
            eventos.Add(evento);

            var resultado = await Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas")).PutEvento(1, CuerpoNesto591(1, "Nuevo", "2026-10-21"));

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EventoDTO>));
            Assert.AreEqual("Nuevo", evento.Titulo);
            Assert.AreEqual("REI", evento.Almacen);
            Assert.AreEqual("CURSO01", evento.ProductoCurso);
            Assert.AreEqual(12, evento.Plazas);
            Assert.AreEqual("Tienda de Reina", evento.Lugar);
            Assert.AreEqual("Dos horas de práctica con modelo.", evento.Descripcion);
        }

        [TestMethod]
        public async Task Nesto591_SinTienda_AdministracionYVentasSiguenSinPoder()
        {
            var admin = await Controlador(Usuario("NUEVAVISION\\Admin", "Administración"))
                .PostEvento(CuerpoNesto591(0, "X", "2026-10-20")) as ResponseMessageResult;
            var reina = await Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas"))
                .PostEvento(CuerpoNesto591(0, "X", "2026-10-20")) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, admin.Response.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, reina.Response.StatusCode);
        }

        [TestMethod]
        public async Task Nesto591_LaRespuestaSeLeeConElEventoModelDeNesto()
        {
            eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 20)));

            List<EventoDTO> lista = await Servicio().LeerEventos("1", true);
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(lista);
            List<EventoModelNesto591> leidos = Newtonsoft.Json.JsonConvert.DeserializeObject<List<EventoModelNesto591>>(json);

            Assert.AreEqual("Masterclass", leidos.Single().Titulo);
            Assert.AreEqual("Masterclass 20/10/2026", leidos.Single().Descripcion, "La Descripcion calculada de Nesto no se pisa");
        }

        /// <summary>Copia del EventoModel de Nesto (Modulos/Cliente/Models/EventoModel.cs) tal como está en producción.</summary>
        private class EventoModelNesto591
        {
            public int Id { get; set; }
            public string Empresa { get; set; }
            public string Titulo { get; set; }
            public DateTime Fecha { get; set; }
            public decimal ImporteSenal { get; set; }
            public bool Activo { get; set; } = true;
            public string Usuario { get; set; }
            public DateTime? FechaModificacion { get; set; }
            public string Descripcion => $"{Titulo} {Fecha:dd/MM/yyyy}";
        }

        [TestMethod]
        public async Task PostEvento_TiendaOnlineYaNoMantieneEventos_403()
        {
            var resultado = await Controlador(Usuario("NUEVAVISION\\Laura", "TiendaOnline"))
                .PostEvento(new EventoDTO { Titulo = "X", Fecha = HOY, ImporteSenal = 50, Almacen = "REI" }) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
        }

        [TestMethod]
        public async Task PostEvento_TiendasDeOtraTienda_403()
        {
            var resultado = await Controlador(Usuario("NUEVAVISION\\Paloma", "Tiendas"))
                .PostEvento(new EventoDTO { Titulo = "X", Fecha = HOY, ImporteSenal = 50, Almacen = "REI" }) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostEvento_TiendasDeSuTienda_Crea()
        {
            var resultado = await Controlador(Usuario("NUEVAVISION\\Paloma", "Tiendas"))
                .PostEvento(new EventoDTO { Titulo = "X", Fecha = HOY, ImporteSenal = 50, Almacen = "ALC" });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EventoDTO>));
        }

        [TestMethod]
        public async Task PostEvento_Direccion_CreaEnCualquierTienda()
        {
            var resultado = await Controlador(Usuario("NUEVAVISION\\Carlos", "Dirección"))
                .PostEvento(new EventoDTO { Titulo = "X", Fecha = HOY, ImporteSenal = 50, Almacen = "ALC" });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EventoDTO>));
        }

        [TestMethod]
        public async Task PutEvento_TiendaDelEvento_Modifica()
        {
            Evento evento = Evento(1, "Viejo", new DateTime(2026, 10, 20));
            eventos.Add(evento);

            var resultado = await Controlador(Usuario("NUEVAVISION\\Reina", "Tiendas"))
                .PutEvento(1, new EventoDTO { Titulo = "Nuevo", Fecha = new DateTime(2026, 10, 21), ImporteSenal = 60, Activo = false, Almacen = "REI" });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EventoDTO>));
            Assert.AreEqual("Nuevo", evento.Titulo);
            Assert.AreEqual(60, evento.ImporteSenal);
            Assert.IsFalse(evento.Activo);
            Assert.AreEqual("NUEVAVISION\\Reina", evento.Usuario);
        }

        [TestMethod]
        public async Task PutEvento_LlevarseUnEventoDeOtraTienda_403()
        {
            Evento evento = Evento(1, "De Reina", new DateTime(2026, 10, 20));
            eventos.Add(evento);

            var resultado = await Controlador(Usuario("NUEVAVISION\\Paloma", "Tiendas"))
                .PutEvento(1, new EventoDTO { Titulo = "Mío", Fecha = new DateTime(2026, 10, 21), ImporteSenal = 60, Almacen = "ALC" }) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            Assert.AreEqual("De Reina", evento.Titulo);
        }

        // ---------------- Marcar señal ----------------

        [TestMethod]
        public async Task MarcarSenal_ApunteAFavorDelCliente_GuardaConElPendienteYElUsuario()
        {
            eventos.Add(Evento(1, "Masterclass Cloasma", new DateTime(2026, 10, 20)));
            extracto.Add(Apunte(3054909, "15191     ", "0  ", -80, -50)); // cobro de 80, ya se han gastado 30

            SenalEventoDTO senal = await Servicio().MarcarSenal(1,
                new MarcarSenalEventoDTO { Empresa = "1", NumOrdenExtracto = 3054909, Cliente = "15191", Contacto = "0" }, "NUEVAVISION\\Admin");

            A.CallTo(() => fakeSenales.Add(A<EventoSenal>.That.Matches(s =>
                s.EventoId == 1 && s.Empresa == "1" && s.Cliente == "15191" && s.Contacto == "0" && s.NumOrdenExtracto == 3054909
                && s.Importe == 80 && s.Usuario == "NUEVAVISION\\Admin" && s.FechaModificacion == HOY)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.AreEqual(EstadoSenalEvento.Pendiente, senal.Estado);
            Assert.AreEqual("Masterclass Cloasma", senal.Evento);
            Assert.AreEqual("CENTRO DE ESTÉTICA LUNA", senal.Nombre);
            Assert.AreEqual(80, senal.Importe, "El importe original del apunte");
            Assert.AreEqual(50, senal.ImportePendiente, "Lo que queda a favor");
        }

        [TestMethod]
        public async Task MarcarSenal_ApunteDeOtroCliente_400SinGuardar()
        {
            eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 20)));
            extracto.Add(Apunte(3054909, "20000", "0", -50, -50));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarSenal(1,
                new MarcarSenalEventoDTO { NumOrdenExtracto = 3054909, Cliente = "15191", Contacto = "0" }, "x"));

            StringAssert.Contains(ex.Message, "20000");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MarcarSenal_OtroContactoDelMismoCliente_400()
        {
            eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 20)));
            extracto.Add(Apunte(3054909, "15191", "1", -50, -50));

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarSenal(1,
                new MarcarSenalEventoDTO { NumOrdenExtracto = 3054909, Cliente = "15191", Contacto = "0" }, "x"));
        }

        [TestMethod]
        public async Task MarcarSenal_ApunteSinNadaAFavor_400()
        {
            eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 20)));
            extracto.Add(Apunte(1, "15191", "0", 120, 120)); // una factura
            extracto.Add(Apunte(2, "15191", "0", -50, 0));   // un cobro ya liquidado

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarSenal(1, new MarcarSenalEventoDTO { NumOrdenExtracto = 1 }, "x"));
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarSenal(1, new MarcarSenalEventoDTO { NumOrdenExtracto = 2 }, "x"));
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MarcarSenal_YaEsSenalDeOtroEvento_400ConElNombreDelEvento()
        {
            eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 20)));
            eventos.Add(Evento(2, "Curso de peelings", new DateTime(2026, 10, 27)));
            extracto.Add(Apunte(3054909, "15191", "0", -50, -50));
            senales.Add(Senal(7, 2, 3054909));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarSenal(1,
                new MarcarSenalEventoDTO { NumOrdenExtracto = 3054909, Cliente = "15191" }, "x"));

            StringAssert.Contains(ex.Message, "Curso de peelings");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MarcarSenal_EventoOApunteQueNoExisten_404()
        {
            eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 20)));

            var sinEvento = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarSenal(9, new MarcarSenalEventoDTO { NumOrdenExtracto = 1 }, "x"));
            var sinApunte = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarSenal(1, new MarcarSenalEventoDTO { NumOrdenExtracto = 1 }, "x"));

            Assert.AreEqual(HttpStatusCode.NotFound, sinEvento.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, sinApunte.StatusCode);
        }

        [TestMethod]
        public async Task PostSenal_SinSerAdministracion_403()
        {
            eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 20)));
            extracto.Add(Apunte(3054909, "15191", "0", -50, -50));

            var resultado = await Controlador(Usuario("NUEVAVISION\\Laura", "TiendaOnline", "Ventas"))
                .PostSenal(1, new MarcarSenalEventoDTO { NumOrdenExtracto = 3054909 }) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => fakeSenales.Add(A<EventoSenal>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostSenal_Administracion_200()
        {
            eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 20)));
            extracto.Add(Apunte(3054909, "15191", "0", -50, -50));

            var resultado = await Controlador(Usuario("NUEVAVISION\\Admin", "Administración"))
                .PostSenal(1, new MarcarSenalEventoDTO { NumOrdenExtracto = 3054909, Cliente = "15191", Contacto = "0" });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<SenalEventoDTO>));
        }

        [TestMethod]
        public async Task DeleteSenal_Administracion_LaQuitaY204()
        {
            EventoSenal senal = Senal(7, 1, 3054909);
            senales.Add(senal);

            var resultado = await Controlador(Usuario("NUEVAVISION\\Admin", "Administración")).DeleteSenal(7) as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.NoContent, resultado.StatusCode);
            A.CallTo(() => fakeSenales.Remove(senal)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeleteSenal_SinPermiso_403()
        {
            senales.Add(Senal(7, 1, 3054909));

            var resultado = await Controlador(Usuario("NUEVAVISION\\Vendedor", "Ventas")).DeleteSenal(7) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => fakeSenales.Remove(A<EventoSenal>._)).MustNotHaveHappened();
        }

        // ---------------- NestoAPI#612: borrar una señal ya usada (Carlos, 16/10/26) ----------------

        private EventoSenal SenalUsada(string factura, CuponLeido cupon)
        {
            EventoSenal senal = Senal(7, 1, 3054909);
            senales.Add(senal);
            if (!eventos.Any(e => e.Id == 1))
            {
                eventos.Add(Evento(1, "Masterclass", new DateTime(2026, 10, 30)));
            }
            inscripciones.Add(new EventoInscripcion
            {
                Id = 30, EventoId = 1, Empresa = "1", Cliente = "15191", Contacto = "0", EventoSenalId = 7,
                Origen = "Transferencia", Estado = "Pagada", FormaPago = "Transferencia", ImportePagado = 50,
                FechaPago = HOY.AddDays(-8), UsuarioPago = "NUEVAVISION\\Admin", FechaLimitePago = null,
                Usuario = "NUEVAVISION\\Admin", FechaModificacion = HOY.AddDays(-8), FechaCreacion = HOY.AddDays(-8)
            });
            A.CallTo(() => facturacionCursos.Leer(30)).Returns(factura == null ? null
                : new FacturacionCursoInscripcion { InscripcionId = 30, EmpresaFactura = "1", Factura = factura, Estado = EstadosFacturacionCurso.FACTURADA });
            A.CallTo(() => cupones.LeerDeInscripcion(30)).Returns(cupon);
            return senal;
        }

        private static CuponLeido Cupon(string estado, int? pedido = null, string factura = null) =>
            new CuponLeido { Id = 5, Codigo = "EV-2610-7K3Q", Empresa = "1", Cliente = "15191", Importe = 50, Estado = estado, EventoInscripcionId = 30, UsadoEnPedido = pedido, FacturaUso = factura };

        [TestMethod]
        public async Task QuitarSenal_YaFacturadaYConCupon_409SinBorrar()
        {
            SenalUsada("CV2600700", Cupon(EstadosCupon.ACTIVO));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().QuitarSenal(7, "NUEVAVISION\\Carmen"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("SENAL_EVENTO_YA_USADA", ex.Context.ErrorCode);
            Assert.AreEqual("Esta señal ya se ha usado para facturar el curso (factura CV2600700) y emitir el cupón EV-2610-7K3Q. " +
                "Para anularla, haga primero la rectificativa del curso y anule el cupón.", ex.Message);
            A.CallTo(() => fakeSenales.Remove(A<EventoSenal>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task QuitarSenal_CuponYaUsado_LoDiceY409()
        {
            SenalUsada("CV2600700", Cupon(EstadosCupon.USADO, 931234, "NV26012345"));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().QuitarSenal(7, "NUEVAVISION\\Carmen"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("Esta señal ya se ha usado para facturar el curso (factura CV2600700) y emitir el cupón EV-2610-7K3Q. " +
                "El cupón ya se ha usado en el pedido 931234 (factura NV26012345), así que no se puede anular y la señal no se puede quitar.",
                ex.Message);
            A.CallTo(() => fakeSenales.Remove(A<EventoSenal>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task QuitarSenal_CuponYaAnulado_SoloPideLaRectificativa()
        {
            SenalUsada("CV2600700", Cupon(EstadosCupon.ANULADO));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().QuitarSenal(7, "NUEVAVISION\\Carmen"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("Esta señal ya se ha usado para facturar el curso (factura CV2600700) y emitir el cupón EV-2610-7K3Q, " +
                "que ya está anulado. Para anularla, haga primero la rectificativa del curso.", ex.Message);
        }

        [TestMethod]
        public async Task QuitarSenal_FacturadaSinCupon_409SoloConLaFactura()
        {
            SenalUsada("CV2600700", null);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().QuitarSenal(7, "NUEVAVISION\\Carmen"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("Esta señal ya se ha usado para facturar el curso (factura CV2600700). " +
                "Para anularla, haga primero la rectificativa del curso.", ex.Message);
        }

        [TestMethod]
        public async Task DeleteSenal_YaUsada_409ConSuCodigo()
        {
            SenalUsada("CV2600700", Cupon(EstadosCupon.ACTIVO));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Controlador(Usuario("NUEVAVISION\\Admin", "Administración")).DeleteSenal(7));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("SENAL_EVENTO_YA_USADA", ex.Context.ErrorCode);
        }

        /// <summary>
        /// NestoAPI#612 (Carlos, 16/10/26): con inscripción pagada con la señal pero sin facturar ni cupón, antes daba 500 por
        /// FK_EventosInscripciones_EventosSenales. Ahora la inscripción se suelta (vuelve a pendiente de pago) y luego se borra
        /// la señal, en una transacción: primero se guarda la inscripción suelta y después se borra la señal.
        /// </summary>
        [TestMethod]
        public async Task QuitarSenal_ConInscripcionSinFacturarNiCupon_SueltaLaInscripcionYBorraEnTransaccion()
        {
            EventoSenal senal = SenalUsada(null, null);
            bool borrada = false;
            A.CallTo(() => fakeSenales.Remove(senal)).Invokes(() => borrada = true);
            var guardados = new List<string>();
            A.CallTo(() => db.SaveChangesAsync()).Invokes(() =>
                guardados.Add($"senal={inscripciones[0].EventoSenalId?.ToString() ?? "null"} borrada={borrada}")).Returns(1);

            await Servicio().QuitarSenal(7, "NUEVAVISION\\Carmen");

            EventoInscripcion inscripcion = inscripciones[0];
            Assert.AreEqual("Reservada", inscripcion.Estado, "Vuelve a pendiente de pago");
            Assert.IsNull(inscripcion.EventoSenalId);
            Assert.IsNull(inscripcion.FormaPago);
            Assert.IsNull(inscripcion.ImportePagado);
            Assert.IsNull(inscripcion.FechaPago);
            Assert.IsNull(inscripcion.UsuarioPago);
            Assert.AreEqual(new DateTime(2026, 10, 21), inscripcion.FechaLimitePago, "7 días desde hoy para volver a pagar");
            Assert.AreEqual("NUEVAVISION\\Carmen", inscripcion.Usuario);
            Assert.AreEqual(HOY, inscripcion.FechaModificacion);
            CollectionAssert.AreEqual(new[] { "senal=null borrada=False", "senal=null borrada=True" }, guardados,
                "Primero se suelta la inscripción y después se borra la señal");
            Assert.AreEqual(1, transacciones);
        }

        // ---------------- NestoAPI#612: quitar la señal tras la rectificativa (Carlos, 16/10/26) ----------------

        /// <summary>La rectificativa del curso, como la deja CopiarFactura (Verifactu #38): Empresa char(1), número original con relleno.</summary>
        private void Rectificar(string facturaOriginal) =>
            rectificaciones.Add(new LinFacturaVtaRectificacion
            {
                Empresa = "1", NumeroFactura = "RV2600010", NumeroLinea = 1, FacturaOriginalNumero = facturaOriginal + "  ",
                FacturaOriginalLinea = 1, CantidadRectificada = 1
            });

        [TestMethod]
        public async Task QuitarSenal_FacturaRectificadaYCuponAnulado_SueltaYBorra()
        {
            EventoSenal senal = SenalUsada("CV2600700", Cupon(EstadosCupon.ANULADO));
            Rectificar("CV2600700");

            await Servicio().QuitarSenal(7, "Carmen");

            A.CallTo(() => fakeSenales.Remove(senal)).MustHaveHappenedOnceExactly();
            Assert.AreEqual("Reservada", inscripciones[0].Estado);
            Assert.IsNull(inscripciones[0].EventoSenalId);
            Assert.AreEqual(1, transacciones);
        }

        [TestMethod]
        public async Task QuitarSenal_FacturaRectificadaSinCupon_SueltaYBorra()
        {
            EventoSenal senal = SenalUsada("CV2600700", null);
            Rectificar("CV2600700");

            await Servicio().QuitarSenal(7, "Carmen");

            A.CallTo(() => fakeSenales.Remove(senal)).MustHaveHappenedOnceExactly();
            Assert.IsNull(inscripciones[0].EventoSenalId);
        }

        [TestMethod]
        public async Task QuitarSenal_SinFacturaYCuponAnulado_SueltaYBorra()
        {
            EventoSenal senal = SenalUsada(null, Cupon(EstadosCupon.ANULADO));

            await Servicio().QuitarSenal(7, "Carmen");

            A.CallTo(() => fakeSenales.Remove(senal)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task QuitarSenal_FacturaRectificadaCuponSinAnular_409SoloPideAnularElCupon()
        {
            SenalUsada("CV2600700", Cupon(EstadosCupon.ACTIVO));
            Rectificar("CV2600700");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().QuitarSenal(7, "Carmen"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("SENAL_EVENTO_YA_USADA", ex.Context.ErrorCode);
            Assert.AreEqual("Esta señal ya se ha usado para facturar el curso (factura CV2600700, que ya tiene rectificativa) y emitir el " +
                "cupón EV-2610-7K3Q. Para anularla, anule primero el cupón.", ex.Message);
            A.CallTo(() => fakeSenales.Remove(A<EventoSenal>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
            Assert.AreEqual("Pagada", inscripciones[0].Estado);
        }

        [TestMethod]
        public async Task QuitarSenal_FacturaRectificadaCuponUsado_409NoSePuede()
        {
            SenalUsada("CV2600700", Cupon(EstadosCupon.USADO, 931234, "NV26012345"));
            Rectificar("CV2600700");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().QuitarSenal(7, "Carmen"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("Esta señal ya se ha usado para facturar el curso (factura CV2600700, que ya tiene rectificativa) y emitir el " +
                "cupón EV-2610-7K3Q. El cupón ya se ha usado en el pedido 931234 (factura NV26012345), así que no se puede anular y la " +
                "señal no se puede quitar.", ex.Message);
            A.CallTo(() => fakeSenales.Remove(A<EventoSenal>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task QuitarSenal_RectificativaDeOtraFactura_NoCuenta()
        {
            SenalUsada("CV2600700", Cupon(EstadosCupon.ANULADO));
            Rectificar("CV2600701");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().QuitarSenal(7, "Carmen"));

            Assert.AreEqual("Esta señal ya se ha usado para facturar el curso (factura CV2600700) y emitir el cupón EV-2610-7K3Q, " +
                "que ya está anulado. Para anularla, haga primero la rectificativa del curso.", ex.Message);
            A.CallTo(() => fakeSenales.Remove(A<EventoSenal>._)).MustNotHaveHappened();
        }

        /// <summary>Pagada con el enlace (tarjeta) y con la señal apuntada después: el pago con tarjeta sigue valiendo, solo se suelta la señal.</summary>
        [TestMethod]
        public async Task QuitarSenal_InscripcionPagadaConTarjeta_SoloSueltaLaSenal()
        {
            EventoSenal senal = SenalUsada(null, null);
            EventoInscripcion inscripcion = inscripciones[0];
            inscripcion.FormaPago = "Tarjeta";
            inscripcion.PagoTPVId = 900;

            await Servicio().QuitarSenal(7, "NUEVAVISION\\Carmen");

            Assert.AreEqual("Pagada", inscripcion.Estado);
            Assert.IsNull(inscripcion.EventoSenalId);
            Assert.AreEqual("Tarjeta", inscripcion.FormaPago);
            Assert.AreEqual(50M, inscripcion.ImportePagado);
            A.CallTo(() => fakeSenales.Remove(senal)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task QuitarSenal_SinInscripciones_SeBorraSinTransaccion()
        {
            EventoSenal senal = Senal(7, 1, 3054909);
            senales.Add(senal);

            await Servicio().QuitarSenal(7, "NUEVAVISION\\Carmen");

            A.CallTo(() => fakeSenales.Remove(senal)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, transacciones);
        }

        // ---------------- Lista de señales ----------------

        private void PrepararCuatroEstados()
        {
            eventos.Add(Evento(1, "Futuro", new DateTime(2026, 10, 20)));        // Pendiente
            eventos.Add(Evento(2, "Hace una semana", new DateTime(2026, 10, 7))); // Liberada
            eventos.Add(Evento(3, "Hace un mes", new DateTime(2026, 9, 14)));     // Sin compra
            extracto.Add(Apunte(101, "15191", "0", -50, -50));
            extracto.Add(Apunte(102, "15191", "1", -50, -20));
            extracto.Add(Apunte(103, "20000", "0", -50, -50));
            extracto.Add(Apunte(104, "20000", "0", -50, 0));
            senales.Add(Senal(1, 1, 101));
            senales.Add(Senal(2, 2, 102, contacto: "1"));
            senales.Add(Senal(3, 3, 103, cliente: "20000"));
            senales.Add(Senal(4, 2, 104, cliente: "20000"));
        }

        [TestMethod]
        public async Task LeerSenales_SinFiltro_TodasConSuEstado()
        {
            PrepararCuatroEstados();

            List<SenalEventoDTO> lista = await Servicio().LeerSenales("1", null, null, null);

            Assert.AreEqual(4, lista.Count);
            Assert.AreEqual(EstadoSenalEvento.Pendiente, lista.Single(s => s.Id == 1).Estado);
            SenalEventoDTO liberada = lista.Single(s => s.Id == 2);
            Assert.AreEqual(EstadoSenalEvento.Liberada, liberada.Estado);
            Assert.AreEqual(20, liberada.ImportePendiente, "Compra parcial: sigue liberada con lo que queda");
            Assert.AreEqual("LUNA (ALCALÁ)", liberada.Nombre);
            Assert.AreEqual(EstadoSenalEvento.SinCompra, lista.Single(s => s.Id == 3).Estado);
            Assert.AreEqual("Sin compra", lista.Single(s => s.Id == 3).EstadoTexto);
            Assert.AreEqual(EstadoSenalEvento.Consumida, lista.Single(s => s.Id == 4).Estado);
        }

        [TestMethod]
        public async Task LeerSenales_PorEstado_SoloLasDeEseEstado()
        {
            PrepararCuatroEstados();

            CollectionAssert.AreEqual(new[] { 1 }, (await Servicio().LeerSenales("1", "Pendiente", null, null)).Select(s => s.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 2 }, (await Servicio().LeerSenales("1", "Liberada", null, null)).Select(s => s.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 3 }, (await Servicio().LeerSenales("1", "Sin compra", null, null)).Select(s => s.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 4 }, (await Servicio().LeerSenales("1", "Consumida", null, null)).Select(s => s.Id).ToArray());
            Assert.AreEqual(4, (await Servicio().LeerSenales("1", "Todas", null, null)).Count);
        }

        [TestMethod]
        public async Task LeerSenalesCliente_SoloLasDeEseClienteYContacto()
        {
            PrepararCuatroEstados();

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, (await Servicio().LeerSenalesCliente("1", "15191", null)).Select(s => s.Id).ToArray());
            CollectionAssert.AreEquivalent(new[] { 2 }, (await Servicio().LeerSenalesCliente("1", "15191", "1")).Select(s => s.Id).ToArray());
        }

        // ---------------- Correo del día del evento ----------------

        private static SenalEventoDTO SenalCorreo(DateTime fechaEvento, decimal pendiente, DateTime hoy) =>
            ServicioEventos.Componer(
                new EventoSenal { Id = 1, EventoId = 1, Empresa = "1", Cliente = "15191", Contacto = "0", NumOrdenExtracto = 101, Importe = 50, Usuario = "Admin" },
                new Evento { Id = 1, Titulo = "Masterclass Cloasma", Fecha = fechaEvento },
                new ExtractoCliente { Nº_Orden = 101, ImportePdte = pendiente, Concepto = "S/Pago a cuenta" },
                "LUNA", hoy);

        private static async Task<(bool enviado, MailMessage correo)> Ejecutar(DateTime hoy, params SenalEventoDTO[] senales)
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage capturado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => capturado = new MailMessage { Subject = m.Subject, Body = m.Body, IsBodyHtml = m.IsBodyHtml })
                .Returns(true);
            bool enviado = await SenalesEventosJobsService.Procesar(new DependenciasSenalesEventos
            {
                LeerSenales = () => Task.FromResult(senales.ToList()),
                Correo = correo,
                Ahora = hoy
            });
            return (enviado, capturado);
        }

        [TestMethod]
        public async Task Correo_ConUnaSenalQueSeLiberaHoy_LoMandaAAdministracion()
        {
            DateTime hoy = HOY;
            var (enviado, correo) = await Ejecutar(hoy, SenalCorreo(hoy.Date, -50, hoy));

            Assert.IsTrue(enviado);
            StringAssert.Contains(correo.Body, "Señales liberadas hoy");
            StringAssert.Contains(correo.Body, "Masterclass Cloasma");
            StringAssert.Contains(correo.Body, "15191");
            StringAssert.Contains(correo.Subject, "1 liberadas hoy");
            Assert.AreEqual("administracion@nuevavision.es", SenalesEventosJobsService.ConstruirCorreo(
                new List<SenalEventoDTO>(), new List<SenalEventoDTO>(), hoy, hoy).To.Single().Address);
        }

        [TestMethod]
        public async Task Correo_ConUnaQuePasaHoyASinCompra_LoMandaConElApartado()
        {
            DateTime hoy = HOY;
            var (enviado, correo) = await Ejecutar(hoy, SenalCorreo(hoy.Date.AddDays(-15), -50, hoy));

            Assert.IsTrue(enviado);
            StringAssert.Contains(correo.Body, "Sin compra");
            StringAssert.Contains(correo.Body, "nueva");
        }

        [TestMethod]
        public async Task Correo_SinNadaNuevoHoy_NoMandaNada()
        {
            DateTime hoy = HOY;
            var (enviado, correo) = await Ejecutar(hoy,
                SenalCorreo(hoy.Date.AddDays(5), -50, hoy),   // pendiente
                SenalCorreo(hoy.Date.AddDays(-3), -50, hoy),  // liberada hace días
                SenalCorreo(hoy.Date.AddDays(-20), -50, hoy), // sin compra desde hace días (ya avisada)
                SenalCorreo(hoy.Date, 0, hoy));               // del evento de hoy, pero ya consumida

            Assert.IsFalse(enviado);
            Assert.IsNull(correo);
        }

        [TestMethod]
        public async Task Correo_SinSenales_NoMandaNada()
        {
            var (enviado, _) = await Ejecutar(HOY);

            Assert.IsFalse(enviado);
        }

        [TestMethod]
        public async Task Correo_ElLunes_RecogeLasDelFinDeSemana()
        {
            DateTime lunes = new DateTime(2026, 10, 19, 7, 50, 0);
            var (enviado, correo) = await Ejecutar(lunes, SenalCorreo(new DateTime(2026, 10, 17), -50, lunes));

            Assert.IsTrue(enviado);
            StringAssert.Contains(correo.Body, "desde el 17/10/2026");
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            return fake;
        }
    }
}
