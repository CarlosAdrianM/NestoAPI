/*
    NestoAPI#577 (corte 3d): cierre de la reposición N laborables ANTES de su llegada (decisión de Carlos, 08/10/26).

      - Tienda → Algete: se cierran a las 10:00 y llegan a Algete a las 13:30 el MISMO día → antelación 0 (como hasta ahora).
      - Algete → Reina (llega L/X/V a las 11:00) y Algete → Alcobendas (llega L/M/J a las 10:00): se cierran el LABORABLE
        ANTERIOR a las 13:00 (la del lunes, el viernes; con festivos en Algete, el laborable anterior) → antelación 1.

    Modelo: DiaSemana sigue siendo el día de LLEGADA (el de la ruta). La columna nueva LaborablesAntelacionCierre dice cuántos
    laborables del origen antes se cierra, a HoraCierre (0 = el mismo día). El CHECK «llegada no anterior al cierre» pasa a
    valer solo con antelación 0 (con antelación 1, la llegada de las 11:00 es anterior a las 13:00 del cierre: es otro día).

    ORDEN: lanzar ANTES del deploy de la API (la columna está en el EDMX: sin ella, todo lo que lee ReposicionesCalendario
    falla) y ANTES de Scripts/Issue577_CalendarioRutasAlgeteTiendas.sql (que la rellena).

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no tiene permiso). Idempotente.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ReposicionesCalendario') IS NULL
BEGIN
    RAISERROR('Falta la tabla ReposicionesCalendario: lanzar antes Scripts/Issue577_ReposicionesCalendario.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

------------------------------------------------------------------------------------------------
-- 1. Columna (las filas que ya hay, tienda → Algete, quedan con 0: se cierran el mismo día, como hasta ahora)
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.ReposicionesCalendario', 'LaborablesAntelacionCierre') IS NULL
BEGIN
    ALTER TABLE dbo.ReposicionesCalendario
        ADD LaborablesAntelacionCierre tinyint NOT NULL
            CONSTRAINT DF_ReposicionesCalendario_Antelacion DEFAULT (0);
    PRINT 'Columna LaborablesAntelacionCierre añadida.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID('dbo.ReposicionesCalendario') AND name = 'CK_ReposicionesCalendario_Antelacion')
BEGIN
    ALTER TABLE dbo.ReposicionesCalendario
        ADD CONSTRAINT CK_ReposicionesCalendario_Antelacion CHECK (LaborablesAntelacionCierre BETWEEN 0 AND 5);
END
GO

------------------------------------------------------------------------------------------------
-- 2. «La llegada no puede ser anterior al cierre» solo con antelación 0
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID('dbo.ReposicionesCalendario') AND name = 'CK_ReposicionesCalendario_Horas'
             AND definition NOT LIKE '%LaborablesAntelacionCierre%')
BEGIN
    ALTER TABLE dbo.ReposicionesCalendario DROP CONSTRAINT CK_ReposicionesCalendario_Horas;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID('dbo.ReposicionesCalendario') AND name = 'CK_ReposicionesCalendario_Horas')
BEGIN
    ALTER TABLE dbo.ReposicionesCalendario
        ADD CONSTRAINT CK_ReposicionesCalendario_Horas CHECK (LaborablesAntelacionCierre > 0 OR HoraLlegadaHabitual >= HoraCierre);
END
GO

SET NOEXEC OFF;
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: la columna (tinyint, default 0) y los dos CHECK
------------------------------------------------------------------------------------------------
SELECT c.name, t.name AS tipo, c.is_nullable, dc.definition AS defecto
FROM sys.columns c
     JOIN sys.types t ON t.user_type_id = c.user_type_id
     LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
WHERE c.object_id = OBJECT_ID('dbo.ReposicionesCalendario') AND c.name = 'LaborablesAntelacionCierre';

SELECT name, definition FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID('dbo.ReposicionesCalendario')
  AND name IN ('CK_ReposicionesCalendario_Antelacion', 'CK_ReposicionesCalendario_Horas');
GO
