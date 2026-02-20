-- Script para crear la tabla PagosTPV (Issues #92, #93, #59)
-- Ejecutar en SQL Server Management Studio antes de actualizar el EDMX

CREATE TABLE dbo.PagosTPV (
    Id                 INT IDENTITY(1,1) PRIMARY KEY,
    NumeroOrden        NVARCHAR(12) NOT NULL,
    Tipo               NVARCHAR(20) NOT NULL DEFAULT 'TPVVirtual', -- 'P2F' o 'TPVVirtual'
    Empresa            NVARCHAR(2) NOT NULL DEFAULT '1 ',
    Cliente            NVARCHAR(10) NULL,
    Importe            DECIMAL(18,2) NOT NULL,
    Descripcion        NVARCHAR(500) NULL,
    Correo             NVARCHAR(256) NULL,
    Movil              NVARCHAR(20) NULL,
    Estado             NVARCHAR(20) NOT NULL DEFAULT 'Pendiente',
    CodigoRespuesta    NVARCHAR(10) NULL,
    CodigoAutorizacion NVARCHAR(50) NULL,
    FechaCreacion      DATETIME NOT NULL DEFAULT GETDATE(),
    FechaActualizacion DATETIME NULL,
    Usuario            NVARCHAR(256) NULL,
    CONSTRAINT UQ_PagosTPV_NumeroOrden UNIQUE (NumeroOrden)
);

CREATE INDEX IX_PagosTPV_Cliente ON dbo.PagosTPV (Empresa, Cliente);

-- Dar permisos al usuario de la aplicación
GRANT SELECT, INSERT, UPDATE ON dbo.PagosTPV TO [NUEVAVISION\RDS2016$];

-- Después de ejecutar este script:
-- 1. Abrir NestoEntities.edmx en Visual Studio
-- 2. Click derecho > "Update Model from Database..."
-- 3. Seleccionar la tabla PagosTPV
-- 4. Guardar el EDMX
-- 5. Eliminar Models/Pagos/PagoTPV.cs (la entidad POCO temporal)
-- 6. Eliminar el DbSet<PagoTPV> temporal de NVEntities.Partial.cs
