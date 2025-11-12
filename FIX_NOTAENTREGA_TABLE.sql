-- ============================================
-- Script para arreglar tabla NotasEntrega
-- ============================================
-- Problema: La tabla no tiene PRIMARY KEY definida
-- Solución: Agregar PRIMARY KEY compuesta por NºOrden + NotaEntrega
-- ============================================

USE NV
GO

-- 1. Verificar si ya existe la PRIMARY KEY
IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_NAME = 'NotasEntrega'
    AND CONSTRAINT_TYPE = 'PRIMARY KEY'
)
BEGIN
    -- 2. Agregar PRIMARY KEY compuesta
    ALTER TABLE [dbo].[NotasEntrega]
    ADD CONSTRAINT PK_NotasEntrega PRIMARY KEY CLUSTERED
    (
        [NºOrden] ASC,
        [NotaEntrega] ASC
    )

    PRINT 'PRIMARY KEY agregada exitosamente a NotasEntrega'
END
ELSE
BEGIN
    PRINT 'La tabla NotasEntrega ya tiene PRIMARY KEY definida'
END
GO

-- 3. Verificar el resultado
SELECT
    CONSTRAINT_NAME,
    CONSTRAINT_TYPE
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
WHERE TABLE_NAME = 'NotasEntrega'
GO
