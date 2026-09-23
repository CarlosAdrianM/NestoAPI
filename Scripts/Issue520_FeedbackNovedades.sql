/*
    NestoAPI#520: feedback de los usuarios en las Novedades (votos y comentarios con captura).

    Ejecutar en SSMS contra NV (NestoConnection), como sa, ANTES de publicar la API que lo usa.
    Idempotente: se puede volver a lanzar sin romper nada.

    Mientras las tablas no existan, la API sigue sirviendo las Novedades igual que siempre (sin los
    contadores de votos/comentarios) y los endpoints de voto/comentario devuelven error.

    Clave del usuario (columna Usuario): la identidad estable del token, nunca el token:
      - Nesto:    el usuario de Windows (NUEVAVISION\usuario).
      - NestoApp: el Id de usuario de Identity (claim nameidentifier), no el UserName.
*/

USE NV;
GO

IF OBJECT_ID('dbo.NovedadesVotos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NovedadesVotos (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NovedadesVotos PRIMARY KEY,
        NovedadId int NOT NULL CONSTRAINT FK_NovedadesVotos_Novedades REFERENCES dbo.Novedades (Id),
        Usuario nvarchar(128) NOT NULL,
        Cliente varchar(20) NOT NULL,
        Voto smallint NOT NULL CONSTRAINT CK_NovedadesVotos_Voto CHECK (Voto IN (-1, 1)),
        Fecha datetime2 NOT NULL CONSTRAINT DF_NovedadesVotos_Fecha DEFAULT SYSDATETIME(),
        CONSTRAINT UQ_NovedadesVotos_NovedadUsuario UNIQUE (NovedadId, Usuario)
    );
END
GO

IF OBJECT_ID('dbo.NovedadesComentarios', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NovedadesComentarios (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NovedadesComentarios PRIMARY KEY,
        NovedadId int NOT NULL CONSTRAINT FK_NovedadesComentarios_Novedades REFERENCES dbo.Novedades (Id),
        Usuario nvarchar(128) NOT NULL,
        NombreVisible nvarchar(100) NOT NULL,
        Cliente varchar(20) NOT NULL,
        VersionCliente varchar(30) NULL,
        Texto nvarchar(2000) NOT NULL,
        Imagen varbinary(max) NULL,
        ImagenTipo varchar(30) NULL,
        Fecha datetime2 NOT NULL CONSTRAINT DF_NovedadesComentarios_Fecha DEFAULT SYSDATETIME(),
        Revisado bit NOT NULL CONSTRAINT DF_NovedadesComentarios_Revisado DEFAULT 0,
        Borrado bit NOT NULL CONSTRAINT DF_NovedadesComentarios_Borrado DEFAULT 0,
        IssueGitHub int NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NovedadesComentarios_Novedad' AND object_id = OBJECT_ID('dbo.NovedadesComentarios'))
    CREATE INDEX IX_NovedadesComentarios_Novedad ON dbo.NovedadesComentarios (NovedadId) INCLUDE (Borrado);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NovedadesComentarios_Revisado' AND object_id = OBJECT_ID('dbo.NovedadesComentarios'))
    CREATE INDEX IX_NovedadesComentarios_Revisado ON dbo.NovedadesComentarios (Revisado, Fecha);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NovedadesVotos_Fecha' AND object_id = OBJECT_ID('dbo.NovedadesVotos'))
    CREATE INDEX IX_NovedadesVotos_Fecha ON dbo.NovedadesVotos (Fecha) INCLUDE (NovedadId, Voto);
GO

-- La API corre con la cuenta de máquina del servidor.
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.NovedadesVotos TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.NovedadesComentarios TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT name, create_date FROM sys.tables WHERE name IN ('NovedadesVotos', 'NovedadesComentarios');
