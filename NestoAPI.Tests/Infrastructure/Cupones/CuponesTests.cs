using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
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

namespace NestoAPI.Tests.Infrastructure.Cupones
{
    /// <summary>
    /// NestoAPI#613 (cupones, corte 3): emisión al confirmar la señal, anulación por no asistir, cada regla de ReglasCupon, uso en
    /// el pedido (línea normalizada, reserva con UPDATE condicionado: dos pedidos con el mismo cupón), liberación, caducidad,
    /// cupones manuales de Dirección, el picking por partes y que la factura con la línea negativa se declara como F1 normal.
    /// </summary>
    [TestClass]
    public class CuponesTests
    {
        private const string PRODUCTO = "CUPONEVENTO";
        private const string CLIENTE = "15191";
        private const int PEDIDO = 930500;
        private static readonly DateTime HOY = new DateTime(2026, 11, 2, 10, 0, 0);
        private static readonly DateTime EVENTO = new DateTime(2026, 10, 30);

        private IRepositorioCupones repositorio;
        private ServicioCanjeCupones canje;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioCupones>();
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._)).ReturnsLazily(() => Task.FromResult(Productos()));
            A.CallTo(() => repositorio.LeerPorCodigo("EV-2610-7K3Q")).ReturnsLazily(() => Task.FromResult(Cupon()));
            A.CallTo(() => repositorio.Reservar(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).Returns(Task.FromResult(true));
            A.CallTo(() => repositorio.LeerPorcentajesIva(A<string>._, A<string>._, A<string>._))
                .ReturnsLazily(() => Task.FromResult(new PorcentajesIvaCupon { Iva = 21, RecargoEquivalencia = 0 }));
            A.CallTo(() => repositorio.LeerPorcentajesIva(A<string>._, "E52", "G21"))
                .ReturnsLazily(() => Task.FromResult(new PorcentajesIvaCupon { Iva = 21, RecargoEquivalencia = 5.2M }));
            canje = new ServicioCanjeCupones(repositorio, () => HOY, PRODUCTO);
        }

        internal static CuponLeido Cupon(string estado = EstadosCupon.ACTIVO, string cliente = CLIENTE, DateTime? desde = null, int? pedidoUso = null) => new CuponLeido
        {
            Id = 7,
            Codigo = "EV-2610-7K3Q",
            Empresa = "1",
            Cliente = cliente,
            Contacto = "0",
            Importe = 50,
            Origen = OrigenesCupon.EVENTO,
            EventoInscripcionId = 101,
            EventoId = 1,
            Evento = "Masterclass Cloasma",
            FechaEmision = new DateTime(2026, 10, 15),
            ValidoDesde = desde ?? EVENTO,
            Caducidad = ReglasCupon.Caducidad(desde ?? EVENTO),
            Estado = estado,
            UsadoEnPedido = pedidoUso,
            PedidoUsoTieneLaLinea = pedidoUso.HasValue
        };

        private static Dictionary<string, ProductoParaCupon> Productos() => new Dictionary<string, ProductoParaCupon>(StringComparer.OrdinalIgnoreCase)
        {
            [PRODUCTO] = new ProductoParaCupon { Numero = PRODUCTO, Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
            ["CREMA"] = new ProductoParaCupon { Numero = "CREMA", Grupo = "COS", IvaRepercutido = "G21" },
            ["CURSO"] = new ProductoParaCupon { Numero = "CURSO", Grupo = "CUR", Ficticio = true, IvaRepercutido = "EX" },
            ["CHEQUE50_OCT26"] = new ProductoParaCupon { Numero = "CHEQUE50_OCT26", Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" }
        };

        private static LinPedidoVta Linea(string producto, decimal total, short cantidad = 1, byte tipo = Constantes.TiposLineaVenta.PRODUCTO) => new LinPedidoVta
        {
            Empresa = "1",
            Número = PEDIDO,
            TipoLinea = tipo,
            Producto = producto,
            Cantidad = cantidad,
            Precio = total,
            Total = total,
            Base_Imponible = Math.Round(total / 1.21M, 2),
            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
            Picking = 0,
            Aplicar_Dto = true
        };

        private static LinPedidoVta LineaCupon(decimal precio = 41.3223M) => new LinPedidoVta
        {
            Empresa = "1",
            Número = PEDIDO,
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = PRODUCTO + "    ",
            Cantidad = -1,
            Precio = precio,
            Total = -50,
            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
            Picking = 0,
            Forma_Venta = "TIE"
        };

        // ---------------- ReglasCupon ----------------

        [TestMethod]
        public void Reglas_LaLineaEsElImporteIvaIncluidoEntre121()
        {
            Assert.AreEqual(41.3223M, ReglasCupon.PrecioLinea(50));
            Assert.AreEqual(100M, ReglasCupon.PedidoMinimo(50));
            Assert.AreEqual(new DateTime(2027, 4, 30), ReglasCupon.Caducidad(EVENTO), "6 meses desde el evento");
        }

        [TestMethod]
        public void Reglas_LaBaseYElIvaDeLaLineaDanElImporteDelCupon()
        {
            decimal baseImponible = Math.Round(-1 * ReglasCupon.PrecioLinea(50), 2, MidpointRounding.AwayFromZero);
            Assert.AreEqual(-41.32M, baseImponible, "50 € → −41,32 de base al 21 %, nunca 50 + IVA");
            Assert.AreEqual(-50.00M, Math.Round(baseImponible * 1.21M, 2, MidpointRounding.AwayFromZero));
        }

        [TestMethod]
        public void Reglas_CodigoLegibleSinAmbiguedades()
        {
            string codigo = ReglasCupon.NuevoCodigo(OrigenesCupon.EVENTO, EVENTO, n => 0);
            Assert.AreEqual("EV-2610-2222", codigo);
            StringAssert.StartsWith(ReglasCupon.NuevoCodigo(OrigenesCupon.MANUAL, EVENTO), "MN-2610-");
            for (int i = 0; i < 200; i++)
            {
                string c = ReglasCupon.NuevoCodigo(OrigenesCupon.EVENTO, EVENTO).Substring(8);
                Assert.IsFalse(c.Any(ch => "01OI".Contains(ch)), c);
            }
        }

        [TestMethod]
        public void Reglas_EstadoDeHoy()
        {
            Assert.AreEqual(EstadosCupon.EMITIDO, ReglasCupon.EstadoEfectivo(Cupon(EstadosCupon.EMITIDO), EVENTO.AddDays(-1)));
            Assert.AreEqual(EstadosCupon.ACTIVO, ReglasCupon.EstadoEfectivo(Cupon(EstadosCupon.EMITIDO), EVENTO), "El día del evento ya vale, aunque no haya pasado el job");
            Assert.AreEqual(EstadosCupon.CADUCADO, ReglasCupon.EstadoEfectivo(Cupon(), new DateTime(2027, 5, 1)));
            Assert.AreEqual(EstadosCupon.ACTIVO, ReglasCupon.EstadoEfectivo(Cupon(), new DateTime(2027, 4, 30)), "El último día todavía vale");
            Assert.AreEqual(EstadosCupon.USADO, ReglasCupon.EstadoEfectivo(Cupon(EstadosCupon.USADO, pedidoUso: 1), HOY));
            CuponLeido huerfano = Cupon(EstadosCupon.USADO, pedidoUso: 1);
            huerfano.PedidoUsoTieneLaLinea = false;
            Assert.AreEqual(EstadosCupon.ACTIVO, ReglasCupon.EstadoEfectivo(huerfano, HOY), "Su pedido ya no tiene la línea: está libre");
        }

        [TestMethod]
        public void Reglas_SoloComputanProductosDeVerdad()
        {
            Dictionary<string, ProductoParaCupon> p = Productos();
            Assert.IsTrue(ReglasCupon.ComputaParaMinimo(Constantes.TiposLineaVenta.PRODUCTO, p["CREMA"]));
            Assert.IsFalse(ReglasCupon.ComputaParaMinimo(Constantes.TiposLineaVenta.PRODUCTO, p["CURSO"]), "Ni cursos");
            Assert.IsFalse(ReglasCupon.ComputaParaMinimo(Constantes.TiposLineaVenta.PRODUCTO, p[PRODUCTO]), "Ni el propio cupón");
            Assert.IsFalse(ReglasCupon.ComputaParaMinimo(Constantes.TiposLineaVenta.PRODUCTO, p["CHEQUE50_OCT26"]), "Ni el cheque regalo");
            Assert.IsFalse(ReglasCupon.ComputaParaMinimo(Constantes.TiposLineaVenta.CUENTA_CONTABLE, null), "Ni portes");
            Assert.IsFalse(ReglasCupon.ComputaParaMinimo(Constantes.TiposLineaVenta.PRODUCTO, new ProductoParaCupon { Grupo = "CUR" }));
        }

        [TestMethod]
        public void Reglas_TotalComputableSobreElNetoConIva()
        {
            var lineas = new List<LinPedidoVta> { Linea("CREMA", 80), Linea("CREMA", 30.25M), Linea("CURSO", 200), Linea("62400002", 6, tipo: Constantes.TiposLineaVenta.CUENTA_CONTABLE), LineaCupon() };
            Assert.AreEqual(110.25M, ReglasCupon.TotalComputable(lineas, Productos()));
        }

        [TestMethod]
        public void Reglas_PedidoMinimo_ElDobleConMensajeClaro()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() => ReglasCupon.Comprobar(Cupon(), "1", PEDIDO, CLIENTE, HOY, false, 99.99M));
            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "el pedido tiene que ser de al menos 100,00 € de productos");
            StringAssert.Contains(ex.Message, "faltan 0,01 €");
            ReglasCupon.Comprobar(Cupon(), "1", PEDIDO, CLIENTE, HOY, false, 100M); // justo el doble vale
        }

        [TestMethod]
        public void Reglas_TodaviaNoVale_DiceDesdeCuando()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() =>
                ReglasCupon.Comprobar(Cupon(EstadosCupon.EMITIDO), "1", PEDIDO, CLIENTE, EVENTO.AddDays(-2), false, 500));
            Assert.AreEqual(ReglasCupon.ERROR_TODAVIA_NO, ex.Context.ErrorCode);
            Assert.AreEqual("El cupón EV-2610-7K3Q vale desde el 30/10/2026.", ex.Message);
        }

        [TestMethod]
        public void Reglas_Caducado_Anulado_Usado_OtroCliente()
        {
            Assert.AreEqual(ReglasCupon.ERROR_CADUCADO, Assert.ThrowsException<NestoBusinessException>(() =>
                ReglasCupon.Comprobar(Cupon(), "1", PEDIDO, CLIENTE, new DateTime(2027, 5, 1), false, 500)).Context.ErrorCode);
            Assert.AreEqual(ReglasCupon.ERROR_ANULADO, Assert.ThrowsException<NestoBusinessException>(() =>
                ReglasCupon.Comprobar(Cupon(EstadosCupon.ANULADO), "1", PEDIDO, CLIENTE, HOY, false, 500)).Context.ErrorCode);
            var usado = Assert.ThrowsException<NestoBusinessException>(() =>
                ReglasCupon.Comprobar(Cupon(EstadosCupon.USADO, pedidoUso: 930400), "1", PEDIDO, CLIENTE, HOY, false, 500));
            Assert.AreEqual(ReglasCupon.ERROR_YA_USADO, usado.Context.ErrorCode);
            StringAssert.Contains(usado.Message, "ya está aplicado en el pedido 930400: solo se puede usar una vez");
            Assert.AreEqual(ReglasCupon.ERROR_OTRO_CLIENTE, Assert.ThrowsException<NestoBusinessException>(() =>
                ReglasCupon.Comprobar(Cupon(), "1", PEDIDO, "20000", HOY, false, 500)).Context.ErrorCode);
        }

        [TestMethod]
        public void Reglas_YaEnEstePedido_NoMiraFechasPeroSiElClienteYElMinimo()
        {
            CuponLeido usado = Cupon(EstadosCupon.USADO, pedidoUso: PEDIDO);
            ReglasCupon.Comprobar(usado, "1", PEDIDO, CLIENTE, new DateTime(2027, 6, 1), true, 120);
            Assert.ThrowsException<NestoBusinessException>(() => ReglasCupon.Comprobar(usado, "1", PEDIDO, CLIENTE, HOY, true, 60));
            Assert.ThrowsException<NestoBusinessException>(() => ReglasCupon.Comprobar(usado, "1", PEDIDO, "20000", HOY, true, 500));
        }

        [TestMethod]
        public void Reglas_TextoDeLaLinea_ConElEventoYAlMaximo50()
        {
            Assert.AreEqual("Cupón EV-2610-7K3Q (Masterclass Cloasma)", ReglasCupon.TextoLinea(Cupon()));
            CuponLeido largo = Cupon();
            largo.Evento = "Una masterclass con un título larguísimo que no cabe";
            Assert.AreEqual(50, ReglasCupon.TextoLinea(largo).Length);
        }

        // ---------------- Canje en el pedido ----------------

        [TestMethod]
        public async Task Normalizar_LaLineaConElCodigo_LaPoneElServidor()
        {
            var linea = new LineaPedidoVentaDTO { CodigoCupon = "ev-2610-7k3q", Cantidad = 5, PrecioUnitario = 50, DescuentoLinea = 0.1M, AplicarDescuento = true };
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = CLIENTE, iva = "G21", Lineas = new List<LineaPedidoVentaDTO> { linea } };

            await canje.NormalizarLineas(pedido);

            Assert.AreEqual(PRODUCTO, linea.Producto);
            Assert.AreEqual((byte?)Constantes.TiposLineaVenta.PRODUCTO, linea.tipoLinea);
            Assert.AreEqual(-1, linea.Cantidad);
            Assert.AreEqual(41.3223M, linea.PrecioUnitario);
            Assert.AreEqual(0, linea.DescuentoLinea);
            Assert.IsFalse(linea.AplicarDescuento);
            Assert.AreEqual("G21", linea.iva);
            Assert.AreEqual("Cupón EV-2610-7K3Q (Masterclass Cloasma)", linea.texto);
            Assert.IsTrue(linea.EsCupon);
            Assert.IsTrue(linea.EsFicticio);
        }

        [TestMethod]
        public async Task Normalizar_SinLineasDeCupon_NoConsultaNada()
        {
            var pedido = new PedidoVentaDTO { empresa = "1", Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { Producto = "CREMA", tipoLinea = 1 } } };

            await canje.NormalizarLineas(pedido);

            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Normalizar_CodigoQueNoExiste_400()
        {
            A.CallTo(() => repositorio.LeerPorCodigo("EV-0000-XXXX")).Returns(Task.FromResult<CuponLeido>(null));
            var pedido = new PedidoVentaDTO { empresa = "1", Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { CodigoCupon = "EV-0000-XXXX" } } };

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.NormalizarLineas(pedido));

            Assert.AreEqual(ReglasCupon.ERROR_NO_EXISTE, ex.Context.ErrorCode);
            Assert.AreEqual("No existe el cupón EV-0000-XXXX.", ex.Message);
        }

        [TestMethod]
        public async Task Normalizar_DosCupones_UnoPorPedido()
        {
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { CodigoCupon = "EV-2610-7K3Q" }, new LineaPedidoVentaDTO { CodigoCupon = "EV-2610-AAAA" } }
            };

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.NormalizarLineas(pedido));

            Assert.AreEqual(ReglasCupon.ERROR_VARIOS, ex.Context.ErrorCode);
            Assert.AreEqual("Solo se puede aplicar un cupón por pedido.", ex.Message);
        }

        [TestMethod]
        public async Task Comprobar_PedidoNuevo_ReservaElCupon()
        {
            await canje.NormalizarLineas(new PedidoVentaDTO { empresa = "1", Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { CodigoCupon = "EV-2610-7K3Q" } } });
            LinPedidoVta cupon = LineaCupon(precio: 50);
            bool recalculado = false;

            PlanCanjeCupon plan = await canje.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 120), cupon }, "G21",
                l => recalculado = true, "NUEVAVISION\\Jesus", pedidoNuevo: true, teniaLineaCupon: false);

            Assert.AreEqual(7, plan.CuponAReservar.Id);
            Assert.AreEqual("TIE", plan.Canal);
            Assert.IsFalse(plan.LiberarOtros);
            Assert.AreEqual(41.3223M, cupon.Precio, "El servidor corrige el precio de la línea");
            Assert.IsTrue(recalculado);

            await canje.Reservar(plan);
            A.CallTo(() => repositorio.Reservar(7, "1", PEDIDO, "TIE", "NUEVAVISION\\Jesus")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Comprobar_NoLlegaAlMinimo_400YNoSeReserva()
        {
            await canje.NormalizarLineas(new PedidoVentaDTO { empresa = "1", Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { CodigoCupon = "EV-2610-7K3Q" } } });

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 60), Linea("CURSO", 200), LineaCupon() }, "G21", null, "x", pedidoNuevo: true, teniaLineaCupon: false));

            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, ex.Context.ErrorCode);
            A.CallTo(() => repositorio.Reservar(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Concurrencia_DosPedidosConElMismoCupon_SoloEntraUno()
        {
            A.CallTo(() => repositorio.Reservar(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(true)).Once().Then.Returns(Task.FromResult(false));
            var plan1 = new PlanCanjeCupon { EmpresaPedido = "1", Pedido = PEDIDO, CuponAReservar = Cupon() };
            var plan2 = new PlanCanjeCupon { EmpresaPedido = "1", Pedido = PEDIDO + 1, CuponAReservar = Cupon() };

            await canje.Reservar(plan1);
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.Reservar(plan2));

            Assert.AreEqual(ReglasCupon.ERROR_YA_USADO, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "se acaba de aplicar en otro pedido");
        }

        [TestMethod]
        public async Task Comprobar_ModificarPedidoQueYaLoLleva_UsaElSuyoSinCodigo()
        {
            A.CallTo(() => repositorio.LeerDelPedido(PEDIDO)).Returns(Task.FromResult(Cupon(EstadosCupon.USADO, pedidoUso: PEDIDO)));

            PlanCanjeCupon plan = await new ServicioCanjeCupones(repositorio, () => new DateTime(2027, 6, 1), PRODUCTO).Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 150), LineaCupon() }, "G21", null, "x", pedidoNuevo: false, teniaLineaCupon: true);

            Assert.AreEqual(7, plan.CuponAReservar.Id, "Aunque ya haya caducado, el pedido que lo tenía lo conserva");
            Assert.IsTrue(plan.LiberarOtros);
        }

        [TestMethod]
        public async Task Comprobar_LineaDeCuponSinCodigoNiCuponDelPedido_400()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 150), LineaCupon() }, "G21", null, "x", pedidoNuevo: true, teniaLineaCupon: false));

            Assert.AreEqual(ReglasCupon.ERROR_SIN_CODIGO, ex.Context.ErrorCode);
        }

        // ---------------- NestoAPI#614: cupón aplicado en la tienda PrestaShop ----------------

        private static LinPedidoVta LineaCuponTienda()
        {
            LinPedidoVta l = LineaCupon(41.32M);
            l.Forma_Venta = Constantes.FormasVenta.TIENDA_ONLINE + " ";
            return l;
        }

        [TestMethod]
        public async Task Comprobar_LineaDeLaTiendaSinCodigo_SeDejaComoVieneYSeMarcaConUsar()
        {
            LinPedidoVta lineaTienda = LineaCuponTienda();
            var canjeTienda = new ServicioCanjeCupones(repositorio, () => HOY, PRODUCTO, () => true);

            // 60 € de productos: por debajo del mínimo de Nesto, pero la tienda ya lo aplicó con sus reglas y cobró
            PlanCanjeCupon plan = await canjeTienda.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 60), lineaTienda }, "G21",
                null, "x", pedidoNuevo: true, teniaLineaCupon: false);

            Assert.IsFalse(plan.HayQueTocarCupones);
            Assert.AreEqual(41.32M, lineaTienda.Precio, "El importe es el que cobró la tienda");
            A.CallTo(() => repositorio.Reservar(A<int>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_LineaDeLaTiendaYaMarcadaConUsar_SeModificaElPedidoSinMirarElMinimo()
        {
            // Regresión: al quitar un producto por falta de stock, un pedido de la tienda con cupón no puede quedarse bloqueado
            A.CallTo(() => repositorio.LeerDelPedido(PEDIDO)).Returns(Task.FromResult(Cupon(EstadosCupon.USADO, pedidoUso: PEDIDO)));
            var canjeTienda = new ServicioCanjeCupones(repositorio, () => HOY, PRODUCTO, () => true);

            PlanCanjeCupon plan = await canjeTienda.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 30), LineaCuponTienda() }, "G21",
                null, "x", pedidoNuevo: false, teniaLineaCupon: true);

            Assert.IsFalse(plan.LiberarOtros, "El cupón sigue en el pedido");
        }

        [TestMethod]
        public async Task Comprobar_LineaDeLaTiendaConElEspejoApagado_ComoAntes400()
        {
            var canjeApagado = new ServicioCanjeCupones(repositorio, () => HOY, PRODUCTO, () => false);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canjeApagado.Comprobar("1", PEDIDO, CLIENTE,
                new List<LinPedidoVta> { Linea("CREMA", 150), LineaCuponTienda() }, "G21", null, "x", pedidoNuevo: true, teniaLineaCupon: false));

            Assert.AreEqual(ReglasCupon.ERROR_SIN_CODIGO, ex.Context.ErrorCode);
        }

        [TestMethod]
        public async Task Comprobar_LineaDeLaTiendaSinCodigoQuitada_SueltaElCupon()
        {
            var canjeTienda = new ServicioCanjeCupones(repositorio, () => HOY, PRODUCTO, () => true);

            PlanCanjeCupon plan = await canjeTienda.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 150) }, "G21", null, "x",
                pedidoNuevo: false, teniaLineaCupon: true);

            Assert.IsTrue(plan.LiberarOtros);
        }

        [TestMethod]
        public async Task Comprobar_QuitarLaLinea_SueltaElCupon()
        {
            PlanCanjeCupon plan = await canje.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 150) }, "G21", null, "x",
                pedidoNuevo: false, teniaLineaCupon: true);
            await canje.LiberarOtros(plan);

            Assert.IsNull(plan.CuponAReservar);
            A.CallTo(() => repositorio.LiberarDelPedido(PEDIDO, 0, "x")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Comprobar_PedidoSinCuponNiAntes_NoTocaNada()
        {
            PlanCanjeCupon plan = await canje.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 150) }, "G21", null, "x",
                pedidoNuevo: false, teniaLineaCupon: false);

            Assert.IsFalse(plan.HayQueTocarCupones);
            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_CambiarDeClienteConElCupon_400()
        {
            A.CallTo(() => repositorio.LeerDelPedido(PEDIDO)).Returns(Task.FromResult(Cupon(EstadosCupon.USADO, pedidoUso: PEDIDO)));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => canje.Comprobar("1", PEDIDO, "20000",
                new List<LinPedidoVta> { Linea("CREMA", 150), LineaCupon() }, "G21", null, "x", pedidoNuevo: false, teniaLineaCupon: true));

            Assert.AreEqual(ReglasCupon.ERROR_OTRO_CLIENTE, ex.Context.ErrorCode);
        }

        [TestMethod]
        public async Task BorrarElPedido_SueltaTodos()
        {
            await canje.LiberarPedido(PEDIDO, "x");

            A.CallTo(() => repositorio.LiberarDelPedido(PEDIDO, 0, "x")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ValidadorCupon_AceptaLaLineaNegativaDelCupon()
        {
            var pedido = new PedidoVentaDTO { Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { Producto = PRODUCTO, Cantidad = -1 } } };

            Assert.IsTrue(new ValidadorCupon(PRODUCTO).EsPedidoValido(pedido, PRODUCTO, null).ValidacionSuperada);
            Assert.IsFalse(new ValidadorCupon(PRODUCTO).EsPedidoValido(pedido, "CREMA", null).ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_ElPipelineAceptaLaLineaDelCupon()
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = CLIENTE, Lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { Producto = "CUPONEVENTO", Cantidad = -1, PrecioUnitario = 41.3223M, AplicarDescuento = false, EsCupon = true }
            } };

            RespuestaValidacion respuesta = NestoAPI.Infraestructure.GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "CUPONEVENTO");

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        [TestMethod]
        public void Portes_ElCuponNoBajaLaBase()
        {
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "CREMA", Cantidad = 1, PrecioUnitario = 100 },
                new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = PRODUCTO, Cantidad = -1, PrecioUnitario = 41.3223M, EsCupon = true }
            };
            var metodo = typeof(NestoAPI.Infraestructure.PedidosVenta.GestorPortes).GetMethod("LineasParaBasePortes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            var resultado = ((IEnumerable<LineaPedidoVentaDTO>)metodo.Invoke(null, new object[] { lineas })).ToList();

            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual("CREMA", resultado.Single().Producto);
        }

        // ---------------- Emisión ----------------

        [TestMethod]
        public async Task Emitir_AlConfirmarLaSenal_ImporteDeLaSenalDesdeElEventoSeisMeses()
        {
            CuponNuevo insertado = null;
            A.CallTo(() => repositorio.LeerDeInscripcion(101)).Returns(Task.FromResult<CuponLeido>(null));
            A.CallTo(() => repositorio.Insertar(A<CuponNuevo>._, A<Func<string>>._)).ReturnsLazily((CuponNuevo c, Func<string> codigo) =>
            {
                insertado = c;
                StringAssert.StartsWith(codigo(), "EV-2610-");
                return Task.FromResult(Cupon(EstadosCupon.EMITIDO));
            });
            IAvisadorCupones avisador = A.Fake<IAvisadorCupones>();
            var emisor = new EmisorCuponesEvento(repositorio, avisador, () => new DateTime(2026, 10, 15));
            var inscripcion = new EventoInscripcion { Id = 101, Empresa = "1", Cliente = "15191", Contacto = "0  ", Estado = "Facturada" };
            var evento = new Evento { Id = 1, Titulo = "Masterclass Cloasma", Fecha = EVENTO, ImporteSenal = 50 };

            await emisor.Emitir(inscripcion, evento, new FacturacionCursoInscripcion { EmpresaFactura = "1", Factura = "CV2600700" }, "SenalesEventos");

            Assert.AreEqual(50, insertado.Importe, "IVA incluido: el de la señal");
            Assert.AreEqual(EVENTO, insertado.ValidoDesde);
            Assert.AreEqual(new DateTime(2027, 4, 30), insertado.Caducidad);
            Assert.AreEqual(EstadosCupon.EMITIDO, insertado.Estado, "Antes del evento el cliente lo ve, pero no vale");
            Assert.AreEqual(101, insertado.EventoInscripcionId);
            Assert.AreEqual("CV2600700", insertado.FacturaOrigen);
            Assert.AreEqual("0", insertado.Contacto);
            A.CallTo(() => avisador.AvisarEmitido(A<CuponLeido>._, inscripcion)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Emitir_YaTieneCupon_NoEmiteOtro()
        {
            A.CallTo(() => repositorio.LeerDeInscripcion(101)).Returns(Task.FromResult(Cupon()));
            var emisor = new EmisorCuponesEvento(repositorio, A.Fake<IAvisadorCupones>(), () => HOY);

            await emisor.Emitir(new EventoInscripcion { Id = 101, Empresa = "1", Cliente = "15191", Estado = "Facturada" },
                new Evento { Id = 1, Fecha = EVENTO, ImporteSenal = 50 }, null, "x");

            A.CallTo(() => repositorio.Insertar(A<CuponNuevo>._, A<Func<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Emitir_PagoTardeDeQuienNoVino_SinCupon()
        {
            var emisor = new EmisorCuponesEvento(repositorio, A.Fake<IAvisadorCupones>(), () => HOY);

            await emisor.Emitir(new EventoInscripcion { Id = 101, Empresa = "1", Cliente = "15191", Estado = "NoAsistio" },
                new Evento { Id = 1, Fecha = EVENTO, ImporteSenal = 50 }, null, "x");

            A.CallTo(() => repositorio.Insertar(A<CuponNuevo>._, A<Func<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Emitir_DespuesDelEvento_YaActivo()
        {
            A.CallTo(() => repositorio.LeerDeInscripcion(101)).Returns(Task.FromResult<CuponLeido>(null));
            CuponNuevo insertado = null;
            A.CallTo(() => repositorio.Insertar(A<CuponNuevo>._, A<Func<string>>._)).ReturnsLazily((CuponNuevo c, Func<string> f) =>
            {
                insertado = c;
                return Task.FromResult(Cupon());
            });
            var emisor = new EmisorCuponesEvento(repositorio, A.Fake<IAvisadorCupones>(), () => HOY);

            await emisor.Emitir(new EventoInscripcion { Id = 101, Empresa = "1", Cliente = "15191", Estado = "Asistio" },
                new Evento { Id = 1, Fecha = EVENTO, ImporteSenal = 50 }, null, "x");

            Assert.AreEqual(EstadosCupon.ACTIVO, insertado.Estado);
        }

        [TestMethod]
        public async Task Avisador_PushAlVendedorYSinCorreoConElInterruptorApagado()
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.Clientes).Returns(Crear(new List<Cliente> { new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "LUNA", Vendedor = "JE " } }));
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            var avisador = new AvisadorCupones(db, () => push, () => correo, () => false, repositorio);

            await avisador.AvisarEmitido(Cupon(), new EventoInscripcion { Id = 101 });

            A.CallTo(() => push.EnviarAVendedor("1", "JE", A<NotificacionPushDTO>.That.Matches(n => n.Cuerpo.Contains("EV-2610-7K3Q") && n.Cuerpo.Contains("100,00 €"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Avisador_ElCorreoLlevaElCodigoYLasNormas()
        {
            using (MailMessage mail = AvisadorCupones.Correo(Cupon(), "Luna", "luna@example.com"))
            {
                StringAssert.Contains(mail.Body, "EV-2610-7K3Q");
                StringAssert.Contains(mail.Body, "al menos 100,00 € de productos");
                StringAssert.Contains(mail.Body, "hasta el 30/04/2027");
                StringAssert.Contains(mail.Body, "Si no asiste al evento, el cupón se pierde.");
                Assert.AreEqual("Su cupón de 50,00 € para su próxima compra", mail.Subject);
            }
        }

        // ---------------- Asistencia, manuales, consulta ----------------

        [TestMethod]
        public async Task NoAsistio_PierdeElCupon_YSiSeCorrigeLoRecupera()
        {
            var servicio = new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO);
            A.CallTo(() => repositorio.AnularDeInscripcion(A<int>._, A<string>._, A<string>._)).Returns(Task.FromResult(1));

            await servicio.AplicarAsistencia(new[] { new KeyValuePair<int, bool>(101, false), new KeyValuePair<int, bool>(102, true) }, "NUEVAVISION\\Reina");

            A.CallTo(() => repositorio.AnularDeInscripcion(101, ServicioCupones.MOTIVO_NO_ASISTIO, "NUEVAVISION\\Reina")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.ReactivarDeInscripcion(102, ServicioCupones.MOTIVO_NO_ASISTIO, "NUEVAVISION\\Reina")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PasarLista_LlamaALosCupones()
        {
            NVEntities db = A.Fake<NVEntities>();
            var inscripciones = new List<EventoInscripcion>
            {
                new EventoInscripcion { Id = 101, EventoId = 1, Empresa = "1", Cliente = "15191", Contacto = "0", Estado = "Facturada", Origen = "Nesto", UsuarioCreacion = "x", Usuario = "x" },
                new EventoInscripcion { Id = 102, EventoId = 1, Empresa = "1", Cliente = "20000", Contacto = "0", Estado = "Facturada", Origen = "Nesto", UsuarioCreacion = "x", Usuario = "x" }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(new List<Evento> { new Evento { Id = 1, Empresa = "1", Titulo = "Masterclass", Fecha = EVENTO, ImporteSenal = 50, Almacen = "REI", Activo = true } }));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(inscripciones));
            A.CallTo(() => db.Clientes).Returns(Crear(new List<Cliente>()));
            A.CallTo(() => db.PagosTPV).Returns(Crear(new List<PagoTPV>()));
            List<KeyValuePair<int, bool>> recibidas = null;
            var servicio = new ServicioInscripcionesEventos(db, () => HOY, () => A.Fake<IServicioPagos>(), (e, u) => "REI", null, null,
                (a, u) => { recibidas = a; return Task.CompletedTask; });
            IPrincipal reina = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Reina"), new Claim(ClaimTypes.Role, "NUEVAVISION\\Tiendas") }, "prueba"));

            await servicio.PasarLista(1, new AsistenciaEventoDTO { Asistentes = new List<int> { 101 } }, reina);

            Assert.AreEqual(2, recibidas.Count);
            Assert.IsTrue(recibidas.Single(r => r.Key == 101).Value);
            Assert.IsFalse(recibidas.Single(r => r.Key == 102).Value);
        }

        [TestMethod]
        public async Task Manual_SoloDireccionYConMotivo()
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.Clientes).Returns(Crear(new List<Cliente> { new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  " } }));
            CuponNuevo insertado = null;
            A.CallTo(() => repositorio.Insertar(A<CuponNuevo>._, A<Func<string>>._)).ReturnsLazily((CuponNuevo c, Func<string> f) =>
            {
                insertado = c;
                StringAssert.StartsWith(f(), "MN-2611-");
                return Task.FromResult(Cupon());
            });
            var servicio = new ServicioCupones(repositorio, db, () => HOY, PRODUCTO);
            IPrincipal vendedor = Usuario("NUEVAVISION\\Jesus", "Ventas");
            IPrincipal direccion = Usuario("NUEVAVISION\\Carlos", "Dirección");

            var prohibido = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                servicio.CrearManual(new CrearCuponManualDTO { Cliente = "15191", Importe = 30, Motivo = "Compensación" }, vendedor));
            var sinMotivo = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                servicio.CrearManual(new CrearCuponManualDTO { Cliente = "15191", Importe = 30 }, direccion));
            await servicio.CrearManual(new CrearCuponManualDTO { Cliente = "15191", Importe = 30, Motivo = "Compensación por el retraso" }, direccion);

            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
            StringAssert.Contains(sinMotivo.Message, "motivo");
            Assert.AreEqual(OrigenesCupon.MANUAL, insertado.Origen);
            Assert.AreEqual(30, insertado.Importe);
            Assert.AreEqual(HOY.Date, insertado.ValidoDesde);
            Assert.AreEqual(HOY.Date.AddMonths(6), insertado.Caducidad);
            Assert.AreEqual(EstadosCupon.ACTIVO, insertado.Estado);
            Assert.AreEqual("Compensación por el retraso", insertado.Motivo);
        }

        [TestMethod]
        public async Task Consulta_LosQueSePuedenUsarPrimero_YElClienteDeLaTiendaSoloLosSuyos()
        {
            CuponLeido usado = Cupon(EstadosCupon.USADO, pedidoUso: 1);
            usado.Id = 8;
            CuponLeido emitido = Cupon(EstadosCupon.EMITIDO, desde: new DateTime(2026, 12, 1));
            emitido.Id = 9;
            A.CallTo(() => repositorio.LeerDelCliente("1", "15191")).Returns(Task.FromResult(new List<CuponLeido> { emitido, usado, Cupon() }));
            var servicio = new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO);

            List<CuponDTO> vigentes = await servicio.LeerDelCliente("1", "15191", false, Usuario("NUEVAVISION\\Jesus", "Ventas"));
            List<CuponDTO> todos = await servicio.LeerDelCliente("1", "15191", true, Usuario("NUEVAVISION\\Jesus", "Ventas"));
            var ajeno = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.LeerDelCliente("1", "15191", false, ClienteTienda("20000")));

            Assert.AreEqual(2, vigentes.Count);
            Assert.AreEqual(7, vigentes[0].Id);
            Assert.IsTrue(vigentes[0].SePuedeUsar);
            Assert.AreEqual(100, vigentes[0].PedidoMinimo);
            Assert.AreEqual(PRODUCTO, vigentes[0].Producto);
            StringAssert.Contains(vigentes[0].Mensaje, "Tiene un cupón de 50,00 €");
            Assert.AreEqual(3, todos.Count);
            Assert.AreEqual(HttpStatusCode.Forbidden, ajeno.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_GetCuponesCliente()
        {
            A.CallTo(() => repositorio.LeerDelCliente("1", "15191")).Returns(Task.FromResult(new List<CuponLeido> { Cupon() }));
            var controlador = new CuponesController(A.Fake<NVEntities>(), new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO))
            {
                Request = new HttpRequestMessage(),
                User = Usuario("NUEVAVISION\\Jesus", "Ventas")
            };

            var resultado = await controlador.GetCuponesCliente("1", "15191") as OkNegotiatedContentResult<List<CuponDTO>>;

            Assert.AreEqual("EV-2610-7K3Q", resultado.Content.Single().Codigo);
        }

        [TestMethod]
        public async Task Controlador_GetCupon_NoExiste404()
        {
            A.CallTo(() => repositorio.LeerPorCodigo("EV-0000-XXXX")).Returns(Task.FromResult<CuponLeido>(null));
            var controlador = new CuponesController(A.Fake<NVEntities>(), new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO))
            {
                Request = new HttpRequestMessage(),
                User = Usuario("NUEVAVISION\\Jesus", "Ventas")
            };

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => controlador.GetCupon("EV-0000-XXXX"));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public async Task Job_ActualizaLosEstadosDeLosCupones()
        {
            bool actualizado = false;
            _ = await InscripcionesEventosJobsService.Procesar(new DependenciasInscripcionesEventos
            {
                CaducarVencidas = () => Task.FromResult(new List<InscripcionEventoDTO>()),
                DarPorAsistidosSinLista = () => Task.FromResult(new List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>>()),
                CorreosTienda = (e, a) => Task.FromResult(new List<string>()),
                Avisar = (u, n) => Task.CompletedTask,
                Correo = A.Fake<IServicioCorreoElectronico>(),
                ActualizarCupones = () => { actualizado = true; return Task.FromResult(2); }
            });

            Assert.IsTrue(actualizado);
        }

        // ---------------- Picking por partes ----------------

        private static LineaPedidoPicking LineaPicking(string producto, int cantidad, decimal total, int reservada) => new LineaPedidoPicking
        {
            Id = producto.GetHashCode(),
            NumeroPedido = 1,
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = producto,
            Cantidad = cantidad,
            Total = total,
            BaseImponible = Math.Round(total / 1.21M, 2),
            CantidadReservada = reservada
        };

        private static Dictionary<string, ProductoParaCupon> ProductosPicking(string empresa, List<string> codigos) => Productos();

        [TestMethod]
        public void Picking_PrimeraEntregaPequena_ElCuponSeQuedaParaLaSiguiente()
        {
            var pedido = new PedidoPicking { Empresa = "1", Id = 1, Lineas = new List<LineaPedidoPicking> { LineaPicking("CREMA", 2, 120, 1), LineaPicking(PRODUCTO, -1, -50, 0) } };

            CuponesPicking.Rellenar(new List<PedidoPicking> { pedido }, PRODUCTO, ProductosPicking, p => new List<LineaEntregadaCupon>(), p => 50);
            GestorReservasStock.ReservarCupones(new List<PedidoPicking> { pedido });

            Assert.IsTrue(pedido.Cupon.Retenido, "Sale una crema de 60 €: no llega a 100 €");
            Assert.IsFalse(pedido.Lineas.Any(l => l.EsCupon));
            Assert.AreEqual(60, pedido.Cupon.TotalComputableConEstaEntrega);
        }

        [TestMethod]
        public void Picking_ConLoYaEntregadoLlegaAlMinimo_SaleElCupon()
        {
            var pedido = new PedidoPicking { Empresa = "1", Id = 1, Lineas = new List<LineaPedidoPicking> { LineaPicking("CREMA", 1, 60, 1), LineaPicking(PRODUCTO, -1, -50, 0) } };

            CuponesPicking.Rellenar(new List<PedidoPicking> { pedido }, PRODUCTO, ProductosPicking,
                p => new List<LineaEntregadaCupon> { new LineaEntregadaCupon { Producto = "CREMA", Total = 40 }, new LineaEntregadaCupon { Producto = "CURSO", Total = 200 } }, p => 50);
            GestorReservasStock.ReservarCupones(new List<PedidoPicking> { pedido });

            Assert.IsFalse(pedido.Cupon.Retenido);
            Assert.AreEqual(-1, pedido.Lineas.Single(l => l.EsCupon).CantidadReservada);
            Assert.AreEqual(100, pedido.Cupon.TotalComputableConEstaEntrega, "El curso entregado no cuenta");
        }

        [TestMethod]
        public void Picking_PedidosSinCupon_NoSeTocan()
        {
            var pedido = new PedidoPicking { Empresa = "1", Id = 1, Lineas = new List<LineaPedidoPicking> { LineaPicking("CREMA", 1, 60, 1) } };

            CuponesPicking.Rellenar(new List<PedidoPicking> { pedido }, PRODUCTO, (e, c) => throw new InvalidOperationException("no debería leer"), p => null, p => null);
            GestorReservasStock.ReservarCupones(new List<PedidoPicking> { pedido });

            Assert.IsNull(pedido.Cupon);
            Assert.AreEqual(1, pedido.Lineas.Count);
        }

        // ---------------- Verifactu ----------------

        [TestMethod]
        public void Verifactu_FacturaConLaLineaNegativaDelCupon_EsUnaF1Normal()
        {
            var factura = new CabFacturaVta
            {
                Empresa = "1",
                Serie = "NV",
                Número = "NV2600900 ",
                Fecha = HOY,
                CifNif = "12345678Z",
                NombreFiscal = "CENTRO DE ESTÉTICA LUNA",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 100.00M, ImporteIVA = 21.00M },
                    new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = -41.32M, ImporteIVA = -8.6772M }
                }
            };

            VerifactuFacturaRequest request = MapeadorFacturaVerifactu.Mapear(factura);

            Assert.AreEqual("F1", request.TipoFactura, "Una línea negativa en una factura normal no la hace rectificativa");
            Assert.IsTrue(request.FacturasRectificadas == null || request.FacturasRectificadas.Count == 0, "Sin facturas rectificadas");
            Assert.AreEqual(1, request.DesgloseIva.Count);
            Assert.AreEqual(58.68M, request.DesgloseIva[0].BaseImponible);
            Assert.AreEqual(12.32M, request.DesgloseIva[0].CuotaIva);
            Assert.AreEqual(71.00M, request.ImporteTotal);
        }

        // ---------------- Recargo de equivalencia: el cupón vale siempre su importe exacto (Carlos, 15/10/26) ----------------

        [TestMethod]
        public void Reglas_ConRecargoDeEquivalencia_LaBaseSaleDelIvaMasElRecargo()
        {
            decimal precio = ReglasCupon.PrecioLinea(50, new PorcentajesIvaCupon { Iva = 21, RecargoEquivalencia = 5.2M });
            decimal baseImponible = Math.Round(precio, 2, MidpointRounding.AwayFromZero);
            decimal total = baseImponible + baseImponible * 21 / 100 + baseImponible * 0.052M;

            Assert.AreEqual(39.6197M, precio, "50 / 1,262, no 50 / 1,21");
            Assert.AreEqual(50.00M, Math.Round(total, 2, MidpointRounding.AwayFromZero), "Con RE descuenta 50 € exactos, no 52,15 €");
        }

        [TestMethod]
        public void Reglas_SinRecargo_SigueSiendo50Entre121()
        {
            Assert.AreEqual(41.3223M, ReglasCupon.PrecioLinea(50, new PorcentajesIvaCupon { Iva = 21, RecargoEquivalencia = 0 }));
            Assert.AreEqual(41.3223M, ReglasCupon.PrecioLinea(50, null));
        }

        [TestMethod]
        public async Task Normalizar_ClienteConRecargo_LaLineaDescuentaElImporteExacto()
        {
            var linea = new LineaPedidoVentaDTO { CodigoCupon = "EV-2610-7K3Q" };
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = CLIENTE, iva = "E52", Lineas = new List<LineaPedidoVentaDTO> { linea } };

            await canje.NormalizarLineas(pedido);

            Assert.AreEqual(39.6197M, linea.PrecioUnitario);
            Assert.AreEqual("G21", linea.iva);
            A.CallTo(() => repositorio.LeerPorcentajesIva("1", "E52", "G21")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Normalizar_PedidoSinIva_LaBaseEsElImporte()
        {
            var linea = new LineaPedidoVentaDTO { CodigoCupon = "EV-2610-7K3Q" };
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = CLIENTE, iva = null, Lineas = new List<LineaPedidoVentaDTO> { linea } };

            await canje.NormalizarLineas(pedido);

            Assert.AreEqual(50M, linea.PrecioUnitario, "Sin IVA la línea no lleva impuestos: descuenta 50 €");
        }

        [TestMethod]
        public async Task Normalizar_SinLaCombinacionEnParametrosIva_Usa21()
        {
            A.CallTo(() => repositorio.LeerPorcentajesIva(A<string>._, "XXX", A<string>._)).Returns(Task.FromResult<PorcentajesIvaCupon>(null));
            var linea = new LineaPedidoVentaDTO { CodigoCupon = "EV-2610-7K3Q" };

            await canje.NormalizarLineas(new PedidoVentaDTO { empresa = "1", cliente = CLIENTE, iva = "XXX", Lineas = new List<LineaPedidoVentaDTO> { linea } });

            Assert.AreEqual(41.3223M, linea.PrecioUnitario);
        }

        [TestMethod]
        public async Task Comprobar_ClienteConRecargo_ElServidorCorrigeElPrecioDeLaLinea()
        {
            await canje.NormalizarLineas(new PedidoVentaDTO { empresa = "1", iva = "E52", Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { CodigoCupon = "EV-2610-7K3Q" } } });
            LinPedidoVta cupon = LineaCupon(precio: 41.3223M);
            bool recalculado = false;

            _ = await canje.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 120), cupon }, "E52",
                l => recalculado = true, "x", pedidoNuevo: true, teniaLineaCupon: false);

            Assert.AreEqual(39.6197M, cupon.Precio, "Con RE la línea antigua a /1,21 descontaría 52,15 €");
            Assert.IsTrue(recalculado);
        }

        /// <summary>NestoAPI#612 (Carlos, 16/10/26): cliente exento, pedido sin IVA: el cupón descuenta 50 € de base (al modificar el pedido).</summary>
        [TestMethod]
        public async Task Comprobar_PedidoSinIva_LaBaseEsElImporte()
        {
            await canje.NormalizarLineas(new PedidoVentaDTO { empresa = "1", iva = null, Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { CodigoCupon = "EV-2610-7K3Q" } } });
            LinPedidoVta cupon = LineaCupon(precio: 41.3223M);
            bool recalculado = false;

            _ = await canje.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("CREMA", 120), cupon }, null,
                l => recalculado = true, "x", pedidoNuevo: true, teniaLineaCupon: false);

            Assert.AreEqual(50M, cupon.Precio, "Sin IVA la línea no lleva impuestos: descuenta 50 € de base");
            Assert.IsTrue(recalculado);
        }

        // ---------------- Devolución del curso: anular el cupón (Carlos, 15/10/26) ----------------

        [TestMethod]
        public async Task Anular_CuponSinUsar_QuedaAnulado()
        {
            A.CallTo(() => repositorio.Anular(7, A<string>._, A<string>._)).Returns(Task.FromResult(1));
            A.CallTo(() => repositorio.LeerPorId(7)).ReturnsLazily(() =>
            {
                CuponLeido c = Cupon(EstadosCupon.ANULADO);
                c.MotivoAnulacion = ServicioCupones.MOTIVO_DEVOLUCION;
                return Task.FromResult(c);
            });
            var servicio = new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO);

            CuponDTO dto = await servicio.Anular("EV-2610-7K3Q", null, Usuario("NUEVAVISION\\Alfredo", "Administración"));

            Assert.AreEqual(EstadosCupon.ANULADO, dto.Estado);
            A.CallTo(() => repositorio.Anular(7, ServicioCupones.MOTIVO_DEVOLUCION, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Anular_CuponYaUsado_AvisaYNoLoToca()
        {
            CuponLeido usado = Cupon(EstadosCupon.USADO, pedidoUso: PEDIDO);
            usado.FacturaUso = "NV2600900";
            A.CallTo(() => repositorio.LeerPorCodigo("EV-2610-7K3Q")).Returns(Task.FromResult(usado));
            var servicio = new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                servicio.Anular("EV-2610-7K3Q", new AnularCuponDTO { Motivo = "Devuelve el curso" }, Usuario("NUEVAVISION\\Carlos", "Dirección")));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual(ServicioCupones.ERROR_USADO_NO_SE_ANULA, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, $"pedido {PEDIDO} (factura NV2600900)");
            A.CallTo(() => repositorio.Anular(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_SinPermiso_403()
        {
            var servicio = new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO);

            var tienda = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.Anular("EV-2610-7K3Q", null, Usuario("NUEVAVISION\\Reina", "Tiendas")));
            var cliente = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.Anular("EV-2610-7K3Q", null, ClienteTienda(CLIENTE)));

            Assert.AreEqual(HttpStatusCode.Forbidden, tienda.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, cliente.StatusCode);
            A.CallTo(() => repositorio.Anular(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_YaAnulado_LoDevuelveSinTocarlo()
        {
            A.CallTo(() => repositorio.LeerPorCodigo("EV-2610-7K3Q")).Returns(Task.FromResult(Cupon(EstadosCupon.ANULADO)));
            var servicio = new ServicioCupones(repositorio, A.Fake<NVEntities>(), () => HOY, PRODUCTO);

            CuponDTO dto = await servicio.Anular("EV-2610-7K3Q", null, Usuario("NUEVAVISION\\Carlos", "Dirección"));

            Assert.AreEqual(EstadosCupon.ANULADO, dto.Estado);
            A.CallTo(() => repositorio.Anular(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        // ---------------- Ayudantes ----------------

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static IPrincipal ClienteTienda(string cliente) =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", cliente) }, "prueba"));

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
