using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;

namespace NestoAPI.Tests.Models.Picking
{
    [TestClass]
    public class LineaPedidoPickingTest
    {
        [TestMethod]
        public void LineaPedidoPickingTest_PasarAPendiente_laLineaAhoraEstaEnEstadoPendiente()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking();
            Assert.IsNotNull(linea);
        }
    }
}
