using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GeneradorPdfModelo347Tests
    {
        [TestMethod]
        public void Generar_ConDatosValidos_DevuelvePdfNoVacio()
        {
            // Arrange
            var generador = new GeneradorPdfModelo347();
            var datos = CrearDatosPrueba();
            var empresa = CrearEmpresaPrueba();
            int anno = 2025;

            // Act
            byte[] resultado = generador.Generar(datos, empresa, anno);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Length > 0, "El PDF generado no debe estar vacío");
        }

        [TestMethod]
        public void Generar_ConDatosValidos_GeneraPdfConCabeceraPdf()
        {
            // Arrange
            var generador = new GeneradorPdfModelo347();
            var datos = CrearDatosPrueba();
            var empresa = CrearEmpresaPrueba();
            int anno = 2025;

            // Act
            byte[] resultado = generador.Generar(datos, empresa, anno);

            // Assert
            // Los archivos PDF comienzan con "%PDF-"
            Assert.IsTrue(resultado.Length >= 5, "El PDF debe tener al menos 5 bytes");
            Assert.AreEqual((byte)'%', resultado[0]);
            Assert.AreEqual((byte)'P', resultado[1]);
            Assert.AreEqual((byte)'D', resultado[2]);
            Assert.AreEqual((byte)'F', resultado[3]);
            Assert.AreEqual((byte)'-', resultado[4]);
        }

        [TestMethod]
        public void Generar_ConImportesEnCero_DevuelvePdfValido()
        {
            // Arrange
            var generador = new GeneradorPdfModelo347();
            var datos = new Mod347DTO
            {
                nombre = "Cliente Sin Operaciones",
                cifNif = "00000000X",
                direccion = "Calle Test 123",
                codigoPostal = "28000",
                trimestre = new decimal[] { 0, 0, 0, 0 }
            };
            var empresa = CrearEmpresaPrueba();
            int anno = 2025;

            // Act
            byte[] resultado = generador.Generar(datos, empresa, anno);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Length > 0);
        }

        [TestMethod]
        public void Generar_ConDatosMinimos_NoLanzaExcepcion()
        {
            // Arrange
            var generador = new GeneradorPdfModelo347();
            var datos = new Mod347DTO
            {
                nombre = "",
                cifNif = "",
                direccion = "",
                codigoPostal = "",
                trimestre = new decimal[] { 0, 0, 0, 0 }
            };
            var empresa = new Empresa
            {
                Nombre = "",
                NIF = "",
                Dirección = "",
                Población = "",
                Provincia = "",
                CodPostal = ""
            };
            int anno = 2025;

            // Act
            byte[] resultado = generador.Generar(datos, empresa, anno);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Length > 0);
        }

        [TestMethod]
        public void Generar_ConImportesAltos_DevuelvePdfValido()
        {
            // Arrange
            var generador = new GeneradorPdfModelo347();
            var datos = new Mod347DTO
            {
                nombre = "Gran Cliente S.L.",
                cifNif = "B12345678",
                direccion = "Avenida Principal 1",
                codigoPostal = "28001",
                trimestre = new decimal[] { 125000.50m, 98765.43m, 150000.00m, 175234.57m }
            };
            var empresa = CrearEmpresaPrueba();
            int anno = 2025;

            // Act
            byte[] resultado = generador.Generar(datos, empresa, anno);

            // Assert
            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Length > 0);
        }

        [TestMethod]
        public void Mod347DTO_Total_CalculaSumaCorrectaTrimestres()
        {
            // Arrange
            var datos = new Mod347DTO
            {
                trimestre = new decimal[] { 1000.00m, 2000.00m, 3000.00m, 4000.00m }
            };

            // Act
            decimal total = datos.total;

            // Assert
            Assert.AreEqual(10000.00m, total);
        }

        [TestMethod]
        public void Mod347DTO_Total_ConDecimales_CalculaCorrectamente()
        {
            // Arrange
            var datos = new Mod347DTO
            {
                trimestre = new decimal[] { 1234.56m, 2345.67m, 3456.78m, 4567.89m }
            };

            // Act
            decimal total = datos.total;

            // Assert
            Assert.AreEqual(11604.90m, total);
        }

        #region Helpers

        private Mod347DTO CrearDatosPrueba()
        {
            return new Mod347DTO
            {
                nombre = "Cliente de Prueba S.L.",
                cifNif = "B12345678",
                direccion = "Calle Test 123, Bajo A",
                codigoPostal = "28001",
                trimestre = new decimal[] { 5000.00m, 6000.00m, 7000.00m, 8000.00m }
            };
        }

        private Empresa CrearEmpresaPrueba()
        {
            return new Empresa
            {
                Nombre = "Nueva Visión S.A.",
                NIF = "A12345678",
                Dirección = "Calle Principal 1",
                Dirección2 = "Nave 5",
                Población = "Algete",
                Provincia = "Madrid",
                CodPostal = "28110"
            };
        }

        #endregion
    }
}
