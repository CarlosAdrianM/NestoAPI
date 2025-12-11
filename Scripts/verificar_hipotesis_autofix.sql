-- ============================================================================
-- VERIFICACIÓN DE HIPÓTESIS: ¿El auto-fix causa el descuadre contable?
-- Fecha: 02/12/2025
-- ============================================================================
--
-- Hipótesis: El auto-fix modifica BaseImponible de forma que después no cuadra
-- con los descuentos que el SP recalcula directamente desde Bruto.
--
-- El asiento contable necesita que:
--   HABER = DEBE
--   SUM(ROUND(Bruto,2)) = SUM(Descuentos) + SUM(BaseImponible)
--
-- Pero:
--   - El auto-fix calcula: BaseImponible = Bruto - ROUND(Bruto * SumaDescuentos, 2)
--   - El SP calcula descuentos: sum(round(Bruto*Descuento,2)) para CADA tipo de descuento
--
-- ============================================================================

-- EJEMPLO: Pedido 904887, Línea 44713
-- Bruto = 67.4325
-- DescuentoProducto = 0.15
-- Otros descuentos = 0

-- ============================================================================
-- CÁLCULO DEL AUTO-FIX (C#/SQL)
-- ============================================================================
DECLARE @Bruto DECIMAL(18,4) = 67.4325
DECLARE @DtoProducto DECIMAL(18,4) = 0.15
DECLARE @DtoCliente DECIMAL(18,4) = 0
DECLARE @Dto DECIMAL(18,4) = 0
DECLARE @DtoPP DECIMAL(18,4) = 0

-- Fórmula del auto-fix:
-- SumaDescuentos = 1 - (1-DtoProducto)*(1-DtoCliente)*(1-Dto)*(1-DtoPP)
DECLARE @SumaDescuentos DECIMAL(18,8) = 1 - (1-@DtoProducto)*(1-@DtoCliente)*(1-@Dto)*(1-@DtoPP)
-- = 1 - (0.85)*(1)*(1)*(1) = 1 - 0.85 = 0.15

DECLARE @ImporteDto_AutoFix DECIMAL(18,4) = ROUND(@Bruto * @SumaDescuentos, 2)
-- = ROUND(67.4325 * 0.15, 2) = ROUND(10.114875, 2) = 10.11

DECLARE @BaseImponible_AutoFix DECIMAL(18,4) = @Bruto - @ImporteDto_AutoFix
-- = 67.4325 - 10.11 = 57.3225

SELECT 'AUTO-FIX calcula:' as Origen,
       @Bruto as Bruto,
       @SumaDescuentos as SumaDescuentos,
       @ImporteDto_AutoFix as ImporteDto,
       @BaseImponible_AutoFix as BaseImponible,
       ROUND(@Bruto, 2) as BrutoRedondeado,
       @ImporteDto_AutoFix + @BaseImponible_AutoFix as 'ImporteDto + BaseImponible'

-- ============================================================================
-- CÁLCULO DEL SP (para el asiento contable)
-- ============================================================================
-- El SP NO usa nuestro ImporteDto ni BaseImponible para los apuntes de descuento
-- El SP calcula los descuentos DIRECTAMENTE desde Bruto:

DECLARE @ImpDtoProducto_SP DECIMAL(18,4) = ROUND(@Bruto * @DtoProducto, 2)
-- = ROUND(67.4325 * 0.15, 2) = 10.11

-- En este caso simple (solo dto producto), es igual.
-- Pero el SP luego usa SUM(BaseImponible) de la vista para el apunte de IVA

-- ============================================================================
-- VERIFICACIÓN DEL ASIENTO
-- ============================================================================
-- HABER:
--   Cuenta 700 (Ventas): SUM(ROUND(Bruto,2)) - SUM(Descuentos_SP)
--   Cuenta 477 (IVA): ROUND(SUM(BaseImponible) * 21 / 100, 2)
--
-- DEBE:
--   Cuenta 665 (Descuentos): SUM(Descuentos_SP) (calculados directamente desde Bruto)
--   Cuenta 430 (Cliente): ROUND(SUM(BaseImponible), 2) + IVA + RE

DECLARE @BrutoRedondeado DECIMAL(18,4) = ROUND(@Bruto, 2)  -- = 67.43

-- Ventas (700) = BrutoRedondeado = 67.43
-- Descuentos (665) = ImpDtoProducto = 10.11
-- Base neta para IVA = 67.43 - 10.11 = 57.32  <-- ¡DIFERENTE de BaseImponible guardada (57.3225)!

SELECT 'SP calcula para asiento:' as Origen,
       @BrutoRedondeado as 'Ventas (700)',
       @ImpDtoProducto_SP as 'Descuento (665)',
       @BrutoRedondeado - @ImpDtoProducto_SP as 'Base neta (700-665)',
       @BaseImponible_AutoFix as 'BaseImponible guardada',
       (@BrutoRedondeado - @ImpDtoProducto_SP) - @BaseImponible_AutoFix as 'DIFERENCIA'

-- ============================================================================
-- ¡AQUÍ ESTÁ EL PROBLEMA!
-- ============================================================================
--
-- El SP pone en el HABER de Ventas: ROUND(Bruto, 2) = 67.43
-- El SP pone en el DEBE de Descuentos: ROUND(Bruto * Dto, 2) = 10.11
-- La diferencia (Ventas - Descuentos) = 67.43 - 10.11 = 57.32
--
-- PERO el SP usa SUM(BaseImponible) para calcular el IVA
-- Y nosotros guardamos BaseImponible = 67.4325 - 10.11 = 57.3225
--
-- Cuando el SP calcula el IVA sobre nuestra BaseImponible:
-- IVA = ROUND(57.3225 * 21 / 100, 2) = ROUND(12.037725, 2) = 12.04
--
-- Y cuando el SP calcula el Total del Cliente:
-- TotalCliente = ROUND(57.3225, 2) + 12.04 = 57.32 + 12.04 = 69.36
--
-- El asiento queda:
-- HABER: 700 = 67.43, 477 = 12.04   → Total HABER = 67.43 + 12.04 = 79.47
-- DEBE:  665 = 10.11, 430 = 69.36   → Total DEBE = 10.11 + 69.36 = 79.47
--
-- ¡En este caso SÍ cuadra! Pero veamos qué pasa cuando hay múltiples líneas...

-- ============================================================================
-- EJEMPLO CON 2 LÍNEAS IGUALES (como pedido 904887)
-- ============================================================================
-- Pedido 904887 tiene 2 líneas iguales: 44713 y 44711
-- Cada una con Bruto = 67.4325 y 15% descuento

DECLARE @NumLineas INT = 2

SELECT 'Con 2 líneas (pedido 904887):' as Escenario

-- Auto-fix guarda por línea:
-- BaseImponible = 57.3225 (sin redondear)
-- SUM(BaseImponible) = 57.3225 + 57.3225 = 114.645

DECLARE @SumBaseImponible DECIMAL(18,4) = @BaseImponible_AutoFix * @NumLineas
-- = 114.645

-- SP calcula para el asiento:
-- Ventas (700) = SUM(ROUND(Bruto,2)) = ROUND(67.4325,2) * 2 = 67.43 * 2 = 134.86
-- Descuentos (665) = SUM(ROUND(Bruto*Dto,2)) = 10.11 * 2 = 20.22
-- Base neta = 134.86 - 20.22 = 114.64

DECLARE @SumVentas DECIMAL(18,4) = @BrutoRedondeado * @NumLineas  -- = 134.86
DECLARE @SumDescuentos DECIMAL(18,4) = @ImpDtoProducto_SP * @NumLineas  -- = 20.22
DECLARE @BaseNeta_SP DECIMAL(18,4) = @SumVentas - @SumDescuentos  -- = 114.64

-- IVA según SP (usando nuestra BaseImponible):
DECLARE @IVA_ConBI_Guardada DECIMAL(18,4) = ROUND(@SumBaseImponible * 21 / 100, 2)
-- = ROUND(114.645 * 0.21, 2) = ROUND(24.07545, 2) = 24.08

-- TotalCliente:
DECLARE @TotalCliente DECIMAL(18,4) = ROUND(@SumBaseImponible, 2) + @IVA_ConBI_Guardada
-- = 114.65 + 24.08 = 138.73  <-- ¡AQUÍ HAY 0.01 de más por el redondeo de BI!

SELECT
    @SumVentas as 'Ventas (700)',
    @SumDescuentos as 'Descuentos (665)',
    @BaseNeta_SP as 'Base neta según SP',
    @SumBaseImponible as 'SUM(BaseImponible) guardada',
    @SumBaseImponible - @BaseNeta_SP as 'Diferencia en Base',
    @IVA_ConBI_Guardada as 'IVA calculado',
    @TotalCliente as 'TotalCliente'

-- VERIFICACIÓN DEL ASIENTO:
-- HABER:
--   Cuenta 700: 134.86 (Ventas)
--   Cuenta 477: 24.08 (IVA)
--   Total HABER = 134.86 + 24.08 = 158.94
--
-- DEBE:
--   Cuenta 665: 20.22 (Descuentos)
--   Cuenta 430: 138.73 (Cliente)
--   Total DEBE = 20.22 + 138.73 = 158.95
--
-- ¡DESCUADRE DE 0.01!

DECLARE @TotalHaber DECIMAL(18,4) = @SumVentas + @IVA_ConBI_Guardada  -- Sin contar descuentos (están en DEBE)
DECLARE @TotalDebe DECIMAL(18,4) = @SumDescuentos + @TotalCliente

SELECT
    'ASIENTO CONTABLE:' as Info,
    @SumVentas as 'HABER: Ventas (700)',
    -@SumDescuentos as 'HABER: Descuentos (-665) [compensan DEBE]',
    @IVA_ConBI_Guardada as 'HABER: IVA (477)',
    @TotalCliente as 'DEBE: Cliente (430)',
    @SumVentas - @SumDescuentos + @IVA_ConBI_Guardada as 'Total HABER neto',
    @TotalCliente as 'Total DEBE',
    (@SumVentas - @SumDescuentos + @IVA_ConBI_Guardada) - @TotalCliente as 'DESCUADRE'

-- ============================================================================
-- CONCLUSIÓN
-- ============================================================================
-- El problema está en que:
-- 1. Nosotros guardamos BaseImponible = Bruto - ROUND(Bruto*Dto,2) = 67.4325 - 10.11 = 57.3225
-- 2. El SP calcula Ventas = ROUND(Bruto,2) = 67.43
-- 3. El SP calcula Descuentos = ROUND(Bruto*Dto,2) = 10.11
-- 4. Ventas - Descuentos = 67.43 - 10.11 = 57.32 ≠ 57.3225
--
-- La diferencia de 0.0025 por línea se acumula y causa descuadres.
--
-- SOLUCIÓN:
-- BaseImponible debería ser = ROUND(Bruto,2) - ROUND(Bruto*Dto,2)
-- No = Bruto - ROUND(Bruto*Dto,2)
-- ============================================================================

DECLARE @BaseImponible_Correcta DECIMAL(18,4) = @BrutoRedondeado - @ImpDtoProducto_SP
-- = 67.43 - 10.11 = 57.32

SELECT 'SOLUCIÓN PROPUESTA:' as Info,
       @BaseImponible_AutoFix as 'BaseImponible actual (Bruto - RoundDto)',
       @BaseImponible_Correcta as 'BaseImponible correcta (RoundBruto - RoundDto)',
       @BaseImponible_AutoFix - @BaseImponible_Correcta as 'Diferencia por línea'
