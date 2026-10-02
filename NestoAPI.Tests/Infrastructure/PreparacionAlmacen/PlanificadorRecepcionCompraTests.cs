using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // NestoAPI#559: qué se hace con cada línea de los pedidos de un proveedor al terminar la recepción.
    [TestClass]
    public class PlanificadorRecepcionCompraTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 5);

        private static LineaCompraPendiente Linea(int pedido, int orden, string producto, int cantidad,
            bool control = true, DateTime? fechaRecepcion = null, bool vistoBueno = true, DateTime? fechaPedido = null)
        {
            return new LineaCompraPendiente
            {
                Pedido = pedido,
                FechaPedido = fechaPedido ?? new DateTime(2026, 9, 1).AddDays(pedido % 100),
                NumeroOrden = orden,
                Producto = producto,
                Cantidad = cantidad,
                FechaRecepcion = fechaRecepcion ?? HOY.AddDays(-1),
                VistoBueno = vistoBueno,
                ControlPendientes = control
            };
        }

        private static Dictionary<string, int> Leido(params (string producto, int cantidad)[] lecturas)
        {
            return lecturas.ToDictionary(l => l.producto, l => l.cantidad);
        }

        [TestMethod]
        public void TodoRecibido_LasLineasSeRecibenEnterasYElPedidoSeAlbaranea()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(
                new[] { Linea(100, 1, "A", 5), Linea(100, 2, "B", 3) }, Leido(("A", 5), ("B", 3)), HOY, esCompras: false);

            Assert.AreEqual(2, plan.Recibidas.Count);
            Assert.IsTrue(plan.Recibidas.All(r => r.Resto == 0));
            Assert.AreEqual(0, plan.Anuladas.Count);
            CollectionAssert.AreEqual(new[] { 100 }, plan.PedidosAAlbaranear);
        }

        [TestMethod]
        public void ParcialConControlDePendientes_LoQueFaltaSigueYPasaAManana()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(new[] { Linea(100, 1, "A", 10, control: true) },
                Leido(("A", 4)), HOY, esCompras: false);

            LineaRecibida recibida = plan.Recibidas.Single();
            Assert.AreEqual(4, recibida.Recibido);
            Assert.AreEqual(6, recibida.Resto);
            Assert.AreEqual(PlanificadorRecepcionCompra.ESTADO_PENDIENTE, recibida.EstadoResto);
            Assert.IsTrue(recibida.FechaResto > HOY, "Lo pendiente no puede entrar en el albarán de hoy");
        }

        [TestMethod]
        public void ParcialConControl_LoQueFaltaPasaAMananaAunqueSeEsperaraMasTarde()
        {
            // Como prdInsertarLineaCmp: fecharecepción = dateadd(d,1,hoy) para todo lo que sigue en curso
            var plan = PlanificadorRecepcionCompra.Planificar(
                new[] { Linea(100, 1, "A", 10, control: true, fechaRecepcion: HOY.AddDays(7)) }, Leido(("A", 4)), HOY, false);

            Assert.AreEqual(HOY.AddDays(1), plan.Recibidas.Single().FechaResto);
        }

        [TestMethod]
        public void ParcialSinControlDePendientes_LoQueFaltaVaA99()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(new[] { Linea(100, 1, "A", 10, control: false) },
                Leido(("A", 4)), HOY, esCompras: false);

            LineaRecibida recibida = plan.Recibidas.Single();
            Assert.AreEqual(6, recibida.Resto);
            Assert.AreEqual(PlanificadorRecepcionCompra.ESTADO_ANULADA, recibida.EstadoResto);
        }

        [TestMethod]
        public void SinControl_LineaNoRecibidaDeUnPedidoRecibido_VaA99()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(
                new[] { Linea(100, 1, "A", 5, control: false), Linea(100, 2, "B", 3, control: false) },
                Leido(("A", 5)), HOY, false);

            CollectionAssert.AreEqual(new[] { 2 }, plan.Anuladas);
        }

        [TestMethod]
        public void SinControl_LineaNoRecibidaQueSeEsperaMasAdelante_NoSeAnulaYPasaAManana()
        {
            // Los -99 históricos son de líneas esperadas hasta el día del albarán (816 de 817 desde 2020)
            var plan = PlanificadorRecepcionCompra.Planificar(
                new[] { Linea(100, 1, "A", 5, control: false), Linea(100, 2, "B", 3, control: false, fechaRecepcion: HOY.AddDays(10)) },
                Leido(("A", 5)), HOY, false);

            Assert.AreEqual(0, plan.Anuladas.Count);
            CollectionAssert.AreEqual(new[] { 2 }, plan.Aplazadas);
        }

        [TestMethod]
        public void ConControl_LineaNoRecibida_SeQuedaPendienteSinTocar()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(
                new[] { Linea(100, 1, "A", 5, control: true), Linea(100, 2, "B", 3, control: true) },
                Leido(("A", 5)), HOY, false);

            Assert.AreEqual(0, plan.Anuladas.Count);
            Assert.AreEqual(1, plan.Recibidas.Count);
            CollectionAssert.AreEqual(new[] { 2 }, plan.Aplazadas, "Pasa a mañana para que el albarán de hoy no se la lleve");
        }

        [TestMethod]
        public void PedidoDelQueNoLlegaNada_NoSeToca()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(
                new[] { Linea(100, 1, "A", 5, control: false), Linea(101, 2, "B", 3, control: false) },
                Leido(("A", 5)), HOY, false);

            Assert.AreEqual(0, plan.Anuladas.Count);
            Assert.AreEqual(0, plan.Aplazadas.Count);
            CollectionAssert.AreEqual(new[] { 100 }, plan.PedidosAAlbaranear);
        }

        [TestMethod]
        public void MismoProductoEnDosPedidos_SeRepartePrimeroElMasAntiguo()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(
                new[]
                {
                    Linea(200, 5, "A", 4, fechaPedido: new DateTime(2026, 9, 20)),
                    Linea(150, 9, "A", 4, fechaPedido: new DateTime(2026, 9, 10))
                },
                Leido(("A", 6)), HOY, false);

            Assert.AreEqual(4, plan.Recibidas.Single(r => r.NumeroOrden == 9).Recibido, "El del 10/09 va primero");
            Assert.AreEqual(2, plan.Recibidas.Single(r => r.NumeroOrden == 5).Recibido);
            CollectionAssert.AreEquivalent(new[] { 150, 200 }, plan.PedidosAAlbaranear);
        }

        [TestMethod]
        public void Exceso_LoRecibeAlguienDeCompras_EntraConVistoBuenoEnElPedidoMasReciente()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(
                new[]
                {
                    Linea(150, 9, "A", 4, fechaPedido: new DateTime(2026, 9, 10)),
                    Linea(200, 5, "A", 4, fechaPedido: new DateTime(2026, 9, 20))
                },
                Leido(("A", 11)), HOY, esCompras: true);

            ExcesoRecepcion exceso = plan.Excesos.Single();
            Assert.AreEqual(3, exceso.Cantidad);
            Assert.AreEqual(5, exceso.CopiaDe);
            Assert.AreEqual(200, exceso.Pedido);
            Assert.IsTrue(exceso.VistoBueno);
            Assert.AreEqual(0, plan.AvisosParaCompras.Count, "Compras ya lo ha visto: no hay que avisar");
        }

        [TestMethod]
        public void Exceso_LoRecibeAlguienQueNoEsDeCompras_EntraSinVistoBuenoYSeAvisaACompras()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(new[] { Linea(100, 1, "A", 4) }, Leido(("A", 6)), HOY, esCompras: false);

            ExcesoRecepcion exceso = plan.Excesos.Single();
            Assert.AreEqual(2, exceso.Cantidad);
            Assert.IsFalse(exceso.VistoBueno);
            Assert.AreEqual(1, plan.AvisosParaCompras.Count);
            StringAssert.Contains(plan.AvisosParaCompras[0], "A");
        }

        [TestMethod]
        public void ProductoQueNoEstaEnNingunPedido_NoEntraYSeDevuelveComoNoPedido()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(new[] { Linea(100, 1, "A", 4) }, Leido(("A", 4), ("Z", 2)), HOY, false);

            Assert.AreEqual("Z", plan.NoPedidos.Single().Producto);
            Assert.AreEqual(2, plan.NoPedidos.Single().Cantidad);
            Assert.AreEqual(0, plan.Excesos.Count);
        }

        [TestMethod]
        public void LineaSinVistoBueno_LaRecibeCompras_QuedaConVistoBueno()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(new[] { Linea(100, 1, "A", 4, vistoBueno: false) }, Leido(("A", 4)), HOY, esCompras: true);

            Assert.IsTrue(plan.Recibidas.Single().VistoBueno);
            CollectionAssert.AreEqual(new[] { 100 }, plan.PedidosAAlbaranear);
        }

        [TestMethod]
        public void LineaSinVistoBueno_LaRecibeAlmacen_NoEntraEnElAlbaranYSeAvisaACompras()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(new[] { Linea(100, 1, "A", 4, vistoBueno: false) }, Leido(("A", 4)), HOY, esCompras: false);

            Assert.IsFalse(plan.Recibidas.Single().VistoBueno);
            Assert.AreEqual(0, plan.PedidosAAlbaranear.Count, "Sin visto bueno el albarán no tendría líneas");
            Assert.AreEqual(1, plan.AvisosParaCompras.Count);
        }

        [TestMethod]
        public void ElProductoLeidoConEspaciosOMinusculas_CasaIgual()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(new[] { Linea(100, 1, "AB12", 2) }, Leido((" ab12 ", 2)), HOY, false);

            Assert.AreEqual(2, plan.Recibidas.Single().Recibido);
            Assert.AreEqual(0, plan.NoPedidos.Count);
        }

        [TestMethod]
        public void LecturasACeroONegativas_NoCuentan()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(new[] { Linea(100, 1, "A", 2) }, Leido(("A", 0), ("Z", -1)), HOY, false);

            Assert.AreEqual(0, plan.Recibidas.Count);
            Assert.AreEqual(0, plan.NoPedidos.Count);
            Assert.AreEqual(0, plan.PedidosAAlbaranear.Count);
        }

        [TestMethod]
        public void PedidoDeCadaProducto_EsElPrimeroAlQueSeAsigna()
        {
            var plan = PlanificadorRecepcionCompra.Planificar(new[] { Linea(100, 1, "A", 2), Linea(101, 2, "B", 2) },
                Leido(("A", 2), ("B", 1), ("Z", 1)), HOY, false);

            Assert.AreEqual(100, plan.PedidoDeProducto["A"]);
            Assert.AreEqual(101, plan.PedidoDeProducto["B"]);
            Assert.IsFalse(plan.PedidoDeProducto.ContainsKey("Z"));
        }
    }
}
