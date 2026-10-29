/*
    NestoAPI#739 / Ariadna#63 (sugerencia 873, Andre, 29/10/26): la carga de la furgoneta del reparto propio.

    Al cargar, cada repartidor (Andre, Miguel…) escanea en Ariadna los bultos que se lleva (RP{envío}-{N}). Cada lectura es una
    fila de EnviosAgenciaCargaBultos: trazabilidad completa («este bulto no me lo disteis» queda descartado) y se sabe qué
    bultos no ha cargado nadie (api/RepartoPropio/SinCargar).

    - El primer bulto que carga un usuario le asigna el envío: el repartidor es el usuario de la ÚLTIMA carga del envío (no hay
      columna aparte). En EnviosHistoria queda «En reparto (Nombre)» (Campo = 'Repartidor').
    - Si el envío ya lo lleva otro, la API da 409 («Este envío lo lleva Miguel»); con Reasignar pasa al nuevo y queda la traza.
    - Idempotente por ClaveIdempotencia (la cola de Ariadna reintenta sin cobertura); el mismo bulto otra vez no se apunta.
    - Solo inserciones: es evidencia. Las filas de un repartidor anterior se quedan (traza).

    Fuera del EDMX: la API la lee y escribe por SQL y comprueba que existe (OBJECT_ID). Sin el script, POST Cargar y
    GET SinCargar dan 400 «falta el script» y GET Envios sale como antes (sin repartidor; soloMios no filtra).

    Requiere Scripts/Issue739_RepartoPropio.sql (agencia 14). Ejecutar en SSMS contra NV como sa (CREATE). Idempotente.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EnviosAgenciaCargaBultos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EnviosAgenciaCargaBultos (
        Id int IDENTITY(1,1) NOT NULL,
        EnvioId int NOT NULL,                               -- EnviosAgencia.Numero
        Bulto smallint NOT NULL,                            -- el N de RP{envío}-{N}
        Usuario varchar(50) NOT NULL,                       -- quien lo carga (el repartidor)
        FechaHora datetime NOT NULL,                        -- la de la PDA (hora local)
        ClaveIdempotencia uniqueidentifier NOT NULL,        -- la pone Ariadna; misma clave = misma respuesta
        FechaRegistro datetime NOT NULL CONSTRAINT DF_EnviosAgenciaCargaBultos_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_EnviosAgenciaCargaBultos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_EnviosAgenciaCargaBultos_Clave UNIQUE (ClaveIdempotencia),
        CONSTRAINT FK_EnviosAgenciaCargaBultos_EnviosAgencia FOREIGN KEY (EnvioId) REFERENCES dbo.EnviosAgencia (Numero),
        CONSTRAINT CK_EnviosAgenciaCargaBultos_Bulto CHECK (Bulto > 0)
    );
    CREATE INDEX IX_EnviosAgenciaCargaBultos_EnvioId ON dbo.EnviosAgenciaCargaBultos (EnvioId, Id) INCLUDE (Bulto, Usuario);
    PRINT 'EnviosAgenciaCargaBultos: creada.';
END
ELSE
    PRINT 'EnviosAgenciaCargaBultos: ya existía.';
GO

-- BD de negocio (NestoConnection): el API accede con la cuenta de máquina. Solo se lee y se inserta (evidencia).
GRANT SELECT, INSERT ON dbo.EnviosAgenciaCargaBultos TO [NUEVAVISION\RDS2016$];
GO

SELECT name FROM sys.tables WHERE name = 'EnviosAgenciaCargaBultos';
