using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.OpenAI;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    [TestClass]
    public class GeneradorContenidoCorreoPostCompraTests
    {
        private IServicioOpenAI _servicioOpenAIMock;
        private GeneradorContenidoCorreoPostCompra _generador;

        [TestInitialize]
        public void Setup()
        {
            _servicioOpenAIMock = A.Fake<IServicioOpenAI>();
            _generador = new GeneradorContenidoCorreoPostCompra(_servicioOpenAIMock);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_ConCorreoNull_DevuelveNull()
        {
            var resultado = await _generador.GenerarContenidoHtml(null);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_SinProductosComprados_DevuelveNull()
        {
            var correo = new CorreoPostCompraClienteDTO
            {
                ClienteNombre = "Test Cliente",
                ProductosComprados = new List<ProductoCompradoConVideoDTO>()
            };

            var resultado = await _generador.GenerarContenidoHtml(correo);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_ProductosCompradosNull_DevuelveNull()
        {
            var correo = new CorreoPostCompraClienteDTO
            {
                ClienteNombre = "Test Cliente",
                ProductosComprados = null
            };

            var resultado = await _generador.GenerarContenidoHtml(correo);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_ConProductosComprados_LlamaAOpenAI()
        {
            var correo = CrearCorreoValido();

            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Returns("<div>HTML generado</div>");

            var resultado = await _generador.GenerarContenidoHtml(correo);

            Assert.IsNotNull(resultado);
            Assert.AreEqual("<div>HTML generado</div>", resultado);
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_IncluyeNombreClienteEnMensaje()
        {
            var correo = CrearCorreoValido();
            correo.ClienteNombre = "Peluquería María";

            string mensajeCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => mensajeCapturado = user)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsTrue(mensajeCapturado.Contains("Peluquería María"));
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_IncluyeProductosCompradosEnMensaje()
        {
            var correo = CrearCorreoValido();

            string mensajeCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => mensajeCapturado = user)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsTrue(mensajeCapturado.Contains("Champú Profesional"));
            Assert.IsTrue(mensajeCapturado.Contains("abc123"));
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_IncluyeProductosRecomendadosEnMensaje()
        {
            var correo = CrearCorreoValido();
            correo.ProductosRecomendados = new List<ProductoRecomendadoDTO>
            {
                new ProductoRecomendadoDTO
                {
                    ProductoId = "RECO1",
                    NombreProducto = "Mascarilla Premium",
                    VideoYoutubeId = "abc123",
                    VideoTitulo = "Tutorial",
                    EnlaceVideoProducto = "https://youtube.com/watch?v=abc123&t=200"
                }
            };

            string mensajeCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => mensajeCapturado = user)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsTrue(mensajeCapturado.Contains("Mascarilla Premium"));
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_SinProductosRecomendados_NoIncluyeSeccionRecomendados()
        {
            var correo = CrearCorreoValido();
            correo.ProductosRecomendados = new List<ProductoRecomendadoDTO>();

            string mensajeCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => mensajeCapturado = user)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsFalse(mensajeCapturado.Contains("PRODUCTOS RECOMENDADOS"));
        }

        [TestMethod]
        public void AgregarUtm_UrlSinParametros_AgregaConInterrogacion()
        {
            var url = "https://youtube.com/watch?v=abc123&t=90";

            var resultado = GeneradorContenidoCorreoPostCompra.AgregarUtm(url);

            Assert.IsTrue(resultado.Contains("&utm_source=correo_postcompra"));
            Assert.IsTrue(resultado.Contains("&utm_medium=email"));
            Assert.IsTrue(resultado.Contains("&utm_campaign=tutoriales_postcompra"));
        }

        [TestMethod]
        public void AgregarUtm_UrlSinQueryString_AgregaConInterrogacion()
        {
            var url = "https://tienda.nuevavision.es/producto.html";

            var resultado = GeneradorContenidoCorreoPostCompra.AgregarUtm(url);

            Assert.IsTrue(resultado.Contains("?utm_source=correo_postcompra"));
        }

        [TestMethod]
        public void AgregarUtm_UrlNull_DevuelveNull()
        {
            var resultado = GeneradorContenidoCorreoPostCompra.AgregarUtm(null);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_SystemPromptMencionaNuevaVision()
        {
            var correo = CrearCorreoValido();

            string systemCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => systemCapturado = system)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsTrue(systemCapturado.Contains("Nueva Visi"));
            Assert.IsTrue(systemCapturado.Contains("916"));
        }

        private CorreoPostCompraClienteDTO CrearCorreoValido()
        {
            return new CorreoPostCompraClienteDTO
            {
                Empresa = "1",
                ClienteId = "00001",
                ClienteNombre = "Cliente Test",
                ClienteEmail = "test@test.com",
                ProductosComprados = new List<ProductoCompradoConVideoDTO>
                {
                    new ProductoCompradoConVideoDTO
                    {
                        ProductoId = "PROD001",
                        NombreProducto = "Champú Profesional",
                        BaseImponibleTotal = 50m,
                        VideoYoutubeId = "abc123",
                        VideoTitulo = "Tutorial Champú",
                        EnlaceVideoProducto = "https://youtube.com/watch?v=abc123&t=120"
                    }
                },
                ProductosRecomendados = new List<ProductoRecomendadoDTO>()
            };
        }
    }
}
