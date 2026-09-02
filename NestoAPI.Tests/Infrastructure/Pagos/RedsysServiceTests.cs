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
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            // Act
            string numeroPedido = service.GenerarNumeroPedido();

            // Assert
            Assert.AreEqual(12, numeroPedido.Length);
        }

        [TestMethod]
        public void GenerarNumeroPedido_ConSufijoCliente_TerminaConCliente()
        {
            // Arrange
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            // Act
            string numeroPedido = service.GenerarNumeroPedido("C15191");

            // Assert
            Assert.AreEqual(12, numeroPedido.Length);
            Assert.IsTrue(numeroPedido.EndsWith("C15191"));
        }

        private static string DecodificarParametros(string base64)
        {
            string normal = base64.Replace('-', '+').Replace('_', '/');
            while (normal.Length % 4 != 0)
            {
                normal += "=";
            }
            return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(normal));
        }

        [TestMethod]
        public void CrearParametrosTPVVirtual_ConReferencia_PagoPorRedireccionSinExencionMIT()
        {
            // Plan B (NestoAPI#178, SIS0883 del 02/09/26): la referencia va en el formulario y el
            // titular se autentica en la pasarela. Ni DIRECTPAYMENT ni EXCEP_SCA: no es MIT.
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            var parametros = service.CrearParametrosTPVVirtual(3.84m, "Pago pedido 925300", "a@b.com", "15191",
                "https://api/notif", "nestotiendas://pago/ok", "nestotiendas://pago/ko", "C",
                tokenTarjeta: "a26a5b0359c693cb3849bcb84d50f6bbf607aab0", cofTxnId: "232026245295044");

            string json = DecodificarParametros(parametros.Ds_MerchantParameters);
            StringAssert.Contains(json, "\"DS_MERCHANT_IDENTIFIER\":\"a26a5b0359c693cb3849bcb84d50f6bbf607aab0\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_COF_INI\":\"N\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_COF_TXNID\":\"232026245295044\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_AMOUNT\":\"384\"");
            Assert.IsFalse(json.Contains("DS_MERCHANT_EXCEP_SCA"), "sin exención MIT: el cliente está presente");
            Assert.IsFalse(json.Contains("DS_MERCHANT_DIRECTPAYMENT"), "no es un pago directo");
            Assert.IsFalse(json.Contains("REQUIRED"), "ya está tokenizada: no se pide otra referencia");
        }

        [TestMethod]
        public void CrearParametrosTPVVirtual_SinReferenciaYConToken_PideTokenizar()
        {
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            var parametros = service.CrearParametrosTPVVirtual(3.84m, "Pago pedido 925300", "a@b.com", "15191",
                "https://api/notif", "ok", "ko", "C", solicitarToken: true);

            string json = DecodificarParametros(parametros.Ds_MerchantParameters);
            StringAssert.Contains(json, "\"DS_MERCHANT_IDENTIFIER\":\"REQUIRED\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_COF_INI\":\"S\"");
        }

        [TestMethod]
        public void GenerarNumeroPedido_DosLlamadas_DevuelvenValoresDiferentes()
        {
            // Arrange
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

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
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            // Act
            string url = service.UrlFormularioRedsys;

            // Assert
            Assert.IsTrue(url.Contains("sis-t.redsys.es"));
        }

        [TestMethod]
        public void UrlFormularioRedsys_ModoProduccion_DevuelveUrlProduccion()
        {
            // Arrange
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", false);

            // Act
            string url = service.UrlFormularioRedsys;

            // Assert
            Assert.IsTrue(url.Contains("sis.redsys.es"));
            Assert.IsFalse(url.Contains("sis-t.redsys.es"));
        }
    }
}
