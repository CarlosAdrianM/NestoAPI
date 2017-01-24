using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using System.Collections.Generic;
using NestoAPI.Models;
using System.Linq;

namespace NestoAPI.Tests.Models.Picking
{
    [TestClass]
    public class GestorReservasStockTest
    {
        [TestMethod]
        public void GestorReservasStock_Reservar_siLaCantidadEsExactaLaAsignaEntera()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
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

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 6
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();
            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(6, linea.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_Reservar_siLaCantidadEsMenorQueElStockLaAsignaEnteraTambien()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
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

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 16
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(6, linea.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_Reservar_siLaCantidadEsMayorQueElStockSoloAsignaElStockDisponible()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 16,
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

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 10
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);
            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(10, linea.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_Reservar_siHayDosLineasQueSumanMasCantidadDelStockDisponibleLaSegundaSeAsignaParcialmente()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 16,
                BaseImponible = 100,
                CantidadReservada = 0,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
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

            PedidoPicking pedido2 = new PedidoPicking
            {
                Id = 2,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido2.Lineas.Add(linea2);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);
            candidatos.Add(pedido2);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 20
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(16, linea.CantidadReservada);
            Assert.AreEqual(4, linea2.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_Reservar_siHayUnPedidoQueEsNotaEntregaNoReservaProductos()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 16,
                BaseImponible = 100,
                CantidadReservada = 0,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
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
                EsNotaEntrega = true,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            PedidoPicking pedido2 = new PedidoPicking
            {
                Id = 2,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido2.Lineas.Add(linea2);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);
            candidatos.Add(pedido2);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 20
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(0, linea.CantidadReservada);
            Assert.AreEqual(7, linea2.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_Reservar_siHayUnPedidoQueEsNotaEntregaSiReservaCuentasContables()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "A",
                Cantidad = 16,
                BaseImponible = 0,
                CantidadReservada = 0,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
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
                EsNotaEntrega = true,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            PedidoPicking pedido2 = new PedidoPicking
            {
                Id = 2,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido2.Lineas.Add(linea2);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);
            candidatos.Add(pedido2);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 20
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(16, linea.CantidadReservada);
            Assert.AreEqual(7, linea2.CantidadReservada);
        }


        [TestMethod]
        public void GestorReservasStock_Reservar_siHayDosTieneQueCogerStockSiempreLaMasAntigua()
        {
            LineaPedidoPicking lineaMasNueva = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 16,
                BaseImponible = 100,
                CantidadReservada = 0,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking lineaMasAntigua = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
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
            pedido.Lineas.Add(lineaMasNueva);

            PedidoPicking pedido2 = new PedidoPicking
            {
                Id = 2,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido2.Lineas.Add(lineaMasAntigua);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);
            candidatos.Add(pedido2);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 20
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(7, lineaMasAntigua.CantidadReservada);
            Assert.AreEqual(13, lineaMasNueva.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_Reservar_siEsCuentaContableLaAsignaEntera()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "624HOLA",
                Cantidad = 6,
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

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 1
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(6, linea.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_Reservar_siHayDosTieneQueReservarLaMasAntiguaAunqueNoSalgaEnPicking()
        {
            LineaPedidoPicking lineaMasNueva = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 16,
                BaseImponible = 100,
                CantidadReservada = 0,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking lineaMasAntigua = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
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
            pedido.Lineas.Add(lineaMasNueva);

            PedidoPicking pedido2 = new PedidoPicking
            {
                Id = 2,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido2.Lineas.Add(lineaMasAntigua);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);
            candidatos.Add(pedido2);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 20
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();
            candidatos.Remove(pedido2);

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(13, lineaMasNueva.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_Reservar_siEsLineaDeTextoYNoEsPedidoEspecialLaAsignaEntera()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.TEXTO,
                Producto = "624HOLA",
                Cantidad = 6,
                BaseImponible = 100,
                CantidadReservada = 0,
                FechaEntrega = new DateTime(),
                EsPedidoEspecial = false
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

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 1
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(6, linea.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_Reservar_siEsLineaDeTextoYEsPedidoEspecialNoAsignaNada()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.TEXTO,
                Producto = "624HOLA",
                Cantidad = 6,
                BaseImponible = 100,
                CantidadReservada = 0,
                FechaEntrega = new DateTime(),
                EsPedidoEspecial = true
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

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            // Esto lo rellenaría en ejecución el servicio RellenadorStocksService
            StockProducto stock = new StockProducto
            {
                Producto = "A",
                StockDisponible = 1
            };
            List<StockProducto> stocks = new List<StockProducto>();
            stocks.Add(stock);

            List<LineaPedidoPicking> lineas = candidatos.Where(c => !c.EsNotaEntrega).SelectMany(l => l.Lineas).OrderBy(l => l.Id).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, lineas);

            Assert.AreEqual(0, linea.CantidadReservada);
        }

        [TestMethod]
        public void GestorReservasStock_BorrarLineasEntregaFutura_despuesDeEjecutarloLasLineasFuturasNoEstan()
        {
            LineaPedidoPicking lineaParaEntregaInmediata = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 16,
                BaseImponible = 100,
                CantidadReservada = 0,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking lineaParaEntregaFutura = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 0,
                FechaEntrega = new DateTime().AddDays(7)
            };

            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(lineaParaEntregaInmediata);

            PedidoPicking pedido2 = new PedidoPicking
            {
                Id = 2,
                ServirJunto = false,
                EsTiendaOnline = false,
                EsNotaEntrega = false,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido2.Lineas.Add(lineaParaEntregaFutura);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);
            candidatos.Add(pedido2);

            GestorReservasStock.BorrarLineasQueNoDebenSalir(candidatos, new DateTime().AddDays(1));
            
            Assert.AreEqual(1, pedido.Lineas.Count);
            Assert.AreEqual(0, pedido2.Lineas.Count);
        }

        [TestMethod]
        public void GestorReservasStock_BorrarLineasTextoSinOtroProducto_despuesDeEjecutarloLasLineasDeTextoSinProductoQueNoSonNotaDeEntregaNoEstan()
        {
            LineaPedidoPicking lineaTexto = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.TEXTO,
                Producto = "A",
                Cantidad = 1,
                BaseImponible = 100,
                CantidadReservada = 1,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking lineaProducto = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
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
            pedido.Lineas.Add(lineaTexto);
            pedido.Lineas.Add(lineaProducto);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            GestorReservasStock.BorrarLineasQueNoDebenSalir(candidatos, new DateTime());

            Assert.AreEqual(1, pedido.Lineas.Count); // la del producto
        }

        [TestMethod]
        public void GestorReservasStock_BorrarLineasTextoSinOtroProducto_despuesDeEjecutarloLasLineasDeTextoSinProductoQueSiSonNotaDeEntregaSiEstan()
        {
            LineaPedidoPicking lineaTexto = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.TEXTO,
                Producto = "A",
                Cantidad = 1,
                BaseImponible = 100,
                CantidadReservada = 1,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking lineaProducto = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
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
                EsNotaEntrega = true,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(lineaTexto);
            pedido.Lineas.Add(lineaProducto);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            GestorReservasStock.BorrarLineasQueNoDebenSalir(candidatos, new DateTime());

            Assert.AreEqual(2, pedido.Lineas.Count); // la del producto y la de texto
        }

        [TestMethod]
        public void GestorReservasStock_BorrarLineasTextoSinOtroProducto_despuesDeEjecutarloLasLineasDeTextoSinProductoPeroConCuentaContableQueSiSonNotaDeEntregaSiEstan()
        {
            LineaPedidoPicking lineaTexto = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.TEXTO,
                Producto = null,
                Cantidad = 1,
                BaseImponible = 0,
                CantidadReservada = 1,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking lineaProducto = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "HOLA123",
                Cantidad = 1,
                BaseImponible = 0,
                CantidadReservada = 1,
                FechaEntrega = new DateTime()
            };

            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = true,
                EsTiendaOnline = false,
                EsNotaEntrega = true,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(lineaTexto);
            pedido.Lineas.Add(lineaProducto);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            GestorReservasStock.BorrarLineasQueNoDebenSalir(candidatos, new DateTime());

            Assert.AreEqual(2, pedido.Lineas.Count); // la del producto y la de texto
        }

        [TestMethod]
        public void GestorReservasStock_BorrarLineasTextoSinOtroProducto_despuesDeEjecutarloLasLineasDeTextoConProductoSiEstan()
        {
            LineaPedidoPicking lineaTexto = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.TEXTO,
                Producto = "A",
                Cantidad = 1,
                BaseImponible = 100,
                CantidadReservada = 1,
                FechaEntrega = new DateTime()
            };

            LineaPedidoPicking lineaProducto = new LineaPedidoPicking
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
            pedido.Lineas.Add(lineaTexto);
            pedido.Lineas.Add(lineaProducto);

            // Esto lo rellenaría en ejecución el servicio RellenadorPickingService
            List<PedidoPicking> candidatos = new List<PedidoPicking>();
            candidatos.Add(pedido);

            GestorReservasStock.BorrarLineasQueNoDebenSalir(candidatos, new DateTime());

            Assert.AreEqual(2, pedido.Lineas.Count); // la del producto y la de texto
        }

    }
}
