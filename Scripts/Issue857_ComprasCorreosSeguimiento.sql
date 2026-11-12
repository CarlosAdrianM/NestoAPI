/*
    NestoAPI#857 (fase 1, EN SOMBRA, 12/11/26): correos de seguimiento del buzón de Compras.

    1. dbo.ComprasCorreosSeguimiento: SOLO los correos con algún seguimiento reconocido o de un remitente marcado (nunca el cuerpo
       entero ni adjuntos): remitente, asunto recortado, agencia reconocida (NULL = solo lo ha captado el genérico), el primer código y
       enlace (Seguimientos = JSON con todos si hay más de uno), un extracto de ~500 caracteres alrededor, y la propuesta en sombra de
       proveedor y pedidos de compra candidatos (JSON). EmpresaReal/PedidoReal/FechaAlbaranReal los rellena el job cuando alguno de los
       candidatos se recibe (para medir si la asignación acierta). Idempotente por MensajeId (Message-ID del correo).
    2. dbo.AgenciasEntradaRemitentes: direcciones o dominios marcados por Compras como de una agencia
       (GET/POST/DELETE api/Compras/AgenciasEntrada/Remitentes). La baja es lógica (Activo = 0).
    3. dbo.ComprasCorreosSeguimientoEstado: por buzón, el deltaLink (o nextLink) de Microsoft Graph por donde sigue la lectura.

    Lanzar como sa en NV. Puede ir ANTES o DESPUÉS de publicar la API (sin las tablas, el job no hace nada y lo apunta una vez en
    ELMAH; los GET devuelven la lista vacía). Idempotente: se puede relanzar.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComprasCorreosSeguimiento (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ComprasCorreosSeguimiento PRIMARY KEY CLUSTERED,
        MensajeId nvarchar(450) NOT NULL,          -- Message-ID (o su SHA-256 si es muy largo)
        Recibido datetime NOT NULL,
        RemitenteDireccion nvarchar(255) NULL,
        RemitenteDominio nvarchar(255) NULL,
        Asunto nvarchar(255) NULL,
        Agencia nvarchar(50) NULL,
        Codigo nvarchar(50) NULL,
        Url nvarchar(1000) NULL,
        Seguimientos nvarchar(4000) NULL,          -- JSON [{Codigo, Url}] si hay más de uno
        Extracto nvarchar(600) NULL,
        ProveedorPropuesto nvarchar(10) NULL,      -- solo si todos los candidatos son de un proveedor
        PedidosCandidatos nvarchar(2000) NULL,     -- JSON [{Empresa, Pedido, Proveedor, Motivo}]
        FechaCaptura datetime NOT NULL CONSTRAINT DF_ComprasCorreosSeguimiento_FechaCaptura DEFAULT (GETDATE()),
        EmpresaReal char(3) NULL,
        PedidoReal int NULL,
        FechaAlbaranReal datetime NULL
    );
    CREATE UNIQUE INDEX UX_ComprasCorreosSeguimiento_MensajeId ON dbo.ComprasCorreosSeguimiento (MensajeId);
    CREATE INDEX IX_ComprasCorreosSeguimiento_Recibido ON dbo.ComprasCorreosSeguimiento (Recibido) INCLUDE (PedidoReal);
END
GO
GRANT SELECT, INSERT, UPDATE ON dbo.ComprasCorreosSeguimiento TO [NUEVAVISION\RDS2016$];
GO
IF OBJECT_ID(N'dbo.AgenciasEntradaRemitentes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AgenciasEntradaRemitentes (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AgenciasEntradaRemitentes PRIMARY KEY CLUSTERED,
        Agencia nvarchar(50) NOT NULL,
        Remitente nvarchar(255) NOT NULL,          -- dirección (con @) o dominio, en minúsculas
        Activo bit NOT NULL CONSTRAINT DF_AgenciasEntradaRemitentes_Activo DEFAULT (1),
        Usuario nvarchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_AgenciasEntradaRemitentes_FechaModificacion DEFAULT (GETDATE())
    );
    CREATE UNIQUE INDEX UX_AgenciasEntradaRemitentes_Remitente ON dbo.AgenciasEntradaRemitentes (Remitente);
END
GO
GRANT SELECT, INSERT, UPDATE ON dbo.AgenciasEntradaRemitentes TO [NUEVAVISION\RDS2016$];
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimientoEstado', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComprasCorreosSeguimientoEstado (
        Buzon nvarchar(100) NOT NULL CONSTRAINT PK_ComprasCorreosSeguimientoEstado PRIMARY KEY CLUSTERED,
        EnlaceDelta nvarchar(max) NULL,            -- NULL = la próxima pasada empieza por los últimos 7 días
        UltimoError nvarchar(500) NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ComprasCorreosSeguimientoEstado_FechaModificacion DEFAULT (GETDATE())
    );
END
GO
GRANT SELECT, INSERT, UPDATE ON dbo.ComprasCorreosSeguimientoEstado TO [NUEVAVISION\RDS2016$];
GO
