using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
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
    /// <summary>
    /// NestoAPI#611 (cupones, corte 1): inscripciones a eventos sin pedido. Estados y transiciones, enlace de pago opcional,
    /// pago a mano (transferencia/efectivo), caducidad, inscripción desde transferencia sin previa, lista de asistencia (con y
    /// sin pasar), el job y los permisos por tienda.
    /// </summary>
    [TestClass]
    public class InscripcionesEventosTests
    {
        // Miércoles
        private static readonly DateTime HOY = new DateTime(2026, 10, 14, 9, 0, 0);
        private const int EVENTO_REINA = 1;
        private const int EVENTO_ALCOBENDAS = 2;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private List<EventoSenal> senales;
        private List<Cliente> clientes;
        private List<PagoTPV> pagosTPV;
        private DbSet<EventoInscripcion> fakeInscripciones;
        private IServicioPagos pagos;
        private IProcesadorInscripcionPagada procesador;
        private int siguienteId;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            siguienteId = 100;
            eventos = new List<Evento>
            {
                new Evento { Id = EVENTO_REINA, Empresa = "1", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 10, 30), ImporteSenal = 50, Activo = true, Almacen = "REI", Plazas = 2, Usuario = "Reina", FechaModificacion = HOY },
                new Evento { Id = EVENTO_ALCOBENDAS, Empresa = "1", Titulo = "Curso Peeling", Fecha = new DateTime(2026, 10, 16), ImporteSenal = 80, Activo = true, Almacen = "ALC", Usuario = "Paloma", FechaModificacion = HOY }
            };
            inscripciones = new List<EventoInscripcion>();
            senales = new List<EventoSenal>();
            pagosTPV = new List<PagoTPV>();
            clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "CENTRO DE ESTÉTICA LUNA", ClientePrincipal = true, Vendedor = "JE " },
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "1  ", Nombre = "LUNA (ALCALÁ)", ClientePrincipal = false, Vendedor = "JE " },
                new Cliente { Empresa = "1", Nº_Cliente = "20000", Contacto = "0  ", Nombre = "OTRA", ClientePrincipal = true, Vendedor = "NV " },
                new Cliente { Empresa = "1", Nº_Cliente = "30000", Contacto = "0  ", Nombre = "TERCERA", ClientePrincipal = true, Vendedor = "NV " }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            fakeInscripciones = Crear(inscripciones);
            A.CallTo(() => fakeInscripciones.Add(A<EventoInscripcion>._)).ReturnsLazily((EventoInscripcion i) =>
            {
                i.Id = siguienteId++;
                inscripciones.Add(i);
                return i;
            });
            A.CallTo(() => fakeInscripciones.Remove(A<EventoInscripcion>._)).ReturnsLazily((EventoInscripcion i) =>
            {
                inscripciones.Remove(i);
                return i;
            });
            A.CallTo(() => db.EventosInscripciones).Returns(fakeInscripciones);
            A.CallTo(() => db.EventosSenales).Returns(Crear(senales));
            A.CallTo(() => db.Clientes).Returns(Crear(clientes));
            A.CallTo(() => db.PagosTPV).Returns(Crear(pagosTPV));

            pagos = A.Fake<IServicioPagos>();
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).ReturnsLazily((SolicitudPagoTPV s, string u) =>
            {
                var token = Guid.NewGuid();
                pagosTPV.Add(new PagoTPV { Id = 555, TokenAcceso = token });
                return Task.FromResult(new RespuestaIniciarPago { IdPago = 555, TokenAcceso = token, UrlPaginaPago = "https://api.nuevavision.es/pago/" + token });
            });
            procesador = A.Fake<IProcesadorInscripcionPagada>();
        }

        private ServicioInscripcionesEventos Servicio(DateTime? ahora = null) =>
            new ServicioInscripcionesEventos(db, () => ahora ?? HOY, () => pagos, AlmacenDe, procesador);

        /// <summary>Reina mantiene los eventos de REI y Paloma los de ALC (su AlmacénPedidoVta).</summary>
        private static string AlmacenDe(string empresa, string usuario) =>
            usuario == "Reina" ? "REI" : usuario == "Paloma" ? "ALC" : null;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static IPrincipal ClienteTienda(string cliente) =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", cliente) }, "prueba"));

        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Ventas");
        private static readonly IPrincipal REINA = Usuario("NUEVAVISION\\Reina", "Tiendas");
        private static readonly IPrincipal PALOMA = Usuario("NUEVAVISION\\Paloma", "Tiendas");
        private static readonly IPrincipal DIRECCION = Usuario("NUEVAVISION\\Carlos", "Dirección");

        private EventoInscripcion Inscripcion(int id, int eventoId, string cliente, EstadoInscripcionEvento estado, DateTime? limite = null, int? pago = null)
        {
            var i = new EventoInscripcion
            {
                Id = id,
                EventoId = eventoId,
                Empresa = "1",
                Cliente = cliente,
                Contacto = "0  ",
                Origen = "NestoApp",
                Estado = estado.ToString(),
                FechaLimitePago = limite,
                PagoTPVId = pago,
                UsuarioCreacion = "NUEVAVISION\\Jesus",
                FechaCreacion = HOY.AddDays(-3),
                Usuario = "NUEVAVISION\\Jesus",
                FechaModificacion = HOY.AddDays(-3)
            };
            inscripciones.Add(i);
            return i;
        }

        // ---------------- Reglas ----------------

        [TestMethod]
        public void Reglas_TransicionesPermitidas()
        {
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Reservada, EstadoInscripcionEvento.Pagada));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Reservada, EstadoInscripcionEvento.Caducada));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Reservada, EstadoInscripcionEvento.Anulada));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Caducada, EstadoInscripcionEvento.Pagada));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Facturada));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Asistio));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Facturada, EstadoInscripcionEvento.NoAsistio));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.NoAsistio, EstadoInscripcionEvento.Asistio));
        }

        [TestMethod]
        public void Reglas_TransicionesProhibidas()
        {
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Reservada, EstadoInscripcionEvento.Asistio));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Reservada, EstadoInscripcionEvento.Facturada));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Anulada));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Caducada));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Anulada, EstadoInscripcionEvento.Pagada));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Caducada, EstadoInscripcionEvento.Asistio));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.Pagada));
        }

        [TestMethod]
        public void Reglas_FechaLimite_SieteDiasPeroNuncaDespuesDelEvento()
        {
            Assert.AreEqual(new DateTime(2026, 10, 21), ReglasInscripcionEvento.FechaLimitePago(HOY, new DateTime(2026, 10, 30)));
            Assert.AreEqual(new DateTime(2026, 10, 16), ReglasInscripcionEvento.FechaLimitePago(HOY, new DateTime(2026, 10, 16)));
            Assert.IsFalse(ReglasInscripcionEvento.EstaCaducada(new DateTime(2026, 10, 14), HOY));
            Assert.IsTrue(ReglasInscripcionEvento.EstaCaducada(new DateTime(2026, 10, 13), HOY));
            Assert.IsFalse(ReglasInscripcionEvento.EstaCaducada(null, HOY));
        }

        [TestMethod]
        public void Reglas_TextosConTildes()
        {
            Assert.AreEqual("Asistió", ReglasInscripcionEvento.Texto(EstadoInscripcionEvento.Asistio));
            Assert.AreEqual("No asistió", ReglasInscripcionEvento.Texto(EstadoInscripcionEvento.NoAsistio));
            Assert.AreEqual(OrigenInscripcionEvento.Telefono, ReglasInscripcionEvento.Origen("Teléfono"));
            Assert.IsNull(ReglasInscripcionEvento.Origen("1"));
        }

        // ---------------- Inscribir ----------------

        [TestMethod]
        public async Task Inscribir_Reservada_ConEnlaceDePagoConElEventoYLaInscripcion()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Origen = "NestoApp", Correo = "luna@example.com" }, VENDEDOR);

            EventoInscripcion guardada = inscripciones.Single();
            Assert.AreEqual("Reservada", guardada.Estado);
            Assert.AreEqual("NestoApp", guardada.Origen);
            Assert.AreEqual("0", guardada.Contacto, "Sin contacto, el principal");
            Assert.AreEqual(new DateTime(2026, 10, 21), guardada.FechaLimitePago);
            Assert.AreEqual(555, guardada.PagoTPVId);
            Assert.AreEqual("Tarjeta", guardada.FormaPago);
            Assert.AreEqual("NUEVAVISION\\Jesus", guardada.UsuarioCreacion);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>.That.Matches(s =>
                s.Importe == 50 && s.Cliente == "15191" && s.Contacto == "0" && s.Descripcion == "Señal Masterclass Cloasma"
                && s.Documento == "INS" + guardada.Id && s.Correo == "luna@example.com" && s.Vendedor == "JE"), A<string>._))
                .MustHaveHappenedOnceExactly();
            StringAssert.StartsWith(dto.UrlPago, "https://api.nuevavision.es/pago/");
            Assert.AreEqual(EstadoInscripcionEvento.Reservada, dto.Estado);
            A.CallTo(() => procesador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Inscribir_EnEfectivo_SinEnlace()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Teléfono", FormaPago = "Efectivo" }, VENDEDOR);

            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual("Efectivo", inscripciones.Single().FormaPago);
            Assert.AreEqual("Telefono", inscripciones.Single().Origen);
            Assert.IsNull(dto.UrlPago);
            Assert.AreEqual(EstadoInscripcionEvento.Reservada, dto.Estado);
        }

        [TestMethod]
        public async Task Inscribir_SinGenerarEnlace_SinEnlace()
        {
            await Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto", GenerarEnlacePago = false }, VENDEDOR);

            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
            Assert.IsNull(inscripciones.Single().PagoTPVId);
        }

        [TestMethod]
        public async Task Inscribir_EventoEnDosDias_LaFechaLimiteEsElDiaDelEvento()
        {
            await Servicio().Inscribir(EVENTO_ALCOBENDAS, new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR);

            Assert.AreEqual(new DateTime(2026, 10, 16), inscripciones.Single().FechaLimitePago);
        }

        [TestMethod]
        public async Task Inscribir_YaInscrito_400()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(2));

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Contacto = "0", Origen = "Nesto" }, VENDEDOR));
            Assert.AreEqual(1, inscripciones.Count);
        }

        [TestMethod]
        public async Task Inscribir_LaCaducadaNoCuentaComoInscrito()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Caducada, HOY.AddDays(-2));

            await Servicio().Inscribir(EVENTO_REINA, new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR);

            Assert.AreEqual(2, inscripciones.Count);
        }

        [TestMethod]
        public async Task Inscribir_SinPlazas_400()
        {
            Inscripcion(1, EVENTO_REINA, "20000", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, EVENTO_REINA, "30000", EstadoInscripcionEvento.Reservada, HOY.AddDays(2));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR));
            StringAssert.Contains(ex.Message, "plazas");
        }

        [TestMethod]
        public async Task Inscribir_FallaElEnlace_NoQuedaInscripcionAMedias()
        {
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).Throws(new ArgumentException("Redsys caído"));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR));

            StringAssert.Contains(ex.Message, "Redsys caído");
            Assert.AreEqual(0, inscripciones.Count);
        }

        [TestMethod]
        public async Task Inscribir_EmpleadoSinOrigen_400()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191" }, VENDEDOR));
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Transferencia" }, VENDEDOR));
        }

        [TestMethod]
        public async Task Inscribir_EventoPasado_400()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio(new DateTime(2026, 10, 17)).Inscribir(EVENTO_ALCOBENDAS,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR));
        }

        [TestMethod]
        public async Task Inscribir_ClienteDeLaTienda_ASiMismoConOrigenTNV()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, ClienteTienda("15191"));

            Assert.AreEqual(OrigenInscripcionEvento.TNV, dto.Origen);
        }

        [TestMethod]
        public async Task Inscribir_ClienteDeLaTienda_AOtro_403()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "20000", Origen = "TNV" }, ClienteTienda("15191")));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual(0, inscripciones.Count);
        }

        // ---------------- Cobro del enlace ----------------

        [TestMethod]
        public async Task CobroEnlace_ReservadaPasaAPagadaYDisparaElCorte2()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3), pago: 555);

            InscripcionEventoDTO dto = await Servicio().RegistrarCobroEnlace(555, 50);

            Assert.AreEqual(EstadoInscripcionEvento.Pagada, dto.Estado);
            EventoInscripcion i = inscripciones.Single();
            Assert.AreEqual("Tarjeta", i.FormaPago);
            Assert.AreEqual(50, i.ImportePagado);
            Assert.AreEqual(HOY, i.FechaPago);
            Assert.AreEqual("NestoAPI", i.UsuarioPago);
            A.CallTo(() => procesador.Procesar(i, A<Evento>.That.Matches(e => e.Id == EVENTO_REINA))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CobroEnlace_DosNotificaciones_SoloUnaVezAlCorte2()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3), pago: 555);

            await Servicio().RegistrarCobroEnlace(555, 50);
            await Servicio().RegistrarCobroEnlace(555, 50);

            A.CallTo(() => procesador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CobroEnlace_CaducadaQuePagaTarde_PasaAPagada()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Caducada, HOY.AddDays(-1), pago: 555);

            InscripcionEventoDTO dto = await Servicio().RegistrarCobroEnlace(555, 50);

            Assert.AreEqual(EstadoInscripcionEvento.Pagada, dto.Estado);
        }

        [TestMethod]
        public async Task CobroEnlace_Anulada_SeQuedaAnuladaYNoDisparaNada()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Anulada, HOY.AddDays(3), pago: 555);

            InscripcionEventoDTO dto = await Servicio().RegistrarCobroEnlace(555, 50);

            Assert.AreEqual(EstadoInscripcionEvento.Anulada, dto.Estado);
            A.CallTo(() => procesador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CobroEnlace_SinInscripcion_Null()
        {
            Assert.IsNull(await Servicio().RegistrarCobroEnlace(999, 50));
        }

        [TestMethod]
        public async Task CobroEnlace_SiFallaElCorte2_LaInscripcionSeQuedaPagada()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3), pago: 555);
            A.CallTo(() => procesador.Procesar(A<EventoInscripcion>._, A<Evento>._)).Throws(new InvalidOperationException("sin factura"));

            InscripcionEventoDTO dto = await Servicio().RegistrarCobroEnlace(555, 50);

            Assert.AreEqual(EstadoInscripcionEvento.Pagada, dto.Estado);
        }

        [TestMethod]
        public void ServicioPagos_SoloLosEnlacesDeInscripcion()
        {
            Assert.IsTrue(CobroInscripcionEvento.EsEnlaceDeInscripcion(new PagoTPV { Tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL, Documento = "INS123" }));
            Assert.IsFalse(CobroInscripcionEvento.EsEnlaceDeInscripcion(new PagoTPV { Tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL, Documento = "925607" }));
            Assert.IsFalse(CobroInscripcionEvento.EsEnlaceDeInscripcion(new PagoTPV { Tipo = Constantes.TiposPagoTPV.PEDIDO_APP, Documento = "INS123" }));
        }

        [TestMethod]
        public async Task ServicioPagos_ElCobroDeUnReintentoMarcaLaInscripcionDelEnlaceRaiz()
        {
            var servicioPagos = new ServicioPagos(A.Fake<IRedsysService>(), A.Fake<IContabilidadService>(), A.Fake<ILectorParametrosUsuario>(),
                A.Fake<IServicioCorreoElectronico>(), A.Fake<ILogService>(), A.Fake<ITarjetaClienteStore>());
            var llamadas = new List<(int, decimal)>();
            servicioPagos.RegistrarCobroInscripcion = (id, importe) => { llamadas.Add((id, importe)); return Task.CompletedTask; };

            await servicioPagos.RegistrarCobroInscripcionEvento(new PagoTPV { Id = 560, PagoOriginalId = 555, Tipo = "TPVVirtual", Documento = "INS100", Importe = 50 });
            await servicioPagos.RegistrarCobroInscripcionEvento(new PagoTPV { Id = 561, Tipo = "TPVVirtual", Documento = "123456", Importe = 50 });

            Assert.AreEqual(1, llamadas.Count);
            Assert.AreEqual((555, 50m), llamadas.Single());
        }

        [TestMethod]
        public async Task ServicioPagos_SiFallaLaInscripcion_NoLanza()
        {
            ILogService log = A.Fake<ILogService>();
            var servicioPagos = new ServicioPagos(A.Fake<IRedsysService>(), A.Fake<IContabilidadService>(), A.Fake<ILectorParametrosUsuario>(),
                A.Fake<IServicioCorreoElectronico>(), log, A.Fake<ITarjetaClienteStore>());
            servicioPagos.RegistrarCobroInscripcion = (id, importe) => throw new InvalidOperationException("BD caída");

            await servicioPagos.RegistrarCobroInscripcionEvento(new PagoTPV { Id = 555, Tipo = "TPVVirtual", Documento = "INS100", Importe = 50 });

            A.CallTo(() => log.LogError(A<string>.That.Contains("#611"), A<Exception>._)).MustHaveHappenedOnceExactly();
        }

        // ---------------- Pago a mano ----------------

        [TestMethod]
        public async Task MarcarPagada_EfectivoEnSuTienda_PagadaConQuienYCuandoYDisparaElCorte2()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3));

            InscripcionEventoDTO dto = await Servicio().MarcarPagada(1,
                new MarcarPagoInscripcionDTO { FormaPago = "Efectivo", Importe = 50 }, REINA);

            Assert.AreEqual(EstadoInscripcionEvento.Pagada, dto.Estado);
            EventoInscripcion i = inscripciones.Single();
            Assert.AreEqual("Efectivo", i.FormaPago);
            Assert.AreEqual(50, i.ImportePagado);
            Assert.AreEqual("NUEVAVISION\\Reina", i.UsuarioPago);
            Assert.AreEqual(HOY, i.FechaPago);
            A.CallTo(() => procesador.Procesar(i, A<Evento>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task MarcarPagada_SinImporte_ElDeLaSenalDelEvento()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3));

            await Servicio().MarcarPagada(1, new MarcarPagoInscripcionDTO { FormaPago = "Transferencia", Fecha = HOY.AddDays(-1) }, DIRECCION);

            Assert.AreEqual(50, inscripciones.Single().ImportePagado);
            Assert.AreEqual(HOY.AddDays(-1), inscripciones.Single().FechaPago);
        }

        [TestMethod]
        public async Task MarcarPagada_DeOtraTienda_403()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarPagada(1,
                new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" }, PALOMA));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual("Reservada", inscripciones.Single().Estado);
        }

        [TestMethod]
        public async Task MarcarPagada_VendedorOAdministracion_403()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarPagada(1,
                new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" }, VENDEDOR));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task MarcarPagada_YaPagada_400()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Pagada);

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarPagada(1,
                new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" }, REINA));
            A.CallTo(() => procesador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MarcarPagada_SinFormaDePagoOFechaFutura_400()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3));

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarPagada(1,
                new MarcarPagoInscripcionDTO { FormaPago = "Bizum" }, REINA));
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().MarcarPagada(1,
                new MarcarPagoInscripcionDTO { FormaPago = "Efectivo", Fecha = HOY.AddDays(1) }, REINA));
            Assert.AreEqual("Reservada", inscripciones.Single().Estado);
        }

        // ---------------- Transferencia sin inscripción previa ----------------

        [TestMethod]
        public async Task Transferencia_SinInscripcionPrevia_LaCreaPagadaConOrigenTransferencia()
        {
            senales.Add(new EventoSenal { Id = 7, EventoId = EVENTO_REINA, Empresa = "1", Cliente = "15191", Contacto = "0  ", NumOrdenExtracto = 3054909, Importe = 50 });

            InscripcionEventoDTO dto = await Servicio().InscribirDesdeTransferencia(7, "NUEVAVISION\\Admin");

            EventoInscripcion i = inscripciones.Single();
            Assert.AreEqual("Transferencia", i.Origen);
            Assert.AreEqual("Pagada", i.Estado);
            Assert.AreEqual("Transferencia", i.FormaPago);
            Assert.AreEqual(7, i.EventoSenalId);
            Assert.AreEqual(50, i.ImportePagado);
            Assert.IsNull(i.FechaLimitePago);
            Assert.AreEqual(EstadoInscripcionEvento.Pagada, dto.Estado);
            A.CallTo(() => procesador.Procesar(i, A<Evento>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Transferencia_ConInscripcionReservada_PagaEsaYNoCreaOtra()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3));
            senales.Add(new EventoSenal { Id = 7, EventoId = EVENTO_REINA, Empresa = "1", Cliente = "15191", Contacto = "0  ", NumOrdenExtracto = 3054909, Importe = 50 });

            await Servicio().InscribirDesdeTransferencia(7, "NUEVAVISION\\Admin");

            Assert.AreEqual(1, inscripciones.Count);
            Assert.AreEqual("Pagada", inscripciones.Single().Estado);
            Assert.AreEqual("NestoApp", inscripciones.Single().Origen, "Conserva el origen de la inscripción");
            Assert.AreEqual(7, inscripciones.Single().EventoSenalId);
        }

        [TestMethod]
        public async Task Transferencia_YaPagada_SoloApuntaLaSenal()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Pagada);
            senales.Add(new EventoSenal { Id = 7, EventoId = EVENTO_REINA, Empresa = "1", Cliente = "15191", Contacto = "0  ", NumOrdenExtracto = 3054909, Importe = 50 });

            await Servicio().InscribirDesdeTransferencia(7, "NUEVAVISION\\Admin");

            Assert.AreEqual(7, inscripciones.Single().EventoSenalId);
            A.CallTo(() => procesador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustNotHaveHappened();
        }

        // ---------------- Anular ----------------

        [TestMethod]
        public async Task Anular_Reservada_Anulada()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3));

            InscripcionEventoDTO dto = await Servicio().Anular(1, VENDEDOR);

            Assert.AreEqual(EstadoInscripcionEvento.Anulada, dto.Estado);
            Assert.AreEqual("NUEVAVISION\\Jesus", inscripciones.Single().Usuario);
        }

        [TestMethod]
        public async Task Anular_Pagada_400()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Pagada);

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anular(1, DIRECCION));
            Assert.AreEqual("Pagada", inscripciones.Single().Estado);
        }

        [TestMethod]
        public async Task Anular_ClienteDeLaTiendaLaDeOtro_403()
        {
            Inscripcion(1, EVENTO_REINA, "20000", EstadoInscripcionEvento.Reservada, HOY.AddDays(3));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anular(1, ClienteTienda("15191")));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        // ---------------- Caducidad ----------------

        [TestMethod]
        public async Task Caducar_SoloLasReservadasConLaFechaLimitePasada()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.Date.AddDays(-1));
            Inscripcion(2, EVENTO_REINA, "20000", EstadoInscripcionEvento.Reservada, HOY.Date);
            Inscripcion(3, EVENTO_REINA, "30000", EstadoInscripcionEvento.Pagada, HOY.Date.AddDays(-5));

            List<InscripcionEventoDTO> caducadas = await Servicio().CaducarVencidas();

            Assert.AreEqual(1, caducadas.Single().Id);
            Assert.AreEqual("Caducada", inscripciones.Single(i => i.Id == 1).Estado);
            Assert.AreEqual("NestoAPI", inscripciones.Single(i => i.Id == 1).Usuario);
            Assert.AreEqual("Reservada", inscripciones.Single(i => i.Id == 2).Estado, "El último día todavía se puede pagar");
            Assert.AreEqual("Pagada", inscripciones.Single(i => i.Id == 3).Estado);
        }

        // ---------------- Lista de asistencia ----------------

        [TestMethod]
        public async Task PasarLista_LosMarcadosAsistenYElRestoDePagadasNo()
        {
            Inscripcion(1, EVENTO_ALCOBENDAS, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, EVENTO_ALCOBENDAS, "20000", EstadoInscripcionEvento.Facturada);
            Inscripcion(3, EVENTO_ALCOBENDAS, "30000", EstadoInscripcionEvento.Reservada, HOY.Date);
            DateTime diaDelEvento = new DateTime(2026, 10, 16, 19, 0, 0);

            List<InscripcionEventoDTO> lista = await Servicio(diaDelEvento).PasarLista(EVENTO_ALCOBENDAS,
                new AsistenciaEventoDTO { Asistentes = new List<int> { 1 } }, PALOMA);

            Assert.AreEqual("Asistio", inscripciones.Single(i => i.Id == 1).Estado);
            Assert.AreEqual("NoAsistio", inscripciones.Single(i => i.Id == 2).Estado);
            Assert.AreEqual("Reservada", inscripciones.Single(i => i.Id == 3).Estado, "Sin pagar no entra en la lista");
            Assert.AreEqual(diaDelEvento, eventos.Single(e => e.Id == EVENTO_ALCOBENDAS).FechaListaAsistencia);
            Assert.AreEqual(3, lista.Count);
        }

        [TestMethod]
        public async Task PasarLista_SePuedeCorregir()
        {
            Inscripcion(1, EVENTO_ALCOBENDAS, "15191", EstadoInscripcionEvento.NoAsistio);
            Inscripcion(2, EVENTO_ALCOBENDAS, "20000", EstadoInscripcionEvento.Asistio);

            await Servicio(new DateTime(2026, 10, 17)).PasarLista(EVENTO_ALCOBENDAS,
                new AsistenciaEventoDTO { Asistentes = new List<int> { 1, 2 } }, DIRECCION);

            Assert.IsTrue(inscripciones.All(i => i.Estado == "Asistio"));
        }

        [TestMethod]
        public async Task PasarLista_MarcarUnaSinPagar_400SinTocarNada()
        {
            Inscripcion(1, EVENTO_ALCOBENDAS, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(3, EVENTO_ALCOBENDAS, "30000", EstadoInscripcionEvento.Reservada, HOY.Date);

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio(new DateTime(2026, 10, 16)).PasarLista(EVENTO_ALCOBENDAS,
                new AsistenciaEventoDTO { Asistentes = new List<int> { 1, 3 } }, PALOMA));

            Assert.AreEqual("Pagada", inscripciones.Single(i => i.Id == 1).Estado);
            Assert.IsNull(eventos.Single(e => e.Id == EVENTO_ALCOBENDAS).FechaListaAsistencia);
        }

        [TestMethod]
        public async Task PasarLista_AntesDelEvento_400()
        {
            Inscripcion(1, EVENTO_ALCOBENDAS, "15191", EstadoInscripcionEvento.Pagada);

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().PasarLista(EVENTO_ALCOBENDAS,
                new AsistenciaEventoDTO { Asistentes = new List<int> { 1 } }, PALOMA));
        }

        [TestMethod]
        public async Task PasarLista_OtraTienda_403()
        {
            Inscripcion(1, EVENTO_ALCOBENDAS, "15191", EstadoInscripcionEvento.Pagada);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio(new DateTime(2026, 10, 16)).PasarLista(EVENTO_ALCOBENDAS,
                new AsistenciaEventoDTO { Asistentes = new List<int> { 1 } }, REINA));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual("Pagada", inscripciones.Single().Estado);
        }

        [TestMethod]
        public async Task SinLista_AlDiaSiguienteTodasLasPagadasAsisten()
        {
            Inscripcion(1, EVENTO_ALCOBENDAS, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, EVENTO_ALCOBENDAS, "20000", EstadoInscripcionEvento.Facturada);
            Inscripcion(3, EVENTO_ALCOBENDAS, "30000", EstadoInscripcionEvento.Caducada, new DateTime(2026, 10, 15));
            DateTime manana = new DateTime(2026, 10, 17, 7, 55, 0);

            var resultado = await Servicio(manana).DarPorAsistidosSinLista();

            Assert.AreEqual(EVENTO_ALCOBENDAS, resultado.Single().Key.Id);
            Assert.AreEqual(2, resultado.Single().Value.Count);
            Assert.AreEqual("Asistio", inscripciones.Single(i => i.Id == 1).Estado);
            Assert.AreEqual("Asistio", inscripciones.Single(i => i.Id == 2).Estado);
            Assert.AreEqual("Caducada", inscripciones.Single(i => i.Id == 3).Estado);
            Assert.AreEqual(manana, eventos.Single(e => e.Id == EVENTO_ALCOBENDAS).FechaListaAsistencia);
        }

        [TestMethod]
        public async Task SinLista_ElMismoDiaDelEventoTodaviaNo()
        {
            Inscripcion(1, EVENTO_ALCOBENDAS, "15191", EstadoInscripcionEvento.Pagada);

            var resultado = await Servicio(new DateTime(2026, 10, 16, 23, 0, 0)).DarPorAsistidosSinLista();

            Assert.AreEqual(0, resultado.Count);
            Assert.AreEqual("Pagada", inscripciones.Single().Estado);
        }

        [TestMethod]
        public async Task SinLista_SiYaSePasoListaNoSeToca()
        {
            eventos.Single(e => e.Id == EVENTO_ALCOBENDAS).FechaListaAsistencia = new DateTime(2026, 10, 16, 20, 0, 0);
            Inscripcion(1, EVENTO_ALCOBENDAS, "15191", EstadoInscripcionEvento.NoAsistio);

            var resultado = await Servicio(new DateTime(2026, 10, 17)).DarPorAsistidosSinLista();

            Assert.AreEqual(0, resultado.Count);
            Assert.AreEqual("NoAsistio", inscripciones.Single().Estado);
        }

        // ---------------- Job ----------------

        [TestMethod]
        public async Task Job_AvisaAQuienCreoLaCaducadaYMandaElCorreoSinListaALaTienda()
        {
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) => { enviado = m; return true; });
            var avisos = new List<(string usuario, NotificacionPushDTO notificacion)>();
            var evento = new EventoDTO { Id = 2, Empresa = "1", Titulo = "Curso Peeling", Fecha = new DateTime(2026, 10, 16), Almacen = "ALC" };

            var (caducadas, sinLista) = await InscripcionesEventosJobsService.Procesar(new DependenciasInscripcionesEventos
            {
                CaducarVencidas = () => Task.FromResult(new List<InscripcionEventoDTO>
                {
                    new InscripcionEventoDTO { Id = 5, EventoId = 1, Evento = "Masterclass Cloasma", Cliente = "15191", Contacto = "0", Nombre = "LUNA", UsuarioCreacion = "NUEVAVISION\\Jesus" }
                }),
                DarPorAsistidosSinLista = () => Task.FromResult(new List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>>
                {
                    new KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>(evento, new List<InscripcionEventoDTO>
                    {
                        new InscripcionEventoDTO { Id = 1, Cliente = "15191", Contacto = "0", Nombre = "LUNA", ImportePagado = 80, FormaPago = "Tarjeta" }
                    })
                }),
                CorreosTienda = (empresa, almacen) => Task.FromResult(almacen == "ALC" ? new List<string> { "paloma@nuevavision.es" } : new List<string>()),
                Avisar = (usuario, notificacion) => { avisos.Add((usuario, notificacion)); return Task.CompletedTask; },
                Correo = correo
            });

            Assert.AreEqual(1, caducadas);
            Assert.AreEqual(1, sinLista);
            Assert.AreEqual("NUEVAVISION\\Jesus", avisos.Single().usuario);
            StringAssert.Contains(avisos.Single().notificacion.Cuerpo, "ha caducado sin pagar");
            Assert.AreEqual("paloma@nuevavision.es", enviado.To.Single().Address);
            StringAssert.Contains(enviado.Subject, "Nadie ha pasado lista");
            StringAssert.Contains(enviado.Body, "LUNA");
        }

        [TestMethod]
        public async Task Job_SinNadieDeLaTiendaConCorreo_AlBuzonDeLaTienda()
        {
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) => { enviado = m; return true; });

            await InscripcionesEventosJobsService.Procesar(new DependenciasInscripcionesEventos
            {
                CaducarVencidas = () => Task.FromResult(new List<InscripcionEventoDTO>()),
                DarPorAsistidosSinLista = () => Task.FromResult(new List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>>
                {
                    new KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>(
                        new EventoDTO { Id = 1, Empresa = "1", Titulo = "Masterclass", Fecha = new DateTime(2026, 10, 13), Almacen = "REI" },
                        new List<InscripcionEventoDTO> { new InscripcionEventoDTO { Id = 1, Cliente = "15191" } })
                }),
                CorreosTienda = (empresa, almacen) => Task.FromResult(new List<string>()),
                Avisar = (usuario, notificacion) => Task.CompletedTask,
                Correo = correo
            });

            Assert.AreEqual(Constantes.Correos.TIENDA_REINA, enviado.To.Single().Address);
        }

        [TestMethod]
        public async Task Job_SinNadaQueHacer_NoMandaNada()
        {
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();

            var (caducadas, sinLista) = await InscripcionesEventosJobsService.Procesar(new DependenciasInscripcionesEventos
            {
                CaducarVencidas = () => Task.FromResult(new List<InscripcionEventoDTO>()),
                DarPorAsistidosSinLista = () => Task.FromResult(new List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>>()),
                CorreosTienda = (empresa, almacen) => Task.FromResult(new List<string>()),
                Avisar = (usuario, notificacion) => Task.CompletedTask,
                Correo = correo
            });

            Assert.AreEqual(0, caducadas + sinLista);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CorreosTienda_SoloLosDelGrupoTiendasConEseAlmacen()
        {
            A.CallTo(() => db.ParametrosUsuario).Returns(Crear(new List<ParametroUsuario>
            {
                new ParametroUsuario { Empresa = "1", Usuario = "Paloma", Clave = "AlmacénPedidoVta", Valor = "ALC" },
                new ParametroUsuario { Empresa = "1", Usuario = "Paloma", Clave = "CorreoDefecto", Valor = "paloma@nuevavision.es" },
                new ParametroUsuario { Empresa = "1", Usuario = "Eva", Clave = "AlmacénPedidoVta", Valor = "ALC" },
                new ParametroUsuario { Empresa = "1", Usuario = "Eva", Clave = "CorreoDefecto", Valor = "eva@nuevavision.es" },
                new ParametroUsuario { Empresa = "1", Usuario = "Reina", Clave = "AlmacénPedidoVta", Valor = "REI" },
                new ParametroUsuario { Empresa = "1", Usuario = "Reina", Clave = "CorreoDefecto", Valor = "reina@nuevavision.es" }
            }));

            List<string> correos = await InscripcionesEventosJobsService.CorreosDe(db, "1", "ALC", new List<string> { "Paloma", "Reina" });

            CollectionAssert.AreEqual(new[] { "paloma@nuevavision.es" }, correos);
        }

        // ---------------- Lectura y permisos ----------------

        [TestMethod]
        public async Task LeerInscripcionesEvento_ConNombreYUrlDePagoMientrasSePuedePagar()
        {
            var token = Guid.NewGuid();
            pagosTPV.Add(new PagoTPV { Id = 555, TokenAcceso = token });
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Reservada, HOY.AddDays(3), pago: 555);
            Inscripcion(2, EVENTO_REINA, "20000", EstadoInscripcionEvento.Pagada);

            List<InscripcionEventoDTO> lista = await Servicio().LeerInscripcionesEvento(EVENTO_REINA, VENDEDOR);

            Assert.AreEqual(2, lista.Count);
            InscripcionEventoDTO reservada = lista.Single(i => i.Id == 1);
            Assert.AreEqual("CENTRO DE ESTÉTICA LUNA", reservada.Nombre);
            Assert.AreEqual("https://api.nuevavision.es/pago/" + token, reservada.UrlPago);
            Assert.AreEqual("REI", reservada.Almacen);
            Assert.IsNull(lista.Single(i => i.Id == 2).UrlPago);
        }

        [TestMethod]
        public async Task LeerInscripcionesEvento_ClienteDeLaTienda_403()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().LeerInscripcionesEvento(EVENTO_REINA, ClienteTienda("15191")));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task LeerInscripcionesCliente_LasSuyasYSoloLasSuyas()
        {
            Inscripcion(1, EVENTO_REINA, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, EVENTO_ALCOBENDAS, "15191", EstadoInscripcionEvento.Caducada, HOY.AddDays(-1));
            Inscripcion(3, EVENTO_REINA, "20000", EstadoInscripcionEvento.Pagada);

            List<InscripcionEventoDTO> suyas = await Servicio().LeerInscripcionesCliente("1", "15191", null, ClienteTienda("15191"));

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, suyas.Select(i => i.Id).ToArray());
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().LeerInscripcionesCliente("1", "20000", null, ClienteTienda("15191")));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_PostInscripcion_DevuelveLaInscripcion()
        {
            var controlador = new EventosController(db, new ServicioEventos(db, () => HOY, AlmacenDe), Servicio())
            {
                Request = new HttpRequestMessage(),
                User = VENDEDOR
            };

            var resultado = await controlador.PostInscripcion(EVENTO_REINA, new InscribirEventoDTO { Cliente = "15191", Origen = "NestoApp" })
                as OkNegotiatedContentResult<InscripcionEventoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(EstadoInscripcionEvento.Reservada, resultado.Content.Estado);
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
