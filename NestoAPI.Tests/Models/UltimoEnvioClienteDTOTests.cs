using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models
{
    [TestClass]
    public class UltimoEnvioClienteDTOTests
    {
        #region UrlSeguimiento Tests

        [TestMethod]
        public void UrlSeguimiento_SinNumeroSeguimiento_DevuelveNull()
        {
            // Arrange
            var dto = new UltimoEnvioClienteDTO
            {
                AgenciaNombre = "Correos Express",
                NumeroSeguimiento = null
            };

            // Act & Assert
            Assert.IsNull(dto.UrlSeguimiento);
        }

        [TestMethod]
        public void UrlSeguimiento_CorreosExpress_DevuelveUrlCorrecta()
        {
            // Arrange
            var dto = new UltimoEnvioClienteDTO
            {
                AgenciaNombre = "Correos Express",
                NumeroSeguimiento = "123456789"
            };

            // Act
            var url = dto.UrlSeguimiento;

            // Assert
            Assert.AreEqual("https://s.correosexpress.com/c?n=123456789", url);
        }

        [TestMethod]
        public void UrlSeguimiento_ASM_ConCodigoPostal_DevuelveUrlCorrecta()
        {
            // Arrange
            var dto = new UltimoEnvioClienteDTO
            {
                AgenciaNombre = "ASM",
                NumeroSeguimiento = "ABC123",
                CodigoPostal = "28001"
            };

            // Act
            var url = dto.UrlSeguimiento;

            // Assert
            Assert.AreEqual("https://mygls.gls-spain.es/e/ABC123/28001", url);
        }

        [TestMethod]
        public void UrlSeguimiento_ASM_SinCodigoPostal_DevuelveNull()
        {
            // Arrange
            var dto = new UltimoEnvioClienteDTO
            {
                AgenciaNombre = "ASM",
                NumeroSeguimiento = "ABC123",
                CodigoPostal = null
            };

            // Act
            var url = dto.UrlSeguimiento;

            // Assert
            Assert.IsNull(url);
        }

        [TestMethod]
        public void UrlSeguimiento_Sending_ConIdentificador_DevuelveUrlCorrecta()
        {
            // Arrange
            var dto = new UltimoEnvioClienteDTO
            {
                AgenciaNombre = "Sending",
                NumeroSeguimiento = "LOC123",
                AgenciaIdentificador = "CLIENTE001"
            };

            // Act
            var url = dto.UrlSeguimiento;

            // Assert
            Assert.AreEqual("https://info.sending.es/fgts/pub/locNumServ.seam?cliente=CLIENTE001&localizador=LOC123", url);
        }

        [TestMethod]
        public void UrlSeguimiento_OnTime_DevuelveUrlConReferencia()
        {
            // Arrange
            var dto = new UltimoEnvioClienteDTO
            {
                AgenciaNombre = "OnTime",
                NumeroSeguimiento = "12345",
                Cliente = "00001",
                Pedido = 99999
            };

            // Act
            var url = dto.UrlSeguimiento;

            // Assert
            Assert.IsNotNull(url);
            Assert.IsTrue(url.Contains("ontimegts.alertran.net"));
            Assert.IsTrue(url.Contains("00001-99999"));
        }

        [TestMethod]
        public void UrlSeguimiento_AgenciaDesconocida_DevuelveNull()
        {
            // Arrange
            var dto = new UltimoEnvioClienteDTO
            {
                AgenciaNombre = "AgenciaQueNoExiste",
                NumeroSeguimiento = "123456"
            };

            // Act
            var url = dto.UrlSeguimiento;

            // Assert
            Assert.IsNull(url);
        }

        #endregion

        #region EstadoDescripcion Tests

        [TestMethod]
        public void EstadoDescripcion_Estado0_DevuelvePendiente()
        {
            var dto = new UltimoEnvioClienteDTO { Estado = 0 };
            Assert.AreEqual("Pendiente", dto.EstadoDescripcion);
        }

        [TestMethod]
        public void EstadoDescripcion_Estado1_DevuelveTramitado()
        {
            var dto = new UltimoEnvioClienteDTO { Estado = 1 };
            Assert.AreEqual("Tramitado", dto.EstadoDescripcion);
        }

        [TestMethod]
        public void EstadoDescripcion_Estado3_DevuelveEntregado()
        {
            var dto = new UltimoEnvioClienteDTO { Estado = 3 };
            Assert.AreEqual("Entregado", dto.EstadoDescripcion);
        }

        [TestMethod]
        public void EstadoDescripcion_EstadoDesconocido_DevuelveDesconocido()
        {
            var dto = new UltimoEnvioClienteDTO { Estado = 99 };
            Assert.AreEqual("Desconocido", dto.EstadoDescripcion);
        }

        #endregion
    }
}
