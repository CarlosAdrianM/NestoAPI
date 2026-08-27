-- NestoAPI#387: buzón de notificaciones persistente (reutilizable por Nesto, NestoApp y NestoTiendas).
-- Hoy las push son fire-and-forget: si el usuario descarta la notificación del sistema, se pierde.
-- Esta tabla guarda cada notificación enviada para que la app pueda mostrar un buzón.
-- El esquema de destinatario es el MISMO que DispositivosNotificaciones, a propósito: así la
-- resolución de destinatarios es común (issue #387).
-- Ejecutar en SSMS (sa) contra NV.

CREATE TABLE dbo.NotificacionesBuzon (
    Id int IDENTITY(1,1) NOT NULL,
    Usuario nvarchar(50) NULL,              -- destinatario resuelto (User.Identity.Name)
    Empresa char(3) NULL,
    Vendedor char(3) NULL,
    Cliente char(10) NULL,
    Contacto char(3) NULL,
    Aplicacion nvarchar(50) NOT NULL,       -- Constantes.Aplicaciones (NestoApp / NestoTiendas / Nesto)
    Titulo nvarchar(200) NOT NULL,
    Cuerpo nvarchar(max) NULL,
    Datos nvarchar(max) NULL,               -- JSON con los datos de la push (tipo, videoId, imagenUrl...)
    FechaCreacion datetime NOT NULL CONSTRAINT DF_NotificacionesBuzon_FechaCreacion DEFAULT (GETDATE()),
    FechaLeida datetime NULL,               -- NULL = no leída
    FechaEliminada datetime NULL,           -- borrado lógico
    CONSTRAINT PK_NotificacionesBuzon PRIMARY KEY (Id)
);
GO

-- El acceso siempre es (destinatario + aplicación), descartando las eliminadas y ordenando por
-- fecha descendente: es exactamente lo que pide la lista del buzón y el contador de no leídas.
CREATE INDEX IX_NotificacionesBuzon_Usuario_Aplicacion
    ON dbo.NotificacionesBuzon (Usuario, Aplicacion, FechaEliminada, FechaCreacion DESC)
    INCLUDE (FechaLeida);
GO

-- GRANTs (BD NV / NestoConnection: el API entra por integrated security con la cuenta de máquina)
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.NotificacionesBuzon TO [NUEVAVISION\RDS2016$];
GO
