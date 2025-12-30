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
            // IMPORTANTE: Existe restricción BD CK_LinPedidoVta_5: ([bruto]=[precio]*[cantidad] OR [tipolinea]<>(1))
            // Por eso NO se puede redondear Bruto - debe ser exactamente Cantidad * Precio
            //
            // CLAVE PARA EL ASIENTO CONTABLE (02/12/25):
            // El SP prdCrearFacturaVta construye el asiento usando:
            //   - HABER Ventas (700): SUM(ROUND(Bruto, 2))
            //   - DEBE Descuentos (665): SUM(ROUND(Bruto * Dto, 2))
            //   - La diferencia (Ventas - Descuentos) debe coincidir con SUM(BaseImponible)
            //
            // Por tanto, BaseImponible DEBE calcularse como:
            //   BaseImponible = ROUND(Bruto, 2) - ROUND(Bruto * SumaDescuentos, 2)
            //
            // SQL:
            //   Bruto = Cantidad * Precio                      -- SIN redondear (por restricción BD)
            //   ImporteDto = ROUND(Bruto * SumaDescuentos, 2)  -- ImporteDto redondeado
            //   [Base Imponible] = ROUND(Bruto, 2) - ImporteDto -- USAR ROUND(Bruto, 2)!
            //   ImporteIVA = NuevaBI * PorcentajeIVA / 100.0
            //   ImporteRE = NuevaBI * PorcentajeRE
            //   Total = NuevaBI + ImporteIVA + ImporteRE

            RoundingHelper.UsarAwayFromZero = true;

            // Datos de ejemplo
            short cantidad = 1;
            decimal precio = 54.90m;
            decimal descuentoCliente = 0m;
            decimal descuentoProducto = 0.30m;
            decimal descuento = 0m;
            decimal descuentoPP = 0m;
            bool aplicarDto = true;
            byte porcentajeIVA = 21;
            decimal porcentajeRE = 0m;

            // Cálculo como C# (coherente con GestorPedidosVenta.CalcularImportesLinea)
            // 1. Bruto NO se redondea (restricción CK_LinPedidoVta_5)
            decimal bruto = cantidad * precio;

            decimal sumaDescuentos = aplicarDto
                ? 1 - ((1 - descuentoCliente) * (1 - descuentoProducto) * (1 - descuento) * (1 - descuentoPP))
                : 1 - ((1 - descuento) * (1 - descuentoPP));

            // 2. ImporteDto se redondea ANTES de restar
            decimal importeDescuento = RoundingHelper.DosDecimalesRound(bruto * sumaDescuentos);
            // 3. BaseImponible = ROUND(Bruto, 2) - ImporteDto (USAR ROUND(Bruto, 2) para cuadrar asiento)
            decimal baseImponible = RoundingHelper.DosDecimalesRound(bruto) - importeDescuento;
            decimal importeIVA = baseImponible * porcentajeIVA / 100;
            decimal importeRE = baseImponible * porcentajeRE;
            decimal total = baseImponible + importeIVA + importeRE;

            // Assert
            Assert.AreEqual(54.90m, bruto, "Bruto = Cantidad * Precio (sin redondear)");
            Assert.AreEqual(0.30m, sumaDescuentos, "Suma descuentos = 30%");
            Assert.AreEqual(16.47m, importeDescuento, "ImporteDto redondeado (54.90 * 0.30 = 16.47)");
            Assert.AreEqual(38.43m, baseImponible, "Base imponible = ROUND(Bruto,2) - ImporteDto");
            Assert.AreEqual(8.0703m, importeIVA, "ImporteIVA sin redondear");
            Assert.AreEqual(0m, importeRE, "ImporteRE sin redondear");
            Assert.AreEqual(46.5003m, total, "Total sin redondear");
        }

        [TestMethod]
        public void RoundingHelper_CalculoLineaPedido_PrecioConCuatroDecimales()
        {
            // Este test documenta cómo se calculan las líneas con precios de 4 decimales
            // (típico de pedidos de CanalesExternos donde precio = precio_con_iva / 1.21)
            //
            // IMPORTANTE: Bruto NO se puede redondear por restricción CK_LinPedidoVta_5
            // PERO BaseImponible SÍ debe usar ROUND(Bruto, 2) para cuadrar el asiento contable.
            //
            // CLAVE (02/12/25): El SP usa ROUND(Bruto, 2) para la cuenta de Ventas (700),
            // por tanto BaseImponible = ROUND(Bruto, 2) - ImporteDto

            RoundingHelper.UsarAwayFromZero = true;

            // Datos de ejemplo de CanalesExternos (precio con 4 decimales)
            short cantidad = 1;
            decimal precio = 4.4998m; // Precio calculado como precio_con_iva / 1.21
            decimal sumaDescuentos = 0m; // Sin descuento

            // Cálculo como lo hace GestorPedidosVenta.CalcularImportesLinea
            decimal bruto = cantidad * precio; // 4.4998 (NO se redondea por restricción BD)
            decimal importeDescuento = RoundingHelper.DosDecimalesRound(bruto * sumaDescuentos); // 0
            // CLAVE: Usar ROUND(Bruto, 2) para que cuadre con el asiento contable
            decimal baseImponible = RoundingHelper.DosDecimalesRound(bruto) - importeDescuento; // 4.50

            // Assert
            Assert.AreEqual(4.4998m, bruto, "Bruto = Cantidad * Precio (NO redondeado por CK_LinPedidoVta_5)");
            Assert.AreEqual(0m, importeDescuento, "ImporteDto = 0 (sin descuento)");
            Assert.AreEqual(4.50m, baseImponible, "BaseImponible = ROUND(Bruto,2) = 4.50 (para cuadrar asiento)");
            Assert.AreNotEqual(bruto, baseImponible,
                "Con precios de 4 decimales, ROUND(Bruto,2) ≠ Bruto. Esto es correcto para cuadrar el asiento.");
        }

        [TestMethod]
        public void RoundingHelper_CalculoLineaPedido_PrecioConCuatroDecimales_ConDescuento()
        {
            // Caso con precio de 4 decimales Y descuento
            // El ImporteDto se redondea, Bruto no, y BaseImponible usa ROUND(Bruto, 2)

            RoundingHelper.UsarAwayFromZero = true;

            short cantidad = 2;
            decimal precio = 5.4545m;
            decimal sumaDescuentos = 0.10m; // 10% descuento

            // Cálculo
            decimal bruto = cantidad * precio; // 10.909 (NO redondeado)
            decimal importeDescuento = RoundingHelper.DosDecimalesRound(bruto * sumaDescuentos); // Round(1.0909) = 1.09
            // CLAVE: Usar ROUND(Bruto, 2) para que cuadre con el asiento contable
            decimal baseImponible = RoundingHelper.DosDecimalesRound(bruto) - importeDescuento; // 10.91 - 1.09 = 9.82

            // Assert
            Assert.AreEqual(10.909m, bruto, "Bruto: 2 * 5.4545 = 10.909 (sin redondear)");
            Assert.AreEqual(1.09m, importeDescuento, "ImporteDto redondeado: 10.909 * 0.10 = 1.0909 -> 1.09");
            Assert.AreEqual(9.82m, baseImponible, "BaseImponible = ROUND(10.909, 2) - 1.09 = 10.91 - 1.09 = 9.82");
        }

        [TestMethod]
        public void RoundingHelper_CalculoLineaPedido_CasoDescuadreAsientoContable()
        {
            // Este test documenta el caso que causaba descuadre en el asiento contable
            // Pedido 904887: 2 líneas con Bruto = 67.4325 y 15% descuento
            //
            // ANTES (INCORRECTO): BaseImponible = Bruto - ImporteDto = 67.4325 - 10.11 = 57.3225
            // AHORA (CORRECTO):   BaseImponible = ROUND(Bruto, 2) - ImporteDto = 67.43 - 10.11 = 57.32
            //
            // La diferencia de 0.0025 por línea se acumulaba y descuadraba el asiento.

            RoundingHelper.UsarAwayFromZero = true;

            decimal bruto = 67.4325m;
            decimal sumaDescuentos = 0.15m;

            // Cálculo INCORRECTO (antes)
            decimal importeDto = RoundingHelper.DosDecimalesRound(bruto * sumaDescuentos); // 10.11
            decimal baseImponibleIncorrecta = bruto - importeDto; // 57.3225

            // Cálculo CORRECTO (ahora)
            decimal baseImponibleCorrecta = RoundingHelper.DosDecimalesRound(bruto) - importeDto; // 57.32

            // Assert
            Assert.AreEqual(10.11m, importeDto, "ImporteDto = ROUND(67.4325 * 0.15, 2) = 10.11");
            Assert.AreEqual(57.3225m, baseImponibleIncorrecta, "Cálculo incorrecto: Bruto - ImporteDto");
            Assert.AreEqual(57.32m, baseImponibleCorrecta, "Cálculo correcto: ROUND(Bruto, 2) - ImporteDto");
            Assert.AreEqual(0.0025m, baseImponibleIncorrecta - baseImponibleCorrecta,
                "Diferencia de 0.0025 por línea - se acumula y descuadra el asiento");

            // Verificar que con 2 líneas la diferencia es 0.005
            decimal diferenciaTotal = (baseImponibleIncorrecta - baseImponibleCorrecta) * 2;
            Assert.AreEqual(0.005m, diferenciaTotal,
                "Con 2 líneas: diferencia = 0.005 - al redondear puede causar 0.01 de descuadre");
        }

        #endregion

        #region Tests restricción CK_LinPedidoVta_5 (Issue 29/12/25)

        [TestMethod]
        public void RoundingHelper_RestriccionBD_PrecioDebeRedondearse4DecimalesAntesDeBruto()
        {
            // Este test documenta el bug y la solución para la restricción CK_LinPedidoVta_5
            //
            // PROBLEMA: Cuando el precio viene con muchos decimales (ej: de CanalesExternos),
            // SQL Server redondea Precio y Bruto INDEPENDIENTEMENTE al tipo money (4 decimales).
            // Esto hace que la restricción [bruto]=[precio]*[cantidad] falle.
            //
            // SOLUCIÓN: Redondear Precio a 4 decimales ANTES de calcular Bruto.
            // De esta forma, cuando SQL Server guarde ambos valores como money,
            // la restricción se cumplirá porque ya están pre-redondeados.

            RoundingHelper.UsarAwayFromZero = true;

            // Datos reales del bug reportado (29/12/25)
            decimal precioOriginal = 27.88429752066115702479338843m;
            short cantidad = 3;

            // ANTES (INCORRECTO): Bruto = Cantidad * PrecioSinRedondear
            decimal brutoIncorrecto = cantidad * precioOriginal; // 83.65289256198347107438016529

            // Cuando SQL Server guarda:
            // - Precio money: 27.8843 (redondeado independientemente)
            // - Bruto money: 83.6529 (redondeado independientemente)
            // - Restricción: 27.8843 * 3 = 83.6529 -> PUEDE FALLAR por redondeo

            // AHORA (CORRECTO): Precio se redondea PRIMERO, luego Bruto = Cantidad * PrecioRedondeado
            decimal precioRedondeado = RoundingHelper.Round(precioOriginal, 4); // 27.8843
            decimal brutoCorrecto = cantidad * precioRedondeado; // 83.6529

            // Assert
            Assert.AreEqual(27.8843m, precioRedondeado, "Precio debe redondearse a 4 decimales");
            Assert.AreEqual(83.6529m, brutoCorrecto, "Bruto = 3 * 27.8843 = 83.6529");

            // Verificar que la restricción se cumple
            Assert.AreEqual(brutoCorrecto, precioRedondeado * cantidad,
                "Restricción CK_LinPedidoVta_5: bruto = precio * cantidad DEBE cumplirse");
        }

        [TestMethod]
        public void RoundingHelper_RestriccionBD_CasoLinea2DelBug()
        {
            // Segunda línea del bug reportado (29/12/25)
            RoundingHelper.UsarAwayFromZero = true;

            decimal precioOriginal = 45.355371900826446280991735537m;
            short cantidad = 2;

            // Solución correcta
            decimal precioRedondeado = RoundingHelper.Round(precioOriginal, 4); // 45.3554
            decimal bruto = cantidad * precioRedondeado; // 90.7108

            // Assert
            Assert.AreEqual(45.3554m, precioRedondeado, "Precio redondeado a 4 decimales");
            Assert.AreEqual(90.7108m, bruto, "Bruto = 2 * 45.3554 = 90.7108");
            Assert.AreEqual(bruto, precioRedondeado * cantidad,
                "Restricción CK_LinPedidoVta_5 se cumple");
        }

        [TestMethod]
        public void RoundingHelper_RestriccionBD_DemostracionDelProblema()
        {
            // Este test demuestra POR QUÉ falla la restricción si no se redondea el precio primero
            //
            // Ejemplo simplificado: precio = 1.00005, cantidad = 2
            // Sin redondeo: bruto = 2.0001
            // SQL Server money: precio = 1.0001, bruto = 2.0001
            // Restricción: 1.0001 * 2 = 2.0002 ≠ 2.0001 -> FALLA

            decimal precio = 1.00005m;
            short cantidad = 2;

            // Sin redondear precio primero
            decimal brutoSinRedondearPrecio = cantidad * precio; // 2.0001

            // Simular redondeo de SQL Server money (4 decimales)
            decimal precioEnMoney = Math.Round(precio, 4, MidpointRounding.AwayFromZero); // 1.0001
            decimal brutoEnMoney = Math.Round(brutoSinRedondearPrecio, 4, MidpointRounding.AwayFromZero); // 2.0001

            // La restricción falla porque se redondearon independientemente
            decimal productoEnMoney = precioEnMoney * cantidad; // 2.0002
            Assert.AreNotEqual(brutoEnMoney, productoEnMoney,
                "PROBLEMA: Bruto(2.0001) ≠ Precio(1.0001) * Cantidad(2) = 2.0002");

            // SOLUCIÓN: Redondear precio ANTES de calcular bruto
            decimal precioRedondeadoPrimero = RoundingHelper.Round(precio, 4); // 1.0001
            decimal brutoConPrecioRedondeado = cantidad * precioRedondeadoPrimero; // 2.0002

            Assert.AreEqual(brutoConPrecioRedondeado, precioRedondeadoPrimero * cantidad,
                "SOLUCIÓN: Bruto(2.0002) = Precio(1.0001) * Cantidad(2) = 2.0002 ✓");
        }

        [TestMethod]
        public void RoundingHelper_Round4Decimales_FuncionaCorrectamente()
        {
            RoundingHelper.UsarAwayFromZero = true;

            // Casos de prueba para redondeo a 4 decimales
            Assert.AreEqual(1.2346m, RoundingHelper.Round(1.23456m, 4), "Redondea hacia arriba");
            Assert.AreEqual(1.2345m, RoundingHelper.Round(1.23454m, 4), "Redondea hacia abajo");
            Assert.AreEqual(1.2346m, RoundingHelper.Round(1.234550m, 4), "0.5 redondea hacia arriba (AwayFromZero)");
            Assert.AreEqual(27.8843m, RoundingHelper.Round(27.88429752066115702479338843m, 4), "Caso real del bug");
            Assert.AreEqual(45.3554m, RoundingHelper.Round(45.355371900826446280991735537m, 4), "Caso real del bug línea 2");
        }

        #endregion
    }
}
