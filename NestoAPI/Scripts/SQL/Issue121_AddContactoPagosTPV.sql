-- Issue #121: Añadir columnas del efecto original a PagosTPV para contabilizar cobros
-- Ejecutar ANTES de desplegar esta versión

-- Primera migración
ALTER TABLE dbo.PagosTPV ADD Contacto NVARCHAR(3) NULL;

-- Segunda migración: campos del efecto original
ALTER TABLE dbo.PagosTPV ADD
    Liquidado INT NULL,
    Documento NVARCHAR(10) NULL,
    Efecto NVARCHAR(3) NULL,
    Vendedor NVARCHAR(3) NULL,
    FormaVenta NVARCHAR(3) NULL,
    Delegacion NVARCHAR(3) NULL,
    TipoApunteEfecto NVARCHAR(3) NULL;
