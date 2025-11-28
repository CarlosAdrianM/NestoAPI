using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class RoundingHelperTests
    {
        [TestInitialize]
        public void Setup()
        {
            // Asegurar estado inicial conocido para cada test
            RoundingHelper.UsarAwayFromZero = true;
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Restaurar el valor por defecto después de cada test
            RoundingHelper.UsarAwayFromZero = true;
        }

        #region Tests AwayFromZero (modo por defecto)

        [TestMethod]
        public void RoundingHelper_AwayFromZero_Redondea5HaciaArriba()
        {
            // Arrange
            RoundingHelper.UsarAwayFromZero = true;

            // Act & Assert - El .5 redondea hacia arriba (away from zero)
            Assert.AreEqual(2.35m, RoundingHelper.DosDecimalesRound(2.345m));
            Assert.AreEqual(2.36m, RoundingHelper.DosDecimalesRound(2.355m));
            Assert.AreEqual(2.47m, RoundingHelper.DosDecimalesRound(2.465m));
        }

        [TestMethod]
        public void RoundingHelper_AwayFromZero_RedondeaNegativos()
        {
            // Arrange
            RoundingHelper.UsarAwayFromZero = true;

            // Act & Assert - Negativos también se alejan del cero
            Assert.AreEqual(-2.35m, RoundingHelper.DosDecimalesRound(-2.345m));
            Assert.AreEqual(-2.36m, RoundingHelper.DosDecimalesRound(-2.355m));
        }

        [TestMethod]
        public void RoundingHelper_AwayFromZero_CasosNormales()
        {
            // Arrange
            RoundingHelper.UsarAwayFromZero = true;

            // Act & Assert - Casos sin ambigüedad
            Assert.AreEqual(10.00m, RoundingHelper.DosDecimalesRound(10.001m));
            Assert.AreEqual(10.01m, RoundingHelper.DosDecimalesRound(10.009m));
            Assert.AreEqual(99.99m, RoundingHelper.DosDecimalesRound(99.994m));
            Assert.AreEqual(100.00m, RoundingHelper.DosDecimalesRound(99.996m));
        }

        #endregion

        #region Tests ToEven (modo VB6)

        [TestMethod]
        public void RoundingHelper_ToEven_RedondeaAlParMasCercano()
        {
            // Arrange
            RoundingHelper.UsarAwayFromZero = false;

            // Act & Assert - El .5 redondea al par más cercano (Banker's rounding)
            Assert.AreEqual(2.34m, RoundingHelper.DosDecimalesRound(2.345m)); // 4 es par, queda 2.34
            Assert.AreEqual(2.36m, RoundingHelper.DosDecimalesRound(2.355m)); // 6 es par, sube a 2.36
            Assert.AreEqual(2.46m, RoundingHelper.DosDecimalesRound(2.465m)); // 6 es par, queda 2.46
        }

        [TestMethod]
        public void RoundingHelper_ToEven_CasosNormales()
        {
            // Arrange
            RoundingHelper.UsarAwayFromZero = false;

            // Act & Assert - Casos sin ambigüedad (igual que AwayFromZero)
            Assert.AreEqual(10.00m, RoundingHelper.DosDecimalesRound(10.001m));
            Assert.AreEqual(10.01m, RoundingHelper.DosDecimalesRound(10.009m));
            Assert.AreEqual(99.99m, RoundingHelper.DosDecimalesRound(99.994m));
            Assert.AreEqual(100.00m, RoundingHelper.DosDecimalesRound(99.996m));
        }

        #endregion

        #region Tests de diferencia entre modos

        [TestMethod]
        public void RoundingHelper_DiferenciaEntreModos_CasosCriticos()
        {
            // Este test documenta las diferencias entre ambos modos
            decimal[] casosConDiferencia = { 2.345m, 2.465m, 0.445m, 1.235m };

            foreach (var valor in casosConDiferencia)
            {
                RoundingHelper.UsarAwayFromZero = true;
                var resultadoAwayFromZero = RoundingHelper.DosDecimalesRound(valor);

                RoundingHelper.UsarAwayFromZero = false;
                var resultadoToEven = RoundingHelper.DosDecimalesRound(valor);

                // Verificar que hay diferencia (0.01)
                Assert.AreEqual(0.01m, resultadoAwayFromZero - resultadoToEven,
                    $"Valor {valor}: AwayFromZero={resultadoAwayFromZero}, ToEven={resultadoToEven}");
            }
        }

        [TestMethod]
        public void RoundingHelper_EjemploIssue243_AcumulacionErrores()
        {
            // Ejemplo de la issue #243: dos líneas de 0.445€
            // Línea a línea: 0.45 + 0.45 = 0.90
            // Sobre total: 0.89 (0.445 + 0.445 = 0.89)

            RoundingHelper.UsarAwayFromZero = true;

            decimal linea1 = 0.445m;
            decimal linea2 = 0.445m;

            // Redondeo línea a línea
            decimal totalLineaALinea = RoundingHelper.DosDecimalesRound(linea1) +
                                       RoundingHelper.DosDecimalesRound(linea2);

            // Redondeo sobre suma total
            decimal totalSobreTotal = RoundingHelper.DosDecimalesRound(linea1 + linea2);

            Assert.AreEqual(0.90m, totalLineaALinea);
            Assert.AreEqual(0.89m, totalSobreTotal);
            Assert.AreEqual(0.01m, totalLineaALinea - totalSobreTotal,
                "Diferencia de 1 céntimo por acumulación de redondeo");
        }

        #endregion

        #region Tests Round con decimales variables

        [TestMethod]
        public void RoundingHelper_Round_ConDecimalesVariables()
        {
            RoundingHelper.UsarAwayFromZero = true;

            Assert.AreEqual(2.3m, RoundingHelper.Round(2.345m, 1));
            Assert.AreEqual(2.35m, RoundingHelper.Round(2.345m, 2));
            Assert.AreEqual(2.345m, RoundingHelper.Round(2.345m, 3));
            Assert.AreEqual(2m, RoundingHelper.Round(2.345m, 0));
        }

        #endregion

        #region Test flag por defecto

        [TestMethod]
        public void RoundingHelper_FlagPorDefecto_EsAwayFromZero()
        {
            // Este test verifica que el valor por defecto es AwayFromZero
            // Si necesitas volver a VB6, cambia el valor en RoundingHelper.UsarAwayFromZero
            Assert.IsTrue(RoundingHelper.UsarAwayFromZero,
                "El modo por defecto debe ser AwayFromZero para cumplir legislación");
        }

        #endregion

        #region Tests cálculo líneas pedido (Issue #242/#243)

        [TestMethod]
        public void RoundingHelper_CalculoLineaPedido_ImporteIVANoSeRedondea()
        {
            // Arrange - Simula cálculo como en GestorPedidosVenta.CalcularImportesLinea
            RoundingHelper.UsarAwayFromZero = true;
            decimal bruto = 100m;
            decimal descuento = 0.30m; // 30%
            byte porcentajeIVA = 21;

            // Act
            decimal baseImponible = RoundingHelper.DosDecimalesRound(bruto - (bruto * descuento)); // 70.00
            decimal importeIVA = baseImponible * porcentajeIVA / 100; // 14.70 (sin redondear)

            // Assert
            Assert.AreEqual(70.00m, baseImponible, "Base imponible debe redondearse a 2 decimales");
            Assert.AreEqual(14.70m, importeIVA, "ImporteIVA NO debe redondearse");
        }

        [TestMethod]
        public void RoundingHelper_CalculoLineaPedido_TotalEsSumaNoMultiplicacion()
        {
            // Arrange - El total debe ser suma, no multiplicación por factor
            RoundingHelper.UsarAwayFromZero = true;
            decimal baseImponible = 70.00m;
            byte porcentajeIVA = 21;
            decimal porcentajeRE = 0.052m; // 5.2% ya dividido

            // Act - Cálculo correcto (como en C#)
            decimal importeIVA = baseImponible * porcentajeIVA / 100; // 14.70
            decimal importeRE = baseImponible * porcentajeRE;          // 3.64
            decimal totalCorrecto = baseImponible + importeIVA + importeRE; // 88.34

            // Cálculo incorrecto (multiplicación por factor)
            decimal totalIncorrecto = baseImponible * (1 + porcentajeIVA / 100.0m + porcentajeRE);

            // Assert
            Assert.AreEqual(88.34m, totalCorrecto, "Total debe ser suma de BI + IVA + RE");
            Assert.AreEqual(totalCorrecto, totalIncorrecto,
                "En este caso coinciden, pero pueden diferir por precisión en otros casos");
        }

        [TestMethod]
        public void RoundingHelper_CalculoLineaPedido_CasoConDiferenciaPrecision()
        {
            // Arrange - Caso donde la multiplicación y suma dan diferente resultado
            RoundingHelper.UsarAwayFromZero = true;
            decimal bruto = 33.45m;
            decimal descuento = 0.30m; // 30%
            byte porcentajeIVA = 21;
            decimal porcentajeRE = 0.052m;

            // Act
            decimal baseImponible = RoundingHelper.DosDecimalesRound(bruto - (bruto * descuento)); // 23.42
            decimal importeIVA = baseImponible * porcentajeIVA / 100;  // 4.9182
            decimal importeRE = baseImponible * porcentajeRE;          // 1.21784

            // Total correcto: suma
            decimal totalSuma = baseImponible + importeIVA + importeRE;

            // Total incorrecto: multiplicación (como estaba el SQL antes del fix)
            decimal totalMultiplicacion = baseImponible * (1 + porcentajeIVA / 100.0m + porcentajeRE);

            // Assert - Ambos métodos deberían dar el mismo resultado matemáticamente
            Assert.AreEqual(totalSuma, totalMultiplicacion, 0.0001m,
                "Suma y multiplicación deben ser equivalentes matemáticamente");

            // Verificar valores intermedios no redondeados
            Assert.AreEqual(23.42m, baseImponible);
            Assert.AreEqual(4.9182m, importeIVA, "ImporteIVA no debe redondearse");
            Assert.AreEqual(1.21784m, importeRE, "ImporteRE no debe redondearse");
        }

        [TestMethod]
        public void RoundingHelper_CalculoLineaPedido_ConsistenciaConSQL()
        {
            // Este test documenta cómo deben calcularse los valores para que
            // coincidan con el SQL del auto-fix en ServicioFacturas.RecalcularLineasPedido
            //
            // SQL:
            //   [Base Imponible] = ROUND(Bruto - ImporteDescuento, 2)
            //   ImporteIVA = NuevaBI * PorcentajeIVA / 100.0
            //   ImporteRE = NuevaBI * PorcentajeRE
            //   Total = NuevaBI + ImporteIVA + ImporteRE

            RoundingHelper.UsarAwayFromZero = true;

            // Datos de ejemplo
            decimal bruto = 54.90m;
            decimal descuentoCliente = 0m;
            decimal descuentoProducto = 0.30m;
            decimal descuento = 0m;
            decimal descuentoPP = 0m;
            bool aplicarDto = true;
            byte porcentajeIVA = 21;
            decimal porcentajeRE = 0m;

            // Cálculo como C#
            decimal sumaDescuentos = aplicarDto
                ? 1 - ((1 - descuentoCliente) * (1 - descuentoProducto) * (1 - descuento) * (1 - descuentoPP))
                : 1 - ((1 - descuento) * (1 - descuentoPP));

            decimal importeDescuento = bruto * sumaDescuentos;
            decimal baseImponible = RoundingHelper.DosDecimalesRound(bruto - importeDescuento);
            decimal importeIVA = baseImponible * porcentajeIVA / 100;
            decimal importeRE = baseImponible * porcentajeRE;
            decimal total = baseImponible + importeIVA + importeRE;

            // Assert
            Assert.AreEqual(0.30m, sumaDescuentos, "Suma descuentos = 30%");
            Assert.AreEqual(38.43m, baseImponible, "Base imponible redondeada");
            Assert.AreEqual(8.0703m, importeIVA, "ImporteIVA sin redondear");
            Assert.AreEqual(0m, importeRE, "ImporteRE sin redondear");
            Assert.AreEqual(46.5003m, total, "Total sin redondear");
        }

        #endregion
    }
}
