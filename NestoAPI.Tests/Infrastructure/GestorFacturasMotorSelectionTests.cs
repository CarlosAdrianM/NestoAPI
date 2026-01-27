using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using System;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests para la lógica de selección de motor PDF (Issue #55)
    /// Verifica que ObtenerGeneradorPdf devuelve el generador correcto según el parámetro MotorPdfFacturas
    /// </summary>
    [TestClass]
    public class GestorFacturasMotorSelectionTests
    {
        private IServicioFacturas _servicioFacturas;
        private ILectorParametrosUsuario _lectorParametros;
        private GestorFacturas _gestor;

        [TestInitialize]
        public void Setup()
        {
            _servicioFacturas = A.Fake<IServicioFacturas>();
            _lectorParametros = A.Fake<ILectorParametrosUsuario>();
            _gestor = new GestorFacturas(_servicioFacturas, _lectorParametros);
        }

        #region Tests para selección de motor según parámetro

        [TestMethod]
        public void ObtenerGeneradorPdf_ConParametroQuestPDF_DevuelveQuestPdf()
        {
            // Arrange
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "Carlos",
                "MotorPdfFacturas"))
                .Returns("QuestPDF");

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("Carlos");

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasQuestPdf),
                "Con parámetro 'QuestPDF', debe devolver GeneradorPdfFacturasQuestPdf");
        }

        [TestMethod]
        public void ObtenerGeneradorPdf_ConParametroRDLC_DevuelveRdlc()
        {
            // Arrange
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "Carlos",
                "MotorPdfFacturas"))
                .Returns("RDLC");

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("Carlos");

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasRdlc),
                "Con parámetro 'RDLC', debe devolver GeneradorPdfFacturasRdlc");
        }

        [TestMethod]
        public void ObtenerGeneradorPdf_ConParametroNull_DevuelveRdlc()
        {
            // Arrange
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "Carlos",
                "MotorPdfFacturas"))
                .Returns(null);

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("Carlos");

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasRdlc),
                "Con parámetro null, debe devolver GeneradorPdfFacturasRdlc (fallback)");
        }

        [TestMethod]
        public void ObtenerGeneradorPdf_ConParametroVacio_DevuelveRdlc()
        {
            // Arrange
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "Carlos",
                "MotorPdfFacturas"))
                .Returns("");

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("Carlos");

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasRdlc),
                "Con parámetro vacío, debe devolver GeneradorPdfFacturasRdlc (fallback)");
        }

        [TestMethod]
        public void ObtenerGeneradorPdf_ConParametroDesconocido_DevuelveRdlc()
        {
            // Arrange
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "Carlos",
                "MotorPdfFacturas"))
                .Returns("OtroMotor");

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("Carlos");

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasRdlc),
                "Con parámetro desconocido, debe devolver GeneradorPdfFacturasRdlc (fallback)");
        }

        [TestMethod]
        public void ObtenerGeneradorPdf_QuestPDFCaseInsensitive_DevuelveQuestPdf()
        {
            // Arrange
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "Carlos",
                "MotorPdfFacturas"))
                .Returns("questpdf");

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("Carlos");

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasQuestPdf),
                "La comparación debe ser case-insensitive");
        }

        #endregion

        #region Tests para fallback cuando usuario es null o vacío

        [TestMethod]
        public void ObtenerGeneradorPdf_SinUsuario_ConsultaDefecto()
        {
            // Arrange
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "(defecto)",
                "MotorPdfFacturas"))
                .Returns("QuestPDF");

            // Act
            var generador = _gestor.ObtenerGeneradorPdf(null);

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasQuestPdf),
                "Sin usuario, debe consultar (defecto)");
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "(defecto)",
                "MotorPdfFacturas"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ObtenerGeneradorPdf_ConUsuarioVacio_ConsultaDefecto()
        {
            // Arrange
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "(defecto)",
                "MotorPdfFacturas"))
                .Returns("RDLC");

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("");

            // Assert
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "(defecto)",
                "MotorPdfFacturas"))
                .MustHaveHappenedOnceExactly();
        }

        #endregion

        #region Tests para manejo de errores y fallback a (defecto)

        [TestMethod]
        public void ObtenerGeneradorPdf_ErrorConUsuario_IntentaConDefecto()
        {
            // Arrange: Error con usuario Carlos, éxito con (defecto)
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "Carlos",
                "MotorPdfFacturas"))
                .Throws(new Exception("Error simulado"));

            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "(defecto)",
                "MotorPdfFacturas"))
                .Returns("QuestPDF");

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("Carlos");

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasQuestPdf),
                "Si falla con usuario, debe usar el parámetro de (defecto)");
        }

        [TestMethod]
        public void ObtenerGeneradorPdf_ErrorConUsuarioYDefecto_DevuelveRdlc()
        {
            // Arrange: Error tanto con usuario como con (defecto)
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "Carlos",
                "MotorPdfFacturas"))
                .Throws(new Exception("Error con usuario"));

            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "(defecto)",
                "MotorPdfFacturas"))
                .Throws(new Exception("Error con defecto"));

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("Carlos");

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasRdlc),
                "Si fallan ambas consultas, debe devolver RDLC como fallback seguro");
        }

        [TestMethod]
        public void ObtenerGeneradorPdf_ErrorConDefecto_DevuelveRdlc()
        {
            // Arrange: Usuario es null, error con (defecto)
            A.CallTo(() => _lectorParametros.LeerParametro(
                Constantes.Empresas.EMPRESA_POR_DEFECTO,
                "(defecto)",
                "MotorPdfFacturas"))
                .Throws(new Exception("Error con defecto"));

            // Act
            var generador = _gestor.ObtenerGeneradorPdf(null);

            // Assert
            Assert.IsInstanceOfType(generador, typeof(GeneradorPdfFacturasRdlc),
                "Si falla con (defecto), debe devolver RDLC como fallback seguro");
        }

        #endregion

        #region Tests para verificar que nunca falla

        [TestMethod]
        public void ObtenerGeneradorPdf_NuncaDevuelveNull()
        {
            // Arrange: Cualquier combinación de errores
            A.CallTo(() => _lectorParametros.LeerParametro(
                A<string>.Ignored,
                A<string>.Ignored,
                A<string>.Ignored))
                .Throws(new Exception("Error total"));

            // Act
            var generador = _gestor.ObtenerGeneradorPdf("Usuario");

            // Assert
            Assert.IsNotNull(generador,
                "ObtenerGeneradorPdf NUNCA debe devolver null");
        }

        [TestMethod]
        public void ObtenerGeneradorPdf_NuncaLanzaExcepcion()
        {
            // Arrange: Errores graves
            A.CallTo(() => _lectorParametros.LeerParametro(
                A<string>.Ignored,
                A<string>.Ignored,
                A<string>.Ignored))
                .Throws(new InvalidOperationException("Error crítico"));

            // Act & Assert: No debe lanzar excepción
            try
            {
                var generador = _gestor.ObtenerGeneradorPdf("Usuario");
                Assert.IsNotNull(generador);
            }
            catch (Exception ex)
            {
                Assert.Fail($"ObtenerGeneradorPdf no debe lanzar excepciones. Excepción capturada: {ex.Message}");
            }
        }

        #endregion
    }
}
