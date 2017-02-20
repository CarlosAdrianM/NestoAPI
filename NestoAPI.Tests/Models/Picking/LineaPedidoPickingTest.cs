using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models.Picking
{
    [TestClass]
    public class LineaPedidoPickingTest
    {
        [TestMethod]
        public void LineaPedidoPickingTest_PasarAPendiente_laLineaAhoraEstaEnEstadoPendiente()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking();
            Assert.IsNotNull(linea);
        }

        [TestMethod]
        public void LineaPedidoPickingTest_BaseImponibleEntrega_siLaEntregaEstaCompletaSumaCorrectamente()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                EsSobrePedido = false, 
                BaseImponible = 100
            };

            Assert.AreEqual(100, linea.BaseImponibleEntrega);
        }


        [TestMethod]
        public void LineaPedidoPickingTest_BaseImponibleEntrega_siLaEntregaEstaIncompletaSumaCorrectamente()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 2,
                EsSobrePedido = false,
                BaseImponible = 100
            };

            Assert.AreEqual((decimal)33.33, Math.Round(linea.BaseImponibleEntrega, 2));
        }

        [TestMethod]
        public void LineaPedidoPickingTest_BaseImponibleEntrega_siLaCantidadEsCeroDevuelveCero()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 0,
                CantidadReservada = 2,
                EsSobrePedido = false,
                BaseImponible = 100
            };

            Assert.AreEqual((decimal)0, Math.Round(linea.BaseImponibleEntrega, 2));
        }

        [TestMethod]
        public void LineaPedidoPickingTest_BaseImponibleEntrega_siLaCantidadReservadaEsCeroDevuelveCero()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 2,
                CantidadReservada = 0,
                EsSobrePedido = false,
                BaseImponible = 100
            };

            Assert.AreEqual((decimal)0, Math.Round(linea.BaseImponibleEntrega, 2));
        }
    }
}
