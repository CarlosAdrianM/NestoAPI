using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    [TestClass]
    public class RecomendacionPostCompraDTOTests
    {
        [TestMethod]
        public void VideoRecomendadoDTO_ProductosComprados_CuentaCorrectamente()
        {
            // Arrange
            var video = new VideoRecomendadoDTO
            {
                VideoId = 1,
                Titulo = "Video de prueba",
                Productos = new List<ProductoEnVideoDTO>
                {
                    new ProductoEnVideoDTO { ProductoId = "PROD1", YaComprado = true },
                    new ProductoEnVideoDTO { ProductoId = "PROD2", YaComprado = true },
                    new ProductoEnVideoDTO { ProductoId = "PROD3", YaComprado = false },
                    new ProductoEnVideoDTO { ProductoId = "PROD4", YaComprado = false },
                    new ProductoEnVideoDTO { ProductoId = "PROD5", YaComprado = false }
                }
            };

            // Act & Assert
            Assert.AreEqual(2, video.ProductosComprados, "Debería contar 2 productos comprados");
            Assert.AreEqual(3, video.ProductosNoComprados, "Debería contar 3 productos no comprados");
        }

        [TestMethod]
        public void VideoRecomendadoDTO_SinProductos_DevuelveCero()
        {
            // Arrange
            var video = new VideoRecomendadoDTO
            {
                VideoId = 1,
                Titulo = "Video vacío",
                Productos = new List<ProductoEnVideoDTO>()
            };

            // Act & Assert
            Assert.AreEqual(0, video.ProductosComprados);
            Assert.AreEqual(0, video.ProductosNoComprados);
        }

        [TestMethod]
        public void VideoRecomendadoDTO_ProductosNull_DevuelveCero()
        {
            // Arrange
            var video = new VideoRecomendadoDTO
            {
                VideoId = 1,
                Titulo = "Video sin productos",
                Productos = null
            };

            // Act & Assert
            Assert.AreEqual(0, video.ProductosComprados);
            Assert.AreEqual(0, video.ProductosNoComprados);
        }

        [TestMethod]
        public void ProductoEnVideoDTO_DistingueEntrePedidoActualYHistorico()
        {
            // Arrange - Producto comprado antes pero no en este pedido
            var productoHistorico = new ProductoEnVideoDTO
            {
                ProductoId = "PROD1",
                YaComprado = true,
                EnPedidoActual = false
            };

            // Arrange - Producto del pedido actual
            var productoPedidoActual = new ProductoEnVideoDTO
            {
                ProductoId = "PROD2",
                YaComprado = true, // También está en histórico porque lo acaba de comprar
                EnPedidoActual = true
            };

            // Arrange - Producto que nunca ha comprado
            var productoNuevo = new ProductoEnVideoDTO
            {
                ProductoId = "PROD3",
                YaComprado = false,
                EnPedidoActual = false
            };

            // Assert
            Assert.IsTrue(productoHistorico.YaComprado);
            Assert.IsFalse(productoHistorico.EnPedidoActual);

            Assert.IsTrue(productoPedidoActual.YaComprado);
            Assert.IsTrue(productoPedidoActual.EnPedidoActual);

            Assert.IsFalse(productoNuevo.YaComprado);
            Assert.IsFalse(productoNuevo.EnPedidoActual);
        }

        [TestMethod]
        public void RecomendacionPostCompraDTO_EstructuraCompleta_SeInicializaCorrectamente()
        {
            // Arrange & Act
            var recomendacion = new RecomendacionPostCompraDTO
            {
                Empresa = "1",
                ClienteId = "12345",
                ClienteNombre = "Cliente de Prueba S.L.",
                ClienteEmail = "cliente@ejemplo.com",
                PedidoNumero = 98765,
                FechaPedido = new System.DateTime(2025, 1, 15),
                Videos = new List<VideoRecomendadoDTO>
                {
                    new VideoRecomendadoDTO
                    {
                        VideoId = 1,
                        VideoYoutubeId = "abc123xyz",
                        Titulo = "Cómo usar el producto X",
                        Productos = new List<ProductoEnVideoDTO>
                        {
                            new ProductoEnVideoDTO
                            {
                                ProductoId = "PROD-X",
                                NombreProducto = "Producto X Premium",
                                TiempoAparicion = "1:30",
                                EnlaceVideo = "https://youtube.com/watch?v=abc123xyz&t=90",
                                EnlaceTienda = "https://tienda.com/producto-x",
                                YaComprado = true,
                                EnPedidoActual = true
                            },
                            new ProductoEnVideoDTO
                            {
                                ProductoId = "PROD-Y",
                                NombreProducto = "Producto Y Complementario",
                                TiempoAparicion = "3:45",
                                EnlaceVideo = "https://youtube.com/watch?v=abc123xyz&t=225",
                                EnlaceTienda = "https://tienda.com/producto-y",
                                YaComprado = false,
                                EnPedidoActual = false
                            }
                        }
                    }
                }
            };

            // Assert
            Assert.AreEqual("1", recomendacion.Empresa);
            Assert.AreEqual("12345", recomendacion.ClienteId);
            Assert.AreEqual("Cliente de Prueba S.L.", recomendacion.ClienteNombre);
            Assert.AreEqual("cliente@ejemplo.com", recomendacion.ClienteEmail);
            Assert.AreEqual(98765, recomendacion.PedidoNumero);
            Assert.AreEqual(1, recomendacion.Videos.Count);
            Assert.AreEqual(2, recomendacion.Videos[0].Productos.Count);
            Assert.AreEqual(1, recomendacion.Videos[0].ProductosComprados);
            Assert.AreEqual(1, recomendacion.Videos[0].ProductosNoComprados);
        }
    }
}
