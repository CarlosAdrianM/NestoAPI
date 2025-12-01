-- ===========================================================================
-- Script: Verifactu_02_CabFacturaVta_Campos.sql
-- Fecha: 03/12/2025
-- Descripción: Añade campos para persistir datos fiscales del cliente
--              y datos de Verifactu en las facturas
-- ===========================================================================

-- Datos fiscales del cliente (inmutables tras facturar)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'NombreFiscal')
BEGIN
    ALTER TABLE CabFacturaVta ADD NombreFiscal NVARCHAR(100) NULL;
    PRINT 'Campo NombreFiscal añadido';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'CifNif')
BEGIN
    ALTER TABLE CabFacturaVta ADD CifNif VARCHAR(20) NULL;
    PRINT 'Campo CifNif añadido';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'DireccionFiscal')
BEGIN
    ALTER TABLE CabFacturaVta ADD DireccionFiscal NVARCHAR(200) NULL;
    PRINT 'Campo DireccionFiscal añadido';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'CodPostalFiscal')
BEGIN
    ALTER TABLE CabFacturaVta ADD CodPostalFiscal VARCHAR(10) NULL;
    PRINT 'Campo CodPostalFiscal añadido';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'PoblacionFiscal')
BEGIN
    ALTER TABLE CabFacturaVta ADD PoblacionFiscal NVARCHAR(100) NULL;
    PRINT 'Campo PoblacionFiscal añadido';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'ProvinciaFiscal')
BEGIN
    ALTER TABLE CabFacturaVta ADD ProvinciaFiscal VARCHAR(50) NULL;
    PRINT 'Campo ProvinciaFiscal añadido';
END
GO

-- Datos Verifactu
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'VerifactuUUID')
BEGIN
    ALTER TABLE CabFacturaVta ADD VerifactuUUID VARCHAR(50) NULL;
    PRINT 'Campo VerifactuUUID añadido';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'VerifactuHuella')
BEGIN
    ALTER TABLE CabFacturaVta ADD VerifactuHuella VARCHAR(100) NULL;
    PRINT 'Campo VerifactuHuella añadido';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'VerifactuQR')
BEGIN
    ALTER TABLE CabFacturaVta ADD VerifactuQR NVARCHAR(MAX) NULL;
    PRINT 'Campo VerifactuQR añadido';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'VerifactuURL')
BEGIN
    ALTER TABLE CabFacturaVta ADD VerifactuURL VARCHAR(500) NULL;
    PRINT 'Campo VerifactuURL añadido';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'VerifactuEstado')
BEGIN
    ALTER TABLE CabFacturaVta ADD VerifactuEstado VARCHAR(50) NULL;
    PRINT 'Campo VerifactuEstado añadido';
END
GO

-- Tipo de rectificativa (copiado del pedido)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'TipoRectificativa')
BEGIN
    ALTER TABLE CabFacturaVta ADD TipoRectificativa CHAR(2) NULL;
    PRINT 'Campo TipoRectificativa añadido';
END
GO

PRINT '=== Campos de CabFacturaVta completados ===';
GO
