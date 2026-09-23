using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#517: las piezas que protegen los cálculos de la plantilla (sugerencias de ofertas y modo de
    /// servicio): caché de lecturas por petición, interruptor sin publicar, caché por huella del pedido y
    /// stock precargado.
    /// </summary>
    [TestClass]
    public class ProteccionesSugerenciasTests
    {
        #region ServicioPreciosCacheado

        [TestMethod]
        public void ServicioPreciosCacheado_LeeCadaProductoUnaSolaVez_AunqueCambieElRelleno()
        {
            IServicioPrecios real = A.Fake<IServicioPrecios>();
            A.CallTo(() => real.BuscarProducto(A<string>._)).Returns(new Producto { Número = "38093" });
            var cache = new ServicioPreciosCacheado(real);

            _ = cache.BuscarProducto("38093");
            _ = cache.BuscarProducto("38093   ");
            _ = cache.BuscarProducto("38093");

            A.CallTo(() => real.BuscarProducto(A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ServicioPreciosCacheado_RecuerdaTambienLosNulos()
        {
            IServicioPrecios real = A.Fake<IServicioPrecios>();
            A.CallTo(() => real.BuscarProducto("NOEXISTE")).Returns(null);
            var cache = new ServicioPreciosCacheado(real);

            Assert.IsNull(cache.BuscarProducto("NOEXISTE"));
            Assert.IsNull(cache.BuscarProducto("NOEXISTE"));

            A.CallTo(() => real.BuscarProducto("NOEXISTE")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ServicioPreciosCacheado_DevuelveCopiaDeLasListas()
        {
            // Un validador que modifique la lista que recibe no puede contaminar al siguiente.
            IServicioPrecios real = A.Fake<IServicioPrecios>();
            A.CallTo(() => real.BuscarOfertasPermitidas("38093")).Returns(new List<OfertaPermitida> { new OfertaPermitida { NºOrden = 7 } });
            var cache = new ServicioPreciosCacheado(real);

            cache.BuscarOfertasPermitidas("38093").Clear();

            Assert.AreEqual(1, cache.BuscarOfertasPermitidas("38093").Count);
        }

        [TestMethod]
        public void ServicioPreciosCacheado_LoQueDependeDelPedidoNoSeCachea()
        {
            IServicioPrecios real = A.Fake<IServicioPrecios>();
            var cache = new ServicioPreciosCacheado(real);
            var pedido = new PedidoVentaDTO { Lineas = new List<LineaPedidoVentaDTO>() };

            _ = cache.FiltrarLineas(pedido, "CHAMPU", "DeMarca");
            _ = cache.FiltrarLineas(pedido, "CHAMPU", "DeMarca");

            A.CallTo(() => real.FiltrarLineas(pedido, "CHAMPU", "DeMarca")).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public void ServicioPreciosCacheado_Envolver_NoApilaCapas()
        {
            var cache = new ServicioPreciosCacheado(A.Fake<IServicioPrecios>());

            Assert.AreSame(cache, ServicioPreciosCacheado.Envolver(cache));
        }

        #endregion

        #region InterruptorCacheado

        [TestMethod]
        public void Interruptor_SinFilaOConOtroValor_EstaActivo_ConCeroApagado()
        {
            Assert.IsTrue(InterruptorCacheado.Interpretar(null));
            Assert.IsTrue(InterruptorCacheado.Interpretar("1"));
            Assert.IsTrue(InterruptorCacheado.Interpretar("  "));
            Assert.IsFalse(InterruptorCacheado.Interpretar("0"));
            Assert.IsFalse(InterruptorCacheado.Interpretar(" 0 "));
        }

        [TestMethod]
        public void Interruptor_SeReleeSoloCuandoCaducaLoLeido()
        {
            DateTime ahora = new DateTime(2026, 9, 23, 11, 0, 0);
            int lecturas = 0;
            string valor = "1";
            var interruptor = new InterruptorCacheado(() => { lecturas++; return valor; }, TimeSpan.FromSeconds(60), () => ahora);

            Assert.IsTrue(interruptor.EstaActivo());
            valor = "0";
            ahora = ahora.AddSeconds(30);
            Assert.IsTrue(interruptor.EstaActivo(), "Dentro del minuto vale lo leído");
            ahora = ahora.AddSeconds(31);
            Assert.IsFalse(interruptor.EstaActivo(), "Pasado el minuto se relee y se apaga");
            Assert.AreEqual(2, lecturas);
        }

        [TestMethod]
        public void Interruptor_SiFallaLaLectura_MantieneLoUltimoQueSupo()
        {
            DateTime ahora = new DateTime(2026, 9, 23, 11, 0, 0);
            bool fallar = false;
            var interruptor = new InterruptorCacheado(() => fallar ? throw new InvalidOperationException("BD caída") : "0",
                TimeSpan.FromSeconds(60), () => ahora);

            Assert.IsFalse(interruptor.EstaActivo());
            fallar = true;
            ahora = ahora.AddMinutes(5);
            Assert.IsFalse(interruptor.EstaActivo(), "Un fallo al leer no lo vuelve a encender");
        }

        #endregion

        #region CachePorHuellaPedido

        private static PedidoVentaDTO Pedido(string cliente, params (string producto, int cantidad, decimal precio)[] lineas)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = cliente, contacto = "0", Lineas = new List<LineaPedidoVentaDTO>() };
            foreach (var l in lineas)
            {
                pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = l.producto, Cantidad = l.cantidad, PrecioUnitario = l.precio, almacen = "ALG", tipoLinea = 1 });
            }
            return pedido;
        }

        [TestMethod]
        public void Huella_NoDependeDelOrdenNiDelRellenoNiDeLaEscala()
        {
            string a = CachePorHuellaPedido.Huella(Pedido("15191", ("38093", 6, 10m), ("SINOF", 1, 5m)));
            string b = CachePorHuellaPedido.Huella(Pedido("15191  ", ("SINOF   ", 1, 5.00m), ("38093", 6, 10.0m)));

            Assert.AreEqual(a, b);
        }

        [TestMethod]
        public void Huella_CambiaConCualquierCambioReal()
        {
            string original = CachePorHuellaPedido.Huella(Pedido("15191", ("38093", 6, 10m)));

            Assert.AreNotEqual(original, CachePorHuellaPedido.Huella(Pedido("15191", ("38093", 7, 10m))), "cantidad");
            Assert.AreNotEqual(original, CachePorHuellaPedido.Huella(Pedido("15191", ("38093", 6, 9m))), "precio");
            Assert.AreNotEqual(original, CachePorHuellaPedido.Huella(Pedido("15192", ("38093", 6, 10m))), "cliente");
            PedidoVentaDTO conOferta = Pedido("15191", ("38093", 6, 10m));
            conOferta.Lineas.First().oferta = 3;
            Assert.AreNotEqual(original, CachePorHuellaPedido.Huella(conOferta), "oferta");
            PedidoVentaDTO conDescuento = Pedido("15191", ("38093", 6, 10m));
            conDescuento.Lineas.First().DescuentoLinea = 0.1m;
            Assert.AreNotEqual(original, CachePorHuellaPedido.Huella(conDescuento), "descuento");
        }

        [TestMethod]
        public void ObtenerOCalcular_ElMismoPedidoNoSeRecalcula_YCadaEspacioVaAparte()
        {
            // Cliente inventado para no cruzarse con otros tests (la caché es de proceso).
            PedidoVentaDTO pedido = Pedido(Guid.NewGuid().ToString("N"), ("38093", 6, 10m));
            int calculos = 0;
            Func<List<string>> calcular = () => { calculos++; return new List<string> { "x" }; };

            _ = CachePorHuellaPedido.ObtenerOCalcular("EspacioA", pedido, calcular);
            _ = CachePorHuellaPedido.ObtenerOCalcular("EspacioA", pedido, calcular);
            Assert.AreEqual(1, calculos, "La repetición sale de la caché");

            _ = CachePorHuellaPedido.ObtenerOCalcular("EspacioB", pedido, calcular);
            Assert.AreEqual(2, calculos, "Otro cálculo no reutiliza el resultado del primero");
        }

        #endregion

        #region GestorStocksPrecargado

        private static (IServicioGestorStocks servicio, GestorStocks real) Stocks(int stockAlg, int pendientesAlg, int stockSedes, int pendientesTotal, int reposicion)
        {
            IServicioGestorStocks servicio = A.Fake<IServicioGestorStocks>();
            A.CallTo(() => servicio.Stock("38093", "ALG")).Returns(stockAlg);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("38093", "ALG")).Returns(pendientesAlg);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("38093")).Returns(stockSedes - pendientesTotal + reposicion);
            var resumen = new ResumenStocksProductos();
            ResumenStocksProductos.Sumar(resumen.StockAlmacen, ResumenStocksProductos.Clave("38093", "ALG"), stockAlg);
            ResumenStocksProductos.Sumar(resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave("38093", "ALG"), pendientesAlg);
            ResumenStocksProductos.Sumar(resumen.StockSedes, ResumenStocksProductos.Clave("38093"), stockSedes);
            ResumenStocksProductos.Sumar(resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave("38093"), pendientesTotal);
            ResumenStocksProductos.Sumar(resumen.PendienteReposicion, ResumenStocksProductos.Clave("38093"), reposicion);
            A.CallTo(() => servicio.LeerResumenStocks(A<IEnumerable<string>>._)).Returns(resumen);
            return (servicio, new GestorStocks(servicio));
        }

        [TestMethod]
        public void StocksPrecargado_DaElMismoColorQueElGestorReal()
        {
            // (stock ALG, pendientes ALG, stock sedes, pendientes total, reposición, cantidad)
            var casos = new[]
            {
                (10, 2, 10, 2, 0, 8),   // verde justo
                (10, 2, 10, 2, 0, 9),   // rojo: no cabe en ALG y en todas las sedes solo hay 8 libres
                (7, 0, 20, 0, 0, 8),    // rosa: hay en otras sedes
                (7, 0, 7, 0, 3, 9),     // rosa gracias a la reposición
                (0, 0, 0, 0, 0, 1)      // rojo
            };
            foreach (var c in casos)
            {
                var (servicio, real) = Stocks(c.Item1, c.Item2, c.Item3, c.Item4, c.Item5);
                IGestorStocks precargado = real.PrecargarParaProductos(new[] { "38093" });

                Assert.AreEqual(real.ColorStock("38093", "ALG", c.Item6), precargado.ColorStock("38093  ", "alg", c.Item6), c.ToString());
            }
        }

        [TestMethod]
        public void StocksPrecargado_NoConsultaProductoAProducto()
        {
            var (servicio, real) = Stocks(10, 0, 10, 0, 0);
            IGestorStocks precargado = real.PrecargarParaProductos(new[] { "38093" });

            _ = precargado.ColorStock("38093", "ALG", 1);

            A.CallTo(() => servicio.LeerResumenStocks(A<IEnumerable<string>>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicio.Stock(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void StocksPrecargado_UnProductoNoPrecargadoVaAlGestorReal()
        {
            var (servicio, real) = Stocks(10, 0, 10, 0, 0);
            IGestorStocks precargado = real.PrecargarParaProductos(new[] { "38093" });

            _ = precargado.ColorStock("OTRO", "ALG", 1);

            A.CallTo(() => servicio.Stock("OTRO", "ALG")).MustHaveHappenedOnceExactly();
        }

        #endregion
    }
}
