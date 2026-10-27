using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Cupones
{
    /// <summary>
    /// NestoAPI#696 (Carlos, 22/10/26): el cheque regalo y los cupones de eventos NO son acumulables. ComprobarCarrito lo dice a la
    /// tienda (CombinableConChequeRegalo = false); al guardar el pedido (POST/PUT de api/PedidosVenta, el carrito de TNV y el pedido de
    /// la tienda que importa Nesto#518) se rechaza con CUPON_CON_CHEQUE_REGALO; y POST api/Cupones/{codigo}/Usar con un pedido que ya
    /// lleva el cheque no da el cupón por usado y avisa a Administración.
    /// </summary>
    [TestClass]
    public class CuponesChequeRegaloNoAcumulablesTests
    {
        private const string PRODUCTO = "CUPONEVENTO";
        private const string CHEQUE = "CHEQUE50_OCT26";
        private const string CLIENTE = "15191";
        private const string CODIGO = "EV-2610-7K3Q";
        private const int PEDIDO = 930500;
        private static readonly DateTime HOY = new DateTime(2026, 11, 2, 10, 0, 0);
        private static readonly List<string> PRODUCTOS_CHEQUE = new List<string> { CHEQUE, "CHEQUE30_FEB27" };

        private IRepositorioCupones repositorio;
        private IServicioCorreoElectronico correo;
        private CuponLeido cupon;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioCupones>();
            correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            cupon = CuponesTests.Cupon();
            A.CallTo(() => repositorio.LeerPorCodigo(A<string>._)).Returns(Task.FromResult<CuponLeido>(null));
            A.CallTo(() => repositorio.LeerPorCodigo(CODIGO)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerPorId(7)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerDelCliente("1", CLIENTE)).ReturnsLazily(() => Task.FromResult(new List<CuponLeido> { cupon }));
            A.CallTo(() => repositorio.LeerIvaCliente("1", CLIENTE)).Returns(Task.FromResult("G21"));
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._)).ReturnsLazily(() => Task.FromResult(Productos()));
            A.CallTo(() => repositorio.LeerPorcentajesIva(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new PorcentajesIvaCupon { Iva = 21, RecargoEquivalencia = 0 }));
            A.CallTo(() => repositorio.LeerUsoTienda(A<int>._)).Returns(Task.FromResult<UsoTiendaCupon>(null));
        }

        private static Dictionary<string, ProductoParaCupon> Productos() => new Dictionary<string, ProductoParaCupon>(StringComparer.OrdinalIgnoreCase)
        {
            [PRODUCTO] = new ProductoParaCupon { Numero = PRODUCTO, Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
            [CHEQUE] = new ProductoParaCupon { Numero = CHEQUE, Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
            ["CREMA"] = new ProductoParaCupon { Numero = "CREMA", Grupo = "COS", IvaRepercutido = "G21" }
        };

        private ServicioCanjeCupones Canje(bool conProductosCheque = true, bool espejoEncendido = false) =>
            new ServicioCanjeCupones(repositorio, () => HOY, PRODUCTO, () => espejoEncendido,
                conProductosCheque ? () => Task.FromResult(new List<string>(PRODUCTOS_CHEQUE)) : (Func<Task<List<string>>>)null);

        private static LinPedidoVta Linea(string producto, short cantidad, decimal baseImponible, string formaVenta = "VEN") => new LinPedidoVta
        {
            Empresa = "1",
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = producto,
            Cantidad = cantidad,
            Base_Imponible = baseImponible,
            Total = Math.Round(baseImponible * 1.21M, 2, MidpointRounding.AwayFromZero),
            IVA = "G21",
            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
            Forma_Venta = formaVenta,
            Picking = 0
        };

        /// <summary>200 € de crema, el cupón y, si se pide, el cheque de 50.</summary>
        private static List<LinPedidoVta> Lineas(bool conCheque, string formaVenta = "VEN")
        {
            var lineas = new List<LinPedidoVta> { Linea("CREMA", 2, 200M, formaVenta), Linea(PRODUCTO, -1, -41.32M, formaVenta) };
            if (conCheque)
            {
                lineas.Add(Linea(CHEQUE, -1, -41.32M, formaVenta));
            }
            return lineas;
        }

        private static PedidoVentaDTO PedidoConCodigo() => new PedidoVentaDTO
        {
            empresa = "1",
            cliente = CLIENTE,
            iva = "G21",
            Lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "CREMA", Cantidad = 2, PrecioUnitario = 100 },
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = PRODUCTO, Cantidad = -1, CodigoCupon = CODIGO }
            }
        };

        // ================= ComprobarCarrito (la tienda) =================

        [TestMethod]
        public async Task ComprobarCarrito_CombinableConChequeRegalo_EsFalseSiempre()
        {
            ServicioCanjeCupones canje = Canje(conProductosCheque: false);

            ComprobacionCarritoCuponDTO aplicable = await canje.ComprobarCarritoTienda(Carrito(CODIGO, ("CREMA", 100M)));
            ComprobacionCarritoCuponDTO noExiste = await canje.ComprobarCarritoTienda(Carrito("EV-0000-XXXX", ("CREMA", 100M)));
            A.CallTo(() => repositorio.LeerDelCliente("1", CLIENTE)).Returns(Task.FromResult(new List<CuponLeido>()));
            ComprobacionCarritoCuponDTO sinCupon = await canje.ComprobarCarritoTienda(Carrito(null, ("CREMA", 100M)));

            Assert.IsTrue(aplicable.Aplicable, aplicable.Mensaje);
            Assert.IsFalse(aplicable.CombinableConChequeRegalo, "Con el cupón aplicable");
            Assert.AreEqual(ReglasCupon.ERROR_NO_EXISTE, noExiste.ErrorCode);
            Assert.IsFalse(noExiste.CombinableConChequeRegalo, "Con un código que no existe (lo que se vio en producción el 22-10)");
            Assert.AreEqual(ReglasCupon.ERROR_SIN_CUPON, sinCupon.ErrorCode);
            Assert.IsFalse(sinCupon.CombinableConChequeRegalo, "Sin cupón");
            Assert.IsFalse(ReglasCupon.COMBINABLE_CON_CHEQUE_REGALO);
            Assert.IsFalse(new ComprobacionCarritoCuponDTO().CombinableConChequeRegalo, "El valor por defecto del DTO");
        }

        private static ComprobarCarritoCuponDTO Carrito(string codigo, params (string producto, decimal baseImponible)[] lineas) => new ComprobarCarritoCuponDTO
        {
            Empresa = "1",
            Cliente = CLIENTE,
            Codigo = codigo,
            Lineas = lineas.Select(l => new LineaCarritoCuponDTO { Producto = l.producto, Cantidad = 1, BaseImponible = l.baseImponible }).ToList()
        };

        // ================= La regla pura =================

        [TestMethod]
        public void LlevaChequeRegalo_ConLaLineaDelProductoDeUnaCampana_True_ConEspaciosYMayusculas()
        {
            Assert.IsTrue(ReglasCupon.LlevaChequeRegalo(Lineas(conCheque: true), PRODUCTOS_CHEQUE));
            Assert.IsTrue(ReglasCupon.LlevaChequeRegalo(new[] { Linea(" cheque50_oct26   ", -1, -41.32M) }, PRODUCTOS_CHEQUE), "char(15) relleno y otra caja");
        }

        [TestMethod]
        public void LlevaChequeRegalo_SinLineaDeCheque_OSinProductosDeCheque_OSinLineas_False()
        {
            Assert.IsFalse(ReglasCupon.LlevaChequeRegalo(Lineas(conCheque: false), PRODUCTOS_CHEQUE));
            Assert.IsFalse(ReglasCupon.LlevaChequeRegalo(Lineas(conCheque: true), new List<string>()), "Sin campañas no hay cheques");
            Assert.IsFalse(ReglasCupon.LlevaChequeRegalo(Lineas(conCheque: true), null));
            Assert.IsFalse(ReglasCupon.LlevaChequeRegalo((IEnumerable<LinPedidoVta>)null, PRODUCTOS_CHEQUE));
        }

        [TestMethod]
        public void LlevaChequeRegalo_SoloCuentanLasLineasDeProducto()
        {
            LinPedidoVta texto = Linea(CHEQUE, 1, 0);
            texto.TipoLinea = Constantes.TiposLineaVenta.TEXTO;

            Assert.IsFalse(ReglasCupon.LlevaChequeRegalo(new[] { texto }, PRODUCTOS_CHEQUE));
        }

        [TestMethod]
        public void LlevaChequeRegalo_EnElDTO_PorProductoOPorLaMarcaEsChequeRegalo()
        {
            var porProducto = new[] { new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = CHEQUE, Cantidad = -1 } };
            var porMarca = new[] { new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "OTRO", Cantidad = -1, EsChequeRegalo = true } };
            var sinNada = new[] { new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "CREMA", Cantidad = 1 } };

            Assert.IsTrue(ReglasCupon.LlevaChequeRegalo(porProducto, PRODUCTOS_CHEQUE));
            Assert.IsTrue(ReglasCupon.LlevaChequeRegalo(porMarca, null), "La marca vale aunque no se conozcan los productos");
            Assert.IsFalse(ReglasCupon.LlevaChequeRegalo(sinNada, PRODUCTOS_CHEQUE));
        }

        [TestMethod]
        public void MensajeConChequeRegalo_DeUsted_ConYSinCodigo()
        {
            Assert.AreEqual("El cupón EV-2610-7K3Q y el cheque regalo no son acumulables: no se pueden usar los dos en el mismo pedido. Quite uno de los dos.",
                ReglasCupon.MensajeConChequeRegalo(" EV-2610-7K3Q "));
            Assert.AreEqual("El cupón y el cheque regalo no son acumulables: no se pueden usar los dos en el mismo pedido. Quite uno de los dos.",
                ReglasCupon.MensajeConChequeRegalo(null));
        }

        // ================= Al guardar el pedido (POST/PUT de api/PedidosVenta, carrito de TNV) =================

        [TestMethod]
        public async Task Comprobar_PedidoConChequeYCupon_400ConSuCodigoYSinReservarNada()
        {
            ServicioCanjeCupones canje = Canje();
            await canje.NormalizarLineas(PedidoConCodigo());

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                canje.Comprobar("1", 0, CLIENTE, Lineas(conCheque: true), "G21", null, "NUEVAVISION\\Carlos", pedidoNuevo: true, teniaLineaCupon: false));

            Assert.AreEqual(ReglasCupon.ERROR_CON_CHEQUE_REGALO, ex.Context.ErrorCode);
            Assert.AreEqual(ReglasCupon.MensajeConChequeRegalo(CODIGO), ex.Message);
            Assert.IsTrue(CarritoCupones.EsErrorDelCupon(ex), "El carrito de TNV lo trata como error del cupón: se calcula sin él y se enseña el motivo");
            A.CallTo(() => repositorio.Reservar(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_AlModificarUnPedidoQueYaTeniaElCupon_TambienSeRechazaSiLeAnadenElCheque()
        {
            cupon = CuponesTests.Cupon(EstadosCupon.USADO, pedidoUso: PEDIDO);
            A.CallTo(() => repositorio.LeerDelPedido(PEDIDO)).ReturnsLazily(() => Task.FromResult(cupon));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Canje().Comprobar("1", PEDIDO, CLIENTE, Lineas(conCheque: true), "G21", null, "NUEVAVISION\\Carlos", pedidoNuevo: false, teniaLineaCupon: true));

            Assert.AreEqual(ReglasCupon.ERROR_CON_CHEQUE_REGALO, ex.Context.ErrorCode);
            Assert.AreEqual(PEDIDO, ex.Context.Pedido);
        }

        [TestMethod]
        public async Task Comprobar_PedidoDeLaTiendaConChequeYCupon_NoEntraEnSilencio_SeRechazaAlImportar()
        {
            // Nesto#518 importa el pedido de PrestaShop (forma de venta WEB) con el espejo encendido: antes ni se miraba el cupón
            ServicioCanjeCupones canje = Canje(espejoEncendido: true);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                canje.Comprobar("1", 0, CLIENTE, Lineas(conCheque: true, formaVenta: Constantes.FormasVenta.TIENDA_ONLINE), "G21", null,
                    "NUEVAVISION\\Carlos", pedidoNuevo: true, teniaLineaCupon: false));

            Assert.AreEqual(ReglasCupon.ERROR_CON_CHEQUE_REGALO, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "no son acumulables");
        }

        [TestMethod]
        public async Task Comprobar_PedidoDeLaTiendaSoloConCupon_SigueEntrandoComoAntes()
        {
            PlanCanjeCupon plan = await Canje(espejoEncendido: true).Comprobar("1", 0, CLIENTE,
                Lineas(conCheque: false, formaVenta: Constantes.FormasVenta.TIENDA_ONLINE), "G21", null, "NUEVAVISION\\Carlos",
                pedidoNuevo: true, teniaLineaCupon: false);

            Assert.IsFalse(plan.HayQueTocarCupones, "Lo cobró la tienda: se marca con POST api/Cupones/{codigo}/Usar");
        }

        [TestMethod]
        public async Task Comprobar_ConCuponYSinCheque_ReservaElCuponComoSiempre()
        {
            ServicioCanjeCupones canje = Canje();
            await canje.NormalizarLineas(PedidoConCodigo());

            PlanCanjeCupon plan = await canje.Comprobar("1", 0, CLIENTE, Lineas(conCheque: false), "G21", null, "NUEVAVISION\\Carlos",
                pedidoNuevo: true, teniaLineaCupon: false);

            Assert.AreEqual(7, plan.CuponAReservar?.Id);
        }

        [TestMethod]
        public async Task Comprobar_SoloConCheque_SinCupon_NoHaceNada()
        {
            var lineas = new List<LinPedidoVta> { Linea("CREMA", 2, 200M), Linea(CHEQUE, -1, -41.32M) };

            PlanCanjeCupon plan = await Canje().Comprobar("1", 0, CLIENTE, lineas, "G21", null, "NUEVAVISION\\Carlos", pedidoNuevo: true, teniaLineaCupon: false);

            Assert.IsFalse(plan.HayQueTocarCupones);
        }

        [TestMethod]
        public async Task Comprobar_SinLosProductosDeCheque_NoMiraNada_CompatibleConLosLlamantesDeAntes()
        {
            ServicioCanjeCupones canje = Canje(conProductosCheque: false);
            await canje.NormalizarLineas(PedidoConCodigo());

            PlanCanjeCupon plan = await canje.Comprobar("1", 0, CLIENTE, Lineas(conCheque: true), "G21", null, "NUEVAVISION\\Carlos",
                pedidoNuevo: true, teniaLineaCupon: false);

            Assert.AreEqual(7, plan.CuponAReservar?.Id);
        }

        // ================= POST api/Cupones/{codigo}/Usar desde Nesto (el pedido ya ha entrado) =================

        private ServicioCupones Servicio(bool conProductosCheque = true) =>
            new ServicioCupones(repositorio, null, () => HOY, PRODUCTO, A.Fake<IEspejoCupones>(), () => correo, () => true,
                conProductosCheque ? () => Task.FromResult(new List<string>(PRODUCTOS_CHEQUE)) : (Func<Task<List<string>>>)null);

        private static IPrincipal Casa() =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos") }, "prueba"));

        private static UsarCuponTiendaDTO UsoDesdeNesto() =>
            new UsarCuponTiendaDTO { Empresa = "1", Pedido = PEDIDO, Canal = "PrestaShop", PedidoCanal = "XKBKNABJK", ImporteDescontado = 50 };

        [TestMethod]
        public async Task UsarDesdeNesto_ElPedidoYaLlevaElCheque_ConflictoSinMarcarElCuponYAvisaAAdministracion()
        {
            A.CallTo(() => repositorio.LeerClienteDelPedidoConLineaCupon("1", PEDIDO)).Returns(Task.FromResult(CLIENTE));
            A.CallTo(() => repositorio.PedidoTieneLineaDe("1", PEDIDO, A<IEnumerable<string>>.That.Matches(p => p.Contains(CHEQUE)))).Returns(Task.FromResult(true));

            UsoCuponTiendaDTO r = await Servicio().UsarEnTienda(CODIGO, UsoDesdeNesto(), Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.CONFLICTO, r.Resultado);
            StringAssert.Contains(r.Mensaje, "no son acumulables");
            StringAssert.Contains(r.Mensaje, $"pedido {PEDIDO}");
            StringAssert.Contains(r.Mensaje, "el cupón se deja libre");
            Assert.IsTrue(r.AvisoAdministracion);
            Assert.AreEqual(EstadosCupon.ACTIVO, r.Cupon.Estado, "El cupón sigue libre");
            A.CallTo(() => repositorio.UsarEnTienda(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task UsarDesdeNesto_ElPedidoNoLlevaElCheque_QuedaUsadoComoSiempre()
        {
            A.CallTo(() => repositorio.LeerClienteDelPedidoConLineaCupon("1", PEDIDO)).Returns(Task.FromResult(CLIENTE));
            A.CallTo(() => repositorio.PedidoTieneLineaDe(A<string>._, A<int>._, A<IEnumerable<string>>._)).Returns(Task.FromResult(false));
            A.CallTo(() => repositorio.UsarEnTienda(7, "1", PEDIDO, A<string>._, A<string>._)).Returns(Task.FromResult(true));

            UsoCuponTiendaDTO r = await Servicio().UsarEnTienda(CODIGO, UsoDesdeNesto(), Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.USADO, r.Resultado);
            Assert.IsFalse(r.AvisoAdministracion);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UsarDesdeNesto_SinLosProductosDeCheque_NoPreguntaPorElPedido()
        {
            A.CallTo(() => repositorio.LeerClienteDelPedidoConLineaCupon("1", PEDIDO)).Returns(Task.FromResult(CLIENTE));
            A.CallTo(() => repositorio.UsarEnTienda(7, "1", PEDIDO, A<string>._, A<string>._)).Returns(Task.FromResult(true));

            UsoCuponTiendaDTO r = await Servicio(conProductosCheque: false).UsarEnTienda(CODIGO, UsoDesdeNesto(), Casa());

            Assert.AreEqual(ResultadosUsoCuponTienda.USADO, r.Resultado);
            A.CallTo(() => repositorio.PedidoTieneLineaDe(A<string>._, A<int>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }
    }
}
