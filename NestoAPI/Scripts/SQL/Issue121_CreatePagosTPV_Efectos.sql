-- Script para crear la tabla PagosTPV_Efectos (Issue #121 - Pagos multiples)
-- Ejecutar en SQL Server Management Studio ANTES de actualizar el EDMX

CREATE TABLE dbo.PagosTPV_Efectos (
    Id                 INT IDENTITY(1,1) PRIMARY KEY,
    IdPago             INT NOT NULL,
    ExtractoClienteId  INT NOT NULL,
    Importe            DECIMAL(18,2) NOT NULL,
    Documento          NVARCHAR(10) NULL,
    Efecto             NVARCHAR(3) NULL,
    Contacto           NVARCHAR(3) NULL,
    Vendedor           NVARCHAR(3) NULL,
    FormaVenta         NVARCHAR(3) NULL,
    Delegacion         NVARCHAR(3) NULL,
    TipoApunte         NVARCHAR(3) NULL,

    CONSTRAINT FK_PagosTPV_Efectos_Pago
        FOREIGN KEY (IdPago) REFERENCES dbo.PagosTPV(Id)
);

CREATE INDEX IX_PagosTPV_Efectos_IdPago
    ON dbo.PagosTPV_Efectos(IdPago);

CREATE INDEX IX_PagosTPV_Efectos_ExtractoClienteId
    ON dbo.PagosTPV_Efectos(ExtractoClienteId);

-- Dar permisos al usuario de la aplicacion
GRANT SELECT, INSERT, UPDATE ON dbo.PagosTPV_Efectos TO [NUEVAVISION\RDS2016$];

-- Despues de ejecutar este script:
-- 1. Abrir NestoEntities.edmx en Visual Studio
-- 2. Click derecho > "Update Model from Database..."
-- 3. Seleccionar la tabla PagosTPV_Efectos
-- 4. Guardar el EDMX
-- 5. Eliminar Models/PagoTPV_Efecto.cs (la entidad POCO temporal)
-- 6. Eliminar el DbSet temporal de NVEntities.Partial.cs
