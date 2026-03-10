using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    [TestClass]
    public class CorreosPostCompraJobsServiceTests
    {
        #region AplicarModoTest

        [TestMethod]
        public void AplicarModoTest_RedirigeAlPrimerEmailDeLaLista()
        {
            var correos = new List<CorreoPostCompraClienteDTO>
            {
                new CorreoPostCompraClienteDTO { ClienteEmail = "cliente1@test.com", ClienteNombre = "Cliente 1" },
                new CorreoPostCompraClienteDTO { ClienteEmail = "cliente2@test.com", ClienteNombre = "Cliente 2" }
            };

            var resultado = CorreosPostCompraJobsService.AplicarModoTest(
                correos, "admin@test.com,otro@test.com");

            Assert.AreEqual(2, resultado.Count);
            Assert.AreEqual("admin@test.com", resultado[0].ClienteEmail);
            Assert.AreEqual("admin@test.com", resultado[1].ClienteEmail);
        }

        [TestMethod]
        public void AplicarModoTest_ConEspaciosEnEmails_LosLimpia()
        {
            var correos = new List<CorreoPostCompraClienteDTO>
            {
                new CorreoPostCompraClienteDTO { ClienteEmail = "cliente@test.com" }
            };

            var resultado = CorreosPostCompraJobsService.AplicarModoTest(
                correos, "  admin@test.com , otro@test.com ");

            Assert.AreEqual("admin@test.com", resultado[0].ClienteEmail);
        }

        [TestMethod]
        public void AplicarModoTest_ConfigVacia_DevuelveListaVacia()
        {
            var correos = new List<CorreoPostCompraClienteDTO>
            {
                new CorreoPostCompraClienteDTO { ClienteEmail = "cliente@test.com" }
            };

            var resultado = CorreosPostCompraJobsService.AplicarModoTest(correos, "");

            Assert.AreEqual(0, resultado.Count);
        }

        [TestMethod]
        public void AplicarModoTest_ConfigNull_DevuelveListaVacia()
        {
            var correos = new List<CorreoPostCompraClienteDTO>
            {
                new CorreoPostCompraClienteDTO { ClienteEmail = "cliente@test.com" }
            };

            var resultado = CorreosPostCompraJobsService.AplicarModoTest(correos, null);

            Assert.AreEqual(0, resultado.Count);
        }

        [TestMethod]
        public void AplicarModoTest_ConservaRestoDeDatosDelCorreo()
        {
            var correos = new List<CorreoPostCompraClienteDTO>
            {
                new CorreoPostCompraClienteDTO
                {
                    ClienteId = "00123",
                    ClienteNombre = "Peluquería María",
                    ClienteEmail = "maria@peluqueria.com",
                    Empresa = "1",
                    ProductosComprados = new List<ProductoCompradoConVideoDTO>
                    {
                        new ProductoCompradoConVideoDTO { ProductoId = "PROD1", NombreProducto = "Champú" }
                    }
                }
            };

            var resultado = CorreosPostCompraJobsService.AplicarModoTest(
                correos, "admin@test.com");

            Assert.AreEqual("admin@test.com", resultado[0].ClienteEmail);
            Assert.AreEqual("00123", resultado[0].ClienteId);
            Assert.AreEqual("Peluquería María", resultado[0].ClienteNombre);
            Assert.AreEqual(1, resultado[0].ProductosComprados.Count);
        }

        [TestMethod]
        public void AplicarModoTest_ListaCorreosVacia_DevuelveListaVacia()
        {
            var correos = new List<CorreoPostCompraClienteDTO>();

            var resultado = CorreosPostCompraJobsService.AplicarModoTest(
                correos, "admin@test.com");

            Assert.AreEqual(0, resultado.Count);
        }

        #endregion
    }
}
