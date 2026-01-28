using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Models
{
    [TestClass]
    public class LineaPedidoVentaDTOTests
    {
        #region Tests para SumaDescuentosSinPP (Issue #79)

        [TestMethod]
        public void SumaDescuentosSinPP_ConAplicarDescuentoTrue_CalculaSumaCorrectamente()
        {
            // Arrange
            var linea = new LineaPedidoVentaDTO
            {
                AplicarDescuento = true,
                DescuentoEntidad = 0.10m,  // 10%
                DescuentoProducto = 0.05m, // 5%
                DescuentoLinea = 0.02m     // 2%
            };

            // Act
            decimal resultado = linea.SumaDescuentosSinPP;

            // Assert
            // 1 - ((1 - 0.10) * (1 - 0.05) * (1 - 0.02))
            // 1 - (0.90 * 0.95 * 0.98)
            // 1 - 0.8379 = 0.1621 (16.21%)
            decimal esperado = 1 - ((1 - 0.10m) * (1 - 0.05m) * (1 - 0.02m));
            Assert.AreEqual(esperado, resultado,
                $"Con AplicarDescuento=true, debería calcular la suma combinada. Esperado: {esperado}, Obtenido: {resultado}");
        }

        [TestMethod]
        public void SumaDescuentosSinPP_ConAplicarDescuentoFalse_DevuelveSoloDescuentoLinea()
        {
            // Arrange
            var linea = new LineaPedidoVentaDTO
            {
                AplicarDescuento = false,
                DescuentoEntidad = 0.10m,  // Ignorado
                DescuentoProducto = 0.05m, // Ignorado
                DescuentoLinea = 0.15m     // 15%
            };

            // Act
            decimal resultado = linea.SumaDescuentosSinPP;

            // Assert
            Assert.AreEqual(0.15m, resultado,
                "Con AplicarDescuento=false, debería devolver solo DescuentoLinea");
        }

        [TestMethod]
        public void SumaDescuentosSinPP_SinDescuentos_DevuelveCero()
        {
            // Arrange
            var linea = new LineaPedidoVentaDTO
            {
                AplicarDescuento = true,
                DescuentoEntidad = 0m,
                DescuentoProducto = 0m,
                DescuentoLinea = 0m
            };

            // Act
            decimal resultado = linea.SumaDescuentosSinPP;

            // Assert
            // 1 - ((1 - 0) * (1 - 0) * (1 - 0)) = 1 - 1 = 0
            Assert.AreEqual(0m, resultado,
                "Sin descuentos, SumaDescuentosSinPP debería ser 0");
        }

        [TestMethod]
        public void SumaDescuentosSinPP_ConDescuento100Porciento_DevuelveUno()
        {
            // Arrange
            var linea = new LineaPedidoVentaDTO
            {
                AplicarDescuento = true,
                DescuentoEntidad = 1.0m,  // 100%
                DescuentoProducto = 0m,
                DescuentoLinea = 0m
            };

            // Act
            decimal resultado = linea.SumaDescuentosSinPP;

            // Assert
            // 1 - ((1 - 1) * (1 - 0) * (1 - 0)) = 1 - 0 = 1
            Assert.AreEqual(1.0m, resultado,
                "Con 100% de descuento, SumaDescuentosSinPP debería ser 1");
        }

        [TestMethod]
        public void SumaDescuentosSinPP_NoIncluyeDescuentoPP()
        {
            // Arrange - Este test verifica que PP NO se incluye en SumaDescuentosSinPP
            var pedido = new PedidoVentaDTO
            {
                DescuentoPP = 0.02m  // 2% Pronto Pago
            };

            var linea = new LineaPedidoVentaDTO
            {
                Pedido = pedido,
                AplicarDescuento = true,
                DescuentoEntidad = 0.10m,
                DescuentoProducto = 0.05m,
                DescuentoLinea = 0m
            };

            // Act
            decimal sumaConPP = linea.SumaDescuentos;       // INCLUYE PP
            decimal sumaSinPP = linea.SumaDescuentosSinPP;  // NO incluye PP

            // Assert
            Assert.AreNotEqual(sumaConPP, sumaSinPP,
                "SumaDescuentosSinPP debe ser diferente a SumaDescuentos cuando hay PP");

            // SumaDescuentosSinPP: 1 - (0.90 * 0.95 * 1.0) = 1 - 0.855 = 0.145
            decimal esperadoSinPP = 1 - ((1 - 0.10m) * (1 - 0.05m) * (1 - 0m));
            Assert.AreEqual(esperadoSinPP, sumaSinPP,
                $"SumaDescuentosSinPP no debe incluir el descuento PP. Esperado: {esperadoSinPP}, Obtenido: {sumaSinPP}");

            // SumaDescuentos: 1 - (0.90 * 0.95 * 1.0 * 0.98) = 1 - 0.8379 = 0.1621
            decimal esperadoConPP = 1 - ((1 - 0.10m) * (1 - 0.05m) * (1 - 0m) * (1 - 0.02m));
            Assert.AreEqual(esperadoConPP, sumaConPP,
                $"SumaDescuentos debe incluir el descuento PP. Esperado: {esperadoConPP}, Obtenido: {sumaConPP}");
        }

        [TestMethod]
        public void SumaDescuentosSinPP_SoloDescuentoLinea_ConAplicarDescuentoTrue()
        {
            // Arrange
            var linea = new LineaPedidoVentaDTO
            {
                AplicarDescuento = true,
                DescuentoEntidad = 0m,
                DescuentoProducto = 0m,
                DescuentoLinea = 0.20m  // 20%
            };

            // Act
            decimal resultado = linea.SumaDescuentosSinPP;

            // Assert
            // 1 - ((1 - 0) * (1 - 0) * (1 - 0.20)) = 1 - 0.80 = 0.20
            Assert.AreEqual(0.20m, resultado,
                "Con solo DescuentoLinea, debería devolver ese valor");
        }

        [TestMethod]
        public void SumaDescuentosSinPP_DescuentosCombinados_CalculaCorrectamente()
        {
            // Arrange - Caso real: 10% entidad + 5% producto + 3% línea
            var linea = new LineaPedidoVentaDTO
            {
                AplicarDescuento = true,
                DescuentoEntidad = 0.10m,
                DescuentoProducto = 0.05m,
                DescuentoLinea = 0.03m
            };

            // Act
            decimal resultado = linea.SumaDescuentosSinPP;

            // Assert
            // Los descuentos se aplican multiplicativamente:
            // Precio final = Precio * (1 - 0.10) * (1 - 0.05) * (1 - 0.03)
            // Precio final = Precio * 0.90 * 0.95 * 0.97 = Precio * 0.82935
            // Descuento total = 1 - 0.82935 = 0.17065 (17.065%)
            decimal esperado = 1 - (0.90m * 0.95m * 0.97m);
            Assert.AreEqual(esperado, resultado, 0.0001m,
                $"Descuentos combinados deben calcularse multiplicativamente. Esperado: {esperado:P2}, Obtenido: {resultado:P2}");
        }

        #endregion

        #region Tests para DescuentoCliente/DescuentoEntidad - Bug Issue (falta mapear DescuentoCliente a DescuentoEntidad)

        /// <summary>
        /// Test que documenta el bug: cuando DescuentoCliente NO se mapea a DescuentoEntidad,
        /// el cálculo de SumaDescuentos es incorrecto.
        ///
        /// Datos reales del pedido 908818:
        /// - DescuentoCliente = 0.10 (10%)
        /// - Descuento = 0.10 (10%)
        /// - SumaDescuentos en BD = 0.19 (19%)
        /// - ImporteDto = -11.40
        ///
        /// Pero en DetallePedidoVenta muestra 6€ porque DescuentoEntidad no está mapeado.
        /// </summary>
        [TestMethod]
        public void SumaDescuentos_ConDescuentoCliente10Porciento_DebeCalcular19Porciento()
        {
            // Arrange - Simula los datos del pedido 908818
            // En la BD: DescuentoCliente=0.10, Descuento=0.10, SumaDescuentos=0.19
            var pedido = new PedidoVentaDTO { DescuentoPP = 0m };
            var linea = new LineaPedidoVentaDTO
            {
                Pedido = pedido,
                AplicarDescuento = true,
                DescuentoEntidad = 0.10m,  // Esto es DescuentoCliente en la BD
                DescuentoProducto = 0m,
                DescuentoLinea = 0.10m,    // Esto es Descuento en la BD
                PrecioUnitario = 60m,
                Cantidad = 1
            };

            // Act
            decimal sumaDescuentos = linea.SumaDescuentos;

            // Assert
            // Fórmula: 1 - ((1 - 0.10) * (1 - 0) * (1 - 0.10)) = 1 - (0.9 * 1 * 0.9) = 1 - 0.81 = 0.19
            decimal esperado = 0.19m;
            Assert.AreEqual(esperado, sumaDescuentos, 0.001m,
                $"Con DescuentoCliente=10% y Descuento=10%, SumaDescuentos debe ser 19%. Obtenido: {sumaDescuentos}");
        }

        [TestMethod]
        public void ImporteDescuento_ConDescuentoCliente10Porciento_DebeCalcular11Euro40()
        {
            // Arrange - Simula los datos del pedido 908818
            // Precio=60€, Cantidad=1, DescuentoCliente=10%, Descuento=10%
            // ImporteDto esperado = 60 * 0.19 = 11.40€
            var pedido = new PedidoVentaDTO { DescuentoPP = 0m };
            var linea = new LineaPedidoVentaDTO
            {
                Pedido = pedido,
                AplicarDescuento = true,
                DescuentoEntidad = 0.10m,  // DescuentoCliente
                DescuentoProducto = 0m,
                DescuentoLinea = 0.10m,    // Descuento
                PrecioUnitario = 60m,
                Cantidad = 1
            };

            // Act
            decimal importeDto = linea.ImporteDescuento;

            // Assert
            // Bruto = 60, SumaDescuentos = 0.19, ImporteDto = 60 * 0.19 = 11.40
            Assert.AreEqual(11.40m, importeDto, 0.01m,
                $"Con Precio=60€ y SumaDescuentos=19%, ImporteDescuento debe ser 11.40€. Obtenido: {importeDto}");
        }

        /// <summary>
        /// Test que demuestra el BUG actual: cuando DescuentoEntidad NO está mapeado (es 0),
        /// el ImporteDescuento sale incorrecto (6€ en lugar de 11.40€).
        ///
        /// Este test DEBERÍA FALLAR después de aplicar el fix.
        /// </summary>
        [TestMethod]
        public void Bug_SinMapearDescuentoCliente_ImporteDescuentoEsIncorrecto()
        {
            // Arrange - Simula el BUG: DescuentoCliente NO se mapea a DescuentoEntidad
            // En la BD existe DescuentoCliente=0.10, pero en el DTO DescuentoEntidad=0 (no mapeado)
            var pedido = new PedidoVentaDTO { DescuentoPP = 0m };
            var linea = new LineaPedidoVentaDTO
            {
                Pedido = pedido,
                AplicarDescuento = true,
                DescuentoEntidad = 0m,     // BUG: No se mapea DescuentoCliente
                DescuentoProducto = 0m,
                DescuentoLinea = 0.10m,    // Descuento = 10%
                PrecioUnitario = 60m,
                Cantidad = 1
            };

            // Act
            decimal importeDto = linea.ImporteDescuento;
            decimal sumaDescuentos = linea.SumaDescuentos;

            // Assert - Este test documenta el comportamiento INCORRECTO actual
            // Con el bug: SumaDescuentos = 0.10 (solo DescuentoLinea)
            // Sin el bug: SumaDescuentos = 0.19 (DescuentoCliente + DescuentoLinea)
            Assert.AreEqual(0.10m, sumaDescuentos, 0.001m,
                "BUG: Sin mapear DescuentoCliente, SumaDescuentos solo considera DescuentoLinea");
            Assert.AreEqual(6m, importeDto, 0.01m,
                "BUG: Sin mapear DescuentoCliente, ImporteDescuento es 6€ en lugar de 11.40€");
        }

        /// <summary>
        /// Test que verifica que cuando se mapea correctamente DescuentoCliente a DescuentoEntidad,
        /// el cálculo de BaseImponible también es correcto.
        /// </summary>
        [TestMethod]
        public void BaseImponible_ConDescuentoCliente_DebeCalcularCorrectamente()
        {
            // Arrange
            var pedido = new PedidoVentaDTO { DescuentoPP = 0m };
            var linea = new LineaPedidoVentaDTO
            {
                Pedido = pedido,
                AplicarDescuento = true,
                DescuentoEntidad = 0.10m,  // DescuentoCliente
                DescuentoProducto = 0m,
                DescuentoLinea = 0.10m,    // Descuento
                PrecioUnitario = 60m,
                Cantidad = 1
            };

            // Act
            decimal baseImponible = linea.BaseImponible;

            // Assert
            // Bruto = 60, ImporteDto = 11.40, BaseImponible = 60 - 11.40 = 48.60
            Assert.AreEqual(48.60m, baseImponible, 0.01m,
                $"BaseImponible debe ser 48.60€. Obtenido: {baseImponible}");
        }

        #endregion
    }
}
