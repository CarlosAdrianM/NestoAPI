using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593 (TNV): el canje del cheque regalo desde la app de clientas (api/Pedidos/Cliente) con las mismas
    /// reglas que POST/PUT de api/PedidosVenta, y el cheque fuera de la base de los portes.
    /// </summary>
    [TestClass]
    public class CarritoChequesRegaloTests
    {
        private const string PRODUCTO_CHEQUE = "CHEQUE50_OCT26";
        private const string CLIENTE = "15191";
        private static readonly DateTime HOY = new DateTime(2026, 10, 20, 10, 0, 0);
        private static readonly List<string> PRODUCTOS_CHEQUE = new List<string> { PRODUCTO_CHEQUE };

        private IRepositorioCanjeChequesRegalo repositorio;
        private ServicioCanjeChequesRegalo servicio;
        private DateTime ahora;

        [TestInitialize]
        public void Inicializar()
        {
            ahora = HOY;
            repositorio = A.Fake<IRepositorioCanjeChequesRegalo>();
            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo> { CanjeChequesRegaloTests.Campana() });
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._)).Returns(RepositorioCanjeChequesRegalo.Diccionario(new[]
            {
                new ProductoParaChequeRegalo { Numero = PRODUCTO_CHEQUE, Nombre = "CHEQUE REGALO 50 €", Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
                new ProductoParaChequeRegalo { Numero = "COSM", Nombre = "CREMA FACIAL", Grupo = "COS", IvaRepercutido = "G21" },
                new ProductoParaChequeRegalo { Numero = "PELU", Nombre = "TINTE", Grupo = "PEL", IvaRepercutido = "G21" }
            }));
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._)).Returns(new List<ChequeRegaloCliente> { Cheque() });
            servicio = new ServicioCanjeChequesRegalo(repositorio, () => ahora);
        }

        private static ChequeRegaloCliente Cheque(string estado = "Generado", int? pedidoCanje = null, bool tieneLaLinea = false) => new ChequeRegaloCliente
        {
            Id = 7,
            Campana = "CHEQUE50_OCT_2026",
            Empresa = "1",
            Cliente = CLIENTE,
            EmpresaFactura = "1",
            FacturaOrigen = "NV2612345",
            FechaGeneracion = new DateTime(2026, 10, 14),
            FechaActivacion = new DateTime(2026, 10, 14),
            Estado = estado,
            PedidoCanje = pedidoCanje,
            PedidoCanjeTieneLaLinea = tieneLaLinea
        };

        /// <summary>El pedido como lo monta ConstructorPedidoCliente: el cliente sale del JWT y la línea del cheque llega
        /// con cantidad 1 y sin precio (no pasa por el cálculo de precios).</summary>
        private static PedidoVentaDTO Pedido(decimal baseProducto, string productoComprado = "COSM", bool conCheque = true, string cliente = CLIENTE)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = cliente, contacto = "0", iva = "G21", Usuario = "APP\\" + cliente };
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = productoComprado, Cantidad = 1, PrecioUnitario = baseProducto,
                AplicarDescuento = false, estado = Constantes.EstadosLineaVenta.EN_CURSO, iva = "G21", PorcentajeIva = 0.21M
            });
            if (conCheque)
            {
                pedido.Lineas.Add(new LineaPedidoVentaDTO
                {
                    tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "cheque50_oct26", Cantidad = 1, PrecioUnitario = 0,
                    estado = Constantes.EstadosLineaVenta.EN_CURSO, PorcentajeIva = 0.21M
                });
            }
            return pedido;
        }

        private Task<ResultadoChequeCarrito> Preparar(PedidoVentaDTO pedido) => new CarritoChequesRegalo(servicio).Preparar(pedido, PRODUCTOS_CHEQUE);

        [TestMethod]
        public async Task Carrito_SinLineaDelCheque_NoHaceNada()
        {
            ResultadoChequeCarrito resultado = await Preparar(Pedido(300, conCheque: false));

            Assert.IsFalse(resultado.LlevaCheque);
            Assert.IsNull(resultado.ParaElCarrito());
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Carrito_ConSuCheque_LaLineaQuedaNormalizadaYElTotalLoDescuenta()
        {
            PedidoVentaDTO pedido = Pedido(300);

            ResultadoChequeCarrito resultado = await Preparar(pedido);

            Assert.IsTrue(resultado.Aplicado);
            LineaPedidoVentaDTO lineaCheque = pedido.Lineas.Single(l => l.EsChequeRegalo);
            Assert.AreEqual(PRODUCTO_CHEQUE, lineaCheque.Producto);
            Assert.AreEqual(-1, lineaCheque.Cantidad);
            Assert.AreEqual(50M, lineaCheque.PrecioUnitario);
            Assert.AreEqual(-50M, lineaCheque.BaseImponible);
            Assert.AreEqual("G21", lineaCheque.iva);
            Assert.AreEqual(250M, pedido.BaseImponible);
            Assert.AreEqual(302.50M, pedido.Total, "(300 − 50) + 21 % de IVA");
            ChequeRegaloCarritoDTO dto = resultado.ParaElCarrito();
            Assert.IsTrue(dto.Aplicado);
            Assert.AreEqual(50M, dto.Importe);
            Assert.AreEqual("Tu cheque regalo de 50 € está descontado en este pedido.", dto.Mensaje);
            Assert.IsNull(dto.Codigo);
        }

        [TestMethod]
        public async Task Carrito_NoReservaElCheque_LoReservaPostPedidoVentaAlGuardar()
        {
            _ = await Preparar(Pedido(300));

            A.CallTo(() => repositorio.ReservarCheque(A<int>._, A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LiberarChequesDelPedido(A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Carrito_SoloBuscaElChequeDelClienteDelPedido_QueSaleDelJwt()
        {
            _ = await Preparar(Pedido(300));

            A.CallTo(() => repositorio.LeerChequesDelCliente("1", CLIENTE, 0)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Carrito_ClienteSinCheque_NoPuedeUsarElDeOtra()
        {
            // El cheque es de 15191; la que pide es 20000 (su JWT): para ella no hay cheque
            A.CallTo(() => repositorio.LeerChequesDelCliente("1", "20000", A<int>._)).Returns(new List<ChequeRegaloCliente>());

            ResultadoChequeCarrito resultado = await Preparar(Pedido(300, cliente: "20000"));

            Assert.IsFalse(resultado.Aplicado);
            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_SIN_CHEQUE, resultado.Error.Context.ErrorCode);
            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, resultado.Error.StatusCode);
            StringAssert.StartsWith(resultado.Error.Message, "No tienes ningún cheque regalo");
        }

        [TestMethod]
        public async Task Carrito_SinSuperarElMinimo_DiceCuantoFalta()
        {
            ResultadoChequeCarrito resultado = await Preparar(Pedido(200));

            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO, resultado.Error.Context.ErrorCode);
            ChequeRegaloCarritoDTO dto = resultado.ParaElCarrito();
            Assert.IsFalse(dto.Aplicado);
            Assert.AreEqual(50.01M, dto.Falta);
            StringAssert.Contains(dto.Mensaje, "superar 250,00 € de producto");
            StringAssert.Contains(dto.Mensaje, "Te faltan 50,01 €.");
        }

        [TestMethod]
        public async Task Carrito_JustoElMinimo_NoVale()
        {
            ResultadoChequeCarrito resultado = await Preparar(Pedido(250));

            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO, resultado.Error.Context.ErrorCode);
        }

        [TestMethod]
        public async Task Carrito_LaPeluqueriaNoCuentaParaElMinimo()
        {
            ResultadoChequeCarrito resultado = await Preparar(Pedido(400, productoComprado: "PELU"));

            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO, resultado.Error.Context.ErrorCode);
            Assert.AreEqual(250.01M, resultado.ParaElCarrito().Falta);
        }

        [TestMethod]
        public async Task Carrito_ChequeYaEnOtroPedido_NoSePuedeUsarDosVeces()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque("Canjeado", 930500, tieneLaLinea: true) });

            ResultadoChequeCarrito resultado = await Preparar(Pedido(300));

            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_YA_USADO, resultado.Error.Context.ErrorCode);
            Assert.AreEqual("Tu cheque regalo ya está aplicado en el pedido 930500: solo se puede usar una vez.", resultado.Error.Message);
        }

        [TestMethod]
        public async Task Carrito_PasadaLaFechaLimite_FueraDePlazo()
        {
            ahora = new DateTime(2026, 11, 8, 9, 0, 0);

            ResultadoChequeCarrito resultado = await Preparar(Pedido(300));

            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_FUERA_DE_PLAZO, resultado.Error.Context.ErrorCode);
            Assert.AreEqual("El plazo para usar tu cheque regalo ya ha terminado.", resultado.Error.Message);
        }

        [TestMethod]
        public async Task Carrito_CampanaApagada_NoSePuedeUsar()
        {
            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo> { CanjeChequesRegaloTests.Campana(activa: false) });

            ResultadoChequeCarrito resultado = await Preparar(Pedido(300));

            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_CAMPANA_INACTIVA, resultado.Error.Context.ErrorCode);
        }

        [TestMethod]
        public async Task Carrito_ChequeAnulado_NoSePuedeUsar()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque("Anulado") });

            ResultadoChequeCarrito resultado = await Preparar(Pedido(300));

            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_ANULADO, resultado.Error.Context.ErrorCode);
        }

        [TestMethod]
        public async Task Carrito_SiNoSePuedeUsar_SeQuitaLaLineaYElCarritoSeCalculaSinEl()
        {
            PedidoVentaDTO pedido = Pedido(200);
            _ = await Preparar(pedido);

            CarritoChequesRegalo.QuitarLineasCheque(pedido);

            Assert.AreEqual(1, pedido.Lineas.Count);
            Assert.AreEqual(200M, pedido.BaseImponible);
        }

        [TestMethod]
        public void Carrito_ElErrorConservaCodigoYDetallesParaLaApp()
        {
            NestoBusinessException original = ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO, "texto para el vendedor",
                "1", 0, CLIENTE, new Dictionary<string, object> { ["minimo"] = 250M, ["baseComputable"] = 100M, ["falta"] = 150.01M });

            NestoBusinessException paraLaClienta = CarritoChequesRegalo.ParaLaClienta(original);

            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO, paraLaClienta.Context.ErrorCode);
            Assert.AreEqual(150.01M, paraLaClienta.Context.AdditionalData["falta"]);
            Assert.IsTrue(CarritoChequesRegalo.EsErrorDelCheque(paraLaClienta));
            Assert.IsFalse(CarritoChequesRegalo.EsErrorDelCheque(new NestoBusinessException("otra cosa")));
        }

        [TestMethod]
        public void Portes_ElChequeNoBajaLaBaseDelEnvioGratis()
        {
            // 420 € en Canarias (envío gratis desde 400): con el cheque siguen sin portes
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "COSM", Cantidad = 1, PrecioUnitario = 420, AplicarDescuento = false },
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = PRODUCTO_CHEQUE, Cantidad = -1, PrecioUnitario = 50, AplicarDescuento = false, EsChequeRegalo = true }
            };

            Assert.AreEqual(420M, GestorPortes.CalcularBaseImponibleProductos(lineas));
            Assert.AreEqual(420M, GestorPortes.CalcularBaseImponibleProductos(lineas, false, null));
        }

        [TestMethod]
        public void Portes_OtraLineaNegativaQueNoEsElCheque_SigueRestando()
        {
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "COSM", Cantidad = 1, PrecioUnitario = 420, AplicarDescuento = false },
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "TiCKET", Cantidad = -1, PrecioUnitario = 50, AplicarDescuento = false }
            };

            Assert.AreEqual(370M, GestorPortes.CalcularBaseImponibleProductos(lineas));
        }

        [TestMethod]
        public void Normalizar_MarcaLaLineaComoCheque()
        {
            var linea = new LineaPedidoVentaDTO { Producto = PRODUCTO_CHEQUE, Cantidad = 50 };

            ReglasCanjeChequeRegalo.Normalizar(linea, CanjeChequesRegaloTests.Campana(), "G21", true);

            Assert.IsTrue(linea.EsChequeRegalo);
        }

        [TestMethod]
        public async Task ControllerTienda_LeeLosProductosDelChequeDelServicioDelCanje()
        {
            var controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>()) { ServicioCanje = servicio };

            List<string> productos = await controller.LeerProductosCheque(new PedidoClienteRequest
            {
                Lineas = new List<LineaPedidoClienteRequest> { new LineaPedidoClienteRequest { Producto = "COSM", Cantidad = 1 } }
            });

            CollectionAssert.AreEqual(PRODUCTOS_CHEQUE, productos);
        }

        [TestMethod]
        public async Task ControllerTienda_SiNoSePuedenLeerLasCampanas_SigueSinCheque()
        {
            IServicioCanjeChequesRegalo roto = A.Fake<IServicioCanjeChequesRegalo>();
            A.CallTo(() => roto.ProductosCheque()).Throws(new InvalidOperationException("sin BD"));
            var controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>()) { ServicioCanje = roto };

            List<string> productos = await controller.LeerProductosCheque(new PedidoClienteRequest
            {
                Lineas = new List<LineaPedidoClienteRequest> { new LineaPedidoClienteRequest { Producto = PRODUCTO_CHEQUE, Cantidad = 1 } }
            });

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public void ControllerTienda_ElControllerDePedidosUsaElMismoCanje()
        {
            var controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>()) { ServicioCanje = servicio };

            PedidosVentaController pedidos = controller.CrearControllerPedidos();

            Assert.AreSame(servicio, pedidos.ServicioCanje);
        }
    }

    /// <summary>NestoAPI#593 (TNV): las push a la clienta al generarse el cheque y el recordatorio antes de CanjeHasta.</summary>
    [TestClass]
    public class PushChequesRegaloTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 2, 10, 0, 0);

        private IRepositorioPushChequesRegalo repositorio;
        private IServicioNotificacionesPush push;
        private List<Exception> errores;
        private AvisadorPushChequesRegalo avisador;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioPushChequesRegalo>();
            push = A.Fake<IServicioNotificacionesPush>();
            errores = new List<Exception>();
            A.CallTo(() => repositorio.ColumnasDisponibles()).Returns(true);
            A.CallTo(() => repositorio.ReservarPushGenerado(A<int>._)).Returns(true);
            A.CallTo(() => repositorio.ReservarPushRecordatorio(A<int>._)).Returns(true);
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).Returns(1);
            avisador = new AvisadorPushChequesRegalo(repositorio, push, () => new DateTime(2026, 10, 14, 18, 0, 0), errores.Add);
        }

        private static ChequeRegaloParaPush Cheque(int id = 7, string cliente = "15191") => new ChequeRegaloParaPush
        {
            Id = id,
            Empresa = "1",
            Cliente = cliente,
            Codigo = "CHEQUE50_OCT_2026",
            ImporteBase = 50,
            MinimoCanje = 250,
            CanjeHasta = new DateTime(2026, 11, 7),
            FechaActivacion = new DateTime(2026, 10, 14)
        };

        [TestMethod]
        public void Texto_AlGenerarse()
        {
            NotificacionPushDTO n = PlantillaPushChequeRegalo.Generado(Cheque(), new DateTime(2026, 10, 14));

            Assert.AreEqual("Tienes un cheque regalo de 50 €", n.Titulo);
            Assert.AreEqual("Tienes un cheque regalo de 50 € para tu próximo pedido de más de 250 €. Úsalo hasta el 7 de noviembre.", n.Cuerpo);
            Assert.AreEqual("chequeregalo", n.Datos["tipo"]);
            Assert.AreEqual("CHEQUE50_OCT_2026", n.Datos["campana"]);
        }

        [TestMethod]
        public void Texto_AlGenerarseConEspera_DiceDesdeCuando()
        {
            ChequeRegaloParaPush cheque = Cheque();
            cheque.FechaActivacion = new DateTime(2026, 10, 20);

            NotificacionPushDTO n = PlantillaPushChequeRegalo.Generado(cheque, new DateTime(2026, 10, 14));

            StringAssert.EndsWith(n.Cuerpo, "Podrás usarlo desde el 20 de octubre hasta el 7 de noviembre.");
        }

        [TestMethod]
        public void Texto_Recordatorio()
        {
            NotificacionPushDTO n = PlantillaPushChequeRegalo.Recordatorio(Cheque(), HOY);

            Assert.AreEqual("Te quedan 5 días para usar tu cheque regalo de 50 €", n.Titulo);
            Assert.AreEqual("Úsalo en un pedido de más de 250 € de producto hasta el 7 de noviembre. Lo tienes en el carrito de la app.", n.Cuerpo);
            Assert.AreEqual("chequeregalo", n.Datos["tipo"]);
            Assert.AreEqual("Te queda 1 día para usar tu cheque regalo de 50 €", PlantillaPushChequeRegalo.Recordatorio(Cheque(), new DateTime(2026, 11, 6)).Titulo);
            Assert.AreEqual("Hoy es el último día para usar tu cheque regalo de 50 €", PlantillaPushChequeRegalo.Recordatorio(Cheque(), new DateTime(2026, 11, 7)).Titulo);
        }

        [TestMethod]
        public async Task AlGenerarse_SeMandaUnaPushALaClienta()
        {
            A.CallTo(() => repositorio.LeerSinPushGenerado("15191")).Returns(new List<ChequeRegaloParaPush> { Cheque() });

            int enviadas = await avisador.AvisarCliente("15191 ");

            Assert.AreEqual(1, enviadas);
            A.CallTo(() => repositorio.ReservarPushGenerado(7)).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => push.EnviarACliente("1", "15191", A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "Tienes un cheque regalo de 50 €")))
                    .MustHaveHappenedOnceExactly());
        }

        [TestMethod]
        public async Task AlGenerarse_SiOtroYaLaHaMandado_NoSeMandaDosVeces()
        {
            A.CallTo(() => repositorio.LeerSinPushGenerado(A<string>._)).Returns(new List<ChequeRegaloParaPush> { Cheque() });
            A.CallTo(() => repositorio.ReservarPushGenerado(7)).Returns(false);

            Assert.AreEqual(0, await avisador.AvisarCliente("15191"));
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinDispositivos_NoSeMandaNiSeMarca()
        {
            // La consulta solo devuelve clientas con la app (EXISTS en DispositivosNotificaciones de NestoTiendas)
            A.CallTo(() => repositorio.LeerSinPushGenerado(A<string>._)).Returns(new List<ChequeRegaloParaPush>());

            Assert.AreEqual(0, await avisador.AvisarCliente("15191"));
            A.CallTo(() => repositorio.ReservarPushGenerado(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            StringAssert.Contains(RepositorioPushChequesRegalo.SQL_SIN_PUSH_GENERADO, "DispositivosNotificaciones");
            StringAssert.Contains(RepositorioPushChequesRegalo.SQL_SIN_PUSH_GENERADO, "FechaPushGenerado IS NULL");
            StringAssert.Contains(RepositorioPushChequesRegalo.SQL_SIN_PUSH_GENERADO, "c.Estado = 'Generado'");
        }

        [TestMethod]
        public async Task SinLasColumnas_NoSeHaceNada()
        {
            A.CallTo(() => repositorio.ColumnasDisponibles()).Returns(false);

            Assert.AreEqual(0, await avisador.AvisarCliente("15191"));
            Assert.AreEqual(0, await avisador.AvisarPendientes());
            Assert.AreEqual(0, await avisador.Recordar(HOY));
            A.CallTo(() => repositorio.LeerSinPushGenerado(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LeerParaRecordatorio(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiLaPushRevienta_SeDeshaceLaMarcaYLosDemasSiguen()
        {
            A.CallTo(() => repositorio.LeerSinPushGenerado(null)).Returns(new List<ChequeRegaloParaPush> { Cheque(7, "15191"), Cheque(8, "20000") });
            A.CallTo(() => push.EnviarACliente("1", "15191", A<NotificacionPushDTO>._)).Throws(new InvalidOperationException("FCM caído"));

            int enviadas = await avisador.AvisarPendientes();

            Assert.AreEqual(1, enviadas);
            A.CallTo(() => repositorio.AnularPushGenerado(7)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.AnularPushGenerado(8)).MustNotHaveHappened();
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task Recordatorio_SeMandaUnaVezALosQueToca()
        {
            A.CallTo(() => repositorio.LeerParaRecordatorio(HOY.Date)).Returns(new List<ChequeRegaloParaPush> { Cheque() });

            int enviados = await avisador.Recordar(HOY);

            Assert.AreEqual(1, enviados);
            A.CallTo(() => repositorio.ReservarPushRecordatorio(7)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.EnviarACliente("1", "15191", A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "Te quedan 5 días para usar tu cheque regalo de 50 €")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.ReservarPushGenerado(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Recordatorio_YaMandado_NoSeRepite()
        {
            A.CallTo(() => repositorio.LeerParaRecordatorio(A<DateTime>._)).Returns(new List<ChequeRegaloParaPush> { Cheque() });
            A.CallTo(() => repositorio.ReservarPushRecordatorio(7)).Returns(false);

            Assert.AreEqual(0, await avisador.Recordar(HOY));
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Recordatorio_LaConsultaSoloCogeLosSinUsarEnSuFecha()
        {
            string sql = RepositorioPushChequesRegalo.SQL_PARA_RECORDATORIO;
            StringAssert.Contains(sql, "c.Estado = 'Generado'");                 // ni canjeados ni anulados ni caducados
            StringAssert.Contains(sql, "c.FechaPushRecordatorio IS NULL");       // una sola vez
            StringAssert.Contains(sql, "DATEDIFF(day, @p0, k.CanjeHasta) <= ISNULL(k.DiasRecordatorioPush, 5)");
            StringAssert.Contains(sql, "k.CanjeHasta >= @p0");                   // no después de la fecha límite
            StringAssert.Contains(sql, "k.Activa = 1");
            StringAssert.Contains(sql, "c.FechaPushGenerado < @p0");             // no el mismo día que la del aviso
            StringAssert.Contains(sql, "DispositivosNotificaciones");
            StringAssert.Contains(RepositorioPushChequesRegalo.SQL_RESERVAR_RECORDATORIO, "FechaPushRecordatorio IS NULL");
            StringAssert.Contains(RepositorioPushChequesRegalo.SQL_RESERVAR_GENERADO, "FechaPushGenerado IS NULL");
        }

        [TestMethod]
        public void Job_PorLaMananaTodosLosDias()
        {
            Assert.AreEqual("cheques-regalo-push", ChequesRegaloPushJobsService.ID_JOB);
            Assert.AreEqual("0 10 * * *", ChequesRegaloPushJobsService.CRON);
        }

        [TestMethod]
        public async Task AlFacturar_TrasElCorreoSeMandaLaPush()
        {
            IGeneradorChequesRegalo generador = A.Fake<IGeneradorChequesRegalo>();
            IAvisadorChequesRegalo correo = A.Fake<IAvisadorChequesRegalo>();
            IAvisadorPushChequesRegalo pushCheque = A.Fake<IAvisadorPushChequesRegalo>();
            A.CallTo(() => generador.GenerarPorFactura(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._))
                .Returns(Task.FromResult(new List<string> { "Cheque" }));
            A.CallTo(() => correo.AvisarCliente("15000")).Returns(Task.FromResult(new List<AvisoChequeRegalo>()));
            A.CallTo(() => pushCheque.AvisarCliente("15000")).Returns(1);
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2600001", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(generador, respuesta, "15000", new DateTime(2026, 10, 13), "Carlos", correo,
                new Lazy<IAvisadorPushChequesRegalo>(() => pushCheque));

            A.CallTo(() => correo.AvisarCliente("15000")).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => pushCheque.AvisarCliente("15000")).MustHaveHappenedOnceExactly());
            Assert.AreEqual("Le ha llegado también un aviso del cheque a la app de la tienda.", respuesta.Avisos.Last());
        }

        [TestMethod]
        public async Task AlFacturar_SinChequeNoHayPush_YSiLaPushFallaLaFacturaSigue()
        {
            IGeneradorChequesRegalo generador = A.Fake<IGeneradorChequesRegalo>();
            IAvisadorPushChequesRegalo pushCheque = A.Fake<IAvisadorPushChequesRegalo>();
            A.CallTo(() => generador.GenerarPorFactura(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._))
                .Returns(Task.FromResult(new List<string>()));
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2600001", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(generador, respuesta, "15000", DateTime.Today, "Carlos", null,
                new Lazy<IAvisadorPushChequesRegalo>(() => pushCheque));
            A.CallTo(() => pushCheque.AvisarCliente(A<string>._)).MustNotHaveHappened();

            A.CallTo(() => generador.GenerarPorFactura(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._))
                .Returns(Task.FromResult(new List<string> { "Cheque" }));
            A.CallTo(() => pushCheque.AvisarCliente(A<string>._)).Throws(new InvalidOperationException("Firebase"));

            await ServicioFacturas.AnadirChequeRegalo(generador, respuesta, "15000", DateTime.Today, "Carlos", null,
                new Lazy<IAvisadorPushChequesRegalo>(() => pushCheque));

            CollectionAssert.AreEqual(new List<string> { "Cheque" }, respuesta.Avisos);
        }
    }
}
