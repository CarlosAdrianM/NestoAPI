using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System;
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

        [TestMethod]
        public void PedidoPicking_saleEnPicking_siPrepagoYDeudaNoVencidaSiSale()
        {
            // Caso: Pedido 60€, Prepago 60€, Deuda cliente 100€ NO vencida
            // Como la deuda no está vencida, el prepago cubre el pedido → SÍ sale en picking
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 1,
                CantidadReservada = 1,
                Total = 60M
            };

            IRellenadorPrepagosService rellenador = A.Fake<IRellenadorPrepagosService>();

            // Prepago de 60€ que cubre exactamente el pedido
            A.CallTo(() => rellenador.Prepagos(A<int>.Ignored))
                .Returns(new List<PrepagoDTO>
                {
                    new PrepagoDTO { Importe = 60M }
                });

            // Deuda de 100€ pero con vencimiento FUTURO (no vencida)
            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored))
                .Returns(new List<ExtractoClienteDTO>
                {
                    new ExtractoClienteDTO
                    {
                        importePendiente = 100M, // Deuda (positivo = nos deben)
                        estado = null,
                        vencimiento = DateTime.Today.AddDays(30) // Vence en 30 días
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

            // El prepago cubre el pedido y la deuda no está vencida → debe salir
            Assert.IsTrue(pedido.saleEnPicking(), "Debería salir en picking: prepago cubre el total y la deuda no está vencida");
            Assert.IsFalse(pedido.RetenidoPorPrepago);
        }

        [TestMethod]
        public void PedidoPicking_saleEnPicking_siPrepagoYDeudaVencidaNoSale()
        {
            // Caso: Pedido 60€, Prepago 60€, Deuda cliente 100€ SÍ vencida
            // Como la deuda está vencida, se descuenta del disponible → NO sale en picking
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 1,
                CantidadReservada = 1,
                Total = 60M
            };

            IRellenadorPrepagosService rellenador = A.Fake<IRellenadorPrepagosService>();

            // Prepago de 60€
            A.CallTo(() => rellenador.Prepagos(A<int>.Ignored))
                .Returns(new List<PrepagoDTO>
                {
                    new PrepagoDTO { Importe = 60M }
                });

            // Deuda de 100€ con vencimiento PASADO (vencida)
            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored))
                .Returns(new List<ExtractoClienteDTO>
                {
                    new ExtractoClienteDTO
                    {
                        importePendiente = 100M, // Deuda (positivo = nos deben)
                        estado = null,
                        vencimiento = DateTime.Today.AddDays(-10) // Venció hace 10 días
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

            // Prepago 60€ - Deuda vencida 100€ = -40€ < 60€ total → NO debe salir
            Assert.IsFalse(pedido.saleEnPicking(), "No debería salir en picking: la deuda vencida hace que no haya suficiente disponible");
            Assert.IsTrue(pedido.RetenidoPorPrepago);
        }

        [TestMethod]
        public void PedidoPicking_saleEnPicking_siPrepagoYMezclaDeudaVencidaYNoVencidaSoloDescuentaVencida()
        {
            // Caso: Pedido 60€, Prepago 60€, Deuda vencida 20€, Deuda no vencida 80€
            // Solo se descuenta la deuda vencida (20€), la no vencida se ignora
            // Disponible = 60€ - 20€ = 40€ < 60€ → NO sale
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 1,
                CantidadReservada = 1,
                Total = 60M
            };

            IRellenadorPrepagosService rellenador = A.Fake<IRellenadorPrepagosService>();

            A.CallTo(() => rellenador.Prepagos(A<int>.Ignored))
                .Returns(new List<PrepagoDTO>
                {
                    new PrepagoDTO { Importe = 60M }
                });

            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored))
                .Returns(new List<ExtractoClienteDTO>
                {
                    new ExtractoClienteDTO
                    {
                        importePendiente = 20M, // Deuda vencida
                        estado = null,
                        vencimiento = DateTime.Today.AddDays(-5)
                    },
                    new ExtractoClienteDTO
                    {
                        importePendiente = 80M, // Deuda NO vencida (se ignora)
                        estado = null,
                        vencimiento = DateTime.Today.AddDays(30)
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

            // Prepago 60€ - Deuda vencida 20€ = 40€ < 60€ → NO sale
            Assert.IsFalse(pedido.saleEnPicking(), "No debería salir: prepago menos deuda vencida no cubre el total");
            Assert.IsTrue(pedido.RetenidoPorPrepago);
        }

        [TestMethod]
        public void PedidoPicking_saleEnPicking_siPrepagoYDeudaVencidaPequenaSiSale()
        {
            // Caso: Pedido 60€, Prepago 80€, Deuda vencida 10€
            // Disponible = 80€ - 10€ = 70€ >= 60€ → SÍ sale
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 1,
                CantidadReservada = 1,
                Total = 60M
            };

            IRellenadorPrepagosService rellenador = A.Fake<IRellenadorPrepagosService>();

            A.CallTo(() => rellenador.Prepagos(A<int>.Ignored))
                .Returns(new List<PrepagoDTO>
                {
                    new PrepagoDTO { Importe = 80M }
                });

            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored))
                .Returns(new List<ExtractoClienteDTO>
                {
                    new ExtractoClienteDTO
                    {
                        importePendiente = 10M, // Deuda vencida pero pequeña
                        estado = null,
                        vencimiento = DateTime.Today.AddDays(-5)
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

            // Prepago 80€ - Deuda vencida 10€ = 70€ >= 60€ → SÍ sale
            Assert.IsTrue(pedido.saleEnPicking(), "Debería salir: prepago menos deuda vencida aún cubre el total");
            Assert.IsFalse(pedido.RetenidoPorPrepago);
        }
    }
}
