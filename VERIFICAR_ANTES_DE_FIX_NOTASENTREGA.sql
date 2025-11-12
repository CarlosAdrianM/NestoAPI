-- ============================================
-- SCRIPTS DE VERIFICACIÓN ANTES DE AGREGAR PRIMARY KEY
-- Ejecutar ANTES de FIX_NOTAENTREGA_TABLE.sql
-- ============================================

USE NV
GO

PRINT '=========================================='
PRINT 'VERIFICACIÓN 1: ¿Ya existe PRIMARY KEY?'
PRINT '=========================================='

IF EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_NAME = 'NotasEntrega'
    AND CONSTRAINT_TYPE = 'PRIMARY KEY'
)
BEGIN
    PRINT '⚠️  ADVERTENCIA: Ya existe una PRIMARY KEY en NotasEntrega'
    PRINT '    NO ejecutar el script de FIX'

    -- Mostrar cuál es la PRIMARY KEY actual
    SELECT
        CONSTRAINT_NAME,
        COLUMN_NAME
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
    WHERE TABLE_NAME = 'NotasEntrega'
    AND CONSTRAINT_NAME IN (
        SELECT CONSTRAINT_NAME
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE TABLE_NAME = 'NotasEntrega'
        AND CONSTRAINT_TYPE = 'PRIMARY KEY'
    )
END
ELSE
BEGIN
    PRINT '✓ OK: No existe PRIMARY KEY (como esperábamos)'
END

PRINT ''
PRINT '=========================================='
PRINT 'VERIFICACIÓN 2: ¿Existen duplicados?'
PRINT '=========================================='

-- Buscar duplicados en la combinación (NºOrden, NotaEntrega)
DECLARE @Duplicados INT

SELECT @Duplicados = COUNT(*)
FROM (
    SELECT
        [NºOrden],
        [NotaEntrega],
        COUNT(*) as Cantidad
    FROM [dbo].[NotasEntrega]
    GROUP BY [NºOrden], [NotaEntrega]
    HAVING COUNT(*) > 1
) AS Duplicados

IF @Duplicados > 0
BEGIN
    PRINT '❌ ERROR: Existen ' + CAST(@Duplicados AS VARCHAR) + ' combinaciones duplicadas'
    PRINT '    Revisar estos registros ANTES de aplicar PRIMARY KEY:'
    PRINT ''

    -- Mostrar los duplicados
    SELECT
        [NºOrden],
        [NotaEntrega],
        COUNT(*) as [Veces Repetido],
        MIN([Fecha]) as [Primera Fecha],
        MAX([Fecha]) as [Última Fecha]
    FROM [dbo].[NotasEntrega]
    GROUP BY [NºOrden], [NotaEntrega]
    HAVING COUNT(*) > 1
    ORDER BY COUNT(*) DESC, [NºOrden], [NotaEntrega]

    PRINT ''
    PRINT '⚠️  ACCIÓN REQUERIDA: Eliminar duplicados antes de crear PRIMARY KEY'
    PRINT '    Ver script LIMPIAR_DUPLICADOS_NOTASENTREGA.sql'
END
ELSE
BEGIN
    PRINT '✓ OK: No existen duplicados en (NºOrden, NotaEntrega)'
END

PRINT ''
PRINT '=========================================='
PRINT 'VERIFICACIÓN 3: Estadísticas de la tabla'
PRINT '=========================================='

-- Estadísticas generales
SELECT
    COUNT(*) as [Total Registros],
    COUNT(DISTINCT [NºOrden]) as [NºOrden Únicos],
    COUNT(DISTINCT [NotaEntrega]) as [NotaEntrega Únicos],
    MIN([Fecha]) as [Fecha Más Antigua],
    MAX([Fecha]) as [Fecha Más Reciente]
FROM [dbo].[NotasEntrega]

PRINT ''
PRINT '=========================================='
PRINT 'VERIFICACIÓN 4: ¿Hay valores NULL?'
PRINT '=========================================='

-- Verificar que no haya NULLs (aunque la tabla dice NOT NULL)
DECLARE @NulosNºOrden INT, @NulosNotaEntrega INT, @NulosFecha INT

SELECT
    @NulosNºOrden = COUNT(*)
FROM [dbo].[NotasEntrega]
WHERE [NºOrden] IS NULL

SELECT
    @NulosNotaEntrega = COUNT(*)
FROM [dbo].[NotasEntrega]
WHERE [NotaEntrega] IS NULL

SELECT
    @NulosFecha = COUNT(*)
FROM [dbo].[NotasEntrega]
WHERE [Fecha] IS NULL

IF @NulosNºOrden > 0 OR @NulosNotaEntrega > 0 OR @NulosFecha > 0
BEGIN
    PRINT '❌ ERROR: Existen valores NULL:'
    PRINT '    NºOrden NULLs: ' + CAST(@NulosNºOrden AS VARCHAR)
    PRINT '    NotaEntrega NULLs: ' + CAST(@NulosNotaEntrega AS VARCHAR)
    PRINT '    Fecha NULLs: ' + CAST(@NulosFecha AS VARCHAR)
END
ELSE
BEGIN
    PRINT '✓ OK: No hay valores NULL'
END

PRINT ''
PRINT '=========================================='
PRINT 'RESUMEN'
PRINT '=========================================='

IF @Duplicados = 0
BEGIN
    PRINT '✓✓✓ LISTO PARA APLICAR PRIMARY KEY ✓✓✓'
    PRINT ''
    PRINT 'Puedes ejecutar el script FIX_NOTAENTREGA_TABLE.sql'
    PRINT 'La operación es segura y no modificará datos.'
END
ELSE
BEGIN
    PRINT '⚠️⚠️⚠️ NO APLICAR PRIMARY KEY TODAVÍA ⚠️⚠️⚠️'
    PRINT ''
    PRINT 'Primero ejecutar: LIMPIAR_DUPLICADOS_NOTASENTREGA.sql'
END

GO
