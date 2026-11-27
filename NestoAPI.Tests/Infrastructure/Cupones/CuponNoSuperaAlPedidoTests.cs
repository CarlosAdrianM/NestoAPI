using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Tests.Infrastructure.Cupones
{
    /// <summary>
    /// Carlos (27/11/26, «Sí, cierra el hueco»): un cupón nunca puede superar al pedido. El pedido mínimo efectivo es el mayor entre
    /// el guardado (o el doble si es NULL) y el propio importe del cupón. Caso real: EV-2611-WGMB, de 100 €, en el pedido 931694 con
    /// 62,87 € de productos IVA incluido (quedaba en −28,65 €). Los pedidos que ya lo llevaban desde antes del 27/11/26 se pueden
    /// seguir modificando con el mínimo de entonces.
    /// </summary>
    [TestClass]
    public class CuponNoSuperaAlPedidoTests
    {
        private const string PRODUCTO = "CUPONEVENTO";
        private const string CLIENTE = "15191";
        private const string CODIGO = "EV-2611-WGMB";
        private const int PEDIDO = 931694;
        private static readonly DateTime HOY = new DateTime(2026, 11, 30, 10, 0, 0);
        private static readonly DateTime DESDE = new DateTime(2026, 11, 24);

        private IRepositorioCupones repositorio;
        private CuponLeido cupon;

        private static CuponLeido Cupon(decimal importe = 100, decimal? pedidoMinimo = 0, string estado = EstadosCupon.ACTIVO,
            int? pedidoUso = null, DateTime? fechaUso = null) => new CuponLeido
        {
            Id = 21,
            Codigo = CODIGO,
            Empresa = "1",
            Cliente = CLIENTE,
            Contacto = "0",
            Importe = importe,
            PedidoMinimo = pedidoMinimo,
            Origen = OrigenesCupon.EVENTO,
            EventoId = 1,
            Evento = "Masterclass",
            FechaEmision = DESDE,
            ValidoDesde = DESDE,
            Caducidad = ReglasCupon.Caducidad(DESDE),
            Estado = estado,
            UsadoEnPedido = pedidoUso,
            PedidoUsoTieneLaLinea = pedidoUso.HasValue,
            FechaUso = fechaUso
        };

        [TestInitialize]
        public void Preparar()
        {
            cupon = Cupon();
            repositorio = A.Fake<IRepositorioCupones>();
            A.CallTo(() => repositorio.LeerPorCodigo(CODIGO)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerDelPedido(PEDIDO)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerDelCliente("1", CLIENTE)).ReturnsLazily(() => Task.FromResult(new List<CuponLeido> { cupon }));
            A.CallTo(() => repositorio.LeerIvaCliente("1", CLIENTE)).Returns(Task.FromResult("G21"));
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._)).ReturnsLazily(() => Task.FromResult(Productos()));
            A.CallTo(() => repositorio.LeerPorcentajesIva(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new PorcentajesIvaCupon { Iva = 21, RecargoEquivalencia = 0 }));
            A.CallTo(() => repositorio.Reservar(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).Returns(Task.FromResult(true));
        }

        private static Dictionary<string, ProductoParaCupon> Productos() => new Dictionary<string, ProductoParaCupon>(StringComparer.OrdinalIgnoreCase)
        {
            [PRODUCTO] = new ProductoParaCupon { Numero = PRODUCTO, Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
            ["CREMA"] = new ProductoParaCupon { Numero = "CREMA", Grupo = "COS", IvaRepercutido = "G21" },
            ["CURSO"] = new ProductoParaCupon { Numero = "CURSO", Grupo = Constantes.Productos.GRUPO_CURSOS, Ficticio = true, IvaRepercutido = "EX" }
        };

        private ServicioCanjeCupones Canje() => new ServicioCanjeCupones(repositorio, () => HOY, PRODUCTO, () => false);

        private static LinPedidoVta Linea(string producto, decimal total, byte tipo = Constantes.TiposLineaVenta.PRODUCTO) => new LinPedidoVta
        {
            Empresa = "1",
            Número = PEDIDO,
            TipoLinea = tipo,
            Producto = producto,
            Cantidad = 1,
            Precio = total,
            Total = total,
            Base_Imponible = Math.Round(total / 1.21M, 2),
            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
            Picking = 0,
            Forma_Venta = "TIE"
        };

        private static LinPedidoVta LineaCupon() => new LinPedidoVta
        {
            Empresa = "1",
            Número = PEDIDO,
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = PRODUCTO,
            Cantidad = -1,
            Precio = 82.6446M,
            Total = -100,
            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
            Picking = 0,
            Forma_Venta = "TIE"
        };

        private async Task ConCodigo(ServicioCanjeCupones canje) =>
            await canje.NormalizarLineas(new PedidoVentaDTO { empresa = "1", cliente = CLIENTE, Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { CodigoCupon = CODIGO } } });

        // ================= La regla (un solo sitio) =================

        [TestMethod]
        public void PedidoMinimo_NuncaMenosQueElCupon()
        {
            Assert.AreEqual(100M, ReglasCupon.PedidoMinimo(100, 0), "Evento sin mínimo: el propio cupón");
            Assert.AreEqual(100M, ReglasCupon.PedidoMinimo(100, 60), "Mínimo del evento por debajo del cupón: el cupón");
            Assert.AreEqual(150M, ReglasCupon.PedidoMinimo(100, 150), "Mínimo del evento 150 y cupón 100: manda 150");
            Assert.AreEqual(200M, ReglasCupon.PedidoMinimo(100, null), "Cupón antiguo (NULL): el doble, como antes");
            Assert.AreEqual(100M, ReglasCupon.PedidoMinimo(50, null), "Cupón antiguo de 50: 100, como antes");
            Assert.AreEqual(ReglasCupon.PedidoMinimoSinTope(50, null), ReglasCupon.PedidoMinimo(50, null), "El doble ya es mayor que el cupón: nada cambia");
        }

        [TestMethod]
        public void PedidoMinimo_AplicadoAntesDel27_11_ElDeEntonces_YDespuesElDeHoy()
        {
            Assert.AreEqual(0M, ReglasCupon.PedidoMinimo(100, 0, new DateTime(2026, 11, 26, 18, 0, 0)));
            Assert.AreEqual(0M, ReglasCupon.PedidoMinimo(100, 0, new DateTime(2026, 11, 26, 23, 59, 0)));
            Assert.AreEqual(100M, ReglasCupon.PedidoMinimo(100, 0, new DateTime(2026, 11, 27)));
            Assert.AreEqual(100M, ReglasCupon.PedidoMinimo(100, 0, null), "Sin fecha de uso (nuevo en el pedido): el de hoy");
            Assert.AreEqual(200M, ReglasCupon.PedidoMinimo(100, null, new DateTime(2026, 11, 20)), "El doble de los antiguos sigue igual");
        }

        [TestMethod]
        public void CasoReal_Cupon100Con62_87_NoSePuede_MensajeDeMostrador()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() =>
                ReglasCupon.Comprobar(Cupon(), "1", PEDIDO, CLIENTE, HOY, false, 62.87M));

            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, ex.Context.ErrorCode);
            Assert.AreEqual("El cupón EV-2611-WGMB es de 100,00 € y este pedido lleva 62,87 € en productos (IVA incluido, sin contar cursos " +
                "ni portes). El cupón se usa de una vez y no puede superar al pedido: guárdelo para un pedido de 100,00 € o más. No caduca " +
                "hasta el 24/05/2027.", ex.Message);
            Assert.AreEqual(100M, ex.Context.AdditionalData["minimo"]);
            Assert.AreEqual(37.13M, ex.Context.AdditionalData["falta"]);
            Assert.AreEqual(true, ex.Context.AdditionalData["cuponSuperaAlPedido"]);
        }

        [TestMethod]
        public void Con100Justos_SiSePuede()
        {
            ReglasCupon.Comprobar(Cupon(), "1", PEDIDO, CLIENTE, HOY, false, 100.00M);
        }

        [TestMethod]
        public void MinimoDelEvento150_Manda150_ConElMensajeDeSiempre()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() =>
                ReglasCupon.Comprobar(Cupon(pedidoMinimo: 150), "1", PEDIDO, CLIENTE, HOY, false, 120M));

            Assert.AreEqual("Para usar el cupón EV-2611-WGMB de 100,00 € el pedido tiene que ser de al menos 150,00 € de productos " +
                "(IVA incluido, sin contar cursos ni portes). Lleva 120,00 €: faltan 30,00 €.", ex.Message);
            Assert.AreEqual(false, ex.Context.AdditionalData["cuponSuperaAlPedido"]);
            ReglasCupon.Comprobar(Cupon(pedidoMinimo: 150), "1", PEDIDO, CLIENTE, HOY, false, 150M);
        }

        [TestMethod]
        public void CuponAntiguoSinMinimoGuardado_SigueElDoble()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() =>
                ReglasCupon.Comprobar(Cupon(importe: 50, pedidoMinimo: null), "1", PEDIDO, CLIENTE, HOY, false, 99.99M));
            StringAssert.Contains(ex.Message, "al menos 100,00 € de productos");
            ReglasCupon.Comprobar(Cupon(importe: 50, pedidoMinimo: null), "1", PEDIDO, CLIENTE, HOY, false, 100M);
        }

        // ================= Guardar el pedido (POST y PUT) =================

        [TestMethod]
        public async Task PedidoNuevo_CursosYPortesNoCuentan_400YNoSeReserva()
        {
            ServicioCanjeCupones canje = Canje();
            await ConCodigo(canje);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta>
                {
                    Linea("CREMA", 62.87M), Linea("CURSO", 200M), Linea("62400003", 10M, Constantes.TiposLineaVenta.CUENTA_CONTABLE), LineaCupon()
                }, "G21", null, "x", pedidoNuevo: true, teniaLineaCupon: false));

            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "este pedido lleva 62,87 € en productos");
            A.CallTo(() => repositorio.Reservar(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PedidoNuevo_LlegaAlCupon_SeReserva()
        {
            ServicioCanjeCupones canje = Canje();
            await ConCodigo(canje);

            PlanCanjeCupon plan = await canje.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 100M), LineaCupon() },
                "G21", null, "x", pedidoNuevo: true, teniaLineaCupon: false);

            Assert.AreEqual(21, plan.CuponAReservar.Id);
        }

        [TestMethod]
        public async Task Put_AnadirElCuponAUnPedidoQueNoLlega_400()
        {
            ServicioCanjeCupones canje = Canje();
            await ConCodigo(canje);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 62.87M), LineaCupon() }, "G21", null, "x", pedidoNuevo: false, teniaLineaCupon: false));

            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, ex.Context.ErrorCode);
        }

        [TestMethod]
        public async Task Put_Pedido931694_ConElCuponDesdeAntesDel28_11_SeSigueGuardando()
        {
            // Lo grabado antes de la regla no se rompe: Almacén puede modificar el pedido aunque el cupón supere a los productos
            cupon = Cupon(estado: EstadosCupon.USADO, pedidoUso: PEDIDO, fechaUso: new DateTime(2026, 11, 26, 12, 0, 0));

            PlanCanjeCupon plan = await Canje().Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 62.87M), LineaCupon() },
                "G21", null, "x", pedidoNuevo: false, teniaLineaCupon: true);

            Assert.AreEqual(21, plan.CuponAReservar.Id);
        }

        [TestMethod]
        public async Task Put_CuponAplicadoConLaRegla_QuitarLineasPorDebajoDelCupon_400()
        {
            cupon = Cupon(estado: EstadosCupon.USADO, pedidoUso: PEDIDO, fechaUso: new DateTime(2026, 11, 29, 9, 0, 0));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Canje().Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 62.87M), LineaCupon() }, "G21", null, "x", pedidoNuevo: false, teniaLineaCupon: true));

            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "no puede superar al pedido");
        }

        // ================= Carrito de la tienda (#694) =================

        [TestMethod]
        public async Task CarritoTienda_62_87_NoAplicable_ConLoQueFalta()
        {
            var carrito = new ComprobarCarritoCuponDTO
            {
                Empresa = "1",
                Cliente = CLIENTE,
                Codigo = CODIGO,
                Lineas = new List<LineaCarritoCuponDTO> { new LineaCarritoCuponDTO { Producto = "CREMA", Cantidad = 1, BaseImponible = 51.96M } }
            };

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(carrito);

            Assert.IsFalse(r.Aplicable);
            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, r.ErrorCode);
            Assert.AreEqual(37.13M, r.Falta);
            StringAssert.Contains(r.Mensaje, "no puede superar al pedido");
        }

        [TestMethod]
        public async Task CarritoTienda_100Justos_Aplicable()
        {
            var carrito = new ComprobarCarritoCuponDTO
            {
                Empresa = "1",
                Cliente = CLIENTE,
                Codigo = CODIGO,
                Lineas = new List<LineaCarritoCuponDTO> { new LineaCarritoCuponDTO { Producto = "CREMA", Cantidad = 1, BaseImponible = 82.65M } }
            };

            ComprobacionCarritoCuponDTO r = await Canje().ComprobarCarritoTienda(carrito);

            Assert.IsTrue(r.Aplicable, r.Mensaje);
        }

        // ================= El aviso de Nesto/NestoApp (CuponDTO), el correo y el espejo =================

        [TestMethod]
        public void Aviso_DiceElMinimoEfectivo()
        {
            CuponDTO dto = ReglasCupon.ADTO(Cupon(), HOY, PRODUCTO);

            Assert.AreEqual(100M, dto.PedidoMinimo, "Nesto y NestoApp calculan lo que falta con este");
            Assert.AreEqual("Tiene un cupón de 100,00 € (EV-2611-WGMB) para un pedido de al menos 100,00 € de productos " +
                "(el cupón no puede superar al pedido), hasta el 24/05/2027.", dto.Mensaje);
        }

        [TestMethod]
        public void Aviso_MinimoDelEventoMayor_ComoSiempre()
        {
            CuponDTO dto = ReglasCupon.ADTO(Cupon(pedidoMinimo: 150), HOY, PRODUCTO);

            Assert.AreEqual(150M, dto.PedidoMinimo);
            Assert.AreEqual("Tiene un cupón de 100,00 € (EV-2611-WGMB) para un pedido de al menos 150,00 € de productos, hasta el 24/05/2027.", dto.Mensaje);
        }

        [TestMethod]
        public void Aviso_UsadoEnSuPedidoDesdeAntes_EnsenaElMinimoDeEntonces()
        {
            CuponDTO dto = ReglasCupon.ADTO(Cupon(estado: EstadosCupon.USADO, pedidoUso: PEDIDO, fechaUso: new DateTime(2026, 11, 26)), HOY, PRODUCTO);

            Assert.AreEqual(0M, dto.PedidoMinimo, "Así Nesto no dice «faltan…» al modificar el 931694");
        }

        [TestMethod]
        public void Correo_DiceQueElPedidoTieneQueLlegarAlCupon()
        {
            using (MailMessage mail = AvisadorCupones.Correo(Cupon(), "Luna", "luna@example.com"))
            {
                StringAssert.Contains(mail.Body, "<li>Se usa una sola vez y entero, en un pedido de productos de al menos 100,00 € (el importe " +
                    "del cupón, IVA incluido; no cuentan cursos ni portes): el cupón no puede superar al pedido. Uno por pedido.</li>");
            }
            using (MailMessage mail = AvisadorCupones.Correo(Cupon(pedidoMinimo: 150), "Luna", "luna@example.com"))
            {
                StringAssert.Contains(mail.Body, "<li>Se usa una sola vez, en un pedido de al menos 150,00 € de productos (IVA incluido; " +
                    "no cuentan cursos ni portes). Uno por pedido.</li>");
            }
        }

        [TestMethod]
        public void EspejoPrestashop_ElMinimoEfectivo()
        {
            var espejo = new CuponEspejo
            {
                Id = 21, Codigo = CODIGO, Empresa = "1", Cliente = CLIENTE, Importe = 100, PedidoMinimo = 0,
                ValidoDesde = DESDE, Caducidad = ReglasCupon.Caducidad(DESDE), Estado = EstadosCupon.ACTIVO
            };

            string minimo = XDocument.Parse(ReglasEspejoPrestashop.XmlCartRule(espejo, 5, true, null, 1, 1))
                .Root.Element("cart_rule").Element("minimum_amount").Value;

            Assert.AreEqual("100.00", minimo);
        }

        // ================= Picking: el cupón solo sale cuando lo entregado llega =================

        private static PedidoPicking PedidoPickingCon(decimal totalCrema) => new PedidoPicking
        {
            Empresa = "1",
            Id = PEDIDO,
            Lineas = new List<LineaPedidoPicking>
            {
                new LineaPedidoPicking { Id = 1, NumeroPedido = PEDIDO, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "CREMA", Cantidad = 1, Total = totalCrema, BaseImponible = Math.Round(totalCrema / 1.21M, 2), CantidadReservada = 1 },
                new LineaPedidoPicking { Id = 2, NumeroPedido = PEDIDO, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = PRODUCTO, Cantidad = -1, Total = -100, BaseImponible = -82.64M, CantidadReservada = 0 }
            }
        };

        [TestMethod]
        public void Picking_CuponAplicadoConLaRegla_NoSaleContraUnaEntregaMenor()
        {
            PedidoPicking pedido = PedidoPickingCon(62.87M);

            CuponesPicking.Rellenar(new List<PedidoPicking> { pedido }, PRODUCTO, (e, c) => Productos(), p => new List<LineaEntregadaCupon>(),
                p => new CuponUsadoPicking { Importe = 100, PedidoMinimo = 0, FechaUso = new DateTime(2026, 11, 29) });
            GestorReservasStock.ReservarCupones(new List<PedidoPicking> { pedido });

            Assert.AreEqual(100M, pedido.Cupon.PedidoMinimo);
            Assert.IsTrue(pedido.Cupon.Retenido, "No sale una factura negativa");
        }

        [TestMethod]
        public void Picking_Pedido931694DesdeAntes_ComoAntes()
        {
            PedidoPicking pedido = PedidoPickingCon(62.87M);

            CuponesPicking.Rellenar(new List<PedidoPicking> { pedido }, PRODUCTO, (e, c) => Productos(), p => new List<LineaEntregadaCupon>(),
                p => new CuponUsadoPicking { Importe = 100, PedidoMinimo = 0, FechaUso = new DateTime(2026, 11, 26) });
            GestorReservasStock.ReservarCupones(new List<PedidoPicking> { pedido });

            Assert.AreEqual(0M, pedido.Cupon.PedidoMinimo);
            Assert.IsFalse(pedido.Cupon.Retenido);
            Assert.IsTrue(pedido.Lineas.Any(l => l.EsCupon));
        }
    }
}
