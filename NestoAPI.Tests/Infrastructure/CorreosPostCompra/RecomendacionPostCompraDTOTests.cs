using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    [TestClass]
    public class CorreoPostCompraClienteDTOTests
    {
        [TestMethod]
        public void CorreoPostCompraClienteDTO_SeInicializaConListasVacias()
        {
            var dto = new CorreoPostCompraClienteDTO();

            Assert.IsNotNull(dto.ProductosComprados);
            Assert.IsNotNull(dto.ProductosRecomendados);
            Assert.AreEqual(0, dto.ProductosComprados.Count);
            Assert.AreEqual(0, dto.ProductosRecomendados.Count);
        }

        [TestMethod]
        public void CorreoPostCompraClienteDTO_EstructuraCompleta_SeInicializaCorrectamente()
        {
            var dto = new CorreoPostCompraClienteDTO
            {
                Empresa = "1",
                ClienteId = "12345",
                ClienteNombre = "Cliente de Prueba S.L.",
                ClienteEmail = "cliente@ejemplo.com",
                SemanaDesde = new DateTime(2026, 3, 4),
                SemanaHasta = new DateTime(2026, 3, 10),
                ProductosComprados = new List<ProductoCompradoConVideoDTO>
                {
                    new ProductoCompradoConVideoDTO
                    {
                        ProductoId = "PROD-X",
                        NombreProducto = "Producto X Premium",
                        BaseImponibleTotal = 150.50m,
                        VideoYoutubeId = "abc123xyz",
                        VideoTitulo = "Tutorial Producto X",
                        EnlaceVideoProducto = "https://youtube.com/watch?v=abc123xyz&t=90",
                        EnlaceTienda = "https://tienda.com/producto-x"
                    }
                },
                ProductosRecomendados = new List<ProductoRecomendadoDTO>
                {
                    new ProductoRecomendadoDTO
                    {
                        ProductoId = "PROD-Y",
                        NombreProducto = "Producto Y Complementario",
                        VideoYoutubeId = "abc123xyz",
                        VideoTitulo = "Tutorial Producto X",
                        EnlaceVideoProducto = "https://youtube.com/watch?v=abc123xyz&t=225",
                        EnlaceTienda = "https://tienda.com/producto-y"
                    }
                }
            };

            Assert.AreEqual("1", dto.Empresa);
            Assert.AreEqual("12345", dto.ClienteId);
            Assert.AreEqual(1, dto.ProductosComprados.Count);
            Assert.AreEqual(150.50m, dto.ProductosComprados[0].BaseImponibleTotal);
            Assert.AreEqual(1, dto.ProductosRecomendados.Count);
        }
    }
}
