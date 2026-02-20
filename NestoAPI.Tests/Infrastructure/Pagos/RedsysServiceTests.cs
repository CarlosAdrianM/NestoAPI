using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    [TestClass]
    public class RedsysServiceTests
    {
        [TestMethod]
        public void GenerarNumeroPedido_Devuelve12Caracteres()
        {
            // Arrange
            var service = new RedsysService("claveTest123", "329515704", true);

            // Act
            string numeroPedido = service.GenerarNumeroPedido();

            // Assert
            Assert.AreEqual(12, numeroPedido.Length);
        }

        [TestMethod]
        public void GenerarNumeroPedido_ConSufijoCliente_TerminaConCliente()
        {
            // Arrange
            var service = new RedsysService("claveTest123", "329515704", true);

            // Act
            string numeroPedido = service.GenerarNumeroPedido("C15191");

            // Assert
            Assert.AreEqual(12, numeroPedido.Length);
            Assert.IsTrue(numeroPedido.EndsWith("C15191"));
        }

        [TestMethod]
        public void GenerarNumeroPedido_DosLlamadas_DevuelvenValoresDiferentes()
        {
            // Arrange
            var service = new RedsysService("claveTest123", "329515704", true);

            // Act
            string pedido1 = service.GenerarNumeroPedido();
            System.Threading.Thread.Sleep(1); // Asegurar que el tick cambia
            string pedido2 = service.GenerarNumeroPedido();

            // Assert
            Assert.AreNotEqual(pedido1, pedido2);
        }

        [TestMethod]
        public void UrlFormularioRedsys_ModoPruebas_DevuelveUrlTest()
        {
            // Arrange
            var service = new RedsysService("claveTest123", "329515704", true);

            // Act
            string url = service.UrlFormularioRedsys;

            // Assert
            Assert.IsTrue(url.Contains("sis-t.redsys.es"));
        }

        [TestMethod]
        public void UrlFormularioRedsys_ModoProduccion_DevuelveUrlProduccion()
        {
            // Arrange
            var service = new RedsysService("claveTest123", "329515704", false);

            // Act
            string url = service.UrlFormularioRedsys;

            // Assert
            Assert.IsTrue(url.Contains("sis.redsys.es"));
            Assert.IsFalse(url.Contains("sis-t.redsys.es"));
        }
    }
}
