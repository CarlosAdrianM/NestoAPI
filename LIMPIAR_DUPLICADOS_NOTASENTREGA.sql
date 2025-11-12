-- ============================================
-- SCRIPT PARA LIMPIAR DUPLICADOS EN NotasEntrega
-- EJECUTAR SOLO SI VERIFICAR_ANTES_DE_FIX_NOTASENTREGA.sql detectó duplicados
-- ============================================
-- IMPORTANTE: Este script debe ejecutarse en una transacción
-- para poder hacer ROLLBACK si algo sale mal
-- ============================================

USE NV
GO

-- ⚠️ IMPORTANTE: Ejecutar línea por línea en modo interactivo
-- NO ejecutar todo de golpe hasta estar seguro

PRINT 'INICIO DE LIMPIEZA DE DUPLICADOS'
PRINT '=================================='
PRINT 'Este script eliminará registros duplicados dejando solo el más antiguo'
PRINT ''

-- Iniciar transacción
BEGIN TRANSACTION

PRINT 'PASO 1: Crear tabla temporal con los registros a MANTENER'
PRINT '==========================================================='

-- Crear tabla temporal con los registros a mantener (el más antiguo de cada grupo)
SELECT
    [NºOrden],
    [NotaEntrega],
    MIN([Fecha]) as [Fecha]
INTO #NotasEntregaLimpias
FROM [dbo].[NotasEntrega]
GROUP BY [NºOrden], [NotaEntrega]

PRINT 'Registros a mantener: ' + CAST(@@ROWCOUNT AS VARCHAR)
PRINT ''

PRINT 'PASO 2: Mostrar registros que se van a ELIMINAR'
PRINT '================================================'

-- Mostrar qué se va a eliminar
SELECT
    ne.[NºOrden],
    ne.[NotaEntrega],
    ne.[Fecha],
    'DUPLICADO - Se eliminará' as [Acción]
FROM [dbo].[NotasEntrega] ne
WHERE NOT EXISTS (
    SELECT 1
    FROM #NotasEntregaLimpias nel
    WHERE nel.[NºOrden] = ne.[NºOrden]
    AND nel.[NotaEntrega] = ne.[NotaEntrega]
    AND nel.[Fecha] = ne.[Fecha]
)
ORDER BY ne.[NºOrden], ne.[NotaEntrega], ne.[Fecha]

DECLARE @RegistrosAEliminar INT
SELECT @RegistrosAEliminar = COUNT(*)
FROM [dbo].[NotasEntrega] ne
WHERE NOT EXISTS (
    SELECT 1
    FROM #NotasEntregaLimpias nel
    WHERE nel.[NºOrden] = ne.[NºOrden]
    AND nel.[NotaEntrega] = ne.[NotaEntrega]
    AND nel.[Fecha] = ne.[Fecha]
)

PRINT ''
PRINT 'Total de registros duplicados a eliminar: ' + CAST(@RegistrosAEliminar AS VARCHAR)
PRINT ''
PRINT '⚠️⚠️⚠️ DECISIÓN REQUERIDA ⚠️⚠️⚠️'
PRINT 'Si los registros anteriores están correctos, ejecutar:'
PRINT '    COMMIT     -- Para aplicar cambios'
PRINT ''
PRINT 'Si quieres cancelar, ejecutar:'
PRINT '    ROLLBACK   -- Para cancelar cambios'
PRINT ''
PRINT 'NO CONTINUAR sin revisar la lista anterior'
PRINT ''

-- Limpiar tabla temporal
DROP TABLE #NotasEntregaLimpias

-- Aquí el usuario debe decidir: COMMIT o ROLLBACK
-- NO incluyo la eliminación automática para mayor seguridad

-- Si el usuario decidió COMMIT después de revisar, entonces ejecutar este paso:
/*
-- PASO 3: Eliminar duplicados (ejecutar SOLO después de revisar)
PRINT 'PASO 3: Eliminando duplicados...'

-- Recrear la tabla temporal
SELECT
    [NºOrden],
    [NotaEntrega],
    MIN([Fecha]) as [Fecha]
INTO #NotasEntregaLimpias
FROM [dbo].[NotasEntrega]
GROUP BY [NºOrden], [NotaEntrega]

-- Eliminar duplicados
DELETE ne
FROM [dbo].[NotasEntrega] ne
WHERE NOT EXISTS (
    SELECT 1
    FROM #NotasEntregaLimpias nel
    WHERE nel.[NºOrden] = ne.[NºOrden]
    AND nel.[NotaEntrega] = ne.[NotaEntrega]
    AND nel.[Fecha] = ne.[Fecha]
)

PRINT 'Registros eliminados: ' + CAST(@@ROWCOUNT AS VARCHAR)

-- Limpiar
DROP TABLE #NotasEntregaLimpias

PRINT ''
PRINT 'Limpieza completada. Ejecutar COMMIT para confirmar.'
*/

GO
