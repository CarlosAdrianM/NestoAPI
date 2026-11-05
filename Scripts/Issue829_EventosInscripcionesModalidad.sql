/*
    NestoAPI#829 / Nesto#626 (Reina, sugerencia 1064; Carlos 05/11/26): eventos mixtos (presenciales y online a la vez) y, en
    cada inscripción, si la clienta viene presencial u online.

    1. Eventos.Formato admite «Mixto» (se rehace CK_Eventos_Formato de Issue690: antes solo Presencial u Online).
    2. EventosInscripciones.FormaAsistencia varchar(15) NULL: «Presencial» u «Online»; NULL = sin indicar (todas las de antes).

    La columna está FUERA del EDMX: la API la lee y la escribe por SQL y comprueba que existe (COL_LENGTH), como Asistentes
    (#811). Sin el script, la API funciona como antes: la lista sale «sin indicar», el PUT de la forma de asistencia y guardar un
    evento «Mixto» contestan que falta el script. Se puede lanzar antes o después del deploy (mejor ANTES, para que Reina pueda
    poner su evento como mixto nada más publicar).

    Ejecutar en SSMS contra NV como sa (ALTER: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    EventosInscripciones y Eventos ya tenían SELECT/INSERT/UPDATE/DELETE (#591, #611): la columna nueva no necesita GRANT nuevo
    (se repite por si acaso, no hace daño).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Eventos.Formato: Presencial, Online o Mixto
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.CK_Eventos_Formato') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name = 'CK_Eventos_Formato' AND definition LIKE '%Mixto%')
    ALTER TABLE dbo.Eventos DROP CONSTRAINT CK_Eventos_Formato;
GO
IF OBJECT_ID('dbo.CK_Eventos_Formato') IS NULL
    ALTER TABLE dbo.Eventos ADD CONSTRAINT CK_Eventos_Formato
        CHECK (Formato IS NULL OR Formato IN ('Presencial', 'Online', 'Mixto'));
GO

------------------------------------------------------------------------------------------------
-- 2. EventosInscripciones.FormaAsistencia
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.EventosInscripciones', 'FormaAsistencia') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD FormaAsistencia varchar(15) NULL;     -- NULL = sin indicar
GO
IF OBJECT_ID('dbo.CK_EventosInscripciones_FormaAsistencia') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD CONSTRAINT CK_EventosInscripciones_FormaAsistencia
        CHECK (FormaAsistencia IS NULL OR FormaAsistencia IN ('Presencial', 'Online'));
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Eventos TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosInscripciones TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: el CHECK con Mixto, la columna nueva y su CHECK
------------------------------------------------------------------------------------------------
SELECT name, definition FROM sys.check_constraints
WHERE name IN ('CK_Eventos_Formato', 'CK_EventosInscripciones_FormaAsistencia');
SELECT COL_LENGTH('dbo.EventosInscripciones', 'FormaAsistencia') AS LongitudFormaAsistencia;
SELECT ISNULL(Formato, '(null = Presencial)') AS Formato, COUNT(*) AS Eventos FROM dbo.Eventos GROUP BY Formato;
GO
