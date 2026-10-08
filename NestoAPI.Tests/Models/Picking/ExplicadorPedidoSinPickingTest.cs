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
    }
}
