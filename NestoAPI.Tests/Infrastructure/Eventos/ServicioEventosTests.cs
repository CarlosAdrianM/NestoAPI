using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
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
        }

        private ServicioEventos Servicio() => new ServicioEventos(db, () => HOY);

        private static Evento Evento(int id, string titulo, DateTime fecha, bool activo = true) =>
            new Evento { Id = id, Empresa = "1", Titulo = titulo, Fecha = fecha, ImporteSenal = 50, Activo = activo, Usuario = "Laura", FechaModificacion = HOY };

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
                Activo = true
            }, "NUEVAVISION\\Laura");

            A.CallTo(() => fakeEventos.Add(A<Evento>.That.Matches(e =>
                e.Titulo == "Masterclass Cloasma" && e.Fecha == new DateTime(2026, 10, 20) && e.Empresa == "1" && e.Usuario == "NUEVAVISION\\Laura")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.AreEqual("Masterclass Cloasma", creado.Titulo);
        }

        [TestMethod]
        public async Task CrearEvento_SinTitulo_400SinGuardar()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CrearEvento(new EventoDTO { Titulo = " ", Fecha = HOY, ImporteSenal = 50 }, "x"));
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostEvento_SinSerTiendaOnline_403()
        {
            var resultado = await Controlador(Usuario("NUEVAVISION\\Admin", "Administración"))
                .PostEvento(new EventoDTO { Titulo = "X", Fecha = HOY, ImporteSenal = 50 }) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutEvento_TiendaOnline_Modifica()
        {
            Evento evento = Evento(1, "Viejo", new DateTime(2026, 10, 20));
            eventos.Add(evento);

            var resultado = await Controlador(Usuario("NUEVAVISION\\Laura", "TiendaOnline"))
                .PutEvento(1, new EventoDTO { Titulo = "Nuevo", Fecha = new DateTime(2026, 10, 21), ImporteSenal = 60, Activo = false });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EventoDTO>));
            Assert.AreEqual("Nuevo", evento.Titulo);
            Assert.AreEqual(60, evento.ImporteSenal);
            Assert.IsFalse(evento.Activo);
            Assert.AreEqual("NUEVAVISION\\Laura", evento.Usuario);
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
