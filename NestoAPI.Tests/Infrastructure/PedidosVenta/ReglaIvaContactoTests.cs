using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.PedidosBase;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>Pedido 929498 (28/10/26): el IVA del pedido es el del contacto de la ficha.</summary>
    [TestClass]
    public class ReglaIvaContactoTests
    {
        private static PedidoVentaDTO Pedido(string iva) => new PedidoVentaDTO
        {
            cliente = "29382", contacto = "1", iva = iva, ParametrosIva = new List<ParametrosIvaBase> { new ParametrosIvaBase { CodigoIvaProducto = "G21", PorcentajeIvaProducto = 0.21m } }
        };

        [TestMethod]
        public void Ajustar_ContactoDeRecargo_SustituyeElIvaYVaciaLosParametros()
        {
            PedidoVentaDTO pedido = Pedido("G21");

            string venia = ReglaIvaContacto.Ajustar(pedido, "E52 ");

            Assert.AreEqual("G21", venia);
            Assert.AreEqual("E52", pedido.iva);
            Assert.IsNull(pedido.ParametrosIva, "se recargan para el IVA bueno");
            StringAssert.Contains(ReglaIvaContacto.MensajeElmah(pedido, venia), "29382/1 venía con IVA G21");
        }

        [TestMethod]
        public void Ajustar_MismoIva_NoTocaNada()
        {
            PedidoVentaDTO pedido = Pedido("E52");

            Assert.IsNull(ReglaIvaContacto.Ajustar(pedido, "E52"));
            Assert.AreEqual("E52", pedido.iva);
            Assert.IsNotNull(pedido.ParametrosIva);
        }

        [TestMethod]
        public void Ajustar_SinIvaEnElPedido_SeRespeta_YSinIvaEnLaFicha_Tampoco()
        {
            PedidoVentaDTO sinIva = Pedido(null);
            Assert.IsNull(ReglaIvaContacto.Ajustar(sinIva, "E52"), "sin IVA es una decisión explícita del usuario");
            Assert.IsNull(sinIva.iva);

            PedidoVentaDTO fichaSinIva = Pedido("G21");
            Assert.IsNull(ReglaIvaContacto.Ajustar(fichaSinIva, null));
            Assert.AreEqual("G21", fichaSinIva.iva);
        }
    }
}
