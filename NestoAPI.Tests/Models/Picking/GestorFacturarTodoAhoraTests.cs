using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using NestoAPI.Models.PedidosVenta;
using Servicio = NestoAPI.Models.Constantes.Pedidos.ModosServicio;
using MF = NestoAPI.Models.Constantes.Pedidos.ModosFacturacion;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#542 (corte 2): en el picking, un pedido que se factura «todo ahora» convierte lo que no tiene
    /// stock en Recoger en vez de dejarlo pendiente. Qué sale ahora lo decide el modo de SERVICIO (Carlos,
    /// 25/09/26): todo junto sin stock de todo → nada sale, todo a Recoger; parciales → sale lo que hay.
    /// </summary>
    [TestClass]
    public class GestorFacturarTodoAhoraTests
    {
        private static PedidoPicking Pedido(byte modoServicio, byte? modoFacturacion, params LineaPedidoPicking[] lineas)
        {
            return new PedidoPicking(A.Fake<IRellenadorPrepagosService>())
            {
                Id = 926346,
                Empresa = "1",
                Ruta = "FW",
                Iva = "G",
                PlazosPago = "CONTADO",
                ModoServicio = modoServicio,
                ServirJunto = modoServicio == Servicio.TODO_JUNTO,
                ModoFacturacion = modoFacturacion,
                MantenerJunto = modoFacturacion == MF.AL_COMPLETAR,
                Lineas = lineas.ToList()
            };
        }

        private static LineaPedidoPicking Linea(int id, int cantidad, int reservada, decimal baseImponible = 10)
        {
            return new LineaPedidoPicking
            {
                Id = id,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "P" + id,
                Almacen = Constantes.Almacenes.ALGETE,
                Cantidad = cantidad,
                CantidadReservada = reservada,
                BaseImponible = baseImponible,
                Total = baseImponible * 1.21M,
                FechaEntrega = DateTime.Today
            };
        }

        [TestMethod]
        public void SegunVayaEntrando_LoQueFaltaPasaARecoger_YLaLineaSaleConLoQueHay()
        {
            LineaPedidoPicking parcial = Linea(1, cantidad: 5, reservada: 3);
            LineaPedidoPicking completa = Linea(2, cantidad: 2, reservada: 2);
            LineaPedidoPicking sinStock = Linea(3, cantidad: 4, reservada: 0);
            PedidoPicking pedido = Pedido(Servicio.SEGUN_VAYA_ENTRANDO, MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, parcial, completa, sinStock);

            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { pedido });

            Assert.AreEqual(2, parcial.CantidadARecoger);
            Assert.AreEqual(2, parcial.CantidadRecogida);
            Assert.AreEqual(3, parcial.Cantidad, "La línea del picking se queda con lo que sale");
            Assert.AreEqual(3, parcial.CantidadReservada);
            Assert.AreEqual(0, completa.CantidadARecoger);
            Assert.AreEqual(4, sinStock.CantidadARecoger);
            Assert.AreEqual(0, sinStock.Cantidad);
            Assert.IsTrue(new GestorStocksPicking(pedido).HayStockDeTodo(), "Convertido, ya no falta nada");
            Assert.IsTrue(pedido.saleEnPicking());
        }

        [TestMethod]
        public void TodoJunto_SiFaltaAlgo_NoSaleNadaYTodoVaARecoger()
        {
            LineaPedidoPicking conStock = Linea(1, cantidad: 2, reservada: 2);
            LineaPedidoPicking sinStock = Linea(2, cantidad: 1, reservada: 0);
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, conStock, sinStock);

            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { pedido });

            Assert.AreEqual(2, conStock.CantidadARecoger);
            Assert.AreEqual(0, conStock.CantidadReservada, "Se suelta lo reservado: se factura ya, se entrega todo junto después");
            Assert.AreEqual(0, conStock.Cantidad);
            Assert.AreEqual(1, sinStock.CantidadARecoger);
            Assert.IsTrue(new GestorStocksPicking(pedido).HayStockDeAlgo(), "El albarán de 0 unidades sale: es como se hacía a mano (926346)");
            Assert.IsTrue(pedido.saleEnPicking());
        }

        [TestMethod]
        public void TodoJunto_ConStockDeTodo_NoCambiaNada()
        {
            LineaPedidoPicking a = Linea(1, cantidad: 2, reservada: 2);
            LineaPedidoPicking b = Linea(2, cantidad: 1, reservada: 1);
            PedidoPicking pedido = Pedido(Servicio.TODO_JUNTO, MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, a, b);

            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { pedido });

            Assert.AreEqual(0, a.CantidadARecoger + b.CantidadARecoger);
            Assert.AreEqual(2, a.CantidadReservada);
        }

        [TestMethod]
        public void AhoraLoQueHayYElRestoDeUnaVez_PrimeraEntrega_SaleLoQueHayYElRestoARecoger()
        {
            LineaPedidoPicking parcial = Linea(1, cantidad: 5, reservada: 3);
            PedidoPicking pedido = Pedido(Servicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, parcial);

            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { pedido });

            Assert.AreEqual(2, parcial.CantidadARecoger);
            Assert.AreEqual(3, parcial.CantidadReservada);
        }

        [TestMethod]
        public void OtrosModosDeFacturacion_NoSeTocan()
        {
            LineaPedidoPicking porEntregas = Linea(1, cantidad: 5, reservada: 3);
            LineaPedidoPicking alCompletar = Linea(2, cantidad: 5, reservada: 3);
            LineaPedidoPicking sinModo = Linea(3, cantidad: 5, reservada: 3);
            var pedidos = new List<PedidoPicking>
            {
                Pedido(Servicio.SEGUN_VAYA_ENTRANDO, MF.POR_ENTREGAS, porEntregas),
                Pedido(Servicio.SEGUN_VAYA_ENTRANDO, MF.AL_COMPLETAR, alCompletar),
                Pedido(Servicio.SEGUN_VAYA_ENTRANDO, null, sinModo)
            };

            GestorFacturarTodoAhora.Aplicar(pedidos);

            Assert.AreEqual(0, porEntregas.CantidadARecoger + alCompletar.CantidadARecoger + sinModo.CantidadARecoger);
            Assert.AreEqual(5, porEntregas.Cantidad);
        }

        [TestMethod]
        public void TrasReponerDeTiendas_MientrasEsperaLaReposicion_NoSeToca()
        {
            // Carlos: primero se trae el stock de las tiendas; la entrega inicial (y la factura) vienen después
            LineaPedidoPicking linea = Linea(1, cantidad: 5, reservada: 3);
            PedidoPicking pedido = Pedido(Servicio.TRAS_REPONER_DE_TIENDAS, MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, linea);
            pedido.EsperaReposicionDeTiendas = true;

            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { pedido });

            Assert.AreEqual(0, linea.CantidadARecoger);
            Assert.IsFalse(pedido.saleEnPicking());
        }

        [TestMethod]
        public void TrasReponerDeTiendas_SinNadaQueTraer_SaleLoQueHayYElRestoARecoger()
        {
            LineaPedidoPicking linea = Linea(1, cantidad: 5, reservada: 3);
            PedidoPicking pedido = Pedido(Servicio.TRAS_REPONER_DE_TIENDAS, MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, linea);
            pedido.EsperaReposicionDeTiendas = false;

            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { pedido });

            Assert.AreEqual(2, linea.CantidadARecoger);
            Assert.IsTrue(pedido.saleEnPicking());
        }

        [TestMethod]
        public void RetenidoPorPrepago_NoSeToca()
        {
            var prepagos = A.Fake<IRellenadorPrepagosService>();
            A.CallTo(() => prepagos.Prepagos(A<int>._)).Returns(new List<PrepagoDTO>());
            A.CallTo(() => prepagos.ExtractosPendientes(A<int>._)).Returns(new List<ExtractoClienteDTO>());
            LineaPedidoPicking linea = Linea(1, cantidad: 5, reservada: 3, baseImponible: 100);
            PedidoPicking pedido = new PedidoPicking(prepagos)
            {
                Id = 1, Empresa = "1", Ruta = "FW", PlazosPago = Constantes.PlazosPago.PREPAGO,
                ModoServicio = Servicio.SEGUN_VAYA_ENTRANDO, ModoFacturacion = MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES,
                Lineas = new List<LineaPedidoPicking> { linea }
            };

            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { pedido });

            Assert.AreEqual(0, linea.CantidadARecoger);
            Assert.IsTrue(pedido.RetenidoPorPrepago);
        }

        [TestMethod]
        public void NotaDeEntrega_NoSeToca()
        {
            LineaPedidoPicking linea = Linea(1, cantidad: 5, reservada: 3);
            PedidoPicking pedido = Pedido(Servicio.SEGUN_VAYA_ENTRANDO, MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, linea);
            pedido.EsNotaEntrega = true;

            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { pedido });

            Assert.AreEqual(0, linea.CantidadARecoger);
        }

        [TestMethod]
        public void LasLineasQueNoSonProducto_NoSeTocan()
        {
            LineaPedidoPicking portes = Linea(1, cantidad: 1, reservada: 0);
            portes.TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE;
            PedidoPicking pedido = Pedido(Servicio.SEGUN_VAYA_ENTRANDO, MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, portes, Linea(2, 1, 1));

            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { pedido });

            Assert.AreEqual(0, portes.CantidadARecoger);
        }

        // Portes (Carlos, 25/09/26): en «todo ahora» el cliente paga la factura entera, así que el mínimo se
        // mira sobre la base completa, no solo sobre lo que sale.

        [TestMethod]
        public void ImportesMinimos_TodoAhora_ElMinimoDePortesSeMiraSobreLaBaseCompleta()
        {
            LineaPedidoPicking linea = Linea(1, cantidad: 10, reservada: 2, baseImponible: 100); // salen 2 de 10 (20 €)
            PedidoPicking todoAhora = Pedido(Servicio.SEGUN_VAYA_ENTRANDO, MF.TODO_AHORA_Y_LO_PENDIENTE_DESPUES, linea);
            GestorFacturarTodoAhora.Aplicar(new List<PedidoPicking> { todoAhora });

            Assert.IsTrue(new GestorImportesMinimos(todoAhora).LaEntregaLlegaAlImporteMinimo(), "Se factura 100 € ≥ 75");

            LineaPedidoPicking lineaNormal = Linea(1, cantidad: 10, reservada: 2, baseImponible: 100);
            PedidoPicking porEntregas = Pedido(Servicio.SEGUN_VAYA_ENTRANDO, MF.POR_ENTREGAS, lineaNormal);

            Assert.IsFalse(new GestorImportesMinimos(porEntregas).LaEntregaLlegaAlImporteMinimo(), "Solo se cobran 20 €");
        }
    }
}
