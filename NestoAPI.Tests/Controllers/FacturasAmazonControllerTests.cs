using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.CanalesExternos.Amazon;
using NestoAPI.Models.CanalesExternos;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#366: endpoints de subir facturas a Amazon y consultar el estado de subida.
    /// </summary>
    [TestClass]
    public class FacturasAmazonControllerTests
    {
        private IServicioFacturasAmazon servicio;
        private FacturasAmazonController controlador;

        [TestInitialize]
        public void Inicializar()
        {
            servicio = A.Fake<IServicioFacturasAmazon>();
            controlador = new FacturasAmazonController(servicio);
        }

        [TestMethod]
        public async Task SubirFactura_SinEmpresaOPedido_BadRequest()
        {
            Assert.IsInstanceOfType(await controlador.SubirFactura(null), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await controlador.SubirFactura(new SubirFacturaAmazonRequestDTO { Empresa = "", Pedido = 1 }), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await controlador.SubirFactura(new SubirFacturaAmazonRequestDTO { Empresa = "1", Pedido = 0 }), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task SubirFactura_Correcto_DevuelveLaRespuestaDelServicio()
        {
            var respuesta = new SubirFacturaAmazonResponseDTO { NumeroFactura = "NV26100200", FeedId = "feed-1", Estado = "ENVIADA" };
            A.CallTo(() => servicio.FacturarYSubirAsync("1", 922000, A<string>._)).Returns(Task.FromResult(respuesta));

            var resultado = await controlador.SubirFactura(new SubirFacturaAmazonRequestDTO { Empresa = "1", Pedido = 922000 })
                as OkNegotiatedContentResult<SubirFacturaAmazonResponseDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("NV26100200", resultado.Content.NumeroFactura);
        }

        [TestMethod]
        public async Task SubirFactura_ElServicioLanzaInvalidOperation_BadRequestConElMensaje()
        {
            A.CallTo(() => servicio.FacturarYSubirAsync("1", 922000, A<string>._))
                .ThrowsAsync(new InvalidOperationException("El pedido 922000 no tiene AmazonOrderId"));

            var resultado = await controlador.SubirFactura(new SubirFacturaAmazonRequestDTO { Empresa = "1", Pedido = 922000 })
                as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "no tiene AmazonOrderId");
        }

        [TestMethod]
        public void FacturasSubidas_ListaSeparadaPorComas_LlamaAlServicioConLosNumeros()
        {
            A.CallTo(() => servicio.ConsultarSubidas("1", A<IReadOnlyCollection<int>>.That.Matches(p => p.Count == 3)))
                .Returns(new List<FacturaSubidaAmazonDTO> { new FacturaSubidaAmazonDTO { Pedido = 922000 } });

            var resultado = controlador.GetFacturasSubidas("1", "922000, 922001,922002")
                as OkNegotiatedContentResult<List<FacturaSubidaAmazonDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
        }

        [TestMethod]
        public void FacturasSubidas_PedidoNoNumerico_BadRequest()
        {
            Assert.IsInstanceOfType(controlador.GetFacturasSubidas("1", "922000,pepe"), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(controlador.GetFacturasSubidas("1", ""), typeof(BadRequestErrorMessageResult));
        }
    }
}
