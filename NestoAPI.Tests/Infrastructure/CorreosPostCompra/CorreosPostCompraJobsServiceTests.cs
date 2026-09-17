using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    [TestClass]
    public class CorreosPostCompraJobsServiceTests
    {
        #region CalcularVentana (job movido a jueves 05:00 el 17/09/26)

        [TestMethod]
        public void CalcularVentana_Jueves_CubreDeJuevesAMiercolesYEnviaElSabado()
        {
            // Jueves 24/09/2026 a las 05:00
            var (desde, hasta, envio) = CorreosPostCompraJobsService.CalcularVentana(new DateTime(2026, 9, 24, 5, 0, 0));

            Assert.AreEqual(new DateTime(2026, 9, 17), desde, "Jueves de la semana anterior");
            Assert.AreEqual(new DateTime(2026, 9, 23), hasta, "Miércoles, el día anterior completo");
            Assert.AreEqual(new DateTime(2026, 9, 26, 10, 0, 0), envio.DateTime, "Sábado a las 10:00");
        }

        [TestMethod]
        public void CalcularVentana_MismaSemanaQueElMiercolesAntiguo()
        {
            // Antes (miércoles 20:30): hoy-6..hoy = jueves 17 .. miércoles 23, envío sábado 26.
            // Ahora (jueves 05:00): tiene que salir exactamente lo mismo.
            var (desde, hasta, envio) = CorreosPostCompraJobsService.CalcularVentana(new DateTime(2026, 9, 24));

            Assert.AreEqual(new DateTime(2026, 9, 17), desde);
            Assert.AreEqual(new DateTime(2026, 9, 23), hasta);
            Assert.AreEqual(DayOfWeek.Saturday, envio.DayOfWeek);
            Assert.AreEqual(new DateTime(2026, 9, 26, 10, 0, 0), envio.DateTime);
        }

        [TestMethod]
        public void CalcularVentana_SemanasConsecutivas_NoDejanHuecoNiSolapan()
        {
            var (_, hasta1, _) = CorreosPostCompraJobsService.CalcularVentana(new DateTime(2026, 9, 24));
            var (desde2, _, _) = CorreosPostCompraJobsService.CalcularVentana(new DateTime(2026, 10, 1));

            Assert.AreEqual(hasta1.AddDays(1), desde2, "El jueves 24 queda dentro de la ventana del 1 de octubre");
        }

        [TestMethod]
        public void CalcularVentana_EnSabado_EnviaElSabadoSiguienteNoElMismoDia()
        {
            var (_, _, envio) = CorreosPostCompraJobsService.CalcularVentana(new DateTime(2026, 9, 26));
            Assert.AreEqual(new DateTime(2026, 10, 3, 10, 0, 0), envio.DateTime);
        }

        #endregion

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
