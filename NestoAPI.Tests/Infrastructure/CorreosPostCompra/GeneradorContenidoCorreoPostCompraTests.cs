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
        public async Task GenerarContenidoHtml_ConRecomendacionNull_DevuelveNull()
        {
            // Act
            var resultado = await _generador.GenerarContenidoHtml(null);

            // Assert
            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_SinVideos_DevuelveNull()
        {
            // Arrange
            var recomendacion = new RecomendacionPostCompraDTO
            {
                ClienteNombre = "Test Cliente",
                Videos = new List<VideoRecomendadoDTO>()
            };

            // Act
            var resultado = await _generador.GenerarContenidoHtml(recomendacion);

            // Assert
            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_ConVideosNull_DevuelveNull()
        {
            // Arrange
            var recomendacion = new RecomendacionPostCompraDTO
            {
                ClienteNombre = "Test Cliente",
                Videos = null
            };

            // Act
            var resultado = await _generador.GenerarContenidoHtml(recomendacion);

            // Assert
            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_ConVideosYProductos_LlamaAOpenAI()
        {
            // Arrange
            var recomendacion = CrearRecomendacionValida();

            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored,
                A<string>.Ignored))
                .Returns("<div>HTML generado</div>");

            // Act
            var resultado = await _generador.GenerarContenidoHtml(recomendacion);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.AreEqual("<div>HTML generado</div>", resultado);
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored,
                A<string>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_ConProductoIdNull_NoLanzaExcepcion()
        {
            // Arrange - Este test documenta el bug encontrado
            var recomendacion = new RecomendacionPostCompraDTO
            {
                ClienteNombre = "Test Cliente",
                Videos = new List<VideoRecomendadoDTO>
                {
                    new VideoRecomendadoDTO
                    {
                        VideoYoutubeId = "abc123",
                        Titulo = "Video Test",
                        Productos = new List<ProductoEnVideoDTO>
                        {
                            new ProductoEnVideoDTO
                            {
                                ProductoId = null, // Producto con ID null
                                NombreProducto = "Producto sin ID",
                                YaComprado = true,
                                EnPedidoActual = true
                            },
                            new ProductoEnVideoDTO
                            {
                                ProductoId = "12345",
                                NombreProducto = "Producto con ID",
                                YaComprado = false,
                                EnPedidoActual = false
                            }
                        }
                    }
                }
            };

            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored,
                A<string>.Ignored))
                .Returns("<div>HTML</div>");

            // Act & Assert - No debería lanzar excepción
            // NOTA: Este test fallará hasta que se aplique el fix del bug
            var resultado = await _generador.GenerarContenidoHtml(recomendacion);

            Assert.IsNotNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_LimitaA2Videos()
        {
            // Arrange
            var recomendacion = new RecomendacionPostCompraDTO
            {
                ClienteNombre = "Test Cliente",
                Videos = new List<VideoRecomendadoDTO>
                {
                    CrearVideoConProducto("video1", "Video 1"),
                    CrearVideoConProducto("video2", "Video 2"),
                    CrearVideoConProducto("video3", "Video 3") // Este no debería incluirse
                }
            };

            string mensajeCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored,
                A<string>.Ignored))
                .Invokes((string system, string user) => mensajeCapturado = user)
                .Returns("<div>HTML</div>");

            // Act
            await _generador.GenerarContenidoHtml(recomendacion);

            // Assert - El mensaje no debería contener "Video 3"
            Assert.IsNotNull(mensajeCapturado);
            Assert.IsTrue(mensajeCapturado.Contains("Video 1"));
            Assert.IsTrue(mensajeCapturado.Contains("Video 2"));
            Assert.IsFalse(mensajeCapturado.Contains("Video 3"));
        }

        private RecomendacionPostCompraDTO CrearRecomendacionValida()
        {
            return new RecomendacionPostCompraDTO
            {
                Empresa = "1",
                ClienteId = "00001",
                ClienteNombre = "Cliente Test",
                ClienteEmail = "test@test.com",
                PedidoNumero = 12345,
                Videos = new List<VideoRecomendadoDTO>
                {
                    CrearVideoConProducto("abc123", "Video Tutorial Test")
                }
            };
        }

        private VideoRecomendadoDTO CrearVideoConProducto(string youtubeId, string titulo)
        {
            return new VideoRecomendadoDTO
            {
                VideoId = 1,
                VideoYoutubeId = youtubeId,
                Titulo = titulo,
                Productos = new List<ProductoEnVideoDTO>
                {
                    new ProductoEnVideoDTO
                    {
                        ProductoId = "PROD001",
                        NombreProducto = "Producto Test",
                        YaComprado = true,
                        EnPedidoActual = true,
                        EnlaceVideo = "https://youtube.com/watch?v=abc123&t=120"
                    }
                }
            };
        }
    }
}
