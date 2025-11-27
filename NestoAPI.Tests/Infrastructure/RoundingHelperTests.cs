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
    }
}
