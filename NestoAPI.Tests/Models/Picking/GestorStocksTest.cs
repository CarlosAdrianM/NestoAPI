﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using System.Collections.Generic;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models.Picking
{
    [TestClass]
    public class GestorStocksTest
    {
        [TestMethod]
        public void GestorStock_HayStockDeAlgo_siUnProductoTieneStockDevuelveTrue()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 1,
                FechaEntrega = new DateTime()
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);
            GestorStocks gestor = new GestorStocks(pedido);

            Assert.IsTrue(gestor.HayStockDeAlgo());
        }

        [TestMethod]
        public void GestorStock_HayStockDeAlgo_siHayUnaCuentaContableEnUnaNotaDeEntregaDevuelveTrue()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "A",
                Cantidad = 1,
                BaseImponible = 0,
                CantidadReservada = 1,
                FechaEntrega = new DateTime()
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = true,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);
            GestorStocks gestor = new GestorStocks(pedido);

            Assert.IsTrue(gestor.HayStockDeAlgo());
        }

        [TestMethod]
        public void GestorStocks_HayDeTodo_siSoloTieneUnProductoYTieneStockDevuelveTrue()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 7,
                FechaEntrega = new DateTime()
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);
            GestorStocks gestor = new GestorStocks(pedido);

            Assert.IsTrue(gestor.HayStockDeTodo());
        }
        
        [TestMethod]
        public void GestorStocks_HayDeTodo_siTieneDosProductosYUnoNoTieneStockDevuelveFalse()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 7,
                FechaEntrega = new DateTime()
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "B",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 6,
                FechaEntrega = new DateTime()
            };
            
            pedido.Lineas.Add(linea2);

            GestorStocks gestor = new GestorStocks(pedido);

            Assert.IsFalse(gestor.HayStockDeTodo());
        }

        [TestMethod]
        public void GestorStock_HayDeTodo_siHayUnaLineaDeCuentaContableTieneQuePonerQueTieneStock()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 0,
                FechaEntrega = new DateTime()
            };

            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            GestorStocks gestor = new GestorStocks(pedido);

            Assert.IsTrue(gestor.HayStockDeTodo());
        }

        [TestMethod]
        public void GestorStocks_TodoLoQueTieneStockEsSobrePedido_siHayUnProductoQueNoEsSobrePedidoConStockDevuelveFalse()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 7,
                FechaEntrega = new DateTime(),
                EsSobrePedido = false
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            GestorStocks gestor = new GestorStocks(pedido);

            Assert.IsFalse(gestor.TodoLoQueTieneStockEsSobrePedido());
        }

        [TestMethod]
        public void GestorStocks_TodoLoQueTieneStockEsSobrePedido_siElUnicoProductoQueHayEsSobrePedidoYTieneStockDevuelveTrue()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 7,
                FechaEntrega = new DateTime(),
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            GestorStocks gestor = new GestorStocks(pedido);

            Assert.IsTrue(gestor.TodoLoQueTieneStockEsSobrePedido());
        }
    }
}
