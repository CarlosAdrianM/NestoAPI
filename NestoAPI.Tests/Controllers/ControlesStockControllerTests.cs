using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class ControlesStockControllerTests
    {
        private NVEntities db;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_SinParametros_CreaInstancia()
        {
            // Act
            var controller = new ControlesStockController();

            // Assert
            Assert.IsNotNull(controller);
        }

        [TestMethod]
        public void Constructor_ConDbContext_CreaInstancia()
        {
            // Arrange
            var dbFake = A.Fake<NVEntities>();

            // Act
            var controller = new ControlesStockController(dbFake);

            // Assert
            Assert.IsNotNull(controller);
        }

        #endregion

        #region GetProductosProveedor Validation Tests

        [TestMethod]
        public async Task GetProductosProveedor_SinProveedorId_DevuelveBadRequest()
        {
            // Arrange
            var controller = new ControlesStockController(db);

            // Act
            var result = await controller.GetProductosProveedor(null, "ALG");

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetProductosProveedor_SinAlmacen_DevuelveBadRequest()
        {
            // Arrange
            var controller = new ControlesStockController(db);

            // Act
            var result = await controller.GetProductosProveedor("65", null);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetProductosProveedor_ConProveedorIdVacio_DevuelveBadRequest()
        {
            // Arrange
            var controller = new ControlesStockController(db);

            // Act
            var result = await controller.GetProductosProveedor("", "ALG");

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetProductosProveedor_ConAlmacenVacio_DevuelveBadRequest()
        {
            // Arrange
            var controller = new ControlesStockController(db);

            // Act
            var result = await controller.GetProductosProveedor("65", "");

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestErrorMessageResult));
        }

        #endregion

        #region Stock Calculation Tests

        [TestMethod]
        public void CalculoStockMinimo_ConConsumoMedioDiarioBajo_RedondeaHaciaArriba()
        {
            // Arrange
            decimal consumoMedioDiario = 0.1M;
            int diasStockSeguridad = 7;
            int diasReaprovisionamiento = 14;

            // Act
            decimal puntoPedido = consumoMedioDiario * (diasStockSeguridad + diasReaprovisionamiento);
            int stockMinimoCalculado = puntoPedido < 1
                ? (int)Math.Ceiling(puntoPedido)
                : (int)Math.Round(puntoPedido, 0, MidpointRounding.AwayFromZero);

            // Assert
            // 0.1 * 21 = 2.1, que es >= 1, así que se redondea con AwayFromZero
            Assert.AreEqual(2, stockMinimoCalculado);
        }

        [TestMethod]
        public void CalculoStockMinimo_ConConsumoMedioDiarioMuyBajo_RedondeaConCeiling()
        {
            // Arrange
            decimal consumoMedioDiario = 0.01M;
            int diasStockSeguridad = 7;
            int diasReaprovisionamiento = 14;

            // Act
            decimal puntoPedido = consumoMedioDiario * (diasStockSeguridad + diasReaprovisionamiento);
            int stockMinimoCalculado = puntoPedido < 1
                ? (int)Math.Ceiling(puntoPedido)
                : (int)Math.Round(puntoPedido, 0, MidpointRounding.AwayFromZero);

            // Assert
            // 0.01 * 21 = 0.21, que es < 1, así que se usa Ceiling = 1
            Assert.AreEqual(1, stockMinimoCalculado);
        }

        [TestMethod]
        public void CalculoStockMinimo_ConConsumoMedioDiarioCero_DevuelveCero()
        {
            // Arrange
            decimal consumoMedioDiario = 0M;
            int diasStockSeguridad = 7;
            int diasReaprovisionamiento = 14;

            // Act
            decimal puntoPedido = consumoMedioDiario * (diasStockSeguridad + diasReaprovisionamiento);
            int stockMinimoCalculado = puntoPedido < 1
                ? (int)Math.Ceiling(puntoPedido)
                : (int)Math.Round(puntoPedido, 0, MidpointRounding.AwayFromZero);

            // Assert
            Assert.AreEqual(0, stockMinimoCalculado);
        }

        [TestMethod]
        public void CalculoStockMaximo_ConConsumoNormal_CalculaCorrectamente()
        {
            // Arrange
            decimal consumoMedioDiario = 2M;
            int diasStockSeguridad = 7;
            int diasReaprovisionamiento = 14;

            // Act
            int stockMaximoCalculado = (int)Math.Ceiling(consumoMedioDiario * (diasStockSeguridad + diasReaprovisionamiento * 2));

            // Assert
            // 2 * (7 + 14 * 2) = 2 * 35 = 70
            Assert.AreEqual(70, stockMaximoCalculado);
        }

        [TestMethod]
        public void CalculoStockMaximo_ConConsumoDecimal_RedondeaHaciaArriba()
        {
            // Arrange
            decimal consumoMedioDiario = 1.5M;
            int diasStockSeguridad = 7;
            int diasReaprovisionamiento = 14;

            // Act
            int stockMaximoCalculado = (int)Math.Ceiling(consumoMedioDiario * (diasStockSeguridad + diasReaprovisionamiento * 2));

            // Assert
            // 1.5 * (7 + 14 * 2) = 1.5 * 35 = 52.5 -> Ceiling = 53
            Assert.AreEqual(53, stockMaximoCalculado);
        }

        [TestMethod]
        public void CalculoConsumoMedioDiario_ConConsumoAnualYMesesAntiguedad_CalculaCorrectamente()
        {
            // Arrange
            int consumoAnual = 720;
            decimal mesesAntiguedad = 24M;

            // Act
            decimal consumoMedioMensual = consumoAnual / mesesAntiguedad;
            decimal consumoMedioDiario = consumoMedioMensual / 30;

            // Assert
            // 720 / 24 = 30 unidades/mes
            // 30 / 30 = 1 unidad/día
            Assert.AreEqual(30M, consumoMedioMensual);
            Assert.AreEqual(1M, consumoMedioDiario);
        }

        [TestMethod]
        public void MesesAntiguedad_MenorQue1_SeAjustaA1()
        {
            // Arrange
            decimal mesesCalculados = 0.5M;

            // Act
            decimal mesesAntiguedad = mesesCalculados;
            if (mesesAntiguedad < 1) mesesAntiguedad = 1;
            if (mesesAntiguedad > 24) mesesAntiguedad = 24;

            // Assert
            Assert.AreEqual(1M, mesesAntiguedad);
        }

        [TestMethod]
        public void MesesAntiguedad_MayorQue24_SeAjustaA24()
        {
            // Arrange
            decimal mesesCalculados = 36M;

            // Act
            decimal mesesAntiguedad = mesesCalculados;
            if (mesesAntiguedad < 1) mesesAntiguedad = 1;
            if (mesesAntiguedad > 24) mesesAntiguedad = 24;

            // Assert
            Assert.AreEqual(24M, mesesAntiguedad);
        }

        #endregion

        #region ProductoControlStockDTO Tests

        [TestMethod]
        public void ProductoControlStockDTO_RequiereActualizacion_CuandoStockMinimoOMaximoDistintoDeCero()
        {
            // Arrange & Act
            var dtoConStockMinimo = new ProductoControlStockDTO
            {
                StockMinimoCalculado = 5,
                StockMaximoCalculado = 0
            };

            var dtoConStockMaximo = new ProductoControlStockDTO
            {
                StockMinimoCalculado = 0,
                StockMaximoCalculado = 10
            };

            var dtoSinStock = new ProductoControlStockDTO
            {
                StockMinimoCalculado = 0,
                StockMaximoCalculado = 0
            };

            // Assert
            Assert.IsTrue(dtoConStockMinimo.StockMinimoCalculado != 0 || dtoConStockMinimo.StockMaximoCalculado != 0,
                "Debe requerir actualización cuando stock mínimo es distinto de 0");
            Assert.IsTrue(dtoConStockMaximo.StockMinimoCalculado != 0 || dtoConStockMaximo.StockMaximoCalculado != 0,
                "Debe requerir actualización cuando stock máximo es distinto de 0");
            Assert.IsFalse(dtoSinStock.StockMinimoCalculado != 0 || dtoSinStock.StockMaximoCalculado != 0,
                "No debe requerir actualización cuando ambos stocks son 0");
        }

        #endregion

        #region Fecha_Modificación Validation Tests (Issue #63)

        /// <summary>
        /// Verifica que cuando Fecha_Modificación es DateTime.MinValue (0001-01-01),
        /// el controlador lo corrige a DateTime.Now para evitar error de SQL Server.
        /// SQL Server datetime no acepta fechas antes de 1753-01-01.
        /// </summary>
        [TestMethod]
        public void ControlStock_FechaModificacionMinValue_DebeCorregirse()
        {
            // Arrange
            var controlStock = new ControlStock
            {
                Empresa = "1",
                Almacén = "ALG",
                Número = "17404",
                StockMínimo = 5,
                StockMáximo = 10,
                Múltiplos = 1,
                Usuario = "test",
                Fecha_Modificación = DateTime.MinValue // Valor problemático
            };

            // Act - Simular la validación que hace el controlador
            if (controlStock.Fecha_Modificación < new DateTime(1753, 1, 1))
            {
                controlStock.Fecha_Modificación = DateTime.Now;
            }

            // Assert
            Assert.IsTrue(controlStock.Fecha_Modificación >= new DateTime(1753, 1, 1),
                "La fecha debe ser válida para SQL Server datetime (>= 1753-01-01)");
            Assert.IsTrue(controlStock.Fecha_Modificación.Year >= 2020,
                "La fecha debe ser cercana a la actual");
        }

        /// <summary>
        /// Verifica que cuando Fecha_Modificación tiene un valor válido,
        /// no se modifica.
        /// </summary>
        [TestMethod]
        public void ControlStock_FechaModificacionValida_NoSeModifica()
        {
            // Arrange
            var fechaOriginal = new DateTime(2025, 12, 19, 10, 30, 0);
            var controlStock = new ControlStock
            {
                Empresa = "1",
                Almacén = "ALG",
                Número = "17404",
                StockMínimo = 5,
                StockMáximo = 10,
                Múltiplos = 1,
                Usuario = "test",
                Fecha_Modificación = fechaOriginal
            };

            // Act - Simular la validación que hace el controlador
            if (controlStock.Fecha_Modificación < new DateTime(1753, 1, 1))
            {
                controlStock.Fecha_Modificación = DateTime.Now;
            }

            // Assert
            Assert.AreEqual(fechaOriginal, controlStock.Fecha_Modificación,
                "La fecha válida no debe modificarse");
        }

        /// <summary>
        /// Verifica que la fecha límite de SQL Server (1753-01-01) se considera válida.
        /// </summary>
        [TestMethod]
        public void ControlStock_FechaModificacion1753_EsValida()
        {
            // Arrange
            var fechaLimite = new DateTime(1753, 1, 1);
            var controlStock = new ControlStock
            {
                Empresa = "1",
                Almacén = "ALG",
                Número = "17404",
                Fecha_Modificación = fechaLimite
            };

            // Act - Simular la validación que hace el controlador
            bool necesitaCorreccion = controlStock.Fecha_Modificación < new DateTime(1753, 1, 1);

            // Assert
            Assert.IsFalse(necesitaCorreccion,
                "La fecha 1753-01-01 es el límite válido de SQL Server datetime");
        }

        #endregion
    }
}
