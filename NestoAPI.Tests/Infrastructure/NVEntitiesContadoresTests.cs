using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;

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
    }
}
