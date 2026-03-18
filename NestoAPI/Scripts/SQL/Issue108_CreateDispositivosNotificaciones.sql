-- Issue #108: Infraestructura para notificaciones push
-- Ejecutar ANTES de desplegar y ANTES de actualizar el EDMX

CREATE TABLE dbo.DispositivosNotificaciones (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Usuario NVARCHAR(50) NOT NULL,
    Empresa CHAR(3) NULL,
    Vendedor CHAR(3) NULL,
    Cliente CHAR(10) NULL,
    Contacto CHAR(3) NULL,
    Token NVARCHAR(500) NOT NULL,
    Plataforma NVARCHAR(20) NOT NULL,       -- 'Android', 'iOS', 'Web'
    Aplicacion NVARCHAR(50) NOT NULL,        -- 'NestoApp', 'TiendasNuevaVision'
    FechaRegistro DATETIME NOT NULL DEFAULT GETDATE(),
    FechaUltimaActividad DATETIME NOT NULL DEFAULT GETDATE(),
    Activo BIT NOT NULL DEFAULT 1
);

CREATE UNIQUE INDEX UQ_DispositivosNotificaciones_Token
    ON dbo.DispositivosNotificaciones (Token);

CREATE INDEX IX_DispositivosNotificaciones_Usuario
    ON dbo.DispositivosNotificaciones (Usuario, Aplicacion);

CREATE INDEX IX_DispositivosNotificaciones_Vendedor
    ON dbo.DispositivosNotificaciones (Empresa, Vendedor, Aplicacion)
    WHERE Vendedor IS NOT NULL;

CREATE INDEX IX_DispositivosNotificaciones_Cliente
    ON dbo.DispositivosNotificaciones (Empresa, Cliente, Contacto, Aplicacion)
    WHERE Cliente IS NOT NULL;

-- Permisos
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DispositivosNotificaciones TO [ABORRAR\sqlServerSvc];

-- Tras ejecutar este script, actualizar el EDMX en Visual Studio:
-- 1. Abrir NestoEntities.edmx
-- 2. Click derecho > "Update Model from Database"
-- 3. Seleccionar la tabla DispositivosNotificaciones
-- 4. Guardar y compilar
