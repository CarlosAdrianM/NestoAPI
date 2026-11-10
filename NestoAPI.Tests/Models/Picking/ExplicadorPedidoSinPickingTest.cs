using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#608: en el picking de UN pedido que no sale, el motivo concreto en vez de «No hay stock suficiente…».
    /// Las decisiones no cambian: estos tests solo miran el motivo y el mensaje.
    /// </summary>
    [TestClass]
    public class ExplicadorPedidoSinPickingTest
    {
        private static LineaPedidoPicking Linea(int id, string producto, int cantidad, int reservada, decimal baseImponible)
        {
            return new LineaPedidoPicking
            {
                Id = id,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto,
                Cantidad = cantidad,
                CantidadReservada = reservada,
                BaseImponible = baseImponible,
                Total = baseImponible * 1.21M
            };
        }

        private static PedidoPicking Pedido(byte modo, params LineaPedidoPicking[] lineas)
        {
            return new PedidoPicking(A.Fake<IRellenadorPrepagosService>())
            {
                Empresa = "1",
                Id = 916947,
                ServirJunto = modo == Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                ModoServicio = modo,
                Lineas = lineas.ToList()
            };
        }

        private static readonly Dictionary<string, string> sinNombres = new Dictionary<string, string>();
        private static readonly Dictionary<string, CompraPendientePicking> sinCompras = new Dictionary<string, CompraPendientePicking>();

        // Regresión Novedades 547 (Alfredo, 08/10/26): pedido 916947, «Según vaya entrando», todo lo de pago con
        // stock y faltan regalos de material promocional (base 0). No sale por #529, pero el mensaje decía «No hay
        // stock suficiente para asignar picking a ninguna línea».
        [TestMethod]
        public void Pedido916947_LoDePagoConStockYFaltanRegalos_DiceQueFaltanLosRegalosYCuandoEntran()
        {
            PedidoPicking pedido = Pedido(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                Linea(1, "38167", 1, 1, 97.20M),
                Linea(2, "44157", 1, 1, 270.90M),
                Linea(3, "37683", 1, 0, 0M),
                Linea(4, "38199", 5, 0, 0M),
                Linea(5, "38201", 5, 5, 0M));

            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido), "la decisión de #529 no cambia");
            Assert.AreEqual(MotivoNoSalePicking.EsperaSoloRegalos, pedido.MotivoNoSale);
            // Lo que hace después GeneradorPendientes con un pedido que no sale: quita las líneas sin nada reservado.
            // El mensaje tiene que seguir nombrándolas.
            pedido.Lineas.RemoveAll(l => l.CantidadReservada == 0);

            var nombres = new Dictionary<string, string> { { "37683", "DISPLAY SHINING LINE" }, { "38199", "TOALLA PEQUEÑA" } };
            var compras = new Dictionary<string, CompraPendientePicking>
            {
                { "37683", new CompraPendientePicking { Pedido = 220513, FechaPrevista = new DateTime(2026, 10, 23) } }
            };
            NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, nombres, compras);

            Assert.IsNotNull(error);
            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_ESPERA_REGALOS, error.Context.ErrorCode);
            Assert.AreEqual(916947, error.Context.Pedido);
            Assert.IsTrue(error.IsWarning);
            StringAssert.StartsWith(error.Message, "El pedido 916947 no sale: lo que se cobra tiene stock, pero faltan regalos");
            StringAssert.Contains(error.Message, "37683 DISPLAY SHINING LINE (falta 1; pedido a proveedor 220513, previsto el 23/10)");
            StringAssert.Contains(error.Message, "38199 TOALLA PEQUEÑA (faltan 5; sin pedido a proveedor)");
            StringAssert.Contains(error.Message, "Quita esas líneas o espera a que entren");
            Assert.IsFalse(error.Message.Contains("38167"), "lo de pago tiene stock: no se nombra");
            Assert.IsFalse(error.Message.Contains("38201"), "el regalo con stock no falta");
            Assert.IsFalse(error.Message.Contains("No hay stock suficiente"));
        }

        [TestMethod]
        public void TodoJunto_FaltaAlgo_DiceQueEstaEnTodoJuntoYQueFalta()
        {
            PedidoPicking pedido = Pedido(Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                Linea(1, "A1", 2, 2, 10M),
                Linea(2, "B2", 3, 1, 20M));

            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.TodoJuntoSinStock, pedido.MotivoNoSale);

            NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras);
            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_TODO_JUNTO_SIN_STOCK, error.Context.ErrorCode);
            StringAssert.Contains(error.Message, "«Todo junto»");
            StringAssert.Contains(error.Message, "B2 (faltan 2; sin pedido a proveedor)");
            Assert.IsFalse(error.Message.Contains("A1"));
        }

        [TestMethod]
        public void AhoraLoQueHay_ConPrimeraEntregaHecha_DiceQueElRestoSaleDeUnaVez()
        {
            PedidoPicking pedido = Pedido(Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ,
                Linea(1, "A1", 2, 2, 10M),
                Linea(2, "B2", 3, 0, 20M));
            pedido.TieneLineasServidas = true;

            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.RestoDeUnaVezSinStock, pedido.MotivoNoSale);
            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_RESTO_DE_UNA_VEZ_SIN_STOCK,
                ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras).Context.ErrorCode);
        }

        [TestMethod]
        public void TrasReponerDeTiendas_EsperandoReposicion_DiceQueEsperaALasTiendas()
        {
            PedidoPicking pedido = Pedido(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
                Linea(1, "A1", 2, 0, 10M));
            pedido.EsperaReposicionDeTiendas = true;

            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.EsperaReposicionDeTiendas, pedido.MotivoNoSale);

            NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras);
            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_ESPERA_REPOSICION_TIENDAS, error.Context.ErrorCode);
            StringAssert.Contains(error.Message, "tiene que llegar de las tiendas");
        }

        [TestMethod]
        public void Prepago_NoCubierto_DiceQueEsperaElPago()
        {
            PedidoPicking pedido = Pedido(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                Linea(1, "A1", 1, 1, 100M));
            pedido.PlazosPago = Constantes.PlazosPago.PREPAGO;

            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.RetenidoPorPrepago, pedido.MotivoNoSale);
            Assert.IsTrue(pedido.RetenidoPorPrepago, "lo de siempre sigue igual");
            pedido.Lineas.Clear(); // GeneradorPendientes puede vaciarlo después: el total es el de la decisión

            NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras);
            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_RETENIDO_PREPAGO, error.Context.ErrorCode);
            StringAssert.Contains(error.Message, "es de prepago y el pago no está cubierto (total 121,00 €, disponible 0,00 €)");
        }

        [TestMethod]
        public void SinStockDeLoDePago_NombraLoDePagoYMantieneElCodigoDeSiempre()
        {
            PedidoPicking pedido = Pedido(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                Linea(1, "A1", 2, 0, 10M),
                Linea(2, "R1", 1, 1, 0M),
                Linea(3, "R2", 1, 0, 0M));

            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.SinStockDePago, pedido.MotivoNoSale);

            NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras);
            Assert.AreEqual(Constantes.Picking.ERROR_SIN_STOCK, error.Context.ErrorCode);
            StringAssert.Contains(error.Message, "no hay stock suficiente de lo que se cobra (A1 (faltan 2; sin pedido a proveedor))");
            StringAssert.Contains(error.Message, "Los regalos sí tienen stock");
            Assert.IsFalse(error.Message.Contains("R2"), "el regalo sin stock no es el motivo");
        }

        [TestMethod]
        public void TodasLasLineasConEntregaPosterior_DiceCuandoSale()
        {
            PedidoPicking pedido = Pedido(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                Linea(1, "A1", 2, 2, 10M));
            pedido.Lineas[0].FechaEntrega = new DateTime(2026, 10, 20);
            GestorReservasStock.BorrarLineasQueNoDebenSalir(new List<PedidoPicking> { pedido }, new DateTime(2026, 10, 8));

            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.SinLineas, pedido.MotivoNoSale);

            NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras);
            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_ENTREGA_FUTURA, error.Context.ErrorCode);
            StringAssert.Contains(error.Message, "20/10/2026");
        }

        [TestMethod]
        public void SiSale_NoHayMotivoNiError()
        {
            PedidoPicking pedido = Pedido(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                Linea(1, "A1", 2, 1, 10M));

            Assert.IsTrue(GestorPicking.DecidirSiSale(pedido));
            Assert.AreEqual(MotivoNoSalePicking.Ninguno, pedido.MotivoNoSale);
            Assert.IsNull(ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras));
        }

        [TestMethod]
        public void MuchosProductos_SeCortaLaLista()
        {
            List<LineaPedidoPicking> lineas = Enumerable.Range(1, 11).Select(i => Linea(i, "P" + i, 1, 0, 10M)).ToList();
            PedidoPicking pedido = Pedido(Constantes.Pedidos.ModosServicio.TODO_JUNTO, lineas.ToArray());

            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido));
            NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras);
            StringAssert.Contains(error.Message, "P8 (falta 1; sin pedido a proveedor), y 3 más");
            Assert.IsFalse(error.Message.Contains("P9 "));
        }
    
        [TestMethod]
        public void ErrorVarios_SinErrores_Null()
        {
            Assert.IsNull(ExplicadorPedidoSinPicking.ErrorVarios(null));
            Assert.IsNull(ExplicadorPedidoSinPicking.ErrorVarios(new List<NestoBusinessException>()));
        }

        [TestMethod]
        public void ErrorVarios_UnoSolo_ElMismo()
        {
            var uno = new NestoBusinessException("El pedido 929864 no sale: está en «Tras reponer de tiendas»", new ErrorContext { ErrorCode = "PICKING_ESPERA_REPOSICION_TIENDAS" });

            Assert.AreSame(uno, ExplicadorPedidoSinPicking.ErrorVarios(new List<NestoBusinessException> { uno }));
        }

        [TestMethod]
        public void ErrorVarios_Varios_UnaLineaPorPedidoYElCodigoDelPrimero()
        {
            // Carlos 03/11/26 (cliente 15191, pedido 929864): el picking de un cliente decía «no hay stock» cuando el motivo era el modo de servicio
            var a = new NestoBusinessException("El pedido 929864 no sale: está en «Tras reponer de tiendas»", new ErrorContext { ErrorCode = "PICKING_ESPERA_REPOSICION_TIENDAS" });
            var b = new NestoBusinessException("El pedido 929865 no sale: está en «Todo junto» y falta stock de: 44553", new ErrorContext { ErrorCode = "PICKING_TODO_JUNTO_SIN_STOCK" });

            NestoBusinessException error = ExplicadorPedidoSinPicking.ErrorVarios(new List<NestoBusinessException> { a, b });

            Assert.AreEqual("PICKING_ESPERA_REPOSICION_TIENDAS", error.Context.ErrorCode);
            Assert.IsTrue(error.IsWarning);
            StringAssert.Contains(error.Message, "929864");
            StringAssert.Contains(error.Message, "929865");
            Assert.AreEqual(2, error.Message.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length);
        }

        // ===== Novedades 1164 (Alfredo, 10/11/26): picking de rutas sin nada que sacar =====

        private static PedidoPicking Descartado(int id, byte modo, Action<PedidoPicking> preparar, params LineaPedidoPicking[] lineas)
        {
            PedidoPicking pedido = Pedido(modo, lineas);
            pedido.Id = id;
            preparar?.Invoke(pedido);
            Assert.IsFalse(GestorPicking.DecidirSiSale(pedido), $"el pedido {id} no debe salir");
            // Lo que hace después GeneradorPendientes con los que no salen
            pedido.Lineas.RemoveAll(l => l.CantidadReservada == 0);
            return pedido;
        }

        private static PedidoPicking EntregaFutura(int id)
        {
            return Descartado(id, Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, p =>
            {
                p.Lineas[0].FechaEntrega = new DateTime(2026, 12, 4);
                GestorReservasStock.BorrarLineasQueNoDebenSalir(new List<PedidoPicking> { p }, new DateTime(2026, 11, 10));
            }, Linea(1, "A1", 1, 1, 10M));
        }

        private static List<PedidoPicking> DescartadosComoLosDel10Nov()
        {
            // Los más antiguos primero, como llegan: cinco de entrega futura (849141…), luego los que esperan a las tiendas
            // y los de «Todo junto» sin stock
            var descartados = new List<PedidoPicking>
            {
                EntregaFutura(849141), EntregaFutura(851002), EntregaFutura(860333), EntregaFutura(871204), EntregaFutura(880115)
            };
            foreach (int id in new[] { 930101, 930102, 930103, 930104, 930105, 930106, 930107 })
            {
                descartados.Add(Descartado(id, Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
                    p => p.EsperaReposicionDeTiendas = true, Linea(1, "17404", 2, 0, 10M), Linea(2, "38167", 1, 1, 20M)));
            }
            descartados.Add(Descartado(930200, Constantes.Pedidos.ModosServicio.TODO_JUNTO, null,
                Linea(1, "A1", 2, 2, 10M), Linea(2, "44553", 3, 1, 20M)));
            descartados.Add(Descartado(930201, Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                p => p.PlazosPago = Constantes.PlazosPago.PREPAGO, Linea(1, "A1", 1, 1, 100M)));
            return descartados;
        }

        [TestMethod]
        public void NadaQueSacar_CasoDel10Nov_NombraLosQueEsperanALasTiendasYNoListaLosDeEntregaFutura()
        {
            NestoBusinessException error = ExplicadorPedidoSinPicking.ErrorNadaQueSacar(DescartadosComoLosDel10Nov());

            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_NADA_QUE_SACAR, error.Context.ErrorCode);
            Assert.IsTrue(error.IsWarning);
            StringAssert.StartsWith(error.Message, "No hay ningún pedido que pueda salir ahora.");
            StringAssert.Contains(error.Message, "Esperan la reposición de las tiendas («Tras reponer de tiendas») (7): 930101 (17404)");
            StringAssert.Contains(error.Message, "930107 (17404)");
            StringAssert.Contains(error.Message, "«Todo junto» y les falta stock (1): 930200 (44553)");
            StringAssert.Contains(error.Message, "De prepago con el pago sin cubrir (1): 930201");
            StringAssert.Contains(error.Message, "Además, 5 pedidos tienen la entrega para más adelante.");
            foreach (string futuro in new[] { "849141", "851002", "860333", "871204", "880115" })
            {
                Assert.IsFalse(error.Message.Contains(futuro), $"el de entrega futura {futuro} no se lista");
            }
            Assert.IsFalse(error.Message.Contains("38167"), "lo que tiene stock no se nombra");
            Assert.IsFalse(error.Message.Contains("No se ha podido"));
            // La reposición, antes que «Todo junto»; y esta, antes que el prepago
            Assert.IsTrue(error.Message.IndexOf("930101") < error.Message.IndexOf("930200"));
            Assert.IsTrue(error.Message.IndexOf("930200") < error.Message.IndexOf("930201"));
        }

        [TestMethod]
        public void NadaQueSacar_MuchosEnUnMotivo_NombraElTopeYDiceCuantosMas()
        {
            var descartados = Enumerable.Range(1, ExplicadorPedidoSinPicking.MAXIMO_PEDIDOS_POR_MOTIVO + 3)
                .Select(i => Descartado(940000 + i, Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
                    p => p.PlazosPago = Constantes.PlazosPago.PREPAGO, Linea(1, "A1", 1, 1, 100M)))
                .ToList();

            string mensaje = ExplicadorPedidoSinPicking.ErrorNadaQueSacar(descartados).Message;

            StringAssert.Contains(mensaje, $"94000{ExplicadorPedidoSinPicking.MAXIMO_PEDIDOS_POR_MOTIVO} y 3 más.");
            Assert.IsFalse(mensaje.Contains("940009"));
        }

        [TestMethod]
        public void NadaQueSacar_SoloEntregaFuturaYSinStock_SoloLosCuentaSinAdemas()
        {
            var descartados = new List<PedidoPicking>
            {
                EntregaFutura(849141), EntregaFutura(851002),
                Descartado(930300, Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, null, Linea(1, "A1", 2, 0, 10M))
            };

            string mensaje = ExplicadorPedidoSinPicking.ErrorNadaQueSacar(descartados).Message;

            Assert.AreEqual("No hay ningún pedido que pueda salir ahora." + Environment.NewLine +
                "2 pedidos tienen la entrega para más adelante y 1 no tiene stock de nada de lo que se cobra.", mensaje);
        }

        [TestMethod]
        public void NadaQueSacar_UnoSolo_SuMotivoConcretoDetrasDeLaPrimeraFrase()
        {
            PedidoPicking pedido = Descartado(930101, Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
                p => p.EsperaReposicionDeTiendas = true, Linea(1, "17404", 2, 0, 10M));
            NestoBusinessException concreto = ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras);

            NestoBusinessException error = ExplicadorPedidoSinPicking.ErrorNadaQueSacar(new List<PedidoPicking> { pedido }, concreto);

            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_NADA_QUE_SACAR, error.Context.ErrorCode);
            Assert.AreEqual("No hay ningún pedido que pueda salir ahora. " + concreto.Message, error.Message);
        }

        [TestMethod]
        public void NadaQueSacar_SinDescartados_Null()
        {
            Assert.IsNull(ExplicadorPedidoSinPicking.ErrorNadaQueSacar(null));
            Assert.IsNull(ExplicadorPedidoSinPicking.ErrorNadaQueSacar(new List<PedidoPicking>()));
        }

        [TestMethod]
        public void PickingDeUnPedido_ElMotivoConcretoNoCambia()
        {
            // El de UN pedido sigue siendo el de #608: «El pedido X no sale: …» con su código, sin la frase del resumen
            PedidoPicking pedido = Descartado(930101, Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
                p => p.EsperaReposicionDeTiendas = true, Linea(1, "17404", 2, 0, 10M));

            NestoBusinessException error = ExplicadorPedidoSinPicking.Error(pedido, sinNombres, sinCompras);

            Assert.AreEqual(ExplicadorPedidoSinPicking.ERROR_ESPERA_REPOSICION_TIENDAS, error.Context.ErrorCode);
            Assert.AreEqual("El pedido 930101 no sale: está en «Tras reponer de tiendas» y está esperando stock suyo que tiene que llegar " +
                "de las tiendas (17404 (faltan 2; sin pedido a proveedor)). Saldrá cuando llegue la reposición.", error.Message);
        }
    }
}
