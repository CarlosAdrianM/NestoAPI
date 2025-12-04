using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System.Collections.Generic;

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
            Assert.IsFalse(pedido.RetenidoPorPrepago);
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
            Assert.IsFalse(pedido.RetenidoPorPrepago);
        }


        [TestMethod]
        public void PedidoPicking_saleEnPicking_siEsPrepagoYNoEstaPagadoNoSale()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 6,
                CantidadReservada = 6,
                Total = 1
            };
            IRellenadorPrepagosService rellenador = A.Fake<IRellenadorPrepagosService>();
            PedidoPicking pedido = new PedidoPicking(rellenador)
            {
                Id = 1,
                ServirJunto = false,
                PlazosPago = Constantes.PlazosPago.PREPAGO,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.saleEnPicking());
            Assert.IsTrue(pedido.RetenidoPorPrepago);
        }

        [TestMethod]
        public void PedidoPicking_saleEnPicking_siPrepagoMasExtractoClienteCubrenTotalSiSale()
        {
            // Caso real: Total 1723.04€, Prepago 1199.23€, ExtractoCliente -523.81€ (a favor)
            // La suma (1199.23 + 523.81 = 1723.04) cubre el total, debería salir en picking
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 1,
                CantidadReservada = 1,
                Total = 1723.04M
            };

            IRellenadorPrepagosService rellenador = A.Fake<IRellenadorPrepagosService>();

            // Configurar prepago de 1199.23€
            A.CallTo(() => rellenador.Prepagos(A<int>.Ignored))
                .Returns(new List<PrepagoDTO>
                {
                    new PrepagoDTO { Importe = 1199.23M }
                });

            // Configurar extracto cliente con saldo a favor de 523.81€ (importePendiente = -523.81)
            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored))
                .Returns(new List<ExtractoClienteDTO>
                {
                    new ExtractoClienteDTO
                    {
                        importePendiente = -523.81M,
                        estado = null
                    }
                });

            PedidoPicking pedido = new PedidoPicking(rellenador)
            {
                Id = 1,
                ServirJunto = false,
                PlazosPago = Constantes.PlazosPago.PREPAGO,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            // La suma de prepago (1199.23) + extracto a favor (523.81) = 1723.04 cubre el total
            Assert.IsTrue(pedido.saleEnPicking(), "Debería salir en picking porque prepago + extracto cliente cubren el total");
            Assert.IsFalse(pedido.RetenidoPorPrepago);
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
                Iva = "GN",
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
                Iva = "GN",
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
                ImporteOriginalSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO + 1,
                ImporteOriginalNoSobrePedido = 0,
                Iva = "GN",
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
                Iva = "GN",
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
        public void PedidoPicking_hayQueSumarPortes_siLosProductoNoSobrePedidoOriginalesLlegabanAlMinimoYTieneAlgoNOSobrePedidoNoSeSumanPortes()
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
                Iva = "GN",
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);
            pedido.Lineas.Add(linea2);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siEsProductoPrecioPublicoFinalYLlegaAlMinimoNoSumaPortes()
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
                EsPrecioPublicoFinal = true,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE_PRECIO_PUBLICO_FINAL + 1,
                Ruta = RUTA_CON_PORTES,
                Iva = "GN",
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siEsProductoPrecioPublicoFinalYNoLlegaAlMinimoSiSumaPortes()
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
                EsPrecioPublicoFinal = true,
                ImporteOriginalNoSobrePedido = GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE_PRECIO_PUBLICO_FINAL - 1,
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsTrue(pedido.hayQueSumarPortes());
        }

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siTodoSonCuentasContablesYEsNegativaNoSuma()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "60000000",
                Cantidad = -1,
                CantidadReservada = -1,
                BaseImponible = -1M,
                EsSobrePedido = false
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsPrecioPublicoFinal = false,
                ImporteOriginalNoSobrePedido = -1M,
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(linea);

            Assert.IsFalse(pedido.hayQueSumarPortes());
        }
    }
}
