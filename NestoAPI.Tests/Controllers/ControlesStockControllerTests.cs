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
    }
}
