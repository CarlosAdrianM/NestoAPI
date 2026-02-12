using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Ventas;
using NestoAPI.Models;
using System;

namespace NestoAPI.Tests.Infrastructure.Ventas
{
    [TestClass]
    public class GestorVentasClienteTests
    {
        private GestorVentasCliente gestor;
        private NVEntities db;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            gestor = new GestorVentasCliente(db);
        }

        #region CalcularRangoFechas Tests

        [TestMethod]
        public void CalcularRangoFechas_ModoAnual_FechaDesdeActualEsPrimeroDeEnero()
        {
            // Act
            var resultado = gestor.CalcularRangoFechas("anual");

            // Assert
            Assert.AreEqual(new DateTime(DateTime.Today.Year, 1, 1), resultado.FechaDesdeActual);
        }

        [TestMethod]
        public void CalcularRangoFechas_ModoAnual_FechaHastaActualEsHoy()
        {
            // Act
            var resultado = gestor.CalcularRangoFechas("anual");

            // Assert
            Assert.AreEqual(DateTime.Today, resultado.FechaHastaActual);
        }

        [TestMethod]
        public void CalcularRangoFechas_ModoAnual_FechaDesdeAnteriorEsPrimeroDeEneroAnnoAnterior()
        {
            // Act
            var resultado = gestor.CalcularRangoFechas("anual");

            // Assert
            Assert.AreEqual(new DateTime(DateTime.Today.Year - 1, 1, 1), resultado.FechaDesdeAnterior);
        }

        [TestMethod]
        public void CalcularRangoFechas_ModoAnual_FechaHastaAnteriorEsHoyHaceUnAnno()
        {
            // Act
            var resultado = gestor.CalcularRangoFechas("anual");

            // Assert
            Assert.AreEqual(DateTime.Today.AddYears(-1), resultado.FechaHastaAnterior);
        }

        [TestMethod]
        public void CalcularRangoFechas_ModoUltimos12Meses_FechaDesdeActualEsHaceMas364Dias()
        {
            // Act
            var resultado = gestor.CalcularRangoFechas("ultimos12meses");

            // Assert
            var esperado = DateTime.Today.AddYears(-1).Date.AddDays(1);
            Assert.AreEqual(esperado, resultado.FechaDesdeActual);
        }

        [TestMethod]
        public void CalcularRangoFechas_ModoUltimos12Meses_FechaHastaActualEsHoy()
        {
            // Act
            var resultado = gestor.CalcularRangoFechas("ultimos12meses");

            // Assert
            Assert.AreEqual(DateTime.Today, resultado.FechaHastaActual);
        }

        [TestMethod]
        public void CalcularRangoFechas_ModoUltimos12Meses_PeriodoAnteriorEsUnAnnoAntes()
        {
            // Act
            var resultado = gestor.CalcularRangoFechas("ultimos12meses");

            // Assert
            Assert.AreEqual(resultado.FechaDesdeActual.AddYears(-1), resultado.FechaDesdeAnterior);
            Assert.AreEqual(resultado.FechaHastaActual.AddYears(-1), resultado.FechaHastaAnterior);
        }

        #endregion
    }
}
