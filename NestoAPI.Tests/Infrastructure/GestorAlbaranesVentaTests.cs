using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.AlbaranesVenta;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorAlbaranesVentaTests
    {
        // ELMAH 29/09-01/10/26 (Andre): «El pedido tiene lineas ya facturadas, tiene que crear la nota de
        // entrega» sin decir de qué pedido; no había forma de saber a cuál le pasaba.
        [TestMethod]
        public async Task CrearAlbaran_FallaElProcedimiento_ElErrorDiceElPedido()
        {
            var servicio = A.Fake<IServicioAlbaranesVenta>();
            A.CallTo(() => servicio.CrearAlbaran("1", 926988, "NUEVAVISION\\Andre", null))
                .ThrowsAsync(new Exception("El pedido tiene lineas ya facturadas, tiene que crear la nota de entrega"));
            var gestor = new GestorAlbaranesVenta(servicio);

            Exception ex = await Assert.ThrowsExceptionAsync<Exception>(() => gestor.CrearAlbaran("1", 926988, "NUEVAVISION\\Andre"));

            StringAssert.Contains(ex.Message, "926988");
            Assert.AreEqual("El pedido tiene lineas ya facturadas, tiene que crear la nota de entrega", ex.InnerException.Message);
        }
    }
}
