/*
    Sugerencia 581 (Paloma; decisión de Carlos 13/10/26): «El repartidor ya se la ha llevado».

    El repartidor no pasa todos los días a la misma hora por la tienda, así que no hay hora fija: la tienda marca en Nesto
    (Productos › Reposición › Enviar, botón «El repartidor ya se la ha llevado») cuándo se ha llevado la reposición de hoy.
    Desde ese momento ya no se puede hacer una reposición puntual hoy (409 «El repartidor ya se ha llevado la reposición de
    hoy (a las hh:mm): lo que quede va en la siguiente.»). Más adelante podrá marcarlo el repartidor desde Ariadna (el
    endpoint POST api/Reposiciones/Recogida ya lo admite con un usuario de Almacén).

    Columnas nuevas en ReposicionesTraspasos (la cabecera de cada viaje):
      - FechaRecogida   datetime    NULL: cuándo se marcó que el repartidor se la ha llevado.
      - UsuarioRecogida varchar(30) NULL: quién lo marcó.
    Se rellenan en la cabecera del job del viaje de hoy (Herramienta 'Automatico' con FechaCorte de hoy, aunque sea una
    marca sin reposición: vacía, ya cubierta…) y en las puntuales de esa ruta creadas hoy.

    Sustituye a Scripts/Issue581_ReposicionesCalendario_HoraRecogida.sql (la hora fija por ruta), que NO se llegó a lanzar
    y se ha borrado. Si por lo que sea se hubiera lanzado, el bloque 2 quita esa columna.

    ORDEN: da igual antes o después del deploy de la API. Las columnas NO van en el EDMX (la API las lee y las escribe con
    SQL tras COL_LENGTH): sin este script la puntual funciona como hasta ahora (sin información de recogida) y el botón
    «El repartidor ya se la ha llevado» sale desactivado («falta lanzar el script»).

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no tiene permiso). Idempotente.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NULL
BEGIN
    RAISERROR('Falta la tabla ReposicionesTraspasos: lanzar antes Scripts/Issue577_ReposicionesTraspasos.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

------------------------------------------------------------------------------------------------
-- 1. Columnas (las filas que ya hay quedan en NULL: sin recoger)
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.ReposicionesTraspasos', 'FechaRecogida') IS NULL
BEGIN
    ALTER TABLE dbo.ReposicionesTraspasos ADD FechaRecogida datetime NULL;
    PRINT 'Columna FechaRecogida añadida.';
END
ELSE
    PRINT 'La columna FechaRecogida ya existía.';
GO

IF COL_LENGTH('dbo.ReposicionesTraspasos', 'UsuarioRecogida') IS NULL
BEGIN
    ALTER TABLE dbo.ReposicionesTraspasos ADD UsuarioRecogida varchar(30) NULL;
    PRINT 'Columna UsuarioRecogida añadida.';
END
ELSE
    PRINT 'La columna UsuarioRecogida ya existía.';
GO

------------------------------------------------------------------------------------------------
-- 2. La hora fija de recogida del calendario (versión anterior, descartada): fuera si alguien la hubiera creado
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.ReposicionesCalendario', 'HoraRecogida') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ReposicionesCalendario DROP COLUMN HoraRecogida;
    PRINT 'Columna ReposicionesCalendario.HoraRecogida quitada (ya no se usa).';
END
GO

------------------------------------------------------------------------------------------------
-- 2b. La reposición puntual se graba con Herramienta 'Puntual' (revisión 13/10/26): el CHECK de Issue577 solo admitía
--     'Nesto', 'Ariadna' y 'Automatico', y POST api/Reposiciones/Puntual fallaría siempre. Se rehace con 'Puntual'.
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ReposicionesTraspasos_Herramienta'
           AND parent_object_id = OBJECT_ID('dbo.ReposicionesTraspasos') AND definition NOT LIKE '%Puntual%')
BEGIN
    ALTER TABLE dbo.ReposicionesTraspasos DROP CONSTRAINT CK_ReposicionesTraspasos_Herramienta;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ReposicionesTraspasos_Herramienta'
               AND parent_object_id = OBJECT_ID('dbo.ReposicionesTraspasos'))
BEGIN
    ALTER TABLE dbo.ReposicionesTraspasos ADD CONSTRAINT CK_ReposicionesTraspasos_Herramienta
        CHECK (Herramienta IN ('Nesto', 'Ariadna', 'Automatico', 'Puntual'));
END
GO

SELECT name, definition FROM sys.check_constraints WHERE name = 'CK_ReposicionesTraspasos_Herramienta';
GO

------------------------------------------------------------------------------------------------
-- 3. Comprobación: las cabeceras de hoy de las tiendas a Algete
------------------------------------------------------------------------------------------------
EXEC sp_executesql N'
SELECT Id, NumTraspaso, Origen, Destino, Herramienta, FechaCorte, FechaPreparada, FechaRecogida, UsuarioRecogida, Omitida
FROM dbo.ReposicionesTraspasos
WHERE Destino = ''ALG'' AND FechaCreacion >= CAST(GETDATE() AS date)
ORDER BY Id';
GO

SET NOEXEC OFF;
GO
