/*
    Sugerencia 1332 (Reina; Carlos 18/11/26): el dosier (PDF) del evento, que sale solo por correo a todas las inscritas vivas
    unas 2 horas antes de empezar (y al momento a quien se inscriba después).

    1. dbo.EventosDosier: un PDF por evento (hasta 10 MB), fuera del EDMX, como EventosImagenes (#690). Se sube, se sustituye,
       se abre y se quita desde la ficha del evento en Nesto (POST/GET/DELETE api/Eventos/{id}/Dosier).
    2. EventosInscripciones.DosierEnviado datetime NULL: cuándo le salió el dosier a esa inscripción (una sola vez). La API la
       aparta con un UPDATE ... WHERE DosierEnviado IS NULL antes de mandar el correo y la suelta si el envío falla.

    Sin el script (falta la tabla o la columna): los endpoints del dosier dan 503, el job «eventos-dosier» no hace nada y los
    eventos salen sin dosier.

    Ejecutar en SSMS contra NV como sa (CREATE/ALTER). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. EventosDosier (si ya existe, no se toca)
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.EventosDosier', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventosDosier (
        EventoId int NOT NULL CONSTRAINT PK_EventosDosier PRIMARY KEY
            CONSTRAINT FK_EventosDosier_Eventos REFERENCES dbo.Eventos (Id),
        Nombre nvarchar(200) NOT NULL,
        Tamano int NOT NULL,
        Contenido varbinary(max) NOT NULL,
        Usuario nvarchar(50) NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_EventosDosier_Fecha DEFAULT GETDATE()
    );
END
GO

------------------------------------------------------------------------------------------------
-- 2. La marca por inscripción, en su propio lote (si ya existe, no se toca)
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.EventosInscripciones', 'DosierEnviado') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD DosierEnviado datetime NULL;
GO

-- EventosInscripciones ya tenía SELECT/INSERT/UPDATE/DELETE (#611): la columna nueva no necesita GRANT. Se repite por si acaso.
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosDosier TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosInscripciones TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
------------------------------------------------------------------------------------------------
SELECT CASE WHEN OBJECT_ID('dbo.EventosDosier', 'U') IS NULL THEN 'NO' ELSE 'SÍ' END AS TablaEventosDosier,
       COL_LENGTH('dbo.EventosInscripciones', 'DosierEnviado') AS ColumnaDosierEnviado;
SELECT 'EventosDosier' AS Tabla, COUNT(*) AS Filas FROM dbo.EventosDosier;
GO
