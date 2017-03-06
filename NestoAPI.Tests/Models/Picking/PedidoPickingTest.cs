using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using System.Collections.Generic;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models.Picking
{
    [TestClass]
    public class PedidoPickingTest
    {
        public const string RUTA_CON_PORTES = "FW";
        public const string RUTA_SIN_PORTES = "AM";

        [TestMethod]
        public void PedidoPicking_Constructor_CrearUnObjeto()
        {
            PedidoPicking pedidoPicking = new PedidoPicking();
            Assert.IsNotNull(pedidoPicking);
        }

        [TestMethod]
        public void PedidoPicking_saleEnPicking_siNoHayDeTodoYEsServirJuntoNoSale()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 5
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = true,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.saleEnPicking());

        }

        [TestMethod]
        public void PedidoPicking_saleEnPicking_siNoHayNingunaLineaNoSale()
        {
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = true,
                Lineas = new List<LineaPedidoPicking>()
            };

            Assert.IsFalse(pedido.saleEnPicking());

        }
        
        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siHayProductosQueNoSonSobrePedidoYElOriginalNoLlegabaAlMinimoSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                EsSobrePedido = false
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO - 1,
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsTrue(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siHayProductosSobrePedidoPeroLaEntregaSiLlegaAlMinimoNoSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO - 1,
                EsSobrePedido = false
            };
            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "B",
                Cantidad = 1,
                CantidadReservada = 1,
                BaseImponible = 2,
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = true,
                ImporteOriginalSobrePedido = 2,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO - 1,
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };

            pedido.Lineas.Add(linea);
            pedido.Lineas.Add(linea2);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siHayProductosSobrePedidoYLaEntregaNoLlegaAlMinimoSiSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO - 10,
                EsSobrePedido = false
            };
            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "B",
                Cantidad = 1,
                CantidadReservada = 1,
                BaseImponible = 2,
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = true,
                ImporteOriginalSobrePedido = 2,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO - 10,
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };

            pedido.Lineas.Add(linea);
            pedido.Lineas.Add(linea2);

            Assert.IsTrue(pedido.hayQueSumarPortes());
        }


        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siHayProductosQueNoSonSobrePedidoYElOriginalLlegabaAlMinimoNoSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                EsSobrePedido = false
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                Ruta = RUTA_CON_PORTES,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO + 1,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siTodosLosProductosSonSobrePedidoYLleganAlImporteMinimoNoSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO + 1,
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siTodosLosProductosSonSobrePedidoYNoLleganAlImporteMinimoNiElOriginalLlegaAlImporteExentoDePortesSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                Producto = "A",
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Cantidad = 6,
                CantidadReservada = 6,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO - 1,
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_SIN_PORTES - 1,
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsTrue(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siTodosLosProductosSonSobrePedidoYNoLleganAlImporteMinimoPeroElOriginalSiLlegabaAlImporteExentoDePortesNoSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                Producto = "A",
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Cantidad = 6,
                CantidadReservada = 6,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO - 1,
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_SIN_PORTES + 1,
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siYaTienePortesNoHayQueSumarlosDeNuevo()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "62400003",
                Cantidad = 6,
                CantidadReservada = 0,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO - 1,
                EsSobrePedido = true
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO - 1,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siNoHayStockDeNadaNoHayQueSumarPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 0,
                BaseImponible = 0
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                Ruta = RUTA_CON_PORTES,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_SIN_PORTES + 1,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siNoLlegaAlMinimoPeroSuRutaNoEsDePortesNoSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                EsSobrePedido = false
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO - 1,
                Ruta = RUTA_SIN_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siHayProductosQueSonSobrePedidoYProductosNoSobrePedidoYElOriginalLlegabaAlMinimoNoSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                EsSobrePedido = false
            };
            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                EsSobrePedido = true
            };


            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                Ruta = RUTA_CON_PORTES,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO + 1,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);
            pedido.Lineas.Add(linea2);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siEsProductoYaFacturadaNoDebeSumarPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                BaseImponible = GestorImportesMinimos.IMPORTE_MINIMO - 1
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                Ruta = RUTA_CON_PORTES,
                EsNotaEntrega = false,
                EsProductoYaFacturado = true,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO - 1,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siLosProductoNoSobrePedidoOriginalesLlegabanAlMinimoNoSeSumanPortes()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                EsSobrePedido = false
            };
            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "B",
                Cantidad = 6,
                CantidadReservada = 0,
                EsSobrePedido = true
            };


            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                Ruta = RUTA_CON_PORTES,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO + 1,
                ImporteOriginalSobrePedido = 1,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);
            pedido.Lineas.Add(linea2);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

    }
}
