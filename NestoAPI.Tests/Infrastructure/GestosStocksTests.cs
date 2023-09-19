using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestosStocksTests
    {
        private GestorStocks gestor;
        private PedidoVentaDTO pedido;
        private IServicioGestorStocks servicio;

        public GestosStocksTests()
        {
            pedido = new PedidoVentaDTO();
            servicio = A.Fake<IServicioGestorStocks>();
            gestor = new GestorStocks(servicio);

            A.CallTo(() => servicio.Stock("producto", "almacen")).Returns(1);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("producto", "almacen")).Returns(0);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("producto")).Returns(1);
        }

        [TestMethod]
        public void GestorStocks_HayStockDisponibleDeTodo_SiNoTieneLineasEsVerdadero()
        {
            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsTrue(resultado);
        }

        [TestMethod]
        public void GestorStocks_HayStockDisponibleDeTodo_SiDeUnProductoNoHayStockDevuelveFalso()
        {
            A.CallTo(() => servicio.Stock("producto", "almacen")).Returns(0);
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "producto",
                almacen = "almacen",
                Cantidad = 1
            };
            pedido.Lineas.Add(linea);

            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void GestorStocks_HayStockDisponibleDeTodo_SiDeUnProductoHayStockDevuelveVerdadero()
        {
            A.CallTo(() => servicio.Stock("producto", "REI")).Returns(1);
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "producto",
                almacen = "REI",
                Cantidad = 1
            };
            pedido.Lineas.Add(linea);

            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsTrue(resultado);
        }

        [TestMethod]
        public void GestorStocks_HayStockDisponibleDeTodo_SiDeUnProductoHayStockPeroMenosDeLoVendidoDevuelveFalso()
        {
            A.CallTo(() => servicio.Stock("producto", "almacen")).Returns(1);
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "producto",
                almacen = "almacen",
                Cantidad = 2
            };
            pedido.Lineas.Add(linea);

            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void GestorStocks_HayStockDisponibleDeTodo_SiDeUnProductoHayStockPeroEstaReservadoDevuelveFalso()
        {
            A.CallTo(() => servicio.Stock("producto", Constantes.Almacenes.REINA)).Returns(1);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("producto", Constantes.Almacenes.REINA)).Returns(1);
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "producto",
                almacen = Constantes.Almacenes.REINA,
                Cantidad = 1
            };
            pedido.Lineas.Add(linea);

            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void GestorStocks_HayStockDisponibleDeTodo_SiDeUnProductoHayDisponiblePeroEstaReservadoEnOtroAlmacenDevuelveFalso()
        {
            A.CallTo(() => servicio.Stock("producto", "almacen")).Returns(1);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("producto", "almacen")).Returns(0);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("producto")).Returns(0);
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "producto",
                almacen = "almacen",
                Cantidad = 1
            };
            pedido.Lineas.Add(linea);

            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void GestorStocks_HayStockDisponibleDeTodo_SiDeUnProductoHayDisponiblePeroMenosDeLoVendidoDevuelveFalso()
        {
            A.CallTo(() => servicio.Stock("producto", "almacen")).Returns(1);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen("producto", "almacen")).Returns(0);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes("producto")).Returns(1);
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "producto",
                almacen = "almacen",
                Cantidad = 2
            };
            pedido.Lineas.Add(linea);

            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsFalse(resultado);
        }
    }
}
