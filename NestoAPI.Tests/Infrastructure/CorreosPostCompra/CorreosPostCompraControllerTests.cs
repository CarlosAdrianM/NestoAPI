using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    [TestClass]
    public class CorreosPostCompraControllerTests
    {
        private IServicioRecomendacionesPostCompra _servicioRecomendacionesMock;
        private IGeneradorContenidoCorreoPostCompra _generadorContenidoMock;
        private IServicioCorreoElectronico _servicioCorreoMock;
        private CorreosPostCompraController _controller;

        [TestInitialize]
        public void Setup()
        {
            _servicioRecomendacionesMock = A.Fake<IServicioRecomendacionesPostCompra>();
            _generadorContenidoMock = A.Fake<IGeneradorContenidoCorreoPostCompra>();
            _servicioCorreoMock = A.Fake<IServicioCorreoElectronico>();
            _controller = new CorreosPostCompraController(
                _servicioRecomendacionesMock, _generadorContenidoMock, _servicioCorreoMock);
        }

        #region GetRecomendaciones

        [TestMethod]
        public async Task GetRecomendaciones_DevuelveOkConListaDeCorreos()
        {
            var correos = new List<CorreoPostCompraClienteDTO>
            {
                CrearCorreoCliente("00001", "Cliente 1")
            };

            A.CallTo(() => _servicioRecomendacionesMock.ObtenerCorreosSemana(
                A<string>.Ignored, A<System.DateTime>.Ignored, A<System.DateTime>.Ignored))
                .Returns(correos);

            var resultado = await _controller.GetRecomendaciones();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<CorreoPostCompraClienteDTO>>));
            var okResult = (OkNegotiatedContentResult<List<CorreoPostCompraClienteDTO>>)resultado;
            Assert.AreEqual(1, okResult.Content.Count);
        }

        [TestMethod]
        public async Task GetRecomendaciones_SinCorreos_DevuelveOkConListaVacia()
        {
            A.CallTo(() => _servicioRecomendacionesMock.ObtenerCorreosSemana(
                A<string>.Ignored, A<System.DateTime>.Ignored, A<System.DateTime>.Ignored))
                .Returns(new List<CorreoPostCompraClienteDTO>());

            var resultado = await _controller.GetRecomendaciones();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<CorreoPostCompraClienteDTO>>));
            var okResult = (OkNegotiatedContentResult<List<CorreoPostCompraClienteDTO>>)resultado;
            Assert.AreEqual(0, okResult.Content.Count);
        }

        #endregion

        #region GetPreviewCorreo

        [TestMethod]
        public async Task GetPreviewCorreo_SinParametroCliente_DevuelveBadRequest()
        {
            var resultado = await _controller.GetPreviewCorreo(cliente: null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetPreviewCorreo_ClienteNoEncontrado_DevuelveOkConMensaje()
        {
            A.CallTo(() => _servicioRecomendacionesMock.ObtenerCorreosSemana(
                A<string>.Ignored, A<System.DateTime>.Ignored, A<System.DateTime>.Ignored))
                .Returns(new List<CorreoPostCompraClienteDTO>());

            var resultado = await _controller.GetPreviewCorreo(cliente: "99999");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<PreviewCorreoDTO>));
            var okResult = (OkNegotiatedContentResult<PreviewCorreoDTO>)resultado;
            Assert.IsNull(okResult.Content.HtmlGenerado);
        }

        [TestMethod]
        public async Task GetPreviewCorreo_ClienteEncontrado_GeneraHtml()
        {
            var correos = new List<CorreoPostCompraClienteDTO>
            {
                CrearCorreoCliente("00001", "Cliente 1")
            };

            A.CallTo(() => _servicioRecomendacionesMock.ObtenerCorreosSemana(
                A<string>.Ignored, A<System.DateTime>.Ignored, A<System.DateTime>.Ignored))
                .Returns(correos);

            A.CallTo(() => _generadorContenidoMock.GenerarContenidoHtml(
                A<CorreoPostCompraClienteDTO>.Ignored))
                .Returns("<div>Preview</div>");

            var resultado = await _controller.GetPreviewCorreo(cliente: "00001");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<PreviewCorreoDTO>));
            var okResult = (OkNegotiatedContentResult<PreviewCorreoDTO>)resultado;
            Assert.AreEqual("<div>Preview</div>", okResult.Content.HtmlGenerado);
            Assert.AreEqual("Cliente 1", okResult.Content.ClienteNombre);
        }

        #endregion

        #region GetEnviarPrueba

        [TestMethod]
        public async Task GetEnviarPrueba_SinParametroCliente_DevuelveBadRequest()
        {
            var resultado = await _controller.GetEnviarPrueba(cliente: null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetEnviarPrueba_ClienteNoEncontrado_DevuelveNoEnviado()
        {
            A.CallTo(() => _servicioRecomendacionesMock.ObtenerCorreosSemana(
                A<string>.Ignored, A<System.DateTime>.Ignored, A<System.DateTime>.Ignored))
                .Returns(new List<CorreoPostCompraClienteDTO>());

            var resultado = await _controller.GetEnviarPrueba(cliente: "99999");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoEnvioPruebaDTO>));
            var okResult = (OkNegotiatedContentResult<ResultadoEnvioPruebaDTO>)resultado;
            Assert.IsFalse(okResult.Content.Enviado);
        }

        [TestMethod]
        public async Task GetEnviarPrueba_TodoOk_EnviaCorreoYDevuelveEnviado()
        {
            var correos = new List<CorreoPostCompraClienteDTO>
            {
                CrearCorreoCliente("00001", "Cliente 1")
            };

            A.CallTo(() => _servicioRecomendacionesMock.ObtenerCorreosSemana(
                A<string>.Ignored, A<System.DateTime>.Ignored, A<System.DateTime>.Ignored))
                .Returns(correos);

            A.CallTo(() => _generadorContenidoMock.GenerarContenidoHtml(
                A<CorreoPostCompraClienteDTO>.Ignored))
                .Returns("<div>HTML</div>");

            A.CallTo(() => _servicioCorreoMock.EnviarCorreoSMTP(
                A<System.Net.Mail.MailMessage>.Ignored))
                .Returns(true);

            var resultado = await _controller.GetEnviarPrueba(cliente: "00001");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoEnvioPruebaDTO>));
            var okResult = (OkNegotiatedContentResult<ResultadoEnvioPruebaDTO>)resultado;
            Assert.IsTrue(okResult.Content.Enviado);
            A.CallTo(() => _servicioCorreoMock.EnviarCorreoSMTP(
                A<System.Net.Mail.MailMessage>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetEnviarPrueba_ErrorGenerandoHtml_DevuelveNoEnviado()
        {
            var correos = new List<CorreoPostCompraClienteDTO>
            {
                CrearCorreoCliente("00001", "Cliente 1")
            };

            A.CallTo(() => _servicioRecomendacionesMock.ObtenerCorreosSemana(
                A<string>.Ignored, A<System.DateTime>.Ignored, A<System.DateTime>.Ignored))
                .Returns(correos);

            A.CallTo(() => _generadorContenidoMock.GenerarContenidoHtml(
                A<CorreoPostCompraClienteDTO>.Ignored))
                .Returns((string)null);

            var resultado = await _controller.GetEnviarPrueba(cliente: "00001");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoEnvioPruebaDTO>));
            var okResult = (OkNegotiatedContentResult<ResultadoEnvioPruebaDTO>)resultado;
            Assert.IsFalse(okResult.Content.Enviado);
        }

        #endregion

        #region Helpers

        private CorreoPostCompraClienteDTO CrearCorreoCliente(string clienteId, string nombre)
        {
            return new CorreoPostCompraClienteDTO
            {
                Empresa = "1",
                ClienteId = clienteId,
                ClienteNombre = nombre,
                ClienteEmail = $"{clienteId}@test.com",
                ProductosComprados = new List<ProductoCompradoConVideoDTO>
                {
                    new ProductoCompradoConVideoDTO
                    {
                        ProductoId = "PROD1",
                        NombreProducto = "Producto Test",
                        VideoYoutubeId = "abc123",
                        VideoTitulo = "Tutorial"
                    }
                },
                ProductosRecomendados = new List<ProductoRecomendadoDTO>()
            };
        }

        #endregion
    }
}
