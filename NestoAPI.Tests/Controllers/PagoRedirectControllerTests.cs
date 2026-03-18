using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Pagos;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class PagoRedirectControllerTests
    {
        [TestMethod]
        public void RespuestaIniciarPago_IncluyeTokenAccesoYUrl()
        {
            var respuesta = new RespuestaIniciarPago
            {
                IdPago = 1,
                TokenAcceso = System.Guid.NewGuid(),
                UrlPaginaPago = $"https://api.nuevavision.es/pago/{System.Guid.NewGuid()}"
            };

            Assert.AreNotEqual(System.Guid.Empty, respuesta.TokenAcceso);
            Assert.IsTrue(respuesta.UrlPaginaPago.StartsWith("https://api.nuevavision.es/pago/"));
        }

        [TestMethod]
        public void RespuestaIniciarPago_UrlPaginaPago_ContieneTokenAcceso()
        {
            var token = System.Guid.NewGuid();
            var respuesta = new RespuestaIniciarPago
            {
                TokenAcceso = token,
                UrlPaginaPago = $"https://api.nuevavision.es/pago/{token}"
            };

            Assert.IsTrue(respuesta.UrlPaginaPago.Contains(token.ToString()));
        }

        [TestMethod]
        public void PagoRedirectController_SePuedeConstruirConDI()
        {
            var redsysService = A.Fake<IRedsysService>();

            var controller = new PagoRedirectController(redsysService);

            Assert.IsNotNull(controller);
        }
    }
}
