using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosCliente;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#612 (cupones, corte 2): la señal confirmada crea el pedido del curso, lo factura exento, liquida la señal y
    /// reserva la plaza. Las tres vías (enlace, señal de #591, pago a mano), importe que no coincide, fallo a medias,
    /// idempotencia, el candado, el interruptor y los permisos.
    /// </summary>
    [TestClass]
    public class FacturacionCursosEventosTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 15, 9, 0, 0);
        private const int EVENTO = 1;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private List<EventoSenal> senales;
        private List<Cliente> clientes;
        private RepositorioFacturacionEnMemoria repositorio;
        private ICreadorPedidoCurso creador;
        private IFacturadorPedidoCurso facturador;
        private ILiquidadorSenalCurso liquidador;
        private IAvisadorFacturacionCurso avisador;
        private IEmisorCuponEvento emisor;
        private bool encendido;
        private int siguienteId;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            siguienteId = 100;
            encendido = true;
            eventos = new List<Evento>
            {
                new Evento { Id = EVENTO, Empresa = "1", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 10, 30), ImporteSenal = 50, Activo = true, Almacen = "REI", ProductoCurso = "90324", Usuario = "Reina", FechaModificacion = HOY }
            };
            inscripciones = new List<EventoInscripcion>();
            senales = new List<EventoSenal>();
            clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "CENTRO DE ESTÉTICA LUNA", ClientePrincipal = true, Vendedor = "JE " }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            DbSet<EventoInscripcion> fakeInscripciones = Crear(inscripciones);
            A.CallTo(() => fakeInscripciones.Add(A<EventoInscripcion>._)).ReturnsLazily((EventoInscripcion i) =>
            {
                i.Id = siguienteId++;
                inscripciones.Add(i);
                return i;
            });
            A.CallTo(() => db.EventosInscripciones).Returns(fakeInscripciones);
            A.CallTo(() => db.EventosSenales).Returns(Crear(senales));
            A.CallTo(() => db.Clientes).Returns(Crear(clientes));
            A.CallTo(() => db.PagosTPV).Returns(Crear(new List<PagoTPV>()));

            repositorio = new RepositorioFacturacionEnMemoria();
            creador = A.Fake<ICreadorPedidoCurso>();
            A.CallTo(() => creador.Crear(A<EventoInscripcion>._, A<Evento>._, A<string>._))
                .Returns(Task.FromResult(new PedidoCursoCreado { Empresa = "1", Pedido = 930001 }));
            facturador = A.Fake<IFacturadorPedidoCurso>();
            A.CallTo(() => facturador.Facturar(A<string>._, A<int>._, A<string>._))
                .Returns(Task.FromResult(new FacturaCursoCreada { EmpresaPedido = "1", EmpresaFactura = "1", Factura = "CV2600700" }));
            liquidador = A.Fake<ILiquidadorSenalCurso>();
            A.CallTo(() => liquidador.Liquidar(A<EventoInscripcion>._, A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new LiquidacionSenalCurso { Estado = EstadosLiquidacionCurso.LIQUIDADA, NumOrdenCobro = 7001, NumOrdenEfecto = 7002 }));
            avisador = A.Fake<IAvisadorFacturacionCurso>();
            emisor = A.Fake<IEmisorCuponEvento>();
        }

        private ConfirmadorSenalEvento Confirmador() =>
            new ConfirmadorSenalEvento(db, () => encendido, repositorio, creador, facturador, liquidador, avisador, emisor, () => HOY, "SenalesEventos");

        private ServicioInscripcionesEventos Servicio() =>
            new ServicioInscripcionesEventos(db, () => HOY, () => A.Fake<IServicioPagos>(), AlmacenDe, Confirmador(), repositorio);

        private static string AlmacenDe(string empresa, string usuario) => usuario == "Reina" ? "REI" : null;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static readonly IPrincipal REINA = Usuario("NUEVAVISION\\Reina", "Tiendas");
        private static readonly IPrincipal ADMINISTRACION = Usuario("NUEVAVISION\\Alfredo", "Administración");
        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Ventas");

        private EventoInscripcion Inscripcion(EstadoInscripcionEvento estado, string formaPago = null, decimal? pagado = null,
            int? pagoTpv = null, int? senal = null, string usuarioPago = null)
        {
            var i = new EventoInscripcion
            {
                Id = siguienteId++,
                EventoId = EVENTO,
                Empresa = "1",
                Cliente = "15191",
                Contacto = "0  ",
                Origen = "NestoApp",
                Estado = estado.ToString(),
                FechaLimitePago = HOY.Date.AddDays(7),
                FormaPago = formaPago,
                ImportePagado = pagado,
                PagoTPVId = pagoTpv,
                EventoSenalId = senal,
                UsuarioPago = usuarioPago,
                UsuarioCreacion = "NUEVAVISION\\Jesus",
                FechaCreacion = HOY.AddDays(-2),
                Usuario = "NUEVAVISION\\Jesus",
                FechaModificacion = HOY.AddDays(-2)
            };
            inscripciones.Add(i);
            return i;
        }

        // ---------------- Interruptor ----------------

        [TestMethod]
        public async Task Apagado_ElEngancheNoHaceNadaYLaInscripcionSeQuedaPagada()
        {
            encendido = false;
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");

            await Confirmador().Procesar(i, eventos.Single());

            Assert.AreEqual("Pagada", i.Estado);
            Assert.AreEqual(0, repositorio.Filas.Count);
            A.CallTo(() => creador.Crear(A<EventoInscripcion>._, A<Evento>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Apagado_RelanzarAMano_400()
        {
            encendido = false;
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Transferencia", 50, usuarioPago: "NUEVAVISION\\Alfredo");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().FacturarCurso(i.Id, ADMINISTRACION));

            StringAssert.Contains(ex.Message, InterruptoresEventos.CLAVE_FACTURAR_CURSO);
        }

        // ---------------- Las tres vías ----------------

        [TestMethod]
        public async Task ViaEnlace_PedidoFacturaLiquidacionYPlaza_ComoSenalesEventos()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");

            await Confirmador().Procesar(i, eventos.Single());

            Assert.AreEqual("Facturada", i.Estado);
            FacturacionCursoInscripcion fila = repositorio.Filas[i.Id];
            Assert.AreEqual(EstadosFacturacionCurso.FACTURADA, fila.Estado);
            Assert.AreEqual(930001, fila.Pedido);
            Assert.AreEqual("CV2600700", fila.Factura);
            Assert.AreEqual(EstadosLiquidacionCurso.LIQUIDADA, fila.EstadoLiquidacion);
            Assert.AreEqual(7001, fila.NumOrdenCobro);
            Assert.AreEqual(7002, fila.NumOrdenEfecto);
            A.CallTo(() => creador.Crear(i, A<Evento>._, "SenalesEventos")).MustHaveHappenedOnceExactly();
            A.CallTo(() => facturador.Facturar("1", 930001, "SenalesEventos")).MustHaveHappenedOnceExactly();
            A.CallTo(() => liquidador.Liquidar(i, "1", "CV2600700", "SenalesEventos")).MustHaveHappenedOnceExactly();
            A.CallTo(() => emisor.Emitir(i, A<Evento>._, A<FacturacionCursoInscripcion>._, "SenalesEventos")).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.IsFalse(repositorio.Reclamadas.Contains(i.Id), "Al acabar se suelta el candado");
        }

        [TestMethod]
        public async Task ViaSenalDeTransferencia_LaInscripcionSePagaYSeFacturaConElUsuarioQueMarcoLaSenal()
        {
            senales.Add(new EventoSenal { Id = 40, EventoId = EVENTO, Empresa = "1", Cliente = "15191", Contacto = "0", NumOrdenExtracto = 8001, Importe = 50, Usuario = "NUEVAVISION\\Alfredo", FechaModificacion = HOY });

            InscripcionEventoDTO dto = await Servicio().InscribirDesdeTransferencia(40, "NUEVAVISION\\Alfredo");

            EventoInscripcion i = inscripciones.Single();
            Assert.AreEqual("Transferencia", i.Origen, "Sin inscripción previa, la crea la señal");
            Assert.AreEqual("Facturada", i.Estado);
            Assert.AreEqual(40, i.EventoSenalId);
            A.CallTo(() => creador.Crear(i, A<Evento>._, "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
            A.CallTo(() => liquidador.Liquidar(i, "1", "CV2600700", "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
            Assert.AreEqual(EstadoInscripcionEvento.Facturada, dto.Estado);
        }

        [TestMethod]
        public async Task ViaPagoAMano_EnEfectivo_FacturaSinCobroQueLiquidar()
        {
            A.CallTo(() => liquidador.Liquidar(A<EventoInscripcion>._, A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new LiquidacionSenalCurso { Estado = EstadosLiquidacionCurso.SIN_COBRO, Mensaje = "Pagado en efectivo: no hay cobro en el extracto que liquidar." }));
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Reservada, "Efectivo");

            await Servicio().MarcarPagada(i.Id, new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" }, REINA);

            Assert.AreEqual("Facturada", i.Estado);
            FacturacionCursoInscripcion fila = repositorio.Filas[i.Id];
            Assert.AreEqual(EstadosLiquidacionCurso.SIN_COBRO, fila.EstadoLiquidacion);
            StringAssert.Contains(fila.Mensaje, "efectivo");
            A.CallTo(() => creador.Crear(i, A<Evento>._, "NUEVAVISION\\Reina")).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinCobroQueDeberiaEstar_AvisaAAdministracionPeroFactura()
        {
            A.CallTo(() => liquidador.Liquidar(A<EventoInscripcion>._, A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new LiquidacionSenalCurso { Estado = EstadosLiquidacionCurso.SIN_COBRO, AvisarAdministracion = true, Mensaje = "No se encuentra el cobro del enlace." }));
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");

            await Confirmador().Procesar(i, eventos.Single());

            Assert.AreEqual("Facturada", i.Estado);
            A.CallTo(() => avisador.Avisar(A<string>.That.Contains("falta liquidar"), A<string>.That.Contains("No se encuentra el cobro del enlace."))).MustHaveHappenedOnceExactly();
        }

        // ---------------- Reglas que paran la automatización ----------------

        [TestMethod]
        public async Task ImporteQueNoCoincide_NoSeAutomatizaYAvisaAAdministracionUnaVez()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Transferencia", 40, usuarioPago: "NUEVAVISION\\Alfredo");

            await Confirmador().Procesar(i, eventos.Single());
            await Confirmador().Procesar(i, eventos.Single());

            Assert.AreEqual("Pagada", i.Estado);
            FacturacionCursoInscripcion fila = repositorio.Filas[i.Id];
            Assert.AreEqual(EstadosFacturacionCurso.NO_AUTOMATIZABLE, fila.Estado);
            StringAssert.Contains(fila.Mensaje, "ha pagado 40,00 € y la señal de «Masterclass Cloasma» es de 50,00 €");
            A.CallTo(() => creador.Crear(A<EventoInscripcion>._, A<Evento>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(A<string>.That.Contains("no se factura solo"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ImporteQueNoCoincide_RelanzarAMano_400ConElMotivo()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Transferencia", 40, usuarioPago: "NUEVAVISION\\Alfredo");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().FacturarCurso(i.Id, ADMINISTRACION));

            StringAssert.Contains(ex.Message, "con importes distintos no se factura solo");
            Assert.IsFalse(repositorio.Reclamadas.Contains(i.Id));
        }

        [TestMethod]
        public async Task EventoSinReferenciaDeCurso_NoSeAutomatiza()
        {
            eventos.Single().ProductoCurso = null;
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Transferencia", 50, usuarioPago: "NUEVAVISION\\Alfredo");

            await Confirmador().Procesar(i, eventos.Single());

            Assert.AreEqual(EstadosFacturacionCurso.NO_AUTOMATIZABLE, repositorio.Filas[i.Id].Estado);
            StringAssert.Contains(repositorio.Filas[i.Id].Mensaje, "no tiene la referencia del curso");
            A.CallTo(() => creador.Crear(A<EventoInscripcion>._, A<Evento>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task InscripcionSinPagar_RelanzarAMano_400()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Reservada, "Transferencia");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().FacturarCurso(i.Id, ADMINISTRACION));

            StringAssert.Contains(ex.Message, "solo se factura el curso de una inscripción pagada");
        }

        // ---------------- Fallo a medias e idempotencia ----------------

        [TestMethod]
        public async Task FalloAlFacturar_SeQuedaPagadaConElPedidoYAlRelanzarNoRepiteElPedido()
        {
            A.CallTo(() => facturador.Facturar(A<string>._, A<int>._, A<string>._))
                .Throws(new InvalidOperationException("Fecha no permitida")).Once()
                .Then.Returns(Task.FromResult(new FacturaCursoCreada { EmpresaPedido = "1", EmpresaFactura = "1", Factura = "CV2600701" }));
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");

            await Confirmador().Procesar(i, eventos.Single());

            Assert.AreEqual("Pagada", i.Estado, "Si algo falla, la inscripción se queda Pagada");
            FacturacionCursoInscripcion fila = repositorio.Filas[i.Id];
            Assert.AreEqual(EstadosFacturacionCurso.ERROR, fila.Estado);
            Assert.AreEqual(1, fila.Intentos);
            Assert.AreEqual("Fecha no permitida", fila.Mensaje);
            Assert.AreEqual(930001, fila.Pedido, "El pedido queda apuntado para no crear otro");
            A.CallTo(() => avisador.Avisar(A<string>.That.Contains("error al facturar"), A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => emisor.Emitir(A<EventoInscripcion>._, A<Evento>._, A<FacturacionCursoInscripcion>._, A<string>._)).MustNotHaveHappened();

            InscripcionEventoDTO dto = await Servicio().FacturarCurso(i.Id, ADMINISTRACION);

            Assert.AreEqual("Facturada", i.Estado);
            Assert.AreEqual(EstadoInscripcionEvento.Facturada, dto.Estado);
            Assert.AreEqual("CV2600701", dto.Facturacion.Factura);
            Assert.AreEqual(EstadosFacturacionCurso.FACTURADA, dto.Facturacion.Estado);
            A.CallTo(() => creador.Crear(A<EventoInscripcion>._, A<Evento>._, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => facturador.Facturar(A<string>._, A<int>._, A<string>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task FalloAlLiquidar_LaFacturaNoSeRepiteYElJobSigueDesdeLaLiquidacion()
        {
            A.CallTo(() => liquidador.Liquidar(A<EventoInscripcion>._, A<string>._, A<string>._, A<string>._))
                .Throws(new InvalidOperationException("Los dos movimientos están ya remesados")).Once()
                .Then.Returns(Task.FromResult(new LiquidacionSenalCurso { Estado = EstadosLiquidacionCurso.LIQUIDADA, NumOrdenCobro = 1, NumOrdenEfecto = 2 }));
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");

            await Confirmador().Procesar(i, eventos.Single());
            Assert.AreEqual(EstadosFacturacionCurso.ERROR, repositorio.Filas[i.Id].Estado);
            Assert.AreEqual("CV2600700", repositorio.Filas[i.Id].Factura, "La factura queda apuntada");

            int relanzadas = await Servicio().ReintentarFacturacionCursos();

            Assert.AreEqual(1, relanzadas);
            Assert.AreEqual("Facturada", i.Estado);
            Assert.AreEqual(EstadosLiquidacionCurso.LIQUIDADA, repositorio.Filas[i.Id].EstadoLiquidacion);
            A.CallTo(() => facturador.Facturar(A<string>._, A<int>._, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => liquidador.Liquidar(A<EventoInscripcion>._, A<string>._, A<string>._, A<string>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task Reintentos_ElJobDejaDeIntentarloAlQuintoFallo()
        {
            A.CallTo(() => facturador.Facturar(A<string>._, A<int>._, A<string>._)).Throws(new InvalidOperationException("No hay líneas para facturar"));
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");
            await Confirmador().Procesar(i, eventos.Single());

            for (int n = 0; n < 6; n++)
            {
                _ = await Servicio().ReintentarFacturacionCursos();
            }

            Assert.AreEqual(ConfirmadorSenalEvento.MAX_INTENTOS_AUTOMATICOS, repositorio.Filas[i.Id].Intentos);
            A.CallTo(() => avisador.Avisar(A<string>.That.Contains("error al facturar"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Idempotente_UnaSenalYaConfirmadaNoVuelveAFacturar()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");
            await Confirmador().Procesar(i, eventos.Single());

            ResultadoConfirmacionSenal otra = await Confirmador().ConfirmarSenal(i, eventos.Single(), aMano: true);

            Assert.IsTrue(otra.Omitido);
            A.CallTo(() => creador.Crear(A<EventoInscripcion>._, A<Evento>._, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => facturador.Facturar(A<string>._, A<int>._, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => liquidador.Liquidar(A<EventoInscripcion>._, A<string>._, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task OtroProcesoLaEstaFacturando_NoHaceNadaYAMano409()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");
            repositorio.Reclamadas.Add(i.Id);

            ResultadoConfirmacionSenal resultado = await Confirmador().ConfirmarSenal(i, eventos.Single(), aMano: false);
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Confirmador().ConfirmarSenal(i, eventos.Single(), aMano: true));

            Assert.IsTrue(resultado.Omitido);
            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            A.CallTo(() => creador.Crear(A<EventoInscripcion>._, A<Evento>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ConListaYaPasada_FacturaPeroNoTocaLaAsistencia()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Asistio, "Transferencia", 50, usuarioPago: "NUEVAVISION\\Alfredo");

            await Confirmador().ConfirmarSenal(i, eventos.Single(), aMano: false);

            Assert.AreEqual("Asistio", i.Estado);
            Assert.AreEqual(EstadosFacturacionCurso.FACTURADA, repositorio.Filas[i.Id].Estado);
        }

        [TestMethod]
        public async Task FalloAlEmitirElCupon_NoDeshaceLaFactura()
        {
            A.CallTo(() => emisor.Emitir(A<EventoInscripcion>._, A<Evento>._, A<FacturacionCursoInscripcion>._, A<string>._))
                .Throws(new InvalidOperationException("Sin tabla de cupones"));
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");

            ResultadoConfirmacionSenal resultado = await Confirmador().ConfirmarSenal(i, eventos.Single(), aMano: true);

            Assert.IsTrue(resultado.Hecho);
            Assert.AreEqual("Facturada", i.Estado);
        }

        // ---------------- Permisos y lectura ----------------

        [TestMethod]
        public async Task FacturarAMano_Vendedor403()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Transferencia", 50, usuarioPago: "NUEVAVISION\\Alfredo");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().FacturarCurso(i.Id, VENDEDOR));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task LaListaDeInscritosEnsenaLaFacturacion()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Tarjeta", 50, pagoTpv: 555, usuarioPago: "NestoAPI");
            await Confirmador().Procesar(i, eventos.Single());

            List<InscripcionEventoDTO> lista = await Servicio().LeerInscripcionesEvento(EVENTO, REINA);

            FacturacionCursoDTO f = lista.Single().Facturacion;
            Assert.AreEqual("Facturada", f.Estado);
            Assert.AreEqual(930001, f.Pedido);
            Assert.AreEqual("CV2600700", f.Factura);
            Assert.AreEqual("Liquidada", f.EstadoLiquidacion);
        }

        [TestMethod]
        public async Task Controlador_PostFacturarCurso_DevuelveLaInscripcion()
        {
            EventoInscripcion i = Inscripcion(EstadoInscripcionEvento.Pagada, "Transferencia", 50, usuarioPago: "NUEVAVISION\\Alfredo");
            var controlador = new EventosController(db, new ServicioEventos(db, () => HOY, AlmacenDe), Servicio())
            {
                Request = new HttpRequestMessage(),
                User = ADMINISTRACION
            };

            var resultado = await controlador.PostFacturarCurso(i.Id) as OkNegotiatedContentResult<InscripcionEventoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(EstadoInscripcionEvento.Facturada, resultado.Content.Estado);
            Assert.AreEqual("CV2600700", resultado.Content.Facturacion.Factura);
        }

        [TestMethod]
        public async Task Controlador_PostSenal_ConElInterruptor_PagaLaInscripcionConLaSenal()
        {
            IServicioEventos eventosServicio = A.Fake<IServicioEventos>();
            A.CallTo(() => eventosServicio.MarcarSenal(EVENTO, A<MarcarSenalEventoDTO>._, A<string>._))
                .Returns(Task.FromResult(new SenalEventoDTO { Id = 40, Cliente = "15191" }));
            IServicioInscripcionesEventos servicioInscripciones = A.Fake<IServicioInscripcionesEventos>();
            var controlador = new EventosController(db, eventosServicio, servicioInscripciones)
            {
                Request = new HttpRequestMessage(),
                User = ADMINISTRACION,
                FacturarCursoAlCobrar = () => true
            };

            var resultado = await controlador.PostSenal(EVENTO, new MarcarSenalEventoDTO { NumOrdenExtracto = 8001 }) as OkNegotiatedContentResult<SenalEventoDTO>;

            Assert.AreEqual(40, resultado.Content.Id);
            A.CallTo(() => servicioInscripciones.InscribirDesdeTransferencia(40, "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controlador_PostSenal_SinElInterruptor_ComoEn591()
        {
            IServicioEventos eventosServicio = A.Fake<IServicioEventos>();
            A.CallTo(() => eventosServicio.MarcarSenal(EVENTO, A<MarcarSenalEventoDTO>._, A<string>._))
                .Returns(Task.FromResult(new SenalEventoDTO { Id = 40, Cliente = "15191" }));
            IServicioInscripcionesEventos servicioInscripciones = A.Fake<IServicioInscripcionesEventos>();
            var controlador = new EventosController(db, eventosServicio, servicioInscripciones)
            {
                Request = new HttpRequestMessage(),
                User = ADMINISTRACION,
                FacturarCursoAlCobrar = () => false
            };

            _ = await controlador.PostSenal(EVENTO, new MarcarSenalEventoDTO { NumOrdenExtracto = 8001 });

            A.CallTo(() => servicioInscripciones.InscribirDesdeTransferencia(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_PostSenal_SiFallaLaInscripcion_LaSenalQuedaMarcada()
        {
            IServicioEventos eventosServicio = A.Fake<IServicioEventos>();
            A.CallTo(() => eventosServicio.MarcarSenal(EVENTO, A<MarcarSenalEventoDTO>._, A<string>._))
                .Returns(Task.FromResult(new SenalEventoDTO { Id = 40, Cliente = "15191" }));
            IServicioInscripcionesEventos servicioInscripciones = A.Fake<IServicioInscripcionesEventos>();
            A.CallTo(() => servicioInscripciones.InscribirDesdeTransferencia(A<int>._, A<string>._)).Throws(new InvalidOperationException("boom"));
            var controlador = new EventosController(db, eventosServicio, servicioInscripciones)
            {
                Request = new HttpRequestMessage(),
                User = ADMINISTRACION,
                FacturarCursoAlCobrar = () => true
            };

            var resultado = await controlador.PostSenal(EVENTO, new MarcarSenalEventoDTO { NumOrdenExtracto = 8001 }) as OkNegotiatedContentResult<SenalEventoDTO>;

            Assert.IsNotNull(resultado);
        }

        // ---------------- Reglas ----------------

        [TestMethod]
        public void Reglas_FormaDePagoDelPedido()
        {
            Assert.AreEqual("TAR", ReglasFacturacionCurso.FormaPagoPedido("Tarjeta"));
            Assert.AreEqual("EFC", ReglasFacturacionCurso.FormaPagoPedido("Efectivo"));
            Assert.AreEqual("TRN", ReglasFacturacionCurso.FormaPagoPedido("Transferencia"));
            Assert.AreEqual("TRN", ReglasFacturacionCurso.FormaPagoPedido(null));
        }

        [TestMethod]
        public void Reglas_UsuarioDeAuditoria_ElQueMarcoOElAutomatico()
        {
            Assert.AreEqual("NUEVAVISION\\Alfredo", ReglasFacturacionCurso.UsuarioAuditoria(new EventoInscripcion { UsuarioPago = "NUEVAVISION\\Alfredo" }, "SenalesEventos"));
            Assert.AreEqual("SenalesEventos", ReglasFacturacionCurso.UsuarioAuditoria(new EventoInscripcion { UsuarioPago = "NestoAPI" }, "SenalesEventos"));
            Assert.AreEqual("SenalesEventos", ReglasFacturacionCurso.UsuarioAuditoria(new EventoInscripcion(), "SenalesEventos"));
        }

        [TestMethod]
        public void Reglas_ImporteIgualALaSenal_SeAutomatiza()
        {
            Assert.IsNull(ReglasFacturacionCurso.MotivoNoAutomatizable(new EventoInscripcion { ImportePagado = 50.00M }, eventos.Single()));
            Assert.IsNotNull(ReglasFacturacionCurso.MotivoNoAutomatizable(new EventoInscripcion { ImportePagado = 50.01M }, eventos.Single()));
            Assert.IsNotNull(ReglasFacturacionCurso.MotivoNoAutomatizable(new EventoInscripcion { ImportePagado = null }, eventos.Single()));
        }

        // ---------------- Liquidador (con la BD de mentira) ----------------

        [TestMethod]
        public async Task Liquidador_EnlaceCobrado_LiquidaElCobroINSContraElEfectoDeLaFactura()
        {
            var extracto = new List<ExtractoCliente>
            {
                new ExtractoCliente { Empresa = "1", Nº_Orden = 9001, Número = "15191", Contacto = "0", TipoApunte = "3", Nº_Documento = "INS101", Importe = -50, ImportePdte = -50 },
                new ExtractoCliente { Empresa = "1", Nº_Orden = 9002, Número = "15191", Contacto = "0", TipoApunte = "2", Nº_Documento = "CV2600700", Importe = 50, ImportePdte = 50 }
            };
            A.CallTo(() => db.ExtractosCliente).Returns(Crear(extracto));
            IServicioLiquidarEfectos liquidar = A.Fake<IServicioLiquidarEfectos>();
            A.CallTo(() => liquidar.Liquidar("1", 9001, 9002, A<string>._)).Returns(Task.FromResult(new ResultadoLiquidacionEfectos { Exito = true }));
            var i = new EventoInscripcion { Id = 101, Empresa = "1  ", Cliente = "15191", Contacto = "0", PagoTPVId = 555, FormaPago = "Tarjeta" };

            LiquidacionSenalCurso r = await new LiquidadorSenalCurso(db, liquidar).Liquidar(i, "1", "CV2600700", "SenalesEventos");

            Assert.AreEqual(EstadosLiquidacionCurso.LIQUIDADA, r.Estado);
            Assert.AreEqual(9001, r.NumOrdenCobro);
            Assert.AreEqual(9002, r.NumOrdenEfecto);
        }

        [TestMethod]
        public async Task Liquidador_SenalDe591_LiquidaSuApunte()
        {
            senales.Add(new EventoSenal { Id = 40, EventoId = EVENTO, Empresa = "1", Cliente = "15191", Contacto = "0", NumOrdenExtracto = 8001, Importe = 50 });
            var extracto = new List<ExtractoCliente>
            {
                new ExtractoCliente { Empresa = "1", Nº_Orden = 8001, Número = "15191", Contacto = "0", TipoApunte = "3", Nº_Documento = "TRANSF", Importe = -50, ImportePdte = -50 },
                new ExtractoCliente { Empresa = "1", Nº_Orden = 9002, Número = "15191", Contacto = "0", TipoApunte = "2", Nº_Documento = "CV2600700", Importe = 50, ImportePdte = 50 }
            };
            A.CallTo(() => db.ExtractosCliente).Returns(Crear(extracto));
            IServicioLiquidarEfectos liquidar = A.Fake<IServicioLiquidarEfectos>();
            A.CallTo(() => liquidar.Liquidar("1", 8001, 9002, "NUEVAVISION\\Alfredo")).Returns(Task.FromResult(new ResultadoLiquidacionEfectos { Exito = true }));
            var i = new EventoInscripcion { Id = 101, Empresa = "1", Cliente = "15191", Contacto = "0", EventoSenalId = 40, FormaPago = "Transferencia" };

            LiquidacionSenalCurso r = await new LiquidadorSenalCurso(db, liquidar).Liquidar(i, "1", "CV2600700", "NUEVAVISION\\Alfredo");

            Assert.AreEqual(EstadosLiquidacionCurso.LIQUIDADA, r.Estado);
            A.CallTo(() => liquidar.Liquidar("1", 8001, 9002, "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Liquidador_EnEfectivo_SinCobroYSinAvisar()
        {
            A.CallTo(() => db.ExtractosCliente).Returns(Crear(new List<ExtractoCliente>()));
            IServicioLiquidarEfectos liquidar = A.Fake<IServicioLiquidarEfectos>();
            var i = new EventoInscripcion { Id = 101, Empresa = "1", Cliente = "15191", Contacto = "0", FormaPago = "Efectivo" };

            LiquidacionSenalCurso r = await new LiquidadorSenalCurso(db, liquidar).Liquidar(i, "1", "CV2600700", "NUEVAVISION\\Reina");

            Assert.AreEqual(EstadosLiquidacionCurso.SIN_COBRO, r.Estado);
            Assert.IsFalse(r.AvisarAdministracion);
            StringAssert.Contains(r.Mensaje, "en efectivo");
            A.CallTo(() => liquidar.Liquidar(A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Liquidador_EnlaceSinCobroEnElExtracto_AvisaAAdministracion()
        {
            A.CallTo(() => db.ExtractosCliente).Returns(Crear(new List<ExtractoCliente>()));
            var i = new EventoInscripcion { Id = 101, Empresa = "1", Cliente = "15191", Contacto = "0", PagoTPVId = 555, FormaPago = "Tarjeta" };

            LiquidacionSenalCurso r = await new LiquidadorSenalCurso(db, A.Fake<IServicioLiquidarEfectos>()).Liquidar(i, "1", "CV2600700", "x");

            Assert.AreEqual(EstadosLiquidacionCurso.SIN_COBRO, r.Estado);
            Assert.IsTrue(r.AvisarAdministracion);
        }

        [TestMethod]
        public async Task Liquidador_FacturaEnLaEspejo_NoLiquidaYAvisa()
        {
            var extracto = new List<ExtractoCliente>
            {
                new ExtractoCliente { Empresa = "1", Nº_Orden = 9001, Número = "15191", Contacto = "0", TipoApunte = "3", Nº_Documento = "INS101", Importe = -50, ImportePdte = -50 }
            };
            A.CallTo(() => db.ExtractosCliente).Returns(Crear(extracto));
            IServicioLiquidarEfectos liquidar = A.Fake<IServicioLiquidarEfectos>();
            var i = new EventoInscripcion { Id = 101, Empresa = "1", Cliente = "15191", Contacto = "0", PagoTPVId = 555, FormaPago = "Tarjeta" };

            LiquidacionSenalCurso r = await new LiquidadorSenalCurso(db, liquidar).Liquidar(i, "3", "GB2600001", "x");

            Assert.AreEqual(EstadosLiquidacionCurso.SIN_COBRO, r.Estado);
            Assert.IsTrue(r.AvisarAdministracion);
            A.CallTo(() => liquidar.Liquidar(A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Liquidador_SiElSPNoLiquida_Lanza()
        {
            var extracto = new List<ExtractoCliente>
            {
                new ExtractoCliente { Empresa = "1", Nº_Orden = 9001, Número = "15191", Contacto = "0", TipoApunte = "3", Nº_Documento = "INS101", Importe = -50, ImportePdte = -50 },
                new ExtractoCliente { Empresa = "1", Nº_Orden = 9002, Número = "15191", Contacto = "0", TipoApunte = "2", Nº_Documento = "CV2600700", Importe = 50, ImportePdte = 50 }
            };
            A.CallTo(() => db.ExtractosCliente).Returns(Crear(extracto));
            IServicioLiquidarEfectos liquidar = A.Fake<IServicioLiquidarEfectos>();
            var fallo = new ResultadoLiquidacionEfectos { Exito = false };
            fallo.Errores.Add("Ambos movimientos están ya remesados.");
            A.CallTo(() => liquidar.Liquidar(A<string>._, A<int>._, A<int>._, A<string>._)).Returns(Task.FromResult(fallo));
            var i = new EventoInscripcion { Id = 101, Empresa = "1", Cliente = "15191", Contacto = "0", PagoTPVId = 555, FormaPago = "Tarjeta" };

            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new LiquidadorSenalCurso(db, liquidar).Liquidar(i, "1", "CV2600700", "x"));

            StringAssert.Contains(ex.Message, "remesados");
        }

        // ---------------- Facturador (con la BD de mentira) ----------------

        [TestMethod]
        public async Task Facturador_SinAlbaran_AlbaranYFactura()
        {
            NVEntities otro = A.Fake<NVEntities>();
            A.CallTo(() => otro.LinPedidoVtas).Returns(Crear(new List<LinPedidoVta> { new LinPedidoVta { Empresa = "1  ", Número = 930001, Estado = 1 } }));
            IServicioAlbaranesVenta albaranes = A.Fake<IServicioAlbaranesVenta>();
            IServicioFacturas facturas = A.Fake<IServicioFacturas>();
            A.CallTo(() => facturas.CrearFactura("1", 930001, "SenalesEventos", "SenalesEventos"))
                .Returns(Task.FromResult(new CrearFacturaResponseDTO { Empresa = "1", NumeroFactura = "CV2600700", NumeroPedido = 930001 }));

            FacturaCursoCreada f = await new FacturadorPedidoCurso(() => otro, () => albaranes, () => facturas).Facturar("1", 930001, "SenalesEventos");

            Assert.AreEqual("CV2600700", f.Factura);
            A.CallTo(() => albaranes.CrearAlbaran("1", 930001, "SenalesEventos", A<DateTime?>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Facturador_YaFacturadoAMano_DevuelveEsaFacturaSinTocarNada()
        {
            NVEntities otro = A.Fake<NVEntities>();
            A.CallTo(() => otro.LinPedidoVtas).Returns(Crear(new List<LinPedidoVta> { new LinPedidoVta { Empresa = "1  ", Número = 930001, Estado = 4, Nº_Factura = "CV2600650 " } }));
            IServicioAlbaranesVenta albaranes = A.Fake<IServicioAlbaranesVenta>();
            IServicioFacturas facturas = A.Fake<IServicioFacturas>();

            FacturaCursoCreada f = await new FacturadorPedidoCurso(() => otro, () => albaranes, () => facturas).Facturar("1", 930001, "x");

            Assert.AreEqual("CV2600650", f.Factura);
            A.CallTo(() => facturas.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => albaranes.CrearAlbaran(A<string>._, A<int>._, A<string>._, A<DateTime?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Facturador_ConAlbaran_SoloFactura()
        {
            NVEntities otro = A.Fake<NVEntities>();
            A.CallTo(() => otro.LinPedidoVtas).Returns(Crear(new List<LinPedidoVta> { new LinPedidoVta { Empresa = "1", Número = 930001, Estado = 2 } }));
            IServicioAlbaranesVenta albaranes = A.Fake<IServicioAlbaranesVenta>();
            IServicioFacturas facturas = A.Fake<IServicioFacturas>();
            A.CallTo(() => facturas.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new CrearFacturaResponseDTO { Empresa = "1", NumeroFactura = "CV2600702" }));

            FacturaCursoCreada f = await new FacturadorPedidoCurso(() => otro, () => albaranes, () => facturas).Facturar("1", 930001, "x");

            Assert.AreEqual("CV2600702", f.Factura);
            A.CallTo(() => albaranes.CrearAlbaran(A<string>._, A<int>._, A<string>._, A<DateTime?>._)).MustNotHaveHappened();
        }

        // ---------------- Job ----------------

        [TestMethod]
        public async Task Job_RelanzaLaFacturacionDeLosCursos()
        {
            bool relanzado = false;
            _ = await InscripcionesEventosJobsService.Procesar(new DependenciasInscripcionesEventos
            {
                CaducarVencidas = () => Task.FromResult(new List<InscripcionEventoDTO>()),
                DarPorAsistidosSinLista = () => Task.FromResult(new List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>>()),
                CorreosTienda = (e, a) => Task.FromResult(new List<string>()),
                Avisar = (u, n) => Task.CompletedTask,
                Correo = A.Fake<IServicioCorreoElectronico>(),
                ReintentarFacturacionCursos = () => { relanzado = true; return Task.FromResult(1); }
            });

            Assert.IsTrue(relanzado);
        }

        // ---------------- Ayudantes ----------------

        /// <summary>EventosInscripcionesCurso en memoria (devuelve copias, como la BD).</summary>
        private class RepositorioFacturacionEnMemoria : IRepositorioFacturacionCursos
        {
            public Dictionary<int, FacturacionCursoInscripcion> Filas { get; } = new Dictionary<int, FacturacionCursoInscripcion>();
            public HashSet<int> Reclamadas { get; } = new HashSet<int>();

            public Task<FacturacionCursoInscripcion> Leer(int inscripcionId) =>
                Task.FromResult(Filas.TryGetValue(inscripcionId, out FacturacionCursoInscripcion f) ? Copia(f) : null);

            public Task<Dictionary<int, FacturacionCursoInscripcion>> Leer(IEnumerable<int> inscripciones) =>
                Task.FromResult(inscripciones.Where(Filas.ContainsKey).ToDictionary(i => i, i => Copia(Filas[i])));

            public Task<bool> Reclamar(int inscripcionId, string empresa, string usuario)
            {
                if (!Filas.ContainsKey(inscripcionId))
                {
                    Filas[inscripcionId] = new FacturacionCursoInscripcion { InscripcionId = inscripcionId, EmpresaPedido = empresa, Estado = EstadosFacturacionCurso.EN_CURSO, Usuario = usuario };
                }
                return Task.FromResult(Reclamadas.Add(inscripcionId));
            }

            public Task Soltar(int inscripcionId)
            {
                _ = Reclamadas.Remove(inscripcionId);
                return Task.CompletedTask;
            }

            public Task Guardar(FacturacionCursoInscripcion fila)
            {
                Filas[fila.InscripcionId] = Copia(fila);
                return Task.CompletedTask;
            }

            public Task<List<int>> ConError(int maxIntentos) =>
                Task.FromResult(Filas.Values.Where(f => f.Estado == EstadosFacturacionCurso.ERROR && f.Intentos < maxIntentos).Select(f => f.InscripcionId).ToList());

            public Task<List<int>> FacturadasSinCupon() => Task.FromResult(new List<int>());

            private static FacturacionCursoInscripcion Copia(FacturacionCursoInscripcion f) => (FacturacionCursoInscripcion)f.GetType()
                .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(f, null);
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
