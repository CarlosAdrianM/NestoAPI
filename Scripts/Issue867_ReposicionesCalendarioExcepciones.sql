/*
    NestoAPI#867: excepciones por fecha en el calendario de reposiciones (sugerencia 1275, Reina 16/11/26).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. No está en el EDMX (la API la lee y escribe con SQL a mano), así que se puede lanzar antes o después del deploy:
    sin ella, el calendario funciona como hasta ahora (semanal, sin excepciones) y GET/POST/DELETE
    api/Reposiciones/Calendario/Excepciones contestan 503.

    Una fila = una ruta (Origen → Destino) en un día de LLEGADA concreto:
      Tipo = 'SinReposicion'   ese día no hay la reposición que dice el calendario semanal (p. ej. el viernes 20/11/26 no
                               reciben las tiendas por el inventario del sábado 21). Las tres horas van a NULL.
      Tipo = 'ReposicionExtra' ese día hay una reposición que el calendario no tiene, con su HoraCierre,
                               LaborablesAntelacionCierre y HoraLlegadaHabitual (las tres obligatorias).
    Una sola fila por (Empresa, Origen, Destino, FechaLlegada). Las de días pasados no estorban (se quedan como constancia).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT, UPDATE y DELETE (desde Nesto se borra una excepción; el rastro queda en Modificaciones).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID(N'dbo.ReposicionesCalendarioExcepciones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReposicionesCalendarioExcepciones (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Origen char(3) NOT NULL,
        Destino char(3) NOT NULL,
        FechaLlegada date NOT NULL,                     -- el día de LLEGADA (como DiaSemana en ReposicionesCalendario)
        Tipo varchar(20) NOT NULL,                      -- SinReposicion | ReposicionExtra
        HoraCierre time(0) NULL,                        -- solo ReposicionExtra
        LaborablesAntelacionCierre tinyint NULL,        -- solo ReposicionExtra (la misma que el resto de la ruta)
        HoraLlegadaHabitual time(0) NULL,               -- solo ReposicionExtra
        Motivo nvarchar(200) NOT NULL,
        Usuario nvarchar(50) NOT NULL CONSTRAINT DF_ReposicionesCalendarioExcepciones_Usuario DEFAULT (SUSER_SNAME()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ReposicionesCalendarioExcepciones_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_ReposicionesCalendarioExcepciones PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_ReposicionesCalendarioExcepciones_RutaDia UNIQUE (Empresa, Origen, Destino, FechaLlegada),
        CONSTRAINT CK_ReposicionesCalendarioExcepciones_Almacenes CHECK (Origen <> Destino)
    );
END
GO

-- En su propio lote (la tabla puede venir de una ejecución anterior a medias).
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ReposicionesCalendarioExcepciones_Tipo')
    ALTER TABLE dbo.ReposicionesCalendarioExcepciones ADD CONSTRAINT CK_ReposicionesCalendarioExcepciones_Tipo
        CHECK (Tipo IN ('SinReposicion', 'ReposicionExtra'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ReposicionesCalendarioExcepciones_Horas')
    ALTER TABLE dbo.ReposicionesCalendarioExcepciones ADD CONSTRAINT CK_ReposicionesCalendarioExcepciones_Horas
        CHECK ((Tipo = 'SinReposicion' AND HoraCierre IS NULL AND LaborablesAntelacionCierre IS NULL AND HoraLlegadaHabitual IS NULL)
            OR (Tipo = 'ReposicionExtra' AND HoraCierre IS NOT NULL AND LaborablesAntelacionCierre IS NOT NULL AND HoraLlegadaHabitual IS NOT NULL
                AND LaborablesAntelacionCierre <= 5));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReposicionesCalendarioExcepciones_Fecha'
               AND object_id = OBJECT_ID(N'dbo.ReposicionesCalendarioExcepciones'))
    CREATE INDEX IX_ReposicionesCalendarioExcepciones_Fecha ON dbo.ReposicionesCalendarioExcepciones (FechaLlegada);
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ReposicionesCalendarioExcepciones TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ReposicionesCalendarioExcepciones');
SELECT COUNT(*) AS Excepciones FROM dbo.ReposicionesCalendarioExcepciones;
GO
