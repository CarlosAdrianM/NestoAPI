using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using System;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Issue #275: número de pedido duplicado (violación de PK_CabPedidoVta) por race del contador.
    /// El patrón 'contador.Pedidos++' + SaveChanges no era atómico: dos peticiones concurrentes leían
    /// el mismo valor y ambas insertaban el mismo Número (el segundo pedido se perdía). La reserva
    /// pasa a hacerse con un único UPDATE ... OUTPUT, que sí es atómico, en un único punto
    /// (NVEntities.TomarSiguienteNumeroPedido) usado por PostPedidoVenta y GestorCopiaPedidos.
    /// </summary>
    [TestClass]
    public class NVEntitiesContadoresTests
    {
        [TestMethod]
        public void TomarSiguienteNumeroPedido_EsVirtual_SePuedeFakear()
        {
            // Si alguien quita el 'virtual', FakeItEasy lanza al configurar y este test avisa:
            // el método tiene que poder fakearse para testear los flujos que crean pedidos.
            var db = A.Fake<NVEntities>();
            A.CallTo(() => db.TomarSiguienteNumeroPedido()).Returns(923456);

            Assert.AreEqual(923456, db.TomarSiguienteNumeroPedido());
        }

        [TestMethod]
        public void SqlDeReserva_EsUnUpdateConOutput_NoUnReadModifyWrite()
        {
            // La atomicidad depende de que sea UN SOLO UPDATE con OUTPUT (bloqueo exclusivo de la
            // fila): un SELECT seguido de UPDATE reabriría la race de #275.
            StringAssert.Contains(NVEntities.SQL_SIGUIENTE_NUMERO_PEDIDO, "UPDATE ContadoresGlobales SET Pedidos = Pedidos + 1");
            StringAssert.Contains(NVEntities.SQL_SIGUIENTE_NUMERO_PEDIDO, "OUTPUT INSERTED.Pedidos");
        }

        // --- NestoAPI#636: el número de un pedido que no se guarda vuelve al contador ---
        // 14/10/26: cuatro pedidos de Manuel rechazados por validación se comieron 928501..928507
        // (desde el 928500 solo había pares). La reserva atómica de #275 se hace antes de validar
        // y nada devolvía el número.

        [TestMethod]
        public void SqlDeDevolucion_SoloBajaSiNadieHaReservadoOtroYNoHayPedido()
        {
            // Sin la condición del contador, devolver con otro POST en marcha repetiría números (#275).
            StringAssert.Contains(NVEntities.SQL_DEVOLVER_NUMERO_PEDIDO, "SET Pedidos = Pedidos - 1 WHERE Pedidos = @numero");
            StringAssert.Contains(NVEntities.SQL_DEVOLVER_NUMERO_PEDIDO, "NOT EXISTS (SELECT 1 FROM CabPedidoVta WHERE Número = @numero)");
        }

        [TestMethod]
        public void DevolverNumeroPedidoNoGuardado_NumeroReservadoPorElPost_LoDevuelve()
        {
            var db = A.Fake<NVEntities>();
            A.CallTo(() => db.DevolverNumeroPedido(928501)).Returns(true);

            bool devuelto = PedidosVentaController.DevolverNumeroPedidoNoGuardado(db, 928501, numeroReservadoAqui: true);

            Assert.IsTrue(devuelto);
            A.CallTo(() => db.DevolverNumeroPedido(928501)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void DevolverNumeroPedidoNoGuardado_NumeroQueTraiaElDto_NoLoToca()
        {
            // Un número que no ha reservado este POST no es suyo: devolverlo podría bajar el contador ajeno.
            var db = A.Fake<NVEntities>();

            bool devuelto = PedidosVentaController.DevolverNumeroPedidoNoGuardado(db, 928501, numeroReservadoAqui: false);

            Assert.IsFalse(devuelto);
            A.CallTo(() => db.DevolverNumeroPedido(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void DevolverNumeroPedidoNoGuardado_FallaLaDevolucion_NoLanza()
        {
            // El error que manda es el del pedido (la validación); la devolución es solo para no dejar hueco.
            var db = A.Fake<NVEntities>();
            A.CallTo(() => db.DevolverNumeroPedido(A<int>._)).Throws(new InvalidOperationException("sin conexión"));

            bool devuelto = PedidosVentaController.DevolverNumeroPedidoNoGuardado(db, 928501, numeroReservadoAqui: true);

            Assert.IsFalse(devuelto);
        }
    }
}
