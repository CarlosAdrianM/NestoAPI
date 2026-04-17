using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Tests del helper que extrae el AmazonOrderId del campo Comentarios (pensado para
    /// el endpoint <c>GET api/PedidosVenta/PorCanalExterno</c>, Issue Nesto#349 Fase 3a).
    /// La extracción se hace vía regex porque el cliente desktop guarda el identificador
    /// del canal en la primera línea de Comentarios sin un campo estructurado.
    /// </summary>
    [TestClass]
    public class PedidosVentaPorCanalExternoTests
    {
        [TestMethod]
        public void ExtraerAmazonOrderId_FormatoEstandar_Extrae()
        {
            string comentarios = "123-4567890-1234567 \r\nJUAN PEREZ\r\njuan@example.com";
            Assert.AreEqual("123-4567890-1234567",
                PedidosVentaController.ExtraerAmazonOrderId(comentarios));
        }

        [TestMethod]
        public void ExtraerAmazonOrderId_PrefijoFBA_Extrae()
        {
            string comentarios = "FBA 456-1234567-7654321 \r\nAve. Principal 1";
            Assert.AreEqual("456-1234567-7654321",
                PedidosVentaController.ExtraerAmazonOrderId(comentarios));
        }

        [TestMethod]
        public void ExtraerAmazonOrderId_SinFormatoReconocible_DevuelveNull()
        {
            Assert.IsNull(PedidosVentaController.ExtraerAmazonOrderId("Pedido manual sin id"));
        }

        [TestMethod]
        public void ExtraerAmazonOrderId_Null_DevuelveNull()
        {
            Assert.IsNull(PedidosVentaController.ExtraerAmazonOrderId(null));
        }

        [TestMethod]
        public void ExtraerAmazonOrderId_VacioOSoloEspacios_DevuelveNull()
        {
            Assert.IsNull(PedidosVentaController.ExtraerAmazonOrderId(""));
            Assert.IsNull(PedidosVentaController.ExtraerAmazonOrderId("   "));
        }

        [TestMethod]
        public void ExtraerAmazonOrderId_PrimerOrderIdGana_IgnoraOcurrenciasPosteriores()
        {
            // Si hubiera más de uno (poco realista), nos quedamos con el primero
            // porque en Nesto es siempre el que aparece al inicio de Comentarios.
            string comentarios = "111-1111111-1111111 primero\r\n222-2222222-2222222 segundo";
            Assert.AreEqual("111-1111111-1111111",
                PedidosVentaController.ExtraerAmazonOrderId(comentarios));
        }

        [TestMethod]
        public void ExtraerAmazonOrderId_NumerosNoFormatoOrderId_DevuelveNull()
        {
            // Números que no cumplen el patrón 3-7-7 no deben reconocerse.
            Assert.IsNull(PedidosVentaController.ExtraerAmazonOrderId("12-3456789-1234567"));   // 2-7-7
            Assert.IsNull(PedidosVentaController.ExtraerAmazonOrderId("123-456789-1234567"));   // 3-6-7
            Assert.IsNull(PedidosVentaController.ExtraerAmazonOrderId("123456789012345"));      // sin guiones
        }
    }
}
