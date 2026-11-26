using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Facturas
{
    /// <summary>NestoAPI#898: una P.O. va en una sola factura (la misma regla que prdCrearFacturaVta, por pedido).</summary>
    [TestClass]
    public class POEnteraTests
    {
        [TestInitialize]
        public void Setup()
        {
            InterruptorPOSoloEntera.Forzado = () => true;
        }

        [TestCleanup]
        public void Limpiar()
        {
            InterruptorPOSoloEntera.Reiniciar();
        }

        [TestMethod]
        public void Regla_SinPO_NuncaBloquea()
        {
            Assert.IsNull(ReglaPOEntera.MotivoNoSePuedeFacturar(new DatosPOEnteraPedido { Pedido = 1, SuPedido = "   ", QuedanSinEntregar = true, OtroPedido = 2 }));
            Assert.IsNull(ReglaPOEntera.MotivoNoSePuedeFacturar(new DatosPOEnteraPedido { Pedido = 1, SuPedido = null, QuedanSinEntregar = true }));
            Assert.IsNull(ReglaPOEntera.MotivoNoSePuedeFacturar(null));
        }

        [TestMethod]
        public void Regla_ConPOYTodoEntregadoSinOtros_SePuedeFacturar()
        {
            Assert.IsNull(ReglaPOEntera.MotivoNoSePuedeFacturar(new DatosPOEnteraPedido { Pedido = 930489, SuPedido = "4500123" }));
        }

        [TestMethod]
        public void Regla_QuedaAlgoSinEntregar_MensajeDeUsted()
        {
            string motivo = ReglaPOEntera.MotivoNoSePuedeFacturar(new DatosPOEnteraPedido { Pedido = 930489, SuPedido = " 4500123  ", QuedanSinEntregar = true, OtroPedido = 930500 });
            Assert.AreEqual("El pedido 930489 es de la P.O. 4500123 y no está todo entregado: una P.O. va en una sola factura. Entregue lo que falta, o quítelo del pedido, y vuelva a facturar.", motivo);
        }

        [TestMethod]
        public void Regla_OtroPedidoDeLaMismaPO_PideAgrupar()
        {
            string motivo = ReglaPOEntera.MotivoNoSePuedeFacturar(new DatosPOEnteraPedido { Pedido = 930489, SuPedido = "PO 12.3", OtroPedido = 930500 });
            Assert.AreEqual("La P.O. PO 12.3 tiene más pedidos sin facturar (el 930500, por ejemplo): agrúpelos antes por P.O. cuando estén todos entregados.", motivo);
        }

        [TestMethod]
        public void QuedanSinEntregar_CuentaSoloMenos1a1ConCantidad()
        {
            Assert.IsTrue(ReglaPOEntera.QuedanSinEntregar(new[] { Linea(-1, 1) }));
            Assert.IsTrue(ReglaPOEntera.QuedanSinEntregar(new[] { Linea(1, -2) }));
            Assert.IsFalse(ReglaPOEntera.QuedanSinEntregar(new[] { Linea(1, 0) }));
            Assert.IsFalse(ReglaPOEntera.QuedanSinEntregar(new[] { Linea(2, 3), Linea(4, 1), Linea(-3, 5) }));
            Assert.IsFalse(ReglaPOEntera.QuedanSinEntregar(null));
        }

        [TestMethod]
        public void Interruptor_SoloElCeroApaga()
        {
            Assert.IsTrue(InterruptorPOSoloEntera.Interpretar(null));
            Assert.IsTrue(InterruptorPOSoloEntera.Interpretar(""));
            Assert.IsTrue(InterruptorPOSoloEntera.Interpretar("1"));
            Assert.IsTrue(InterruptorPOSoloEntera.Interpretar("no"));
            Assert.IsFalse(InterruptorPOSoloEntera.Interpretar(" 0 "));
        }

        [TestMethod]
        public void LectorSql_ComparaLaPOSoloSinEspaciosYMiraLosEstadosDelProcedimiento()
        {
            StringAssert.Contains(LectorPOEnteraSql.SQL, "RTRIM(LTRIM(c2.SuPedido)) = RTRIM(LTRIM(c.SuPedido))");
            StringAssert.Contains(LectorPOEnteraSql.SQL, "l.Estado BETWEEN -1 AND 1 AND l.Cantidad <> 0");
            StringAssert.Contains(LectorPOEnteraSql.SQL, "l2.Estado BETWEEN -1 AND 2 AND l2.Cantidad <> 0");
            Assert.IsFalse(LectorPOEnteraSql.SQL.Contains("ClientesSuPedidoObligatorio"), "Es por pedido, no por cliente marcado");
        }

        [TestMethod]
        public async Task CrearFactura_ConPONoEntera_LanzaConElMotivoAntesDelProcedimiento()
        {
            bool procedimientoLlamado = false;
            var servicio = new ServicioFacturas(A.Fake<NVEntities>())
            {
                LectorPOEntera = (empresa, pedido) => Task.FromResult(new DatosPOEnteraPedido { Pedido = pedido, SuPedido = "4500123", OtroPedido = 930500 }),
                ProcedimientoCrearFacturaVta = (e, p, u) => { procedimientoLlamado = true; return Task.FromResult("NV1"); }
            };
            var cab = new CabPedidoVta { Empresa = "1", Número = 930489, SuPedido = "4500123" };

            FacturacionException ex = await Assert.ThrowsExceptionAsync<FacturacionException>(() => servicio.ComprobarPOEntera(cab, "1", 930489, "test"));

            Assert.AreEqual(ReglaPOEntera.CODIGO, ex.GetErrorCode());
            StringAssert.Contains(ex.Message, "el 930500");
            Assert.IsFalse(procedimientoLlamado);
        }

        [TestMethod]
        public async Task CrearFactura_SinPOOApagado_NiSiquieraLee()
        {
            bool leido = false;
            var servicio = new ServicioFacturas(A.Fake<NVEntities>())
            {
                LectorPOEntera = (empresa, pedido) => { leido = true; return Task.FromResult(new DatosPOEnteraPedido { Pedido = pedido, SuPedido = "X", QuedanSinEntregar = true }); }
            };

            await servicio.ComprobarPOEntera(new CabPedidoVta { Número = 1, SuPedido = "  " }, "1", 1, "test");
            Assert.IsFalse(leido);

            InterruptorPOSoloEntera.Forzado = () => false;
            await servicio.ComprobarPOEntera(new CabPedidoVta { Número = 1, SuPedido = "X" }, "1", 1, "test");
            Assert.IsFalse(leido);
        }

        [TestMethod]
        public async Task CrearFactura_SiNoSePuedeLeer_SigueYLoDecideElProcedimiento()
        {
            var servicio = new ServicioFacturas(A.Fake<NVEntities>())
            {
                LectorPOEntera = (empresa, pedido) => throw new System.InvalidOperationException("sin conexión")
            };

            await servicio.ComprobarPOEntera(new CabPedidoVta { Número = 1, SuPedido = "X" }, "1", 1, "test");
        }

        [TestMethod]
        public void FacturarRutas_MiraLoSinEntregarEnMemoriaYLosOtrosDeLaTanda()
        {
            var pedido = new CabPedidoVta { Empresa = "1", Número = 930489, SuPedido = "4500123", LinPedidoVtas = new List<LinPedidoVta> { Linea(2, 1) } };
            var tanda = new Dictionary<int, DatosPOEnteraPedido>
            {
                // La lectura de antes del albarán decía «sin entregar»: no cuenta, manda lo que hay en memoria.
                { 930489, new DatosPOEnteraPedido { Pedido = 930489, SuPedido = "4500123", QuedanSinEntregar = true } }
            };
            Assert.IsNull(GestorFacturacionRutas.MotivoPOEntera(pedido, tanda));

            tanda[930489].OtroPedido = 930500;
            StringAssert.Contains(GestorFacturacionRutas.MotivoPOEntera(pedido, tanda), "el 930500");

            pedido.LinPedidoVtas.Add(Linea(1, 2));
            StringAssert.Contains(GestorFacturacionRutas.MotivoPOEntera(pedido, null), "no está todo entregado");
        }

        [TestMethod]
        public void FacturarRutas_SinPONoBloquea()
        {
            var pedido = new CabPedidoVta { Empresa = "1", Número = 1, SuPedido = null, LinPedidoVtas = new List<LinPedidoVta> { Linea(1, 2) } };
            Assert.IsNull(GestorFacturacionRutas.MotivoPOEntera(pedido, null));
        }

        private static LinPedidoVta Linea(short estado, short cantidad)
        {
            return new LinPedidoVta { Estado = estado, Cantidad = cantidad };
        }
    }
}
