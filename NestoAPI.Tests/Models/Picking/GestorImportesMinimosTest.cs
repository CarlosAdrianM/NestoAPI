using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using System.Collections.Generic;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models.Picking
{
    [TestClass]
    public class GestorImportesMinimosTest
    {
        private const string RUTA_CON_PORTES = "FW";
        private const string RUTA_SIN_PORTES = "AM";

        [TestMethod]
        public void GestorImportesMinimos_LosProductosSobrePedidoLleganAlImporteMinimo_siNoHayNingunProductoSobrePedidoDevuelveFalse()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 25,
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
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            GestorImportesMinimos gestor = new GestorImportesMinimos(pedido);

            Assert.IsFalse(gestor.LosProductosSobrePedidoLleganAlImporteMinimo());
        }

        [TestMethod]
        public void GestorImportesMinimos_LosProductosSobrePedidoLleganAlImporteMinimo_siHayLineasSobrePedidoQueSuperanElMinimoDevuelveTrue()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 2500,
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
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            GestorImportesMinimos gestor = new GestorImportesMinimos(pedido);

            Assert.IsTrue(gestor.LosProductosSobrePedidoLleganAlImporteMinimo());
        }

        [TestMethod]
        public void GestorImportesMinimos_LosProductosSobrePedidoLleganAlImporteMinimo_siSoloLasLineasSobrePedidoNoSuperanElMinimoDevuelveFalse()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 25,
                CantidadReservada = 7,
                FechaEntrega = new DateTime(),
                EsSobrePedido = true
            };
            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 2500,
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
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);
            pedido.Lineas.Add(linea2);

            GestorImportesMinimos gestor = new GestorImportesMinimos(pedido);

            Assert.IsFalse(gestor.LosProductosSobrePedidoLleganAlImporteMinimo());
        }

        [TestMethod]
        public void GestorImportesMinimos_LosProductosSobrePedidoLleganAlImporteMinimo_siEsDeTiendaOnlineMiraOtroImporteMinimo()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE + 1,
                CantidadReservada = 7,
                FechaEntrega = new DateTime(),
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                Ruta = RUTA_CON_PORTES,
                EsTiendaOnline = true,
                EsNotaEntrega = false,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE + 1,
                ImporteOriginalSobrePedido = 0,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            GestorImportesMinimos gestor = new GestorImportesMinimos(pedido);

            Assert.IsTrue(gestor.LosProductosSobrePedidoLleganAlImporteMinimo());
        }
        
        [TestMethod]
        public void GestorImportesMinimos_LosProductosNoSobrePedidoOriginalesLlegabanAlImporteMinimo_siEsDeTiendaOnlineMiraOtroImporteMinimo()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE + 1,
                CantidadReservada = 7,
                FechaEntrega = new DateTime(),
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                Ruta = RUTA_CON_PORTES,
                EsTiendaOnline = true,
                EsNotaEntrega = false,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE + 1,
                ImporteOriginalSobrePedido = 0,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            GestorImportesMinimos gestor = new GestorImportesMinimos(pedido);

            Assert.IsTrue(gestor.LosProductosNoSobrePedidoOriginalesLlegabanAlImporteMinimo());
        }

        [TestMethod]
        public void GestorImportesMinimos_LosProductosNoSobrePedidoOriginalesLlegabanAlImporteMinimo_siEsDeRutaSinPortesSiempreLlegan()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE - 1,
                CantidadReservada = 7,
                FechaEntrega = new DateTime(),
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = true,
                EsNotaEntrega = false,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE - 1,
                ImporteOriginalSobrePedido = 0,
                Ruta = RUTA_SIN_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            GestorImportesMinimos gestor = new GestorImportesMinimos(pedido);

            Assert.IsTrue(gestor.LosProductosNoSobrePedidoOriginalesLlegabanAlImporteMinimo());
        }
        
    }
}
