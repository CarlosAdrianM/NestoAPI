using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// La issue #130 impedía apuntar un prepago en cuanto el pedido tenía picking («Anule el picking
    /// primero»). Un prepago es dinero ya cobrado y apuntarlo no cambia lo que sale ni lo que se
    /// factura: lo único que hace daño es que la etiqueta ya esté impresa con reembolso, porque la
    /// agencia se lo cobraría otra vez al cliente.
    /// </summary>
    [TestClass]
    public class PedidosVentaPrepagoConPickingTests
    {
        [TestMethod]
        public void MotivoParaNoAnadirPrepago_PedidoConPickingSinEtiqueta_SePuede()
        {
            // El caso del 30/09/26: pedido con picking, sin facturar, y no dejaba apuntar el cobro
            Assert.IsNull(PedidosVentaController.MotivoParaNoAnadirPrepago(false, true, null));
        }

        [TestMethod]
        public void MotivoParaNoAnadirPrepago_EtiquetaImpresaSinReembolso_SePuede()
        {
            Assert.IsNull(PedidosVentaController.MotivoParaNoAnadirPrepago(false, true, 0m));
        }

        [TestMethod]
        public void MotivoParaNoAnadirPrepago_EtiquetaImpresaConReembolso_NoSePuede()
        {
            string motivo = PedidosVentaController.MotivoParaNoAnadirPrepago(false, true, 121.5m);

            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "etiqueta impresa");
            StringAssert.Contains(motivo, "reembolso");
        }

        [TestMethod]
        public void MotivoParaNoAnadirPrepago_ElPedidoYaTeniaPrepago_SePuedeModificar()
        {
            // Cambiar el importe o la cuenta de un prepago que ya estaba no es añadir uno
            Assert.IsNull(PedidosVentaController.MotivoParaNoAnadirPrepago(true, true, 121.5m));
        }

        [TestMethod]
        public void MotivoParaNoAnadirPrepago_ElPedidoNoLlevaPrepago_NoHayNadaQueImpedir()
        {
            Assert.IsNull(PedidosVentaController.MotivoParaNoAnadirPrepago(false, false, 121.5m));
        }
    }
}
