using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// Novedades 1233: un curso que pasa de pago a gratuito no deja reservas «de pago» colgando (todo o nada), los jobs de pago se
    /// saltan los eventos gratuitos, el curso por compra nace sin señal y un cobro tardío sobre una convertida no factura.
    /// </summary>
    [TestClass]
    public class PasoAGratuitoEventosTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 12, 10, 0, 0);
        private const int CURSO = 1;
        private const int GRATIS = 2;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private List<PagoTPV> pagosTpv;
        private List<Modificacion> modificaciones;
        private IRepositorioFacturacionCursos facturacion;
        private IRepositorioCupones cupones;
        private List<int> enlacesAnulados;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            eventos = new List<Evento>
            {
                new Evento { Id = CURSO, Empresa = "1", Titulo = "Curso por compra", Fecha = new DateTime(2026, 11, 30), ImporteSenal = 100,
                    ImporteSenalNoReembolsable = 60, Activo = true, Almacen = "REI", Usuario = "Reina", FechaModificacion = HOY, Plazas = 10 },
                new Evento { Id = GRATIS, Empresa = "1", Titulo = "Presentación", Fecha = new DateTime(2026, 11, 30), ImporteSenal = 0,
                    ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "ALC", Usuario = "Paloma", FechaModificacion = HOY }
            };
            inscripciones = new List<EventoInscripcion>();
            pagosTpv = new List<PagoTPV>();
            modificaciones = new List<Modificacion>();
            enlacesAnulados = new List<int>();
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(inscripciones));
            A.CallTo(() => db.PagosTPV).Returns(Crear(pagosTpv));
            A.CallTo(() => db.Clientes).Returns(Crear(new List<Cliente>()));
            A.CallTo(() => db.EventosSenales).Returns(Crear(new List<EventoSenal>()));
            A.CallTo(() => db.Productos).Returns(Crear(new List<Producto> { new Producto { Empresa = "1", Número = "17404" } }));
            DbSet<Modificacion> fakeModificaciones = Crear(modificaciones);
            A.CallTo(() => fakeModificaciones.Add(A<Modificacion>._)).ReturnsLazily((Modificacion m) => { modificaciones.Add(m); return m; });
            A.CallTo(() => db.Modificaciones).Returns(fakeModificaciones);
            facturacion = A.Fake<IRepositorioFacturacionCursos>();
            A.CallTo(() => facturacion.Leer(A<IEnumerable<int>>._)).Returns(Task.FromResult(new Dictionary<int, FacturacionCursoInscripcion>()));
            cupones = A.Fake<IRepositorioCupones>();
            A.CallTo(() => cupones.LeerDeInscripcion(A<int>._)).Returns(Task.FromResult<CuponLeido>(null));
        }

        private ServicioEventos Servicio()
        {
            var servicio = new ServicioEventos(db, () => HOY, (e, u) => "REI", facturacion, cupones, trabajo => trabajo());
            servicio.AnularEnlacesPago = (empresa, ids) =>
            {
                List<int> hechos = ids.Where(id => pagosTpv.Any(p => p.Id == id && p.Estado == Constantes.EstadosPagoTPV.PENDIENTE)).ToList();
                enlacesAnulados.AddRange(hechos);
                return Task.FromResult(hechos);
            };
            return servicio;
        }

        private EventoInscripcion Inscripcion(int id, int evento, string cliente, EstadoInscripcionEvento estado, string modalidad = "Reembolsable")
        {
            var i = new EventoInscripcion
            {
                Id = id, EventoId = evento, Empresa = "1", Cliente = cliente, Contacto = "0  ", Origen = "Nesto", Estado = estado.ToString(),
                Modalidad = modalidad, FormaPago = "Transferencia", FechaLimitePago = new DateTime(2026, 11, 23),
                UsuarioCreacion = "Reina", FechaCreacion = HOY.AddDays(-5), Usuario = "Reina", FechaModificacion = HOY.AddDays(-5)
            };
            inscripciones.Add(i);
            return i;
        }

        private static EventoDTO AGratuito() => JsonConvert.DeserializeObject<EventoDTO>(
            "{ \"Titulo\": \"Curso por compra\", \"Fecha\": \"2026-11-30\", \"ImporteSenal\": 0, \"ImporteSenalNoReembolsable\": 0, \"Activo\": true }");

        // ---------------- Paso a gratuito ----------------

        [TestMethod]
        public async Task PasoAGratuito_ReservadasSinPago_PasanAInscritasGratuitasConRastro()
        {
            EventoInscripcion a = Inscripcion(1, CURSO, "15191", EstadoInscripcionEvento.Reservada);
            EventoInscripcion b = Inscripcion(2, CURSO, "20000", EstadoInscripcionEvento.Reservada, "NoReembolsable");
            b.PagoTPVId = 700;
            pagosTpv.Add(new PagoTPV { Id = 700, Empresa = "1", Tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL, Estado = Constantes.EstadosPagoTPV.PENDIENTE });

            EventoDTO r = await Servicio().ModificarEvento(CURSO, AGratuito(), "NUEVAVISION\\Reina");

            foreach (EventoInscripcion i in new[] { a, b })
            {
                Assert.AreEqual("Inscrita", i.Estado);
                Assert.AreEqual("Gratuita", i.Modalidad);
                Assert.IsNull(i.FechaLimitePago);
                Assert.IsNull(i.FormaPago);
                Assert.AreEqual(HOY, i.FechaModificacion);
            }
            Assert.AreEqual(700, b.PagoTPVId, "Se conserva para reconocer un cobro tardío");
            CollectionAssert.AreEqual(new[] { 700 }, enlacesAnulados);
            Assert.AreEqual(2, modificaciones.Count);
            StringAssert.Contains(modificaciones.Single(m => m.Anterior.StartsWith("Inscripción 2")).Nuevo, "enlace anulado (700)");
            Assert.IsTrue(r.Gratuito);
            Assert.AreEqual(2, r.InscripcionesConvertidasAGratuitas);
            Assert.AreEqual(1, r.EnlacesPagoAnulados);
            StringAssert.Contains(r.MensajePasoAGratuito, "2 reservas sin pagar han pasado a inscritas gratuitas");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task PasoAGratuito_ConUnaPagadaOFacturada_SeRechazaYNoSeConvierteNinguna()
        {
            EventoInscripcion reservada = Inscripcion(1, CURSO, "15191", EstadoInscripcionEvento.Reservada);
            _ = Inscripcion(2, CURSO, "20000", EstadoInscripcionEvento.Pagada);
            _ = Inscripcion(3, CURSO, "30000", EstadoInscripcionEvento.Facturada);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().ModificarEvento(CURSO, AGratuito(), "Reina"));

            StringAssert.Contains(ex.Message, "Hay 2 inscripciones con dinero o documentos");
            StringAssert.Contains(ex.Message, "1 pagada");
            StringAssert.Contains(ex.Message, "1 facturada");
            Assert.AreEqual("Reservada", reservada.Estado, "Todo o nada");
            Assert.AreEqual(0, modificaciones.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PasoAGratuito_ReservaConFacturaOCuponOSenalOEnlaceCobrado_SeRechaza()
        {
            _ = Inscripcion(1, CURSO, "15191", EstadoInscripcionEvento.Reservada);
            A.CallTo(() => facturacion.Leer(A<IEnumerable<int>>._)).Returns(Task.FromResult(
                new Dictionary<int, FacturacionCursoInscripcion> { [1] = new FacturacionCursoInscripcion { InscripcionId = 1 } }));
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().ModificarEvento(CURSO, AGratuito(), "Reina"));
            StringAssert.Contains(ex.Message, ReglasPasoAGratuitoEvento.MOTIVO_DOCUMENTOS);

            Assert.AreEqual(ReglasPasoAGratuitoEvento.MOTIVO_SENAL, ReglasPasoAGratuitoEvento.MotivoBloqueo(
                new EventoInscripcion { Estado = "Reservada", EventoSenalId = 4 }, false, null, false));
            Assert.AreEqual(ReglasPasoAGratuitoEvento.MOTIVO_COBRO, ReglasPasoAGratuitoEvento.MotivoBloqueo(
                new EventoInscripcion { Estado = "Caducada", ImportePagado = 100 }, false, null, false));
            Assert.AreEqual(ReglasPasoAGratuitoEvento.MOTIVO_ENLACE_COBRADO, ReglasPasoAGratuitoEvento.MotivoBloqueo(
                new EventoInscripcion { Estado = "Reservada", PagoTPVId = 9 }, false,
                new[] { new EnlacePagoInscripcionLeido { Id = 9, Estado = Constantes.EstadosPagoTPV.AUTORIZADO } }, false));
            Assert.AreEqual(ReglasPasoAGratuitoEvento.MOTIVO_ENLACE_COMPARTIDO, ReglasPasoAGratuitoEvento.MotivoBloqueo(
                new EventoInscripcion { Estado = "Reservada", PagoTPVId = 9 }, false, null, true));
            Assert.IsNull(ReglasPasoAGratuitoEvento.MotivoBloqueo(new EventoInscripcion { Estado = "Anulada", ImportePagado = 5 }, true, null, true));
        }

        [TestMethod]
        public async Task PasoAGratuito_CuponDeLaReserva_SeRechaza()
        {
            _ = Inscripcion(1, CURSO, "15191", EstadoInscripcionEvento.Reservada);
            A.CallTo(() => cupones.LeerDeInscripcion(1)).Returns(Task.FromResult(new CuponLeido { Id = 33 }));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().ModificarEvento(CURSO, AGratuito(), "Reina"));

            StringAssert.Contains(ex.Message, ReglasPasoAGratuitoEvento.MOTIVO_DOCUMENTOS);
        }

        [TestMethod]
        public async Task PasoAGratuito_EnlaceQueAcabanDePagar_409YNadaCambia()
        {
            EventoInscripcion a = Inscripcion(1, CURSO, "15191", EstadoInscripcionEvento.Reservada);
            a.PagoTPVId = 700;
            pagosTpv.Add(new PagoTPV { Id = 700, Empresa = "1", Tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL, Estado = Constantes.EstadosPagoTPV.PENDIENTE });
            ServicioEventos servicio = Servicio();
            servicio.AnularEnlacesPago = (empresa, ids) => Task.FromResult(new List<int>());

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarEvento(CURSO, AGratuito(), "Reina"));

            Assert.AreEqual(System.Net.HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("Reservada", a.Estado);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PasoAGratuito_CaducadasQueCaben_SeRecuperan()
        {
            _ = Inscripcion(1, CURSO, "15191", EstadoInscripcionEvento.Reservada);
            EventoInscripcion caducada = Inscripcion(2, CURSO, "20000", EstadoInscripcionEvento.Caducada);
            EventoInscripcion noPodra = Inscripcion(3, CURSO, "30000", EstadoInscripcionEvento.Caducada);
            noPodra.AsistenciaRespuesta = "NoPodraIr";
            EventoInscripcion yaReinscrita = Inscripcion(4, CURSO, "15191", EstadoInscripcionEvento.Caducada);

            EventoDTO r = await Servicio().ModificarEvento(CURSO, AGratuito(), "Reina");

            Assert.AreEqual("Inscrita", caducada.Estado);
            Assert.AreEqual("Gratuita", caducada.Modalidad);
            Assert.AreEqual("Caducada", noPodra.Estado, "Dijo que no podía ir");
            Assert.AreEqual("Caducada", yaReinscrita.Estado, "Ya tiene otra viva");
            Assert.AreEqual(1, r.InscripcionesConvertidasAGratuitas);
            Assert.AreEqual(1, r.InscripcionesRecuperadas);
            Assert.AreEqual(0, r.CaducadasSinRecuperar);
        }

        [TestMethod]
        public async Task PasoAGratuito_CaducadasQueNoCabenTodas_NoSeRecuperaNinguna()
        {
            eventos[0].Plazas = 2;
            _ = Inscripcion(1, CURSO, "15191", EstadoInscripcionEvento.Reservada);
            EventoInscripcion c1 = Inscripcion(2, CURSO, "20000", EstadoInscripcionEvento.Caducada);
            EventoInscripcion c2 = Inscripcion(3, CURSO, "30000", EstadoInscripcionEvento.Caducada);

            EventoDTO r = await Servicio().ModificarEvento(CURSO, AGratuito(), "Reina");

            Assert.AreEqual("Caducada", c1.Estado);
            Assert.AreEqual("Caducada", c2.Estado);
            Assert.AreEqual(1, r.InscripcionesConvertidasAGratuitas, "Las reservadas sí");
            Assert.AreEqual(2, r.CaducadasSinRecuperar);
            StringAssert.Contains(r.MensajePasoAGratuito, "no caben en las plazas libres (1)");
            StringAssert.Contains(r.MensajePasoAGratuito, "vuelva a inscribirlas a mano");
        }

        [TestMethod]
        public async Task PasoAGratuito_SinInscripciones_SoloCambiaElEvento()
        {
            EventoDTO r = await Servicio().ModificarEvento(CURSO, AGratuito(), "Reina");

            Assert.IsTrue(r.Gratuito);
            Assert.AreEqual(0, r.InscripcionesConvertidasAGratuitas);
            Assert.IsNull(r.MensajePasoAGratuito);
        }

        [TestMethod]
        public void PasoAGratuito_TransicionSoloParaEsteCaso()
        {
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasarPorPasoAGratuito(EstadoInscripcionEvento.Reservada));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasarPorPasoAGratuito(EstadoInscripcionEvento.Caducada));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasarPorPasoAGratuito(EstadoInscripcionEvento.Pagada));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Reservada, EstadoInscripcionEvento.Inscrita),
                "La máquina de siempre no lo permite");
        }

        // ---------------- Cobro tardío ----------------

        [TestMethod]
        public async Task CobroTardio_SobreUnaConvertida_NoLaPagaNiFactura()
        {
            EventoInscripcion convertida = Inscripcion(1, GRATIS, "15191", EstadoInscripcionEvento.Inscrita, "Gratuita");
            convertida.PagoTPVId = 700;
            IProcesadorInscripcionPagada procesador = A.Fake<IProcesadorInscripcionPagada>();
            var servicio = new ServicioInscripcionesEventos(db, () => HOY, () => A.Fake<IServicioPagos>(), (e, u) => null, procesador);

            _ = await servicio.RegistrarCobroEnlace(700, 100);

            Assert.AreEqual("Inscrita", convertida.Estado);
            Assert.IsNull(convertida.ImportePagado);
            A.CallTo(() => procesador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        // ---------------- Jobs: se saltan los gratuitos ----------------

        [TestMethod]
        public async Task Jobs_SeSaltanLasReservasDeEventosGratuitos()
        {
            EventoInscripcion dePago = Inscripcion(1, CURSO, "15191", EstadoInscripcionEvento.Reservada);
            dePago.FechaLimitePago = HOY.Date.AddDays(-1);
            EventoInscripcion deGratis = Inscripcion(2, GRATIS, "20000", EstadoInscripcionEvento.Reservada);
            deGratis.FechaLimitePago = HOY.Date.AddDays(-1);
            var servicio = new ServicioInscripcionesEventos(db, () => HOY, () => A.Fake<IServicioPagos>(), (e, u) => null,
                A.Fake<IProcesadorInscripcionPagada>());

            List<AvisoPagoInscripcion> avisos = await servicio.PendientesDeAvisoPago();
            List<InscripcionEventoDTO> caducadas = await servicio.CaducarVencidas();

            Assert.AreEqual(1, avisos.Single().Inscripcion.Id, "Solo la de pago recibe el aviso");
            Assert.AreEqual(1, caducadas.Single().Id);
            Assert.AreEqual("Caducada", dePago.Estado);
            Assert.AreEqual("Reservada", deGratis.Estado);
        }

        [TestMethod]
        public async Task Jobs_LiberarSeSaltaLasDeEventosGratuitos()
        {
            EventoInscripcion dePago = Inscripcion(1, CURSO, "15191", EstadoInscripcionEvento.Reservada);
            dePago.FechaLimitePago = HOY.Date.AddDays(-1);
            dePago.AvisoPagoEnviado = HOY.AddDays(-3);
            EventoInscripcion deGratis = Inscripcion(2, GRATIS, "20000", EstadoInscripcionEvento.Reservada);
            deGratis.FechaLimitePago = HOY.Date.AddDays(-1);
            deGratis.AvisoPagoEnviado = HOY.AddDays(-3);
            var servicio = new ServicioInscripcionesEventos(db, () => HOY, () => A.Fake<IServicioPagos>(), (e, u) => null,
                A.Fake<IProcesadorInscripcionPagada>());

            List<InscripcionEventoDTO> liberadas = await servicio.LiberarReservasVencidas();

            Assert.AreEqual(1, liberadas.Single().Id);
            Assert.AreEqual("Reservada", deGratis.Estado);
        }

        // ---------------- Crear: curso por compra sin señal ----------------

        private ServicioEventos ServicioConRequeridos()
        {
            ServicioEventos servicio = Servicio();
            IRepositorioProductosRequeridosEventos requeridos = A.Fake<IRepositorioProductosRequeridosEventos>();
            A.CallTo(() => requeridos.Guardar(A<int>._, A<IReadOnlyCollection<string>>._, A<string>._)).Returns(Task.FromResult(true));
            A.CallTo(() => requeridos.GuardarModo(A<int>._, A<bool>._)).Returns(Task.FromResult(true));
            A.CallTo(() => requeridos.GuardarImporteMinimo(A<int>._, A<decimal?>._)).Returns(Task.FromResult(true));
            servicio.ProductosRequeridos = requeridos;
            return servicio;
        }

        [TestMethod]
        public async Task Crear_ConProductosYSinImportes_NaceSinSenal()
        {
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Curso por compra\", \"Fecha\": \"2026-12-01\", \"Almacen\": \"REI\", \"ProductosRequeridos\": [ { \"Producto\": \"17404\" } ] }");

            EventoDTO creado = await ServicioConRequeridos().CrearEvento(cuerpo, "Reina");

            Assert.AreEqual(0, creado.ImporteSenal);
            Assert.AreEqual(0, creado.ImporteSenalNoReembolsable);
            Assert.IsTrue(creado.Gratuito);
        }

        [TestMethod]
        public async Task Crear_ConProductosEImportes_SeRespetan()
        {
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Curso por compra\", \"Fecha\": \"2026-12-01\", \"Almacen\": \"REI\", \"ImporteSenal\": 50, \"ImporteSenalNoReembolsable\": 20, \"ProductosRequeridos\": [ { \"Producto\": \"17404\" } ] }");

            EventoDTO creado = await ServicioConRequeridos().CrearEvento(cuerpo, "Reina");

            Assert.AreEqual(50, creado.ImporteSenal);
            Assert.AreEqual(20, creado.ImporteSenalNoReembolsable);
        }

        [TestMethod]
        public async Task Crear_SinProductos_CienYSesentaComoHoy()
        {
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Masterclass\", \"Fecha\": \"2026-12-01\", \"Almacen\": \"REI\", \"ProductosRequeridos\": [] }");

            EventoDTO creado = await ServicioConRequeridos().CrearEvento(cuerpo, "Reina");

            Assert.AreEqual(100, creado.ImporteSenal);
            Assert.AreEqual(60, creado.ImporteSenalNoReembolsable);
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
