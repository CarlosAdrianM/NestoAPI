using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
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
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#746 (política de Pilar aprobada por Carlos, 30/10/26): «se reserva sin pagar MIENTRAS HAYA PLAZAS». Con el evento
    /// lleno, un aviso con plazo a cada reserva sin pagar; vencido el plazo, Caducada; la inscripción nueva sin pagar se rechaza
    /// diciendo cuántas reservas hay y cuándo vencen; la que entra pagada pasa; el pago tarde avisa a Administración.
    /// </summary>
    [TestClass]
    public class PlazasLlenasEventosTests
    {
        // Viernes 30/10/26, 11:00
        private static readonly DateTime AHORA = new DateTime(2026, 10, 30, 11, 0, 0);
        private const int EVENTO = 1;
        private const int EVENTO_SIN_PLAZAS = 2;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private List<PagoTPV> pagosTPV;
        private IServicioPagos pagos;
        private RepositorioPlazasLlenasEnMemoria repo;
        private List<int> encolados;
        private List<KeyValuePair<EventoInscripcion, EstadoInscripcionEvento>> pagosFueraDePlazo;
        private ParametrosPlazasLlenasEventos parametros;
        private int siguienteId;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            siguienteId = 100;
            eventos = new List<Evento>
            {
                new Evento { Id = EVENTO, Empresa = "1", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 11, 20), HoraInicio = new TimeSpan(10, 0, 0),
                    ImporteSenal = 50, Activo = true, Almacen = "REI", Plazas = 2, Usuario = "Reina", FechaModificacion = AHORA },
                new Evento { Id = EVENTO_SIN_PLAZAS, Empresa = "1", Titulo = "Curso Peeling", Fecha = new DateTime(2026, 11, 20), ImporteSenal = 80,
                    Activo = true, Almacen = "REI", Usuario = "Reina", FechaModificacion = AHORA }
            };
            inscripciones = new List<EventoInscripcion>();
            pagosTPV = new List<PagoTPV>();
            var clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "LUNA", ClientePrincipal = true, Vendedor = "JE " },
                new Cliente { Empresa = "1", Nº_Cliente = "20000", Contacto = "0  ", Nombre = "OTRA", ClientePrincipal = true, Vendedor = "NV " },
                new Cliente { Empresa = "1", Nº_Cliente = "30000", Contacto = "0  ", Nombre = "TERCERA", ClientePrincipal = true, Vendedor = "NV " }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            DbSet<EventoInscripcion> fake = Crear(inscripciones);
            A.CallTo(() => fake.Add(A<EventoInscripcion>._)).ReturnsLazily((EventoInscripcion i) =>
            {
                i.Id = siguienteId++;
                inscripciones.Add(i);
                return i;
            });
            A.CallTo(() => db.EventosInscripciones).Returns(fake);
            A.CallTo(() => db.EventosSenales).Returns(Crear(new List<EventoSenal>()));
            A.CallTo(() => db.Clientes).Returns(Crear(clientes));
            A.CallTo(() => db.PagosTPV).Returns(Crear(pagosTPV));
            A.CallTo(() => db.PersonasContactoClientes).Returns(Crear(new List<PersonaContactoCliente>()));
            pagos = A.Fake<IServicioPagos>();
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).ReturnsLazily((SolicitudPagoTPV s, string u) =>
                Task.FromResult(new RespuestaIniciarPago { IdPago = 555, TokenAcceso = Guid.NewGuid(), UrlPaginaPago = "https://api.nuevavision.es/pago/x" }));
            repo = new RepositorioPlazasLlenasEnMemoria();
            encolados = new List<int>();
            pagosFueraDePlazo = new List<KeyValuePair<EventoInscripcion, EstadoInscripcionEvento>>();
            parametros = new ParametrosPlazasLlenasEventos();
        }

        private ServicioInscripcionesEventos Servicio(DateTime? ahora = null) =>
            new ServicioInscripcionesEventos(db, () => ahora ?? AHORA, () => pagos, (e, u) => u == "Reina" ? "REI" : null,
                A.Fake<IProcesadorInscripcionPagada>())
            {
                PlazasLlenas = repo,
                ParametrosPlazasLlenas = () => parametros,
                EncolarPlazasLlenas = id => encolados.Add(id),
                AvisarPagoFueraDePlazo = (i, e, estado, ocupadas) =>
                {
                    pagosFueraDePlazo.Add(new KeyValuePair<EventoInscripcion, EstadoInscripcionEvento>(i, estado));
                    return Task.CompletedTask;
                }
            };

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static readonly IPrincipal REINA = Usuario("NUEVAVISION\\Reina", "Tiendas");
        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Ventas");

        private EventoInscripcion Inscripcion(int id, string cliente, EstadoInscripcionEvento estado, DateTime? limite = null, int? pago = null,
            int eventoId = EVENTO)
        {
            var i = new EventoInscripcion
            {
                Id = id,
                EventoId = eventoId,
                Empresa = "1",
                Cliente = cliente,
                Contacto = "0  ",
                Origen = "Nesto",
                Estado = estado.ToString(),
                FechaLimitePago = limite,
                PagoTPVId = pago,
                UsuarioCreacion = "NUEVAVISION\\Jesus",
                FechaCreacion = AHORA.AddDays(-3).AddMinutes(id),
                Usuario = "NUEVAVISION\\Jesus",
                FechaModificacion = AHORA.AddDays(-3)
            };
            inscripciones.Add(i);
            return i;
        }

        // ---------------- Reglas y parámetros ----------------

        [TestMethod]
        public void Parametros_SinFilas_EncendidoY48Horas()
        {
            ParametrosPlazasLlenasEventos p = ParametrosPlazasLlenasEventos.Interpretar(null, null);
            Assert.IsTrue(p.Activo);
            Assert.AreEqual(48, p.HorasPago);
            Assert.IsFalse(ParametrosPlazasLlenasEventos.Interpretar("0", "24").Activo);
            Assert.IsFalse(ParametrosPlazasLlenasEventos.Interpretar("false ", null).Activo);
            Assert.AreEqual(24, ParametrosPlazasLlenasEventos.Interpretar("1", " 24 ").HorasPago);
            Assert.AreEqual(48, ParametrosPlazasLlenasEventos.Interpretar("1", "x").HorasPago, "Un valor raro no rompe: 48");
            Assert.AreEqual(48, ParametrosPlazasLlenasEventos.Interpretar("1", "0").HorasPago);
        }

        [TestMethod]
        public void Reglas_SoloLosEventosConPlazasSeLlenan()
        {
            Assert.IsTrue(ReglasPlazasLlenas.EstaLleno(eventos[0], 2));
            Assert.IsTrue(ReglasPlazasLlenas.EstaLleno(eventos[0], 3));
            Assert.IsFalse(ReglasPlazasLlenas.EstaLleno(eventos[0], 1));
            Assert.IsFalse(ReglasPlazasLlenas.EstaLleno(eventos[1], 500), "Plazas NULL = sin límite");
        }

        [TestMethod]
        public void Reglas_ReservaSinPagar_NoCuentaLaQueDijoNoPodreIr()
        {
            Assert.IsTrue(ReglasPlazasLlenas.EsReservaSinPagar(new EventoInscripcion { Estado = "Reservada" }));
            Assert.IsFalse(ReglasPlazasLlenas.EsReservaSinPagar(new EventoInscripcion { Estado = "Reservada", AsistenciaRespuesta = "NoPodraIr" }));
            Assert.IsFalse(ReglasPlazasLlenas.EsReservaSinPagar(new EventoInscripcion { Estado = "Pagada" }));
            Assert.IsFalse(ReglasPlazasLlenas.EsReservaSinPagar(new EventoInscripcion { Estado = "Caducada" }));
        }

        [TestMethod]
        public void Plazo_48HorasDesdeElAviso()
        {
            Assert.AreEqual(AHORA.AddHours(48), ReglasPlazasLlenas.Plazo(AHORA, 48, new DateTime(2026, 11, 13), eventos[0]));
        }

        [TestMethod]
        public void Plazo_NuncaDespuesDeSuUltimoDiaParaPagar()
        {
            // Último día el sábado 31: hasta las 23:59 de ese día, no 48 h
            Assert.AreEqual(new DateTime(2026, 10, 31, 23, 59, 0), ReglasPlazasLlenas.Plazo(AHORA, 48, new DateTime(2026, 10, 31), eventos[0]));
        }

        [TestMethod]
        public void Plazo_NuncaDespuesDelInicioDelEvento()
        {
            var manana = new Evento { Fecha = new DateTime(2026, 10, 31), HoraInicio = new TimeSpan(10, 0, 0) };
            Assert.AreEqual(new DateTime(2026, 10, 31, 10, 0, 0), ReglasPlazasLlenas.Plazo(AHORA, 48, null, manana));
        }

        [TestMethod]
        public void Mensaje_SinReservas_SoloDiceQueEstaLleno()
        {
            Assert.AreEqual("No quedan plazas en «Masterclass Cloasma»: las 2 plazas están ocupadas.",
                ReglasPlazasLlenas.MensajeSinPlazas(eventos[0], new List<VencimientoReservaEvento>()));
        }

        [TestMethod]
        public void Mensaje_ConReservas_DiceCuantasYCuandoVenceLaPrimera()
        {
            string mensaje = ReglasPlazasLlenas.MensajeSinPlazas(eventos[0], new List<VencimientoReservaEvento>
            {
                new VencimientoReservaEvento { Momento = new DateTime(2026, 11, 2, 18, 0, 0), ConHora = true },
                new VencimientoReservaEvento { Momento = new DateTime(2026, 11, 1, 18, 30, 0), ConHora = true }
            });
            Assert.AreEqual("No quedan plazas en «Masterclass Cloasma»: las 2 plazas están ocupadas y 2 de ellas son reservas pendientes de pago; " +
                "la primera vence el domingo 1 de noviembre a las 18:30. Si no se pagan a tiempo, esas plazas quedarán libres y podrá inscribirse entonces.",
                mensaje);
        }

        // ---------------- Punto 5: inscripción nueva con el evento lleno ----------------

        [TestMethod]
        public async Task Inscribir_EventoLlenoConReservaSinPagar_RechazaDiciendoCuantasYCuandoVence()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 6));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO,
                new InscribirEventoDTO { Empresa = "1", Cliente = "30000", Origen = "Nesto" }, REINA));

            StringAssert.Contains(ex.Message, "una de ellas es una reserva pendiente de pago, que vence el viernes 6 de noviembre (último día para pagarla)");
            Assert.AreEqual(2, inscripciones.Count);
        }

        [TestMethod]
        public async Task Inscribir_EventoLleno_ConPlazoDelAviso_LoDiceConHora()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 6));
            repo.Marcas[2] = new MarcaPlazasLlenas { Id = 2, AvisoPlazasLlenas = AHORA, PlazoPagoPlazasLlenas = new DateTime(2026, 11, 1, 11, 0, 0) };

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO,
                new InscribirEventoDTO { Empresa = "1", Cliente = "30000", Origen = "Nesto" }, REINA));

            StringAssert.Contains(ex.Message, "vence el domingo 1 de noviembre a las 11:00");
        }

        [TestMethod]
        public async Task Inscribir_EventoLleno_YaPagada_EntraYDisparaElAviso()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 6));

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO, new InscribirEventoDTO
            {
                Empresa = "1",
                Cliente = "30000",
                Origen = "Nesto",
                Pago = new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" }
            }, REINA);

            Assert.AreEqual(EstadoInscripcionEvento.Pagada, dto.Estado);
            Assert.AreEqual(50, inscripciones.Single(i => i.Cliente == "30000").ImportePagado);
            Assert.IsNull(dto.UrlPago, "Ya pagada: sin enlace");
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
            CollectionAssert.AreEqual(new[] { EVENTO }, encolados, "3 ocupadas de 2 con una sin pagar: aviso");
        }

        [TestMethod]
        public async Task Inscribir_ConPago_QuienNoPuedeMarcarPagadas_Prohibido()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO,
                new InscribirEventoDTO { Empresa = "1", Cliente = "30000", Origen = "Nesto", Pago = new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" } },
                VENDEDOR));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.IsFalse(inscripciones.Any());
        }

        [TestMethod]
        public async Task Inscribir_LaQueLlenaElEvento_EncolaElAviso()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 6));

            _ = await Servicio().Inscribir(EVENTO, new InscribirEventoDTO { Empresa = "1", Cliente = "20000", Origen = "Nesto", GenerarEnlacePago = false }, REINA);

            CollectionAssert.AreEqual(new[] { EVENTO }, encolados);
        }

        [TestMethod]
        public async Task Inscribir_QuedanPlazas_NoEncolaNada()
        {
            _ = await Servicio().Inscribir(EVENTO, new InscribirEventoDTO { Empresa = "1", Cliente = "20000", Origen = "Nesto", GenerarEnlacePago = false }, REINA);

            Assert.AreEqual(0, encolados.Count);
        }

        [TestMethod]
        public async Task Transferencia_EventoLleno_EntraPagadaYDisparaElAviso()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 6));
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 6));
            var senales = new List<EventoSenal> { new EventoSenal { Id = 7, EventoId = EVENTO, Empresa = "1", Cliente = "30000", Contacto = "0  ", Importe = 50 } };
            A.CallTo(() => db.EventosSenales).Returns(Crear(senales));

            InscripcionEventoDTO dto = await Servicio().InscribirDesdeTransferencia(7, "NUEVAVISION\\Admin");

            Assert.AreEqual(EstadoInscripcionEvento.Pagada, dto.Estado);
            Assert.AreEqual(3, inscripciones.Count, "El dinero entra aunque el evento esté lleno");
            CollectionAssert.AreEqual(new[] { EVENTO }, encolados);
        }

        // ---------------- Puntos 3 y 4: la pasada ----------------

        [TestMethod]
        public async Task Procesar_EventoLleno_AvisaUnaVezACadaReservaSinPagarConSuPlazo()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13), pago: 900);
            pagosTPV.Add(new PagoTPV { Id = 900, TokenAcceso = new Guid("11111111-2222-3333-4444-555555555555") });

            ResultadoPlazasLlenas r = await Servicio().ProcesarPlazasLlenas(null);

            Assert.IsNull(r.MotivoNoSeProcesa);
            Assert.AreEqual(1, r.Avisos.Count);
            Assert.AreEqual(2, r.Avisos[0].Inscripcion.Id);
            Assert.AreEqual(AHORA.AddHours(48), r.Avisos[0].Plazo);
            Assert.AreEqual("https://api.nuevavision.es/pago/11111111-2222-3333-4444-555555555555", r.Avisos[0].UrlPago);
            Assert.AreEqual(AHORA, repo.Marcas[2].AvisoPlazasLlenas);
            Assert.AreEqual(AHORA.AddHours(48), r.ProximosPlazos[EVENTO]);

            ResultadoPlazasLlenas otra = await Servicio(AHORA.AddHours(1)).ProcesarPlazasLlenas(EVENTO);
            Assert.AreEqual(0, otra.Avisos.Count, "Un solo aviso por reserva");
        }

        [TestMethod]
        public async Task Procesar_QuedanPlazas_NoAvisaANadie()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13));
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13)).AsistenciaRespuesta = "NoPodraIr";

            ResultadoPlazasLlenas r = await Servicio().ProcesarPlazasLlenas(null);

            Assert.AreEqual(0, r.Avisos.Count, "«No podré ir» libera la plaza: 1 de 2");
            Assert.AreEqual(0, repo.Marcas.Count);
        }

        [TestMethod]
        public async Task Procesar_EventoSinPlazas_NoSeToca()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13), eventoId: EVENTO_SIN_PLAZAS);

            ResultadoPlazasLlenas r = await Servicio().ProcesarPlazasLlenas(null);

            Assert.AreEqual(0, r.Avisos.Count);
        }

        [TestMethod]
        public async Task Procesar_PlazoVencidoConElEventoLleno_AnulaLaReservaYLiberaLaPlaza()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            EventoInscripcion reserva = Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13));
            repo.Marcas[2] = new MarcaPlazasLlenas { Id = 2, AvisoPlazasLlenas = AHORA.AddHours(-48), PlazoPagoPlazasLlenas = AHORA.AddMinutes(-1) };

            ResultadoPlazasLlenas r = await Servicio().ProcesarPlazasLlenas(EVENTO);

            Assert.AreEqual("Caducada", reserva.Estado);
            Assert.AreEqual(ServicioInscripcionesEventos.USUARIO_AUTOMATICO, reserva.Usuario);
            Assert.AreEqual(1, r.Anuladas.Count);
            Assert.AreEqual(0, r.Avisos.Count);
            Assert.IsFalse(r.ProximosPlazos.ContainsKey(EVENTO));
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task Procesar_PlazoVencidoConPlazasLibres_NoAnulaYLeQuitaLaMarca()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Anulada);
            EventoInscripcion reserva = Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13));
            repo.Marcas[2] = new MarcaPlazasLlenas { Id = 2, AvisoPlazasLlenas = AHORA.AddHours(-48), PlazoPagoPlazasLlenas = AHORA.AddMinutes(-1) };

            ResultadoPlazasLlenas r = await Servicio().ProcesarPlazasLlenas(EVENTO);

            Assert.AreEqual("Reservada", reserva.Estado);
            Assert.AreEqual(1, r.Indultadas);
            Assert.IsFalse(repo.Marcas.ContainsKey(2), "Si se vuelve a llenar, nuevo aviso");
        }

        [TestMethod]
        public async Task Procesar_PlazoSinVencer_NiAnulaNiReavisa()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            EventoInscripcion reserva = Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13));
            repo.Marcas[2] = new MarcaPlazasLlenas { Id = 2, AvisoPlazasLlenas = AHORA.AddHours(-1), PlazoPagoPlazasLlenas = AHORA.AddHours(47) };

            ResultadoPlazasLlenas r = await Servicio().ProcesarPlazasLlenas(EVENTO);

            Assert.AreEqual("Reservada", reserva.Estado);
            Assert.AreEqual(0, r.Avisos.Count + r.Anuladas.Count);
            Assert.AreEqual(AHORA.AddHours(47), r.ProximosPlazos[EVENTO]);
        }

        [TestMethod]
        public async Task Procesar_Apagado_NoHaceNada()
        {
            parametros = ParametrosPlazasLlenasEventos.Interpretar("0", null);
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13));

            ResultadoPlazasLlenas r = await Servicio().ProcesarPlazasLlenas(null);

            Assert.AreEqual(ServicioInscripcionesEventos.MOTIVO_APAGADO, r.MotivoNoSeProcesa);
            Assert.AreEqual(0, repo.Marcas.Count);
        }

        [TestMethod]
        public async Task Procesar_SinElScript_NoHaceNada()
        {
            repo.Existe = false;
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13));

            ResultadoPlazasLlenas r = await Servicio().ProcesarPlazasLlenas(null);

            Assert.AreEqual(ServicioInscripcionesEventos.MOTIVO_SIN_SCRIPT, r.MotivoNoSeProcesa);
        }

        [TestMethod]
        public async Task Procesar_HorasDelParametro()
        {
            parametros = ParametrosPlazasLlenasEventos.Interpretar("1", "24");
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13));

            ResultadoPlazasLlenas r = await Servicio().ProcesarPlazasLlenas(null);

            Assert.AreEqual(AHORA.AddHours(24), r.Avisos.Single().Plazo);
        }

        // ---------------- Pago fuera de plazo ----------------

        [TestMethod]
        public async Task PagoTarde_DeUnaCaducada_QuedaPagadaYAvisaAAdministracion()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(3, "30000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13));
            EventoInscripcion caducada = Inscripcion(2, "20000", EstadoInscripcionEvento.Caducada, new DateTime(2026, 10, 28), pago: 900);

            _ = await Servicio().RegistrarCobroEnlace(900, 50);

            Assert.AreEqual("Pagada", caducada.Estado, "El dinero manda: no se pierde");
            Assert.AreEqual(1, pagosFueraDePlazo.Count);
            Assert.AreEqual(EstadoInscripcionEvento.Caducada, pagosFueraDePlazo[0].Value);
            CollectionAssert.AreEqual(new[] { EVENTO }, encolados, "3 de 2 con una sin pagar: aviso");
        }

        [TestMethod]
        public async Task PagoTarde_DeUnaAnulada_SigueAnuladaYAvisaAAdministracion()
        {
            EventoInscripcion anulada = Inscripcion(2, "20000", EstadoInscripcionEvento.Anulada, pago: 900);

            _ = await Servicio().RegistrarCobroEnlace(900, 50);

            Assert.AreEqual("Anulada", anulada.Estado);
            Assert.AreEqual(EstadoInscripcionEvento.Anulada, pagosFueraDePlazo.Single().Value);
        }

        [TestMethod]
        public void PagoTarde_Texto_DiceSiElEventoQuedaPorEncimaDeSusPlazas()
        {
            var i = new EventoInscripcion { Id = 2, Cliente = "20000", Contacto = "0  " };
            string texto = PlazasLlenasEventosJobsService.TextoPagoFueraDePlazo(i, eventos[0], EstadoInscripcionEvento.Caducada, 3);
            StringAssert.Contains(texto, "que estaba caducada");
            StringAssert.Contains(texto, "3 de 2 plazas ocupadas");
            StringAssert.Contains(texto, "por encima de sus plazas");
            using (MailMessage mail = PlazasLlenasEventosJobsService.CorreoPagoFueraDePlazo(i, eventos[0], EstadoInscripcionEvento.Caducada, 3))
            {
                Assert.AreEqual(Constantes.Correos.CORREO_ADMON, mail.To.Single().Address);
            }
        }

        // ---------------- El job: canales, horario y programación ----------------

        private AvisoPlazasLlenasInscripcion Aviso() => new AvisoPlazasLlenasInscripcion
        {
            Evento = eventos[0],
            Inscripcion = Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13)),
            Plazo = new DateTime(2026, 11, 1, 11, 0, 0),
            UrlPago = "https://api.nuevavision.es/pago/x"
        };

        private (DependenciasPlazasLlenasEventos deps, IServicioNotificacionesPush push, IServicioCorreoElectronico correo,
            List<string> avisados, List<KeyValuePair<int, DateTime>> programados) Deps(DateTime ahora, ResultadoPlazasLlenas resultado)
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            var avisados = new List<string>();
            var programados = new List<KeyValuePair<int, DateTime>>();
            var deps = new DependenciasPlazasLlenasEventos
            {
                Ahora = () => ahora,
                Procesar = id => Task.FromResult(resultado),
                CorreoCliente = (e, c, k) => Task.FromResult("luna@example.com"),
                NombreCliente = (e, c, k) => Task.FromResult("LUNA"),
                Push = push,
                Correo = correo,
                AvisarUsuario = (u, n) => { avisados.Add(u + "|" + n.Titulo); return Task.CompletedTask; },
                Programar = (id, cuando) => programados.Add(new KeyValuePair<int, DateTime>(id, cuando))
            };
            return (deps, push, correo, avisados, programados);
        }

        [TestMethod]
        public async Task Job_Aviso_AlClienteYAQuienLaInscribio_YProgramaElVencimiento()
        {
            var resultado = new ResultadoPlazasLlenas();
            resultado.Avisos.Add(Aviso());
            resultado.ProximosPlazos[EVENTO] = new DateTime(2026, 11, 1, 11, 0, 0);
            var (deps, push, correo, avisados, programados) = Deps(AHORA, resultado);

            _ = await PlazasLlenasEventosJobsService.Procesar(deps, EVENTO);

            A.CallTo(() => push.EnviarACliente("1", "20000", A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == PlazasLlenasEventosJobsService.TIPO_NOTIFICACION_AVISO && n.Datos["urlPago"] == "https://api.nuevavision.es/pago/x")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m => m.To.Single().Address == "luna@example.com"
                && m.Body.Contains("Pagar mi plaza")))).MustHaveHappenedOnceExactly();
            Assert.AreEqual("NUEVAVISION\\Jesus|Evento lleno: reserva pendiente de pago", avisados.Single());
            Assert.AreEqual(new DateTime(2026, 11, 1, 11, 1, 0), programados.Single().Value, "Justo al vencer (sin sondeo)");
        }

        [TestMethod]
        public async Task Job_Anulada_AvisaAlClienteYAQuienLaInscribio()
        {
            var resultado = new ResultadoPlazasLlenas();
            resultado.Anuladas.Add(Aviso());
            var (deps, push, correo, avisados, programados) = Deps(AHORA, resultado);

            _ = await PlazasLlenasEventosJobsService.Procesar(deps, EVENTO);

            A.CallTo(() => push.EnviarACliente("1", "20000", A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == PlazasLlenasEventosJobsService.TIPO_NOTIFICACION_ANULADA))).MustHaveHappenedOnceExactly();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual("NUEVAVISION\\Jesus|Reserva anulada por falta de pago", avisados.Single());
        }

        [TestMethod]
        public async Task Job_FueraDeHorario_NoProcesaYSeAplazaALas9()
        {
            var resultado = new ResultadoPlazasLlenas();
            resultado.Avisos.Add(Aviso());
            var (deps, push, correo, avisados, programados) = Deps(new DateTime(2026, 10, 30, 22, 30, 0), resultado);
            bool procesado = false;
            deps.Procesar = id => { procesado = true; return Task.FromResult(resultado); };

            ResultadoPlazasLlenas r = await PlazasLlenasEventosJobsService.Procesar(deps, EVENTO);

            Assert.IsFalse(procesado);
            Assert.AreEqual(PlazasLlenasEventosJobsService.MOTIVO_HORARIO, r.MotivoNoSeProcesa);
            Assert.AreEqual(new DateTime(2026, 10, 31, 9, 0, 0), programados.Single().Value);
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Textos_AvisoAlCliente_DeUsted()
        {
            AvisoPlazasLlenasInscripcion aviso = Aviso();
            NotificacionPushDTO push = PlazasLlenasEventosJobsService.NotificacionAviso(aviso);
            Assert.AreEqual("Pague su plaza en «Masterclass Cloasma»", push.Titulo);
            Assert.AreEqual("Ya están reservadas todas las plazas. Para mantener la suya, pague antes del domingo 1 de noviembre a las 11:00; si no, la perderá.",
                push.Cuerpo);
            using (MailMessage mail = PlazasLlenasEventosJobsService.CorreoAviso(aviso, "LUNA", "luna@example.com"))
            {
                StringAssert.Contains(mail.Body, "pague antes del domingo 1 de noviembre a las 11:00");
                StringAssert.Contains(mail.Body, "Si no, la perderá");
                Assert.AreEqual("Pague su plaza en «Masterclass Cloasma» antes del domingo 1 de noviembre a las 11:00", mail.Subject);
            }
        }

        [TestMethod]
        public void Textos_AvisoSinEnlace_OfrecePagarEnLaTienda()
        {
            AvisoPlazasLlenasInscripcion aviso = Aviso();
            aviso.UrlPago = null;
            using (MailMessage mail = PlazasLlenasEventosJobsService.CorreoAviso(aviso, null, "luna@example.com"))
            {
                StringAssert.Contains(mail.Body, "Puede pagarla en su tienda o pedirnos el enlace de pago.");
            }
        }

        // ---------------- Punto 6: plazas en el EventoDTO ----------------

        [TestMethod]
        public async Task EventoDTO_PlazasSinPagar_DeLasOcupadas()
        {
            Inscripcion(1, "15191", EstadoInscripcionEvento.Pagada);
            Inscripcion(2, "20000", EstadoInscripcionEvento.Reservada, new DateTime(2026, 11, 13));
            var servicio = new ServicioEventos(db, () => AHORA, (e, u) => null);

            EventoDTO dto = await servicio.LeerEvento(EVENTO);

            Assert.AreEqual(2, dto.PlazasOcupadas);
            Assert.AreEqual(1, dto.PlazasSinPagar);
        }

        // ---------------- Utilidades ----------------

        private class RepositorioPlazasLlenasEnMemoria : IRepositorioPlazasLlenas
        {
            public bool Existe { get; set; } = true;
            public Dictionary<int, MarcaPlazasLlenas> Marcas { get; } = new Dictionary<int, MarcaPlazasLlenas>();

            public bool Disponible() => Existe;

            public Task<Dictionary<int, MarcaPlazasLlenas>> Leer(IReadOnlyCollection<int> inscripciones) =>
                Task.FromResult(Marcas.Where(m => inscripciones.Contains(m.Key))
                    .ToDictionary(m => m.Key, m => new MarcaPlazasLlenas { Id = m.Key, AvisoPlazasLlenas = m.Value.AvisoPlazasLlenas, PlazoPagoPlazasLlenas = m.Value.PlazoPagoPlazasLlenas }));

            public Task Apuntar(int inscripcion, DateTime? aviso, DateTime? plazo)
            {
                if (aviso == null && plazo == null)
                {
                    _ = Marcas.Remove(inscripcion);
                }
                else
                {
                    Marcas[inscripcion] = new MarcaPlazasLlenas { Id = inscripcion, AvisoPlazasLlenas = aviso, PlazoPagoPlazasLlenas = plazo };
                }
                return Task.CompletedTask;
            }
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
