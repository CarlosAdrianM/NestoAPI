-- ===========================================================================
-- Script: Verifactu_03_LinFacturaVtaRectificacion.sql
-- Fecha: 03/12/2025
-- Descripción: Crea tabla para vincular líneas de facturas rectificativas
--              con las líneas de las facturas originales
-- ===========================================================================

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'LinFacturaVtaRectificacion')
BEGIN
    CREATE TABLE LinFacturaVtaRectificacion (
        -- Clave de la línea rectificativa
        Empresa CHAR(1) NOT NULL,
        NumeroFactura VARCHAR(15) NOT NULL,
        NumeroLinea INT NOT NULL,

        -- Referencia a la factura/línea original
        FacturaOriginalNumero VARCHAR(15) NOT NULL,
        FacturaOriginalLinea INT NOT NULL,

        -- Cantidad que se rectifica de esta factura original
        CantidadRectificada DECIMAL(18,4) NOT NULL,

        CONSTRAINT PK_LinFacturaVtaRectificacion
            PRIMARY KEY (Empresa, NumeroFactura, NumeroLinea,
                         FacturaOriginalNumero, FacturaOriginalLinea)
    );

    PRINT 'Tabla LinFacturaVtaRectificacion creada';
END
ELSE
BEGIN
    PRINT 'La tabla LinFacturaVtaRectificacion ya existe';
END
GO

-- Comentarios de la tabla
EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Vincula líneas de facturas rectificativas con las facturas originales que rectifican. Una línea rectificativa puede afectar a múltiples facturas originales.',
    @level0type = N'SCHEMA', @level0name = N'dbo',
    @level1type = N'TABLE',  @level1name = N'LinFacturaVtaRectificacion';
GO

PRINT '=== Tabla LinFacturaVtaRectificacion completada ===';
GO

-- ===========================================================================
-- Ejemplo de uso:
--
-- Si la factura rectificativa RV25/000001, línea 1, rectifica 10 unidades
-- que provienen de:
--   - 5 unidades de NV25/001234, línea 3
--   - 5 unidades de NV25/001100, línea 2
--
-- Se insertarían 2 registros:
-- ('1', 'RV25/000001', 1, 'NV25/001234', 3, 5.0000)
-- ('1', 'RV25/000001', 1, 'NV25/001100', 2, 5.0000)
-- ===========================================================================
