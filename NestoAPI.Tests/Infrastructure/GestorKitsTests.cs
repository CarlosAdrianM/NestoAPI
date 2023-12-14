using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorKitsTests
    {
        [TestMethod]
        public void GestorKits_AlMontarKit_UnKitCompuestoPorUnSoloProductoDevuelveDosLineas()
        {
            // Arrange
            IProductoService servicio = A.Fake<IProductoService>();
            A.CallTo(() => servicio.LeerProducto("1", "PROD", true)).Returns(new ProductoDTO()
            {
                ProductosKit = new List<ProductoKit>
                {
                    new ProductoKit { Cantidad = 2, ProductoId = "KIT"}
                }
            });
            GestorKits gestorKits = new GestorKits(servicio, A.Fake<IUbicacionService>());

            // Act
            var listaPreExtracto = gestorKits.ProductosMontarKit("1", "ALM", "PROD", 3, "usuario").GetAwaiter().GetResult();

            // Assert
            Assert.AreEqual(2, listaPreExtracto.Count);
            Assert.AreEqual(3, listaPreExtracto[0].Cantidad);
            Assert.AreEqual(-6, listaPreExtracto[1].Cantidad);
            Assert.AreEqual("PROD", listaPreExtracto[0].Producto);
            Assert.AreEqual("KIT", listaPreExtracto[1].Producto);
            Assert.AreEqual("Montaje del kit PROD", listaPreExtracto[0].Texto);
            Assert.AreEqual("Montaje del kit PROD", listaPreExtracto[1].Texto);
        }

        [TestMethod]
        public void GestorKits_AlMontarKit_SiLaCantidadEsNegativaSeConsideraDesmontaje()
        {
            // Arrange
            IProductoService servicio = A.Fake<IProductoService>();
            A.CallTo(() => servicio.LeerProducto("1", "PROD", true)).Returns(new ProductoDTO()
            {
                ProductosKit = new List<ProductoKit>
                {
                    new ProductoKit { Cantidad = 2, ProductoId = "KIT"}
                }
            });
            GestorKits gestorKits = new GestorKits(servicio, A.Fake<IUbicacionService>());

            // Act
            var listaPreExtracto = gestorKits.ProductosMontarKit("1", "ALM", "PROD", -3, "usuario").GetAwaiter().GetResult();

            // Assert
            Assert.AreEqual(2, listaPreExtracto.Count);
            Assert.AreEqual(-3, listaPreExtracto[0].Cantidad);
            Assert.AreEqual(6, listaPreExtracto[1].Cantidad);
            Assert.AreEqual("PROD", listaPreExtracto[0].Producto);
            Assert.AreEqual("KIT", listaPreExtracto[1].Producto);
            Assert.AreEqual("Desmontaje del kit PROD", listaPreExtracto[0].Texto);
            Assert.AreEqual("Desmontaje del kit PROD", listaPreExtracto[1].Texto);
        }
    }
}
