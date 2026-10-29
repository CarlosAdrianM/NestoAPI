using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Pagos;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#732 (sugerencia 863, Paloma; Carlos 29/10/26): eventos gratuitos (las dos señales a 0) y «gratuitos por compra»
    /// (hay que haber comprado o pedido el producto; el pedido justifica la inscripción y, si se borra o se le quita la línea,
    /// se avisa y, confirmado, se anula).
    /// </summary>
    [TestClass]
    public class ProductosRequeridosEventosTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 29, 9, 0, 0);
        private const int EVENTO_GRATUITO = 1;
        private const int EVENTO_POR_COMPRA = 4;
        private const int EVENTO_DE_PAGO = 5;
        private const int PEDIDO = 930001;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private List<Modificacion> modificaciones;
        private IRepositorioProductosRequeridosEventos repositorio;
        private IServicioPagos pagos;
        private int siguienteId;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            siguienteId = 100;
            eventos = new List<Evento>
            {
                new Evento { Id = EVENTO_GRATUITO, Empresa = "1", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 11, 20), ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "REI", Usuario = "Reina", FechaModificacion = HOY },
                new Evento { Id = EVENTO_POR_COMPRA, Empresa = "1", Titulo = "Formación por compra Lifting Coreano", Fecha = new DateTime(2026, 11, 25), ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "ALC", Usuario = "Paloma", FechaModificacion = HOY },
                new Evento { Id = EVENTO_DE_PAGO, Empresa = "1", Titulo = "Curso Peeling", Fecha = new DateTime(2026, 11, 26), ImporteSenal = 100, ImporteSenalNoReembolsable = 60, Activo = true, Almacen = "ALC", Usuario = "Paloma", FechaModificacion = HOY }
            };
            inscripciones = new List<EventoInscripcion>();
            modificaciones = new List<Modificacion>();
            var clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0", Nombre = "CENTRO DE ESTÉTICA LUNA", ClientePrincipal = true, Vendedor = "JE", Teléfono = "915555555" },
                new Cliente { Empresa = "1", Nº_Cliente = "20000", Contacto = "0", Nombre = "OTRA", ClientePrincipal = true, Vendedor = "NV" }
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
            A.CallTo(() => db.Clientes).Returns(Crear(clientes));
            A.CallTo(() => db.PagosTPV).Returns(Crear(new List<PagoTPV>()));
            A.CallTo(() => db.PersonasContactoClientes).Returns(Crear(new List<PersonaContactoCliente>()));
            DbSet<Modificacion> fakeModificaciones = Crear(modificaciones);
            A.CallTo(() => fakeModificaciones.Add(A<Modificacion>._)).ReturnsLazily((Modificacion m) => { modificaciones.Add(m); return m; });
            A.CallTo(() => db.Modificaciones).Returns(fakeModificaciones);

            repositorio = A.Fake<IRepositorioProductosRequeridosEventos>();
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>._)).ReturnsLazily((IReadOnlyCollection<int> ids) =>
                Task.FromResult(ids.Contains(EVENTO_POR_COMPRA)
                    ? new Dictionary<int, List<ProductoRequeridoLeido>>
                    {
                        [EVENTO_POR_COMPRA] = new List<ProductoRequeridoLeido>
                        {
                            new ProductoRequeridoLeido { EventoId = EVENTO_POR_COMPRA, Producto = "45363", Nombre = "LIFTING COREANO KIT", FechaCreacion = new DateTime(2026, 10, 20, 12, 0, 0) }
                        }
                    }
                    : new Dictionary<int, List<ProductoRequeridoLeido>>()));
            A.CallTo(() => repositorio.PonerJustificante(A<int>._, A<int>._)).Returns(Task.FromResult(true));
            A.CallTo(() => repositorio.LeerJustificantes(A<IReadOnlyCollection<int>>._)).Returns(Task.FromResult(new Dictionary<int, int>()));

            pagos = A.Fake<IServicioPagos>();
        }

        private ServicioInscripcionesEventos Servicio()
        {
            var servicio = new ServicioInscripcionesEventos(db, () => HOY, () => pagos, (e, u) => null, A.Fake<IProcesadorInscripcionPagada>());
            servicio.ProductosRequeridos = repositorio;
            return servicio;
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Ventas");
        private static readonly IPrincipal CLIENTE_TIENDA =
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", "15191") }, "prueba"));

        // ---------------- Gratuitas sin más ----------------

        [TestMethod]
        public async Task Gratuita_ElVendedorDesdeNestoApp_InscritaSinPagoNiJustificante()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_GRATUITO, new InscribirEventoDTO { Cliente = "15191", Origen = "NestoApp" }, VENDEDOR);

            Assert.AreEqual(EstadoInscripcionEvento.Inscrita, dto.Estado);
            Assert.AreEqual(ModalidadInscripcionEvento.Gratuita, dto.Modalidad);
            Assert.AreEqual(OrigenInscripcionEvento.NestoApp, dto.Origen);
            Assert.IsNull(dto.UrlPago);
            Assert.IsNull(dto.PedidoJustificante);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.BuscarJustificante(A<string>._, A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Gratuita_LaClientaDesdeLaTienda_SeInscribeASiMisma()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_GRATUITO, new InscribirEventoDTO { Cliente = "15191" }, CLIENTE_TIENDA);

            Assert.AreEqual(EstadoInscripcionEvento.Inscrita, dto.Estado);
            Assert.AreEqual(OrigenInscripcionEvento.TNV, dto.Origen);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Gratuita_LaClientaDesdeLaTienda_NoInscribeAOtro()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Inscribir(EVENTO_GRATUITO, new InscribirEventoDTO { Cliente = "20000" }, CLIENTE_TIENDA));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        // ---------------- Gratuitas por compra ----------------

        [TestMethod]
        public async Task PorCompra_ConElProductoComprado_InscritaConSuPedido()
        {
            A.CallTo(() => repositorio.BuscarJustificante("1", "15191", A<IReadOnlyCollection<string>>._, A<DateTime>._))
                .Returns(Task.FromResult(new JustificanteCompraEvento { Pedido = PEDIDO, Producto = "45363", Estado = Constantes.EstadosLineaVenta.FACTURA }));

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_POR_COMPRA, new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR);

            Assert.AreEqual(EstadoInscripcionEvento.Inscrita, dto.Estado);
            Assert.AreEqual(PEDIDO, dto.PedidoJustificante);
            A.CallTo(() => repositorio.PonerJustificante(dto.Id, PEDIDO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PorCompra_SoloPedido_TambienVale()
        {
            A.CallTo(() => repositorio.BuscarJustificante(A<string>._, A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._))
                .Returns(Task.FromResult(new JustificanteCompraEvento { Pedido = PEDIDO, Producto = "45363", Estado = Constantes.EstadosLineaVenta.PENDIENTE }));

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_POR_COMPRA, new InscribirEventoDTO { Cliente = "15191" }, CLIENTE_TIENDA);

            Assert.AreEqual(EstadoInscripcionEvento.Inscrita, dto.Estado);
            Assert.AreEqual(PEDIDO, dto.PedidoJustificante);
        }

        [TestMethod]
        public async Task PorCompra_BuscaElProductoDesde60DiasAntesDelRequisito()
        {
            A.CallTo(() => repositorio.BuscarJustificante(A<string>._, A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._))
                .Returns(Task.FromResult(new JustificanteCompraEvento { Pedido = PEDIDO, Producto = "45363", Estado = 2 }));

            _ = await Servicio().Inscribir(EVENTO_POR_COMPRA, new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR);

            A.CallTo(() => repositorio.BuscarJustificante("1", "15191",
                A<IReadOnlyCollection<string>>.That.Matches(p => p.Count == 1 && p.Contains("45363")),
                new DateTime(2026, 8, 21))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PorCompra_SinElProducto_400ClaroYNoSeInscribe()
        {
            A.CallTo(() => repositorio.BuscarJustificante(A<string>._, A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._))
                .Returns(Task.FromResult<JustificanteCompraEvento>(null));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Inscribir(EVENTO_POR_COMPRA, new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.AreEqual(ReglasProductosRequeridosEvento.ERROR_REQUIERE_COMPRA, ex.GetErrorCode());
            StringAssert.StartsWith(ex.Message, "Para apuntarse a «Formación por compra Lifting Coreano» hay que haber comprado o pedido 45363 (LIFTING COREANO KIT), desde el 21/08/2026.");
            Assert.AreEqual(0, inscripciones.Count);
        }

        [TestMethod]
        public async Task PorCompra_SinLaTabla_ComoHoy()
        {
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>._)).Returns(Task.FromResult(new Dictionary<int, List<ProductoRequeridoLeido>>()));

            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_POR_COMPRA, new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto" }, VENDEDOR);

            Assert.AreEqual(EstadoInscripcionEvento.Inscrita, dto.Estado);
            Assert.IsNull(dto.PedidoJustificante);
        }

        [TestMethod]
        public void Reglas_MensajeConVariosProductos()
        {
            var requeridos = new List<ProductoRequeridoLeido>
            {
                new ProductoRequeridoLeido { Producto = "45364", Nombre = "KIT GRANDE" },
                new ProductoRequeridoLeido { Producto = "45363", Nombre = null }
            };

            Assert.AreEqual("alguno de estos productos: 45363, 45364 (KIT GRANDE)", ReglasProductosRequeridosEvento.Lista(requeridos));
            CollectionAssert.AreEqual(new[] { "45363", "X" }, ReglasProductosRequeridosEvento.Normalizar(new[]
            {
                new ProductoRequeridoEventoDTO { Producto = " 45363 " }, new ProductoRequeridoEventoDTO { Producto = "45363" },
                new ProductoRequeridoEventoDTO { Producto = "" }, null, new ProductoRequeridoEventoDTO { Producto = "X" }
            }).ToArray());
        }

        // ---------------- Al borrar o tocar el pedido ----------------

        private EventoInscripcion Inscripcion(int id, int eventoId, EstadoInscripcionEvento estado, string origen = "Nesto")
        {
            var i = new EventoInscripcion
            {
                Id = id, EventoId = eventoId, Empresa = "1", Cliente = "15191", Contacto = "0", Origen = origen,
                Estado = estado.ToString(), Modalidad = "Gratuita", UsuarioCreacion = "NUEVAVISION\\Jesus",
                FechaCreacion = HOY.AddDays(-2), Usuario = "NUEVAVISION\\Jesus", FechaModificacion = HOY.AddDays(-2)
            };
            inscripciones.Add(i);
            return i;
        }

        private IAvisadorInscripcionAnuladaPorPedido avisador;

        private ServicioInscripcionesPorCompraEventos Guardian()
        {
            avisador = A.Fake<IAvisadorInscripcionAnuladaPorPedido>();
            return new ServicioInscripcionesPorCompraEventos(db, repositorio, () => HOY, avisador);
        }

        [TestMethod]
        public async Task AlBorrar_SoloLasVivasDeEventosQueNoHanPasado()
        {
            Inscripcion(1, EVENTO_POR_COMPRA, EstadoInscripcionEvento.Inscrita);
            Inscripcion(2, EVENTO_POR_COMPRA, EstadoInscripcionEvento.Anulada);
            Inscripcion(3, EVENTO_POR_COMPRA, EstadoInscripcionEvento.Asistio);
            eventos.Add(new Evento { Id = 9, Empresa = "1", Titulo = "Pasado", Fecha = HOY.AddDays(-1), Activo = true, Almacen = "ALC", Usuario = "x", FechaModificacion = HOY });
            Inscripcion(4, 9, EstadoInscripcionEvento.Inscrita);
            A.CallTo(() => repositorio.InscripcionesDelPedido("1", PEDIDO)).Returns(Task.FromResult(new List<int> { 1, 2, 3, 4 }));

            List<InscripcionAfectadaPedidoDTO> afectadas = await Guardian().AfectadasAlBorrar("1", PEDIDO);

            Assert.AreEqual(1, afectadas.Single().Id);
            Assert.AreEqual("Formación por compra Lifting Coreano", afectadas.Single().Evento);
            Assert.AreEqual("CENTRO DE ESTÉTICA LUNA", afectadas.Single().Nombre);
        }

        [TestMethod]
        public async Task AlBorrar_UnPedidoQueNoJustificaNada_NingunaYSinMirarMas()
        {
            A.CallTo(() => repositorio.InscripcionesDelPedido(A<string>._, A<int>._)).Returns(Task.FromResult(new List<int>()));

            List<InscripcionAfectadaPedidoDTO> afectadas = await Guardian().AfectadasAlBorrar("1", PEDIDO);

            Assert.AreEqual(0, afectadas.Count);
            A.CallTo(() => db.EventosInscripciones).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AlQuitarLaLinea_SinElProducto_Afectada_ConElProducto_No()
        {
            Inscripcion(1, EVENTO_POR_COMPRA, EstadoInscripcionEvento.Inscrita);
            A.CallTo(() => repositorio.InscripcionesDelPedido("1", PEDIDO)).Returns(Task.FromResult(new List<int> { 1 }));

            Assert.AreEqual(0, (await Guardian().AfectadasAlModificar("1", PEDIDO, new[] { "OTRO", "45363 " })).Count);
            Assert.AreEqual(1, (await Guardian().AfectadasAlModificar("1", PEDIDO, new[] { "OTRO" })).Count);
        }

        [TestMethod]
        public void ProductosQueQuedan_LineasConCantidadYLasProtegidasQueNoVienen()
        {
            var pedido = new PedidoVentaDTO
            {
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO { id = 1, Producto = "45363", Cantidad = 0 },
                    new LineaPedidoVentaDTO { id = 0, Producto = "NUEVO", Cantidad = 2 }
                }
            };
            var grabadas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 1, Producto = "45363", Cantidad = 1, Estado = -1 },
                new LinPedidoVta { Nº_Orden = 2, Producto = "FACTURADO", Cantidad = 1, Estado = 4 },
                new LinPedidoVta { Nº_Orden = 3, Producto = "BORRADA", Cantidad = 1, Estado = -1 }
            };

            List<string> quedan = PedidosVentaController.ProductosQueQuedanTrasElPut(pedido, grabadas);

            CollectionAssert.AreEquivalent(new[] { "NUEVO", "FACTURADO" }, quedan);
        }

        [TestMethod]
        public async Task Anular_PasaAAnuladaConTrazaYAvisaALaTienda()
        {
            Inscripcion(1, EVENTO_POR_COMPRA, EstadoInscripcionEvento.Inscrita);
            A.CallTo(() => repositorio.InscripcionesDelPedido("1", PEDIDO)).Returns(Task.FromResult(new List<int> { 1 }));
            ServicioInscripcionesPorCompraEventos guardian = Guardian();
            List<InscripcionAfectadaPedidoDTO> afectadas = await guardian.AfectadasAlBorrar("1", PEDIDO);

            List<InscripcionAfectadaPedidoDTO> anuladas = await guardian.Anular(afectadas, PEDIDO, borrarPedido: true, "NUEVAVISION\\Laura");

            Assert.AreEqual(1, anuladas.Count);
            Assert.AreEqual(EstadoInscripcionEvento.Anulada.ToString(), inscripciones.Single().Estado);
            Assert.AreEqual("EventosInscripciones", modificaciones.Single().Tabla);
            StringAssert.Contains(modificaciones.Single().Nuevo, $"se ha borrado el pedido {PEDIDO}");
            A.CallTo(() => avisador.Avisar(A<InscripcionAfectadaPedidoDTO>.That.Matches(a => a.Id == 1), PEDIDO, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Anular_SiFallaElAviso_QuedaAnuladaIgual()
        {
            Inscripcion(1, EVENTO_POR_COMPRA, EstadoInscripcionEvento.Inscrita);
            A.CallTo(() => repositorio.InscripcionesDelPedido("1", PEDIDO)).Returns(Task.FromResult(new List<int> { 1 }));
            ServicioInscripcionesPorCompraEventos guardian = Guardian();
            A.CallTo(() => avisador.Avisar(A<InscripcionAfectadaPedidoDTO>._, A<int>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("SMTP"));

            List<InscripcionAfectadaPedidoDTO> anuladas = await guardian.Anular(await guardian.AfectadasAlBorrar("1", PEDIDO), PEDIDO, true, "x");

            Assert.AreEqual(1, anuladas.Count);
            Assert.AreEqual(EstadoInscripcionEvento.Anulada.ToString(), inscripciones.Single().Estado);
        }

        [TestMethod]
        public void Aviso_409ConCodigoYLasInscripciones()
        {
            var afectadas = new List<InscripcionAfectadaPedidoDTO>
            {
                new InscripcionAfectadaPedidoDTO { Id = 1, Evento = "Formación por compra Lifting Coreano", FechaEvento = new DateTime(2026, 11, 25), Cliente = "15191", Contacto = "0", Nombre = "LUNA" }
            };

            NestoBusinessException aviso = ReglasProductosRequeridosEvento.Aviso("1", PEDIDO, afectadas, borrarPedido: true);

            Assert.AreEqual(HttpStatusCode.Conflict, aviso.StatusCode);
            Assert.AreEqual(ReglasProductosRequeridosEvento.ERROR_PEDIDO_JUSTIFICA, aviso.GetErrorCode());
            Assert.AreEqual(PEDIDO, aviso.Context.Pedido);
            Assert.AreSame(afectadas, aviso.Context.AdditionalData["inscripciones"]);
            Assert.AreEqual($"El pedido {PEDIDO} justifica la inscripción gratuita por compra de: 15191/0 (LUNA) en «Formación por compra Lifting Coreano» " +
                $"del 25/11/2026. Si borra el pedido {PEDIDO}, se anulará la inscripción y se avisará a la tienda del evento. ¿Quiere seguir?", aviso.Message);
        }

        // ---------------- DELETE api/PedidosVenta ----------------

        private PedidosVentaController ControllerBorrar(IInscripcionesPorCompraEventos guardian)
        {
            A.CallTo(() => db.CabPedidoVtas).Returns(Crear(new List<CabPedidoVta> { new CabPedidoVta { Empresa = "1", Número = PEDIDO, Nº_Cliente = "15191", Contacto = "0" } }));
            A.CallTo(() => db.EnviosAgencias).Returns(Crear(new List<EnviosAgencia>()));
            A.CallTo(() => db.LinPedidoVtas).Returns(Crear(new List<LinPedidoVta>()));
            return new PedidosVentaController(db)
            {
                InscripcionesPorCompra = guardian,
                ServicioCanje = A.Fake<NestoAPI.Infraestructure.ChequesRegalo.IServicioCanjeChequesRegalo>(),
                ServicioCupones = A.Fake<IServicioCanjeCupones>(),
                RequestContext = new HttpRequestContext { Principal = VENDEDOR }
            };
        }

        [TestMethod]
        public async Task BorrarPedido_SinConfirmar_409YNoSeBorra()
        {
            IInscripcionesPorCompraEventos guardian = A.Fake<IInscripcionesPorCompraEventos>();
            A.CallTo(() => guardian.AfectadasAlBorrar("1", PEDIDO)).Returns(Task.FromResult(new List<InscripcionAfectadaPedidoDTO>
            {
                new InscripcionAfectadaPedidoDTO { Id = 1, Evento = "Formación", FechaEvento = HOY.AddDays(20), Cliente = "15191", Contacto = "0" }
            }));
            PedidosVentaController controller = ControllerBorrar(guardian);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => controller.DeleteCabPedidoVta("1", PEDIDO));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual(ReglasProductosRequeridosEvento.ERROR_PEDIDO_JUSTIFICA, ex.GetErrorCode());
            A.CallTo(() => db.CabPedidoVtas.Remove(A<CabPedidoVta>._)).MustNotHaveHappened();
            A.CallTo(() => guardian.Anular(A<List<InscripcionAfectadaPedidoDTO>>._, A<int>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task BorrarPedido_Confirmado_SeBorraYSeAnulan()
        {
            IInscripcionesPorCompraEventos guardian = A.Fake<IInscripcionesPorCompraEventos>();
            var afectadas = new List<InscripcionAfectadaPedidoDTO>
            {
                new InscripcionAfectadaPedidoDTO { Id = 1, Evento = "Formación", FechaEvento = HOY.AddDays(20), Cliente = "15191", Contacto = "0" }
            };
            A.CallTo(() => guardian.AfectadasAlBorrar("1", PEDIDO)).Returns(Task.FromResult(afectadas));
            PedidosVentaController controller = ControllerBorrar(guardian);

            IHttpActionResult resultado = await controller.DeleteCabPedidoVta("1", PEDIDO, anularInscripcionesEventos: true);

            Assert.IsInstanceOfType(resultado, typeof(System.Web.Http.Results.OkNegotiatedContentResult<CabPedidoVta>));
            A.CallTo(() => guardian.Anular(afectadas, PEDIDO, true, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task BorrarPedido_SinInscripciones_ComoSiempre()
        {
            IInscripcionesPorCompraEventos guardian = A.Fake<IInscripcionesPorCompraEventos>();
            A.CallTo(() => guardian.AfectadasAlBorrar(A<string>._, A<int>._)).Returns(Task.FromResult(new List<InscripcionAfectadaPedidoDTO>()));
            PedidosVentaController controller = ControllerBorrar(guardian);

            IHttpActionResult resultado = await controller.DeleteCabPedidoVta("1", PEDIDO);

            Assert.IsInstanceOfType(resultado, typeof(System.Web.Http.Results.OkNegotiatedContentResult<CabPedidoVta>));
        }

        [TestMethod]
        public async Task BorrarPedido_SiFallaLaComprobacion_SeBorraIgual()
        {
            IInscripcionesPorCompraEventos guardian = A.Fake<IInscripcionesPorCompraEventos>();
            A.CallTo(() => guardian.AfectadasAlBorrar(A<string>._, A<int>._)).Throws(new InvalidOperationException("BD"));
            PedidosVentaController controller = ControllerBorrar(guardian);

            IHttpActionResult resultado = await controller.DeleteCabPedidoVta("1", PEDIDO);

            Assert.IsInstanceOfType(resultado, typeof(System.Web.Http.Results.OkNegotiatedContentResult<CabPedidoVta>));
        }

        // ---------------- Mantenimiento del evento (PUT/POST) ----------------

        private ServicioEventos ServicioEventos()
        {
            A.CallTo(() => db.Productos).Returns(Crear(new List<Producto> { new Producto { Empresa = "1", Número = "45363", Nombre = "LIFTING COREANO KIT" } }));
            A.CallTo(() => db.EventosSenales).Returns(Crear(new List<EventoSenal>()));
            var servicio = new ServicioEventos(db, () => HOY, (e, u) => "ALC");
            servicio.ProductosRequeridos = repositorio;
            return servicio;
        }

        private static EventoDTO PutPorCompra(params string[] productos) => new EventoDTO
        {
            Titulo = "Formación por compra Lifting Coreano",
            Fecha = new DateTime(2026, 11, 25),
            Activo = true,
            ProductosRequeridos = productos.Select(p => new ProductoRequeridoEventoDTO { Producto = p }).ToList()
        };

        [TestMethod]
        public async Task PutEvento_GuardaLosProductosRequeridosYLosDevuelve()
        {
            A.CallTo(() => repositorio.Guardar(A<int>._, A<IReadOnlyCollection<string>>._, A<string>._)).Returns(Task.FromResult(true));

            EventoDTO dto = await ServicioEventos().ModificarEvento(EVENTO_POR_COMPRA, PutPorCompra(" 45363 "), "NUEVAVISION\\Paloma");

            A.CallTo(() => repositorio.Guardar(EVENTO_POR_COMPRA, A<IReadOnlyCollection<string>>.That.Matches(p => p.Single() == "45363"), A<string>._))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual("LIFTING COREANO KIT", dto.ProductosRequeridos.Single().Nombre);
        }

        [TestMethod]
        public async Task PutEvento_ProductoQueNoExiste_400SinGuardar()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                ServicioEventos().ModificarEvento(EVENTO_POR_COMPRA, PutPorCompra("45363", "NOEXISTE"), "x"));

            StringAssert.Contains(ex.Message, "No existe el producto NOEXISTE");
            A.CallTo(() => repositorio.Guardar(A<int>._, A<IReadOnlyCollection<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutEvento_SinElCampo_NoTocaLosProductos()
        {
            var sinCampo = new EventoDTO { Titulo = "Formación", Fecha = new DateTime(2026, 11, 25), Activo = true };

            EventoDTO dto = await ServicioEventos().ModificarEvento(EVENTO_POR_COMPRA, sinCampo, "x");

            A.CallTo(() => repositorio.Guardar(A<int>._, A<IReadOnlyCollection<string>>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual("45363", dto.ProductosRequeridos.Single().Producto, "Los que tenía");
        }

        [TestMethod]
        public async Task PutEvento_SinElScript_400QueFaltaElScript()
        {
            A.CallTo(() => repositorio.Guardar(A<int>._, A<IReadOnlyCollection<string>>._, A<string>._)).Returns(Task.FromResult(false));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                ServicioEventos().ModificarEvento(EVENTO_POR_COMPRA, PutPorCompra("45363"), "x"));

            StringAssert.Contains(ex.Message, "Issue732");
        }

        [TestMethod]
        public async Task LeerEventos_TraenSusProductosRequeridos()
        {
            List<EventoDTO> lista = await ServicioEventos().LeerEventos("1", true);

            Assert.AreEqual("45363", lista.Single(e => e.Id == EVENTO_POR_COMPRA).ProductosRequeridos.Single().Producto);
            Assert.AreEqual(0, lista.Single(e => e.Id == EVENTO_GRATUITO).ProductosRequeridos.Count);
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
