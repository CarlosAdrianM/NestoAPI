-- ============================================================================
-- Issue #242: Cambiar redondeo de ToEven a AwayFromZero
-- ============================================================================
--
-- CONTEXTO:
-- NestoAPI ahora usa MidpointRounding.AwayFromZero en RoundingHelper.
-- El procedimiento almacenado de facturación usa ROUND() de SQL Server,
-- que por defecto redondea ToEven (Banker's rounding).
-- Esto causa descuadres de hasta 0.02€ que hacen fallar la facturación.
--
-- SOLUCIÓN:
-- Usar la función dbo.RoundAwayFromZero que ya existe en la BD.
--
-- ROLLBACK:
-- Si necesitas volver al comportamiento anterior:
-- 1. En NestoAPI: RoundingHelper.UsarAwayFromZero = false
-- 2. En SQL: Revertir los cambios de este script (volver a usar ROUND)
-- ============================================================================

-- Verificar que la función existe
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RoundAwayFromZero]') AND type = N'FN')
BEGIN
    PRINT 'ATENCIÓN: La función dbo.RoundAwayFromZero no existe. Créala primero:'
    PRINT '
    CREATE FUNCTION [dbo].[RoundAwayFromZero](@value DECIMAL(38, 10), @decimals INT)
    RETURNS DECIMAL(38, 10)
    AS
    BEGIN
        DECLARE @multiplier DECIMAL(38, 10) = POWER(10.0, @decimals)
        RETURN FLOOR(ABS(@value) * @multiplier + 0.5) * SIGN(@value) / @multiplier
    END
    '
    RAISERROR('La función dbo.RoundAwayFromZero no existe', 16, 1)
END
ELSE
BEGIN
    PRINT 'OK: La función dbo.RoundAwayFromZero existe.'
END
GO

-- ============================================================================
-- PASO 1: Identificar lugares que usan ROUND en facturación
-- ============================================================================
-- Ejecuta esta consulta para encontrar todos los objetos que usan ROUND:
--
-- SELECT DISTINCT
--     o.name AS ObjectName,
--     o.type_desc AS ObjectType
-- FROM sys.sql_modules m
-- INNER JOIN sys.objects o ON m.object_id = o.object_id
-- WHERE m.definition LIKE '%ROUND(%'
--   AND (o.name LIKE '%Factura%' OR o.name LIKE '%Contabil%' OR o.name LIKE '%Total%')
-- ORDER BY o.type_desc, o.name
-- ============================================================================

-- ============================================================================
-- PASO 2: Modificar vista vstContabilizarFacturaVta (si aplica)
-- ============================================================================
-- NOTA: Revisa la definición actual de la vista antes de modificar.
-- Ejemplo de cambio:
--
-- ANTES:  SELECT ... ROUND(TotalAgrupado, 2) AS Total ...
-- DESPUÉS: SELECT ... dbo.RoundAwayFromZero(TotalAgrupado, 2) AS Total ...
-- ============================================================================

-- ============================================================================
-- PASO 3: Modificar procedimiento de creación de facturas
-- ============================================================================
-- El procedimiento tiene esta validación:
--
--   if abs(@TotalCliente - @TotalClienteCuadre) > 0.02 begin
--       raiserror('Se ha producido un descuadre. Avise al Dpto. Informática',11,1)
--       rollback
--       return(-3)
--   end
--
-- Busca todos los ROUND(valor, 2) y cámbialos por dbo.RoundAwayFromZero(valor, 2)
--
-- Ejemplo de lugares comunes a modificar:
--
-- ANTES:
--   select @TotalClienteCuadre = sum(round(TotalAgrupado,2)) from ...
--
-- DESPUÉS:
--   select @TotalClienteCuadre = sum(dbo.RoundAwayFromZero(TotalAgrupado,2)) from ...
-- ============================================================================

-- ============================================================================
-- VERIFICACIÓN: Comparar resultados de ambos métodos
-- ============================================================================
-- Ejecuta esto para ver la diferencia:

SELECT
    2.345 AS Valor,
    ROUND(2.345, 2) AS Round_ToEven,
    dbo.RoundAwayFromZero(2.345, 2) AS RoundAwayFromZero,
    ROUND(2.345, 2) - dbo.RoundAwayFromZero(2.345, 2) AS Diferencia

UNION ALL

SELECT
    2.355,
    ROUND(2.355, 2),
    dbo.RoundAwayFromZero(2.355, 2),
    ROUND(2.355, 2) - dbo.RoundAwayFromZero(2.355, 2)

UNION ALL

SELECT
    0.445,
    ROUND(0.445, 2),
    dbo.RoundAwayFromZero(0.445, 2),
    ROUND(0.445, 2) - dbo.RoundAwayFromZero(0.445, 2)
GO

-- ============================================================================
-- NOTAS IMPORTANTES
-- ============================================================================
--
-- 1. NO modifiques datos históricos. Solo afecta a nuevas facturas.
--
-- 2. Si Nesto viejo sigue creando pedidos (con ToEven), esos pedidos
--    podrían dar error de descuadre al facturar. Esto es el comportamiento
--    esperado - indica inconsistencia que debe revisarse.
--
-- 3. Para volver al comportamiento anterior rápidamente:
--    - En NestoAPI: Cambiar RoundingHelper.UsarAwayFromZero = false
--    - Los cambios SQL pueden quedarse (RoundAwayFromZero con flag false
--      en NestoAPI es equivalente a ROUND con ToEven)
--
-- ============================================================================
