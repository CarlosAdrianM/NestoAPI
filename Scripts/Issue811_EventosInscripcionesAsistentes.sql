/*
    NestoAPI#811 (Reina, sugerencia 1006; Carlos 04/11/26): número de asistentes por inscripción.

    Una inscripción es de un centro (cliente/contacto) y puede traer a varias personas (la clienta 41286/0 compró dos kits del
    Lifting Coreano y vienen dos empleadas). Cada inscripción ocupa «Asistentes» plazas y, en un evento «gratuito por compra»,
    hace falta una unidad comprada del producto requerido por asistente.

    - EventosInscripciones.Asistentes int NOT NULL DEFAULT 1 (CHECK >= 1). Las de antes quedan con 1.

    La columna está FUERA del EDMX: la API la lee y la escribe por SQL y comprueba que existe (COL_LENGTH). Sin el script, la
    API funciona como antes (todas las inscripciones son de 1 asistente); inscribir a más de 1 y el PUT de asistentes contestan
    que falta el script.

    Ejecutar en SSMS contra NV como sa (ALTER). Idempotente: se puede lanzar dos veces. Se puede lanzar antes o después del deploy.
    EventosInscripciones ya tenía SELECT/INSERT/UPDATE/DELETE (#611): la columna nueva no necesita GRANT nuevo.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.EventosInscripciones', 'Asistentes') IS NULL
    ALTER TABLE dbo.EventosInscripciones
        ADD Asistentes int NOT NULL CONSTRAINT DF_EventosInscripciones_Asistentes DEFAULT (1);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EventosInscripciones_Asistentes')
    ALTER TABLE dbo.EventosInscripciones
        ADD CONSTRAINT CK_EventosInscripciones_Asistentes CHECK (Asistentes >= 1);
GO

-- Por si se lanza en una BD sin el GRANT de #611 (no hace daño repetirlo)
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosInscripciones TO [NUEVAVISION\RDS2016$];
GO

-- VERIFICACIÓN: la columna existe y todas las de antes valen 1
SELECT COL_LENGTH('dbo.EventosInscripciones', 'Asistentes') AS LongitudAsistentes;
SELECT Asistentes, COUNT(*) AS Inscripciones FROM dbo.EventosInscripciones GROUP BY Asistentes;
GO
