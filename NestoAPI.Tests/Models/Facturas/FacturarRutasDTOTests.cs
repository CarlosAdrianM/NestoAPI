using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Models.Facturas
{
    [TestClass]
    public class FacturarRutasDTOTests
    {
        #region FacturarRutasRequestDTO Tests

        [TestMethod]
        public void FacturarRutasRequestDTO_TipoRutaPropia_SeAsignaCorrectamente()
        {
            // Arrange & Act
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia
            };

            // Assert
            Assert.AreEqual(TipoRutaFacturacion.RutaPropia, request.TipoRuta);
        }

        [TestMethod]
        public void FacturarRutasRequestDTO_TipoRutaAgencias_SeAsignaCorrectamente()
        {
            // Arrange & Act
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutasAgencias
            };

            // Assert
            Assert.AreEqual(TipoRutaFacturacion.RutasAgencias, request.TipoRuta);
        }

        [TestMethod]
        public void FacturarRutasRequestDTO_FechaEntregaDesde_SeAsignaCorrectamente()
        {
            // Arrange
            var fechaEsperada = new DateTime(2025, 10, 28);

            // Act
            var request = new FacturarRutasRequestDTO
            {
                FechaEntregaDesde = fechaEsperada
            };

            // Assert
            Assert.AreEqual(fechaEsperada, request.FechaEntregaDesde);
        }

        [TestMethod]
        public void FacturarRutasRequestDTO_FechaEntregaDesdeNull_PermiteNull()
        {
            // Arrange & Act
            var request = new FacturarRutasRequestDTO
            {
                FechaEntregaDesde = null
            };

            // Assert
            Assert.IsNull(request.FechaEntregaDesde);
        }

        #endregion

        #region FacturarRutasResponseDTO Tests

        [TestMethod]
        public void FacturarRutasResponseDTO_InicializaContadoresEnCero()
        {
            // Arrange & Act
            var response = new FacturarRutasResponseDTO();

            // Assert
            Assert.AreEqual(0, response.PedidosProcesados);
            Assert.AreEqual(0, response.AlbaranesCreados);
            Assert.AreEqual(0, response.FacturasCreadas);
            Assert.AreEqual(0, response.FacturasImpresas);
            Assert.AreEqual(0, response.AlbaranesImpresos);
        }

        [TestMethod]
        public void FacturarRutasResponseDTO_ListaErrores_NoEsNull()
        {
            // Arrange & Act
            var response = new FacturarRutasResponseDTO();

            // Assert
            Assert.IsNotNull(response.PedidosConErrores);
            Assert.AreEqual(0, response.PedidosConErrores.Count);
        }

        [TestMethod]
        public void FacturarRutasResponseDTO_TiempoTotal_SeAsignaCorrectamente()
        {
            // Arrange
            var tiempo = TimeSpan.FromSeconds(42);

            // Act
            var response = new FacturarRutasResponseDTO
            {
                TiempoTotal = tiempo
            };

            // Assert
            Assert.AreEqual(tiempo, response.TiempoTotal);
        }

        #endregion

        #region PedidoConErrorDTO Tests

        [TestMethod]
        public void PedidoConErrorDTO_PropiedadesBasicas_SeAsignanCorrectamente()
        {
            // Arrange & Act
            var error = new PedidoConErrorDTO
            {
                Empresa = "1",
                NumeroPedido = 901555,
                Cliente = "14375",
                Contacto = "1",
                NombreCliente = "MIRIAM TRELIS GARAY",
                Ruta = "16",
                PeriodoFacturacion = "NRM",
                TipoError = "Factura",
                MensajeError = "Error al crear factura",
                FechaEntrega = new DateTime(2025, 10, 27),
                Total = 447.91m
            };

            // Assert
            Assert.AreEqual("1", error.Empresa);
            Assert.AreEqual(901555, error.NumeroPedido);
            Assert.AreEqual("14375", error.Cliente);
            Assert.AreEqual("1", error.Contacto);
            Assert.AreEqual("MIRIAM TRELIS GARAY", error.NombreCliente);
            Assert.AreEqual("16", error.Ruta);
            Assert.AreEqual("NRM", error.PeriodoFacturacion);
            Assert.AreEqual("Factura", error.TipoError);
            Assert.AreEqual("Error al crear factura", error.MensajeError);
            Assert.AreEqual(new DateTime(2025, 10, 27), error.FechaEntrega);
            Assert.AreEqual(447.91m, error.Total);
        }

        #endregion

        #region TipoRutaFacturacion Enum Tests

        [TestMethod]
        public void TipoRutaFacturacion_TieneDosValores()
        {
            // Arrange & Act
            var valores = Enum.GetValues(typeof(TipoRutaFacturacion));

            // Assert
            Assert.AreEqual(2, valores.Length);
        }

        [TestMethod]
        public void TipoRutaFacturacion_ContieneRutaPropia()
        {
            // Arrange & Act
            var existe = Enum.IsDefined(typeof(TipoRutaFacturacion), "RutaPropia");

            // Assert
            Assert.IsTrue(existe);
        }

        [TestMethod]
        public void TipoRutaFacturacion_ContieneRutasAgencias()
        {
            // Arrange & Act
            var existe = Enum.IsDefined(typeof(TipoRutaFacturacion), "RutasAgencias");

            // Assert
            Assert.IsTrue(existe);
        }

        #endregion
    }
}
