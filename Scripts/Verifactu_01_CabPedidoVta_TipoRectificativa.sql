-- ===========================================================================
-- Script: Verifactu_01_CabPedidoVta_TipoRectificativa.sql
-- Fecha: 02/12/2025
-- Descripción: Añade campo TipoRectificativa a la tabla CabPedidoVta
--              para indicar el tipo de factura rectificativa (R1, R3, R4)
-- ===========================================================================

-- Verificar si el campo ya existe antes de añadirlo
IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'CabPedidoVta'
    AND COLUMN_NAME = 'TipoRectificativa'
)
BEGIN
    ALTER TABLE CabPedidoVta
    ADD TipoRectificativa CHAR(2) NULL;

    PRINT 'Campo TipoRectificativa añadido a CabPedidoVta';
END
ELSE
BEGIN
    PRINT 'El campo TipoRectificativa ya existe en CabPedidoVta';
END
GO

-- Comentario del campo
EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Tipo de factura rectificativa: R1 (devolución productos), R3 (deuda incobrable), R4 (error). NULL si no es rectificativa.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'CabPedidoVta',
    @level2type = N'COLUMN', @level2name = N'TipoRectificativa';
GO

-- ===========================================================================
-- Valores permitidos:
--   R1 = Art. 80.1, 80.2, 80.6 LIVA - Devolución de productos (DEFAULT)
--   R3 = Art. 80.4 LIVA - Deuda incobrable (más de 1 año sin cobrar)
--   R4 = Resto - Error en factura (datos incorrectos)
--   NULL = No es una factura rectificativa
-- ===========================================================================
