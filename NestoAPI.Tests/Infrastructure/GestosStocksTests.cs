using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                producto = "producto",
                almacen = "almacen",
                cantidad = 1
            };
            pedido.LineasPedido.Add(linea);

            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void GestorStocks_HayStockDisponibleDeTodo_SiDeUnProductoHayStockDevuelveVerdadero()
        {
            A.CallTo(() => servicio.Stock("producto", "REI")).Returns(1);
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                producto = "producto",
                almacen = "REI",
                cantidad = 1
            };
            pedido.LineasPedido.Add(linea);

            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsTrue(resultado);
        }

        [TestMethod]
        public void GestorStocks_HayStockDisponibleDeTodo_SiDeUnProductoHayStockPeroMenosDeLoVendidoDevuelveFalso()
        {
            A.CallTo(() => servicio.Stock("producto", "almacen")).Returns(1);
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                producto = "producto",
                almacen = "almacen",
                cantidad = 2
            };
            pedido.LineasPedido.Add(linea);

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
                producto = "producto",
                almacen = Constantes.Almacenes.REINA,
                cantidad = 1
            };
            pedido.LineasPedido.Add(linea);

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
                producto = "producto",
                almacen = "almacen",
                cantidad = 1
            };
            pedido.LineasPedido.Add(linea);

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
                producto = "producto",
                almacen = "almacen",
                cantidad = 2
            };
            pedido.LineasPedido.Add(linea);

            bool resultado = gestor.HayStockDisponibleDeTodo(pedido);

            Assert.IsFalse(resultado);
        }
    }
}
