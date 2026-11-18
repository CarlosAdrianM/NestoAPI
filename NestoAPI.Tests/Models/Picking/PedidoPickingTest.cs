using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;

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

        // NestoAPI#482: modo de servicio. Sin modo informado manda ServirJunto (los tests de arriba);
        // con modo, una estrategia por modo.

        private static PedidoPicking PedidoConStockParcial(byte? modo, bool servirJunto, bool yaServido)
        {
            return new PedidoPicking
            {
                Id = 1,
                ServirJunto = servirJunto,
                ModoServicio = modo,
                TieneLineasServidas = yaServido,
                Lineas = new List<LineaPedidoPicking>
                {
                    new LineaPedidoPicking { Id = 1, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "A", Cantidad = 6, CantidadReservada = 5, BaseImponible = 60M }
                }
            };
        }

        // NestoAPI#529: si lo único que quedaría pendiente es un regalo (base 0), no se sirve a
        // medias en ningún modo parcial: el regalo acabaría saliendo solo en un envío de 0 €.

        private static PedidoPicking PedidoConRegaloSinStock(byte modo, bool yaServido = false)
        {
            return new PedidoPicking
            {
                Id = 926923,
                ServirJunto = false,
                ModoServicio = modo,
                TieneLineasServidas = yaServido,
                Lineas = new List<LineaPedidoPicking>
                {
                    new LineaPedidoPicking { Id = 1, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "19828", Cantidad = 6, CantidadReservada = 6, BaseImponible = 62.28M },
                    new LineaPedidoPicking { Id = 2, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "44357", Cantidad = 1, CantidadReservada = 0, BaseImponible = 0M }
                }
            };
        }

        [TestMethod]
        public void PedidoPicking_ModosParciales_SiSoloQuedaPendienteElRegalo_NoSaleAMedias()
        {
            foreach (byte modo in new[] { Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
                Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ })
            {
                PedidoPicking pedido = PedidoConRegaloSinStock(modo);

                Assert.IsTrue(pedido.ExigeStockDeTodo(), "modo " + modo);
                Assert.IsFalse(pedido.saleEnPicking(), "modo " + modo + ": el regalo no se queda solo");

                pedido.Lineas[1].CantidadReservada = 1; // llega el regalo
                Assert.IsTrue(pedido.saleEnPicking(), "modo " + modo + ": con el regalo, sale todo");
            }
        }

        [TestMethod]
        public void PedidoPicking_ModosParciales_SiQuedaPendienteAlgoDePago_SaleLoQueHay()
        {
            // Si además del regalo falta algo que se cobra, habrá otra entrega de todos modos:
            // se sirve lo que hay, como siempre.
            PedidoPicking pedido = PedidoConRegaloSinStock(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO);
            pedido.Lineas.Add(new LineaPedidoPicking { Id = 3, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "12634", Cantidad = 40, CantidadReservada = 0, BaseImponible = 66.8M });

            Assert.IsFalse(pedido.ExigeStockDeTodo());
            Assert.IsTrue(pedido.saleEnPicking());
        }

        [TestMethod]
        public void PedidoPicking_ModoServicio_SinInformar_MandaServirJunto()
        {
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TODO_JUNTO, PedidoConStockParcial(null, true, false).ModoServicioEfectivo);
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, PedidoConStockParcial(null, false, false).ModoServicioEfectivo);
            Assert.IsFalse(PedidoConStockParcial(null, true, false).saleEnPicking());
            Assert.IsTrue(PedidoConStockParcial(null, false, false).saleEnPicking());
        }

        [TestMethod]
        public void PedidoPicking_ModoServicio_ServirJuntoMandaSobreLosModos1Y2()
        {
            // Regla del 16/09/26: el Nesto viejo escribe ServirJunto sin conocer el modo. Si lo
            // desmarca sobre un modo 1, se sirve según entre; si lo marca sobre un 2, todo junto.
            Assert.IsTrue(PedidoConStockParcial(Constantes.Pedidos.ModosServicio.TODO_JUNTO, false, false).saleEnPicking());
            Assert.IsFalse(PedidoConStockParcial(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, true, false).saleEnPicking());
            // Y sobre un modo parcial (4) marcado: todo junto
            Assert.IsFalse(PedidoConStockParcial(Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, true, false).saleEnPicking());
        }

        [TestMethod]
        public void PedidoPicking_Modo4_AntesDeLaPrimeraEntrega_SaleLoQueHay()
        {
            PedidoPicking pedido = PedidoConStockParcial(Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, false, yaServido: false);

            Assert.IsFalse(pedido.ExigeStockDeTodo());
            Assert.IsTrue(pedido.saleEnPicking());
        }

        [TestMethod]
        public void PedidoPicking_Modo4_TrasLaPrimeraEntrega_ElRestoSoloSaleDeUnaVez()
        {
            PedidoPicking pedido = PedidoConStockParcial(Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, false, yaServido: true);

            Assert.IsTrue(pedido.ExigeStockDeTodo());
            Assert.IsFalse(pedido.saleEnPicking(), "Con stock parcial y una entrega ya hecha, el resto espera a estar completo");

            pedido.Lineas[0].CantidadReservada = 6; // ya hay stock de todo lo pendiente
            Assert.IsTrue(pedido.saleEnPicking());
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

        // Regresión NestoAPI#202: pedido 916885 (cliente 34991), total 207.87€,
        // prepago 57.87€ + movimiento pendiente del extracto -150€. La suma cubre
        // el total. Reportado en mayo 2026 con FormaPago=PRE; la mitigación temporal
        // fue cambiar a CONTADO manualmente.
        [TestMethod]
        public void PedidoPicking_saleEnPicking_caso916885_PrepagoMasPagoACuentaCubrenTotal_SiSale()
        {
            // Arrange: replica de los datos del pedido 916885.
            var linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "WOW",
                Cantidad = 1,
                CantidadReservada = 1,
                Total = 207.87M
            };

            var rellenador = A.Fake<IRellenadorPrepagosService>();
            A.CallTo(() => rellenador.Prepagos(A<int>.Ignored))
                .Returns(new List<PrepagoDTO>
                {
                    new PrepagoDTO { Importe = 57.87M }
                });
            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored))
                .Returns(new List<ExtractoClienteDTO>
                {
                    new ExtractoClienteDTO
                    {
                        importePendiente = -150M, // "S/Pago a cuenta curso WOW"
                        estado = null,
                        vencimiento = null
                    }
                });

            var pedido = new PedidoPicking(rellenador)
            {
                Id = 916885,
                ServirJunto = false,
                PlazosPago = Constantes.PlazosPago.PREPAGO,
                Lineas = new List<LineaPedidoPicking> { linea }
            };

            // Act + Assert
            Assert.IsTrue(pedido.saleEnPicking(),
                "Prepago 57.87 + saldo a favor 150 = 207.87 cubre el total 207.87, " +
                "debería salir en picking aunque la forma de pago sea PRE.");
            Assert.IsFalse(pedido.RetenidoPorPrepago);
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

        [TestMethod]
        public void PedidoPicking_hayQueSumarPortes_siEsTiendaOnlineYYaLlevaPortes_NoLosBorra()
        {
            // Escenario: pedido de Amazon/TiendaOnline que ya trae portes desde el canal externo.
            // Al sacar picking NO debe intentar añadir más portes (hayQueSumarPortes=false),
            // y los portes existentes se preservan (picking nunca borra líneas de portes).
            LineaPedidoPicking lineaProducto = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 1,
                CantidadReservada = 1,
                BaseImponible = 30,
                EsSobrePedido = false
            };
            LineaPedidoPicking lineaPortes = new LineaPedidoPicking
            {
                Id = 2,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = Constantes.Cuentas.CUENTA_PORTES_ONTIME,
                Cantidad = 1,
                CantidadReservada = 1,
                BaseImponible = Constantes.Portes.Provincial(System.DateTime.Now),
                EsSobrePedido = false
            };
            PedidoPicking pedido = new PedidoPicking
            {
                Id = 1,
                ServirJunto = false,
                EsTiendaOnline = true,
                ImporteOriginalNoSobrePedido = 30,
                Ruta = RUTA_CON_PORTES,
                Lineas = new List<LineaPedidoPicking>()
            };
            pedido.Lineas.Add(lineaProducto);
            pedido.Lineas.Add(lineaPortes);

            // hayQueSumarPortes devuelve false → no se llama a GeneradorPortes.Ejecutar()
            // Por tanto los portes existentes se preservan intactos
            Assert.IsFalse(pedido.hayQueSumarPortes(),
                "Tienda online: no debe intentar añadir portes (los gestiona el canal externo)");
            Assert.AreEqual(2, pedido.Lineas.Count,
                "Las líneas (producto + portes del canal externo) se preservan intactas");
            Assert.IsTrue(pedido.Lineas.Any(l =>
                l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE &&
                l.Producto == Constantes.Cuentas.CUENTA_PORTES_ONTIME),
                "La línea de portes del canal externo sigue presente");
        }
    
        #region NestoAPI#815: factura contra reembolso pendiente de entrega (reembolso diferido, #742)

        // Datos de la incidencia 1013 (Laura, 04/11/26): cliente 31517, pedido prepago 929888 cubierto por su prepago;
        // la NV2617724 (70,87 €, pedido 929773, envío 250452) salió contra reembolso, está en reparto y figura pendiente y
        // vencida porque el reembolso diferido no se contabiliza hasta que la agencia entrega.
        private static PedidoPicking PedidoPrepago929888(IRellenadorPrepagosService rellenador)
        {
            PedidoPicking pedido = new PedidoPicking(rellenador)
            {
                Id = 929888,
                ServirJunto = false,
                PlazosPago = Constantes.PlazosPago.PREPAGO,
                Lineas = new List<LineaPedidoPicking>
                {
                    new LineaPedidoPicking
                    {
                        Id = 1,
                        TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                        Producto = "A",
                        Cantidad = 1,
                        CantidadReservada = 1,
                        Total = 45.30M
                    }
                }
            };
            return pedido;
        }

        private static IRellenadorPrepagosService RellenadorConFacturaContraReembolso(bool pendienteDeEntrega)
        {
            IRellenadorPrepagosService rellenador = A.Fake<IRellenadorPrepagosService>();
            A.CallTo(() => rellenador.Prepagos(A<int>.Ignored))
                .Returns(new List<PrepagoDTO> { new PrepagoDTO { Importe = 45.30M } });
            var extracto = new ExtractoClienteDTO
            {
                importePendiente = 70.87M,
                estado = null,
                documento = "NV2617724 ",
                vencimiento = DateTime.Today.AddDays(-1)
            };
            if (pendienteDeEntrega)
            {
                RellenadorPrepagosService.MarcarReembolsosPendientes(new List<ExtractoClienteDTO> { extracto },
                    new Dictionary<string, int> { { "NV2617724", 929773 } });
            }
            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored))
                .Returns(new List<ExtractoClienteDTO> { extracto });
            return rellenador;
        }

        [TestMethod]
        public void PedidoPicking_CubiertoPorPrepago_FacturaContraReembolsoPendienteDeEntrega_NoCuentaComoDeudaYSale()
        {
            PedidoPicking pedido = PedidoPrepago929888(RellenadorConFacturaContraReembolso(pendienteDeEntrega: true));

            Assert.IsTrue(pedido.CubiertoPorPrepago(), "La factura contra reembolso aún sin entregar no es deuda vencida");
            Assert.IsTrue(pedido.saleEnPicking());
            Assert.IsFalse(pedido.RetenidoPorPrepago);
            Assert.AreEqual(45.30M, pedido.ImporteDisponiblePrepago);
        }

        [TestMethod]
        public void PedidoPicking_CubiertoPorPrepago_FacturaContraReembolsoYaContabilizadaOSinDiferir_RetieneComoAntes()
        {
            // Sin fila pendiente en EnviosAgenciaReembolsosAlEntregar (reembolso ya contabilizado o envío sin diferir):
            // la factura pendiente y vencida es deuda, como siempre.
            PedidoPicking pedido = PedidoPrepago929888(RellenadorConFacturaContraReembolso(pendienteDeEntrega: false));

            Assert.IsFalse(pedido.CubiertoPorPrepago());
            Assert.IsFalse(pedido.saleEnPicking());
            Assert.IsTrue(pedido.RetenidoPorPrepago);
            Assert.AreEqual(MotivoNoSalePicking.RetenidoPorPrepago, pedido.MotivoNoSale);
        }

        [TestMethod]
        public void PedidoPicking_CubiertoPorPrepago_FacturaContraReembolsoPendienteMasOtraDeudaVencida_SoloRetieneLaOtra()
        {
            IRellenadorPrepagosService rellenador = RellenadorConFacturaContraReembolso(pendienteDeEntrega: true);
            List<ExtractoClienteDTO> extractos = rellenador.ExtractosPendientes(929888);
            extractos.Add(new ExtractoClienteDTO
            {
                importePendiente = 10M,
                estado = null,
                documento = "NV2617000",
                vencimiento = DateTime.Today.AddDays(-20)
            });
            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored)).Returns(extractos);
            PedidoPicking pedido = PedidoPrepago929888(rellenador);

            Assert.IsFalse(pedido.saleEnPicking());
            Assert.AreEqual(35.30M, pedido.ImporteDisponiblePrepago, "Solo resta la deuda vencida que no es reembolso pendiente");
            string mensaje = ExplicadorPedidoSinPicking.Error(pedido, null, null).Message;
            StringAssert.Contains(mensaje, "La NV2617724 está pendiente de que la agencia entregue el pedido 929773: no cuenta como deuda.");
        }

        [TestMethod]
        public void RellenadorPrepagos_MarcarReembolsosPendientes_SoloLasDeudasDeEsasFacturas()
        {
            var deEsaFactura = new ExtractoClienteDTO { importePendiente = 70.87M, documento = "NV2617724 " };
            var otraFactura = new ExtractoClienteDTO { importePendiente = 30M, documento = "NV2617000" };
            var cobroAFavor = new ExtractoClienteDTO { importePendiente = -70.87M, documento = "NV2617724" };

            RellenadorPrepagosService.MarcarReembolsosPendientes(new List<ExtractoClienteDTO> { deEsaFactura, otraFactura, cobroAFavor },
                new Dictionary<string, int> { { "NV2617724", 929773 } });

            Assert.IsTrue(deEsaFactura.ReembolsoPendienteDeEntrega);
            Assert.AreEqual(929773, deEsaFactura.PedidoReembolsoPendiente);
            Assert.IsFalse(otraFactura.ReembolsoPendienteDeEntrega);
            Assert.IsFalse(cobroAFavor.ReembolsoPendienteDeEntrega, "Un saldo a favor no es deuda: no se marca");
        }

        [TestMethod]
        public void RellenadorPrepagos_MarcarReembolsosPendientes_SinFacturasPendientes_NoCambiaNada()
        {
            var extracto = new ExtractoClienteDTO { importePendiente = 70.87M, documento = "NV2617724" };

            RellenadorPrepagosService.MarcarReembolsosPendientes(new List<ExtractoClienteDTO> { extracto }, new Dictionary<string, int>());

            Assert.IsFalse(extracto.ReembolsoPendienteDeEntrega);
            Assert.IsNull(extracto.PedidoReembolsoPendiente);
        }

        #endregion

        #region Novedades 1302: el saldo a favor se reparte una sola vez entre los pedidos PRE del cliente

        private const string CLIENTE_42004 = "42004";

        /// <summary>Pedidos y facturas del cliente 42004 (lo que el rellenador lee de la BD para clasificar).</summary>
        private static readonly int[] PEDIDOS_42004 = { 930831, 930835 };
        private static readonly string[] FACTURAS_42004 = { "NV2617724" };

        private static ExtractoClienteDTO ACuenta(decimal importe, string concepto = null)
        {
            return new ExtractoClienteDTO { importePendiente = -importe, estado = "NRM", concepto = concepto };
        }

        /// <summary>Cliente 42004: pago a cuenta con tarjeta de 375,33 (saldo a favor), sin prepagos en ningún pedido. Sin concepto
        /// que nombre un documento = saldo libre; con <paramref name="saldos"/>, esos apuntes clasificados como el rellenador.</summary>
        private static IRellenadorPrepagosService Rellenador42004(List<PedidoPrepagoEnPickingDTO> enPicking = null, decimal saldoAFavor = 375.33M,
            ExtractoClienteDTO[] saldos = null)
        {
            IRellenadorPrepagosService rellenador = A.Fake<IRellenadorPrepagosService>();
            A.CallTo(() => rellenador.Prepagos(A<int>.Ignored)).Returns(new List<PrepagoDTO>());
            List<ExtractoClienteDTO> extractos = saldos != null && saldos.Length > 0
                ? saldos.ToList()
                : new List<ExtractoClienteDTO> { ACuenta(saldoAFavor) };
            ClasificadorSaldoAFavor.Clasificar(extractos, PEDIDOS_42004, FACTURAS_42004);
            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored)).Returns(extractos);
            A.CallTo(() => rellenador.PedidosPrepagoEnPicking(A<int>.Ignored))
                .ReturnsLazily((int pedido) => (enPicking ?? new List<PedidoPrepagoEnPickingDTO>()).Where(p => p.Pedido != pedido).ToList());
            return rellenador;
        }

        private static PedidoPicking PedidoPre42004(IRellenadorPrepagosService rellenador, int id, decimal total, string plazos = Constantes.PlazosPago.PREPAGO)
        {
            return new PedidoPicking(rellenador)
            {
                Id = id,
                Cliente = CLIENTE_42004 + "     ",
                PlazosPago = plazos,
                Lineas = new List<LineaPedidoPicking>
                {
                    new LineaPedidoPicking
                    {
                        Id = id,
                        TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                        Producto = "P" + id,
                        Cantidad = 1,
                        CantidadReservada = 1,
                        BaseImponible = Math.Round(total / 1.21M, 2),
                        Total = total
                    }
                }
            };
        }

        /// <summary>Lo que hace GestorPicking: prepara el reparto y decide por número de pedido.</summary>
        private static Dictionary<int, bool> PasadaDePicking(params PedidoPicking[] candidatos)
        {
            List<PedidoPicking> lista = candidatos.ToList();
            _ = RepartoSaldoAFavorPrepago.Preparar(lista);
            var sale = new Dictionary<int, bool>();
            foreach (PedidoPicking pedido in RepartoSaldoAFavorPrepago.EnOrden(lista))
            {
                sale[pedido.Id] = GestorPicking.DecidirSiSale(pedido);
            }
            return sale;
        }

        // Regresión Novedades 1302 (picking 100105): los dos pedidos PRE del 42004 salían con el mismo pago a cuenta.
        [TestMethod]
        public void PedidoPicking_SaldoAFavor_Caso930831y930835_MismaPasada_SaleElPrimeroYElSegundoNo()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004();
            PedidoPicking pedido930831 = PedidoPre42004(rellenador, 930831, 375.33M);
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Dictionary<int, bool> sale = PasadaDePicking(pedido930835, pedido930831);

            Assert.IsTrue(sale[930831], "El más antiguo se lleva el pago a cuenta y sale");
            Assert.IsFalse(sale[930835], "El mismo dinero no cubre otro pedido");
            Assert.IsFalse(pedido930831.RetenidoPorPrepago);
            Assert.IsTrue(pedido930835.RetenidoPorPrepago);
            Assert.AreEqual(MotivoNoSalePicking.RetenidoPorPrepago, pedido930835.MotivoNoSale);
            Assert.AreEqual(375.33M, pedido930835.ImporteConsumidoPorOtrosPedidos);
            Assert.AreEqual(0M, pedido930835.ImporteDisponiblePrepago);
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_Caso930831y930835_DaIgualElOrdenYCuantasVecesSePregunte()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004();
            PedidoPicking pedido930831 = PedidoPre42004(rellenador, 930831, 375.33M);
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);
            _ = RepartoSaldoAFavorPrepago.Preparar(new List<PedidoPicking> { pedido930835, pedido930831 });

            // Ensayos de antes del bucle («todo ahora», muestras) en cualquier orden
            Assert.IsFalse(pedido930835.CubiertoPorPrepago());
            Assert.IsTrue(pedido930831.CubiertoPorPrepago());
            Assert.IsFalse(pedido930835.CubiertoPorPrepago());
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_Caso930831EnUnPickingAnterior_930835NoSale()
        {
            var enPicking = new List<PedidoPrepagoEnPickingDTO>
            {
                new PedidoPrepagoEnPickingDTO { Pedido = 930831, ImporteEnPicking = 375.33M, Prepagos = 0 }
            };
            IRellenadorPrepagosService rellenador = Rellenador42004(enPicking);
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Assert.IsFalse(PasadaDePicking(pedido930835)[930835]);
            Assert.IsTrue(pedido930835.RetenidoPorPrepago);
            Assert.AreEqual(375.33M, pedido930835.ImporteConsumidoPorOtrosPedidos);

            // Y suelto (sin pasada de picking), igual
            PedidoPicking suelto = PedidoPre42004(rellenador, 930835, 159.72M);
            Assert.IsFalse(suelto.CubiertoPorPrepago());
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_PedidoEnPickingAnteriorConSuPropioPrepago_NoConsumeSaldo()
        {
            var enPicking = new List<PedidoPrepagoEnPickingDTO>
            {
                new PedidoPrepagoEnPickingDTO { Pedido = 930831, ImporteEnPicking = 375.33M, Prepagos = 375.33M }
            };
            PedidoPicking pedido930835 = PedidoPre42004(Rellenador42004(enPicking), 930835, 159.72M);

            Assert.IsTrue(PasadaDePicking(pedido930835)[930835], "El otro lo paga su prepago: el saldo a favor sigue libre");
            Assert.AreEqual(0M, pedido930835.ImporteConsumidoPorOtrosPedidos);
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_SaldoSuficienteParaLosDos_SalenLosDos()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004(saldoAFavor: 535.05M);
            PedidoPicking pedido930831 = PedidoPre42004(rellenador, 930831, 375.33M);
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Dictionary<int, bool> sale = PasadaDePicking(pedido930831, pedido930835);

            Assert.IsTrue(sale[930831]);
            Assert.IsTrue(sale[930835]);
            Assert.IsFalse(pedido930835.RetenidoPorPrepago);
        }

        private const string CONCEPTO_PALOMA_930831 = "S/Pago a cuenta a Paloma Pedido 930831";

        // Carlos 17/11/26: un pago es de UN pedido. Si el pedido pagado no sale (sin stock), su dinero no se lo lleva otro.
        [TestMethod]
        public void PedidoPicking_SaldoAFavor_ElPagadoNoSalePorStock_SuPagoNoCubreOtroPedido()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004(saldos: new[] { ACuenta(375.33M, CONCEPTO_PALOMA_930831) });
            PedidoPicking pedido930831 = PedidoPre42004(rellenador, 930831, 375.33M);
            pedido930831.ServirJunto = true;
            pedido930831.Lineas[0].Cantidad = 2;  // falta una unidad y es «todo junto»
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Dictionary<int, bool> sale = PasadaDePicking(pedido930831, pedido930835);

            Assert.IsFalse(sale[930831]);
            Assert.IsFalse(sale[930835], "El pago a cuenta es del 930831: no cubre el 930835 aunque el 930831 no salga");
            Assert.IsTrue(pedido930835.RetenidoPorPrepago);
            Assert.AreEqual(0M, pedido930835.ImporteDisponiblePrepago);
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_ReservadoAlPedido_MismaPasada_SaleElPagadoYElOtroNo()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004(saldos: new[] { ACuenta(375.33M, CONCEPTO_PALOMA_930831) });
            PedidoPicking pedido930831 = PedidoPre42004(rellenador, 930831, 375.33M);
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Dictionary<int, bool> sale = PasadaDePicking(pedido930835, pedido930831);

            Assert.IsTrue(sale[930831]);
            Assert.IsFalse(sale[930835]);
            Assert.AreEqual(375.33M, pedido930831.ImporteDisponiblePrepago);
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_ReservadoAOtroPedido_NuncaCubreEste_NiSueltoNiConElOtroFacturado()
        {
            // El 930831 ya no está en ningún picking (facturado o sin sacar): su pago a cuenta sigue siendo suyo
            IRellenadorPrepagosService rellenador = Rellenador42004(saldos: new[] { ACuenta(375.33M, CONCEPTO_PALOMA_930831) });
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Assert.IsFalse(PasadaDePicking(pedido930835)[930835]);
            Assert.IsFalse(PedidoPre42004(rellenador, 930835, 159.72M).CubiertoPorPrepago());
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_ElPagadoEnUnPickingAnterior_NoGastaElSaldoLibre()
        {
            var enPicking = new List<PedidoPrepagoEnPickingDTO>
            {
                new PedidoPrepagoEnPickingDTO { Pedido = 930831, ImporteEnPicking = 375.33M, Prepagos = 0 }
            };
            IRellenadorPrepagosService rellenador = Rellenador42004(enPicking, saldos: new[]
            {
                ACuenta(375.33M, CONCEPTO_PALOMA_930831),
                ACuenta(159.72M, "S/Pago a cuenta curso WOW")
            });
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Assert.IsTrue(PasadaDePicking(pedido930835)[930835], "El 930831 lo paga su a cuenta: el libre queda para el 930835");
            Assert.AreEqual(0M, pedido930835.ImporteConsumidoPorOtrosPedidos);
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_NumeroQueNoEsDelCliente_EsSaldoLibre()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004(saldos: new[] { ACuenta(159.72M, "pedido nueva visión 987654 del 17/11/26") });
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Assert.IsTrue(PasadaDePicking(pedido930835)[930835]);
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_ReservadoAUnaFactura_NoCubreNingunPedido()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004(saldos: new[] { ACuenta(159.72M, "Pago factura NV2617724") });
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Assert.IsFalse(PasadaDePicking(pedido930835)[930835]);
            Assert.IsTrue(pedido930835.RetenidoPorPrepago);
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_Libre_ElAntiguoNoSalePorStock_ElSiguienteSeLoLleva()
        {
            // Saldo LIBRE (no nombra ningún documento): no es de nadie, así que si el más antiguo no sale queda para el siguiente
            IRellenadorPrepagosService rellenador = Rellenador42004();
            PedidoPicking pedido930831 = PedidoPre42004(rellenador, 930831, 375.33M);
            pedido930831.ServirJunto = true;
            pedido930831.Lineas[0].Cantidad = 2;
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Dictionary<int, bool> sale = PasadaDePicking(pedido930831, pedido930835);

            Assert.IsFalse(sale[930831]);
            Assert.IsTrue(sale[930835]);
        }

        [TestMethod]
        public void ClasificadorSaldoAFavor_Numeros_SacaTodosLosNumerosDelTexto()
        {
            CollectionAssert.AreEqual(new List<int> { 930614 }, ClasificadorSaldoAFavor.Numeros("Pago TPV Pedido: 930614. Formación + EQUIPO + VIAL"));
            CollectionAssert.AreEqual(new List<int> { 17, 11, 26, 930831 }, ClasificadorSaldoAFavor.Numeros("17/11/26 Pedido 930831"));
            CollectionAssert.AreEqual(new List<int>(), ClasificadorSaldoAFavor.Numeros("S/Pago a cuenta curso WOW"));
            CollectionAssert.AreEqual(new List<int>(), ClasificadorSaldoAFavor.Numeros(null));
            CollectionAssert.AreEqual(new List<string> { "NV2617724" }, ClasificadorSaldoAFavor.Referencias("Pago factura nv2617724."));
        }

        [TestMethod]
        public void ClasificadorSaldoAFavor_Clasificar_PedidoFacturaOLibre()
        {
            var pedidoTpv = ACuenta(100M, "Pago TPV Pedido: 930614. Formación + EQUIPO + VIAL");
            var paloma = ACuenta(375.33M, CONCEPTO_PALOMA_930831);
            var deOtroCliente = ACuenta(10M, "pedido nueva visión 987654");
            var wow = ACuenta(150M, "S/Pago a cuenta curso WOW");
            var factura = ACuenta(20M, "Pago factura NV2617724 de 12,50");
            var porDocumento = new ExtractoClienteDTO { importePendiente = -5M, concepto = "Pago a cuenta", documento = "930831    " };
            var deuda = new ExtractoClienteDTO { importePendiente = 50M, concepto = "Pedido 930831" };

            ClasificadorSaldoAFavor.Clasificar(new List<ExtractoClienteDTO> { pedidoTpv, paloma, deOtroCliente, wow, factura, porDocumento, deuda },
                new List<int> { 930614, 930831 }, new List<string> { "NV2617724 " });

            Assert.AreEqual(930614, pedidoTpv.PedidoReservado);
            Assert.AreEqual(930831, paloma.PedidoReservado);
            Assert.IsTrue(ClasificadorSaldoAFavor.EsLibre(deOtroCliente), "987654 no es del cliente");
            Assert.IsTrue(ClasificadorSaldoAFavor.EsLibre(wow));
            Assert.AreEqual("NV2617724", factura.FacturaReservada);
            Assert.IsNull(factura.PedidoReservado, "12 y 50 no son pedidos del cliente");
            Assert.AreEqual(930831, porDocumento.PedidoReservado);
            Assert.IsNull(deuda.PedidoReservado, "Solo se clasifican los saldos a favor");
            Assert.AreEqual(150M + 10M, ClasificadorSaldoAFavor.Libre(new[] { pedidoTpv, paloma, deOtroCliente, wow, factura, porDocumento, deuda }));
            Assert.AreEqual(375.33M + 5M, ClasificadorSaldoAFavor.ReservadoA(new[] { pedidoTpv, paloma, deOtroCliente, wow, factura, porDocumento, deuda }, 930831));
        }

        [TestMethod]
        public void ClasificadorSaldoAFavor_Clasificar_RectificativaQueSeNombraASiMismaEsLibre()
        {
            // Un abono se nombra a sí mismo en el concepto: no se reserva a sí mismo y sigue cubriendo pedidos como antes.
            var abono = new ExtractoClienteDTO { importePendiente = -40M, concepto = "Efecto 1/1 factura RV2600154", documento = "RV2600154 " };
            var abonoDeOtra = new ExtractoClienteDTO { importePendiente = -10M, concepto = "Abono de la factura NV2617724", documento = "RV2600155" };

            ClasificadorSaldoAFavor.Clasificar(new List<ExtractoClienteDTO> { abono, abonoDeOtra },
                new List<int>(), new List<string> { "RV2600154", "RV2600155", "NV2617724" });

            Assert.IsTrue(ClasificadorSaldoAFavor.EsLibre(abono));
            Assert.AreEqual("NV2617724", abonoDeOtra.FacturaReservada);
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_LosQueNoSonPrepago_NoCambian()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004();
            PedidoPicking prepago = PedidoPre42004(rellenador, 930831, 375.33M);
            PedidoPicking contado = PedidoPre42004(rellenador, 930835, 159.72M, plazos: "CONTADO");

            Dictionary<int, bool> sale = PasadaDePicking(prepago, contado);

            Assert.IsTrue(sale[930831]);
            Assert.IsTrue(sale[930835]);
            Assert.IsFalse(contado.RetenidoPorPrepago);
            A.CallTo(() => rellenador.Prepagos(930835)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_OtroClienteNoComparteSaldo()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004();
            PedidoPicking pedido930831 = PedidoPre42004(rellenador, 930831, 375.33M);
            PedidoPicking deOtro = PedidoPre42004(rellenador, 930835, 159.72M);
            deOtro.Cliente = "11111";

            Dictionary<int, bool> sale = PasadaDePicking(pedido930831, deOtro);

            Assert.IsTrue(sale[930831]);
            Assert.IsTrue(sale[930835]);
        }

        [TestMethod]
        public void PedidoPicking_SaldoAFavor_DescuadrePermitido_SigueValiendo()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004(saldoAFavor: 535.00M);
            PedidoPicking pedido930831 = PedidoPre42004(rellenador, 930831, 375.33M);
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);

            Dictionary<int, bool> sale = PasadaDePicking(pedido930831, pedido930835);

            Assert.IsTrue(sale[930835], "Faltan 5 céntimos: dentro del descuadre permitido");
        }

        [TestMethod]
        public void ReglaSaldoAFavorPrepago_Disponible_SinOtrosEsLaCuentaDeSiempreYLosOtrosNoSeComenLosPrepagos()
        {
            Assert.AreEqual(100M + 50M - 80M, ReglaSaldoAFavorPrepago.Disponible(100M, 50M, 80M, 0));
            Assert.AreEqual(100M + 50M - 80M, ReglaSaldoAFavorPrepago.Disponible(100M, 50M, 80M, 30M),
                "Si la deuda supera el saldo no queda saldo que consumir: lo de los otros no se come los prepagos propios");
            Assert.AreEqual(100M + 50M - 10M - 30M, ReglaSaldoAFavorPrepago.Disponible(100M, 50M, 10M, 30M));
            Assert.AreEqual(100M, ReglaSaldoAFavorPrepago.Disponible(100M, 50M, 10M, 500M));
        }

        [TestMethod]
        public void ReglaSaldoAFavorPrepago_ConsumoAlSalir_LoQueNoCubrenSusPrepagosComoMuchoElSaldoLibre()
        {
            Assert.AreEqual(275.33M, ReglaSaldoAFavorPrepago.ConsumoAlSalir(375.33M, 100M, 375.33M));
            Assert.AreEqual(0M, ReglaSaldoAFavorPrepago.ConsumoAlSalir(375.33M, 400M, 375.33M));
            Assert.AreEqual(159.70M, ReglaSaldoAFavorPrepago.ConsumoAlSalir(159.72M, 0M, 159.70M));
        }

        [TestMethod]
        public void ExplicadorPedidoSinPicking_RetenidoPorPrepago_DiceLoQueCubrenOtrosPedidos()
        {
            IRellenadorPrepagosService rellenador = Rellenador42004();
            PedidoPicking pedido930831 = PedidoPre42004(rellenador, 930831, 375.33M);
            PedidoPicking pedido930835 = PedidoPre42004(rellenador, 930835, 159.72M);
            _ = PasadaDePicking(pedido930831, pedido930835);

            var error = ExplicadorPedidoSinPicking.Error(pedido930835, new Dictionary<string, string>(), null);

            StringAssert.Contains(error.Message, "375,33 € ya cubren otros pedidos suyos de prepago");
        }

        #endregion

        #region Novedades 1013: desglose de la retención por prepago a ELMAH

        private static PedidoPicking PedidoPrepagoParaTraza(decimal prepago, List<ExtractoClienteDTO> extractos, List<Exception> registrados)
        {
            IRellenadorPrepagosService rellenador = A.Fake<IRellenadorPrepagosService>();
            A.CallTo(() => rellenador.Prepagos(A<int>.Ignored)).Returns(new List<PrepagoDTO> { new PrepagoDTO { Importe = prepago } });
            A.CallTo(() => rellenador.ExtractosPendientes(A<int>.Ignored)).Returns(extractos);
            A.CallTo(() => rellenador.PedidosPrepagoEnPicking(A<int>.Ignored)).Returns(new List<PedidoPrepagoEnPickingDTO>());
            var pedido = new PedidoPicking(rellenador)
            {
                Id = 930959,
                Empresa = "1",
                Cliente = "15191",
                PlazosPago = Constantes.PlazosPago.PREPAGO,
                Lineas = new List<LineaPedidoPicking>
                {
                    new LineaPedidoPicking { Id = 1, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "A", Cantidad = 1, CantidadReservada = 1, Total = 100M }
                }
            };
            pedido.RegistrarEnElmah = registrados.Add;
            return pedido;
        }

        [TestMethod]
        public void PedidoPicking_CubiertoPorPrepago_Retenido_RegistraElDesgloseUnaSolaVezConLasCifras()
        {
            var registrados = new List<Exception>();
            var extractos = new List<ExtractoClienteDTO>
            {
                new ExtractoClienteDTO { empresa = "4", documento = "FV4001", importePendiente = 60M, vencimiento = DateTime.Today.AddDays(-30) },
                new ExtractoClienteDTO { empresa = "1", documento = "PAGO", importePendiente = -50M }
            };
            PedidoPicking pedido = PedidoPrepagoParaTraza(20M, extractos, registrados);

            Assert.IsFalse(pedido.CubiertoPorPrepago());
            Assert.IsFalse(pedido.CubiertoPorPrepago(), "Segunda pregunta en la misma pasada (todo ahora + decisión)");

            Assert.AreEqual(1, registrados.Count);
            string texto = registrados[0].Message;
            StringAssert.StartsWith(texto, PedidoPicking.PREFIJO_TRAZA_RETENIDO);
            StringAssert.Contains(texto, "Pedido 930959");
            StringAssert.Contains(texto, "cliente 15191");
            StringAssert.Contains(texto, $"Total={100M:0.00}");
            StringAssert.Contains(texto, $"Prepagos={20M:0.00}");
            StringAssert.Contains(texto, $"DeudaVencida={60M:0.00}");
            StringAssert.Contains(texto, $"FV4001 (empresa 4) {60M:0.00} vto. {DateTime.Today.AddDays(-30):dd/MM/yyyy}");
            StringAssert.Contains(texto, $"PAGO (empresa 1) {50M:0.00} libre");
        }

        [TestMethod]
        public void PedidoPicking_CubiertoPorPrepago_Cubierto_NoRegistraNada()
        {
            var registrados = new List<Exception>();
            PedidoPicking pedido = PedidoPrepagoParaTraza(100M, new List<ExtractoClienteDTO>(), registrados);

            Assert.IsTrue(pedido.CubiertoPorPrepago());
            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public void PedidoPicking_EvaluarPrepago_NoCubierto_NoRegistraNada()
        {
            var registrados = new List<Exception>();
            PedidoPicking pedido = PedidoPrepagoParaTraza(10M, new List<ExtractoClienteDTO>(), registrados);

            Assert.IsFalse(pedido.EvaluarPrepago());
            Assert.AreEqual(0, registrados.Count, "La primera vuelta del reparto del saldo no deja traza");
        }

        [TestMethod]
        public void PedidoPicking_CubiertoPorPrepago_SiElRegistroFalla_NoRompeElPicking()
        {
            PedidoPicking pedido = PedidoPrepagoParaTraza(10M, new List<ExtractoClienteDTO>(), new List<Exception>());
            pedido.RegistrarEnElmah = _ => throw new InvalidOperationException("ELMAH caído");

            Assert.IsFalse(pedido.CubiertoPorPrepago());
            Assert.IsTrue(pedido.RetenidoPorPrepago);
        }

        #endregion
    }
}
