/*
    NestoAPI#622: reposiciones automáticas con una preparación sin enviar a la hora de corte.

    Hasta ahora, si a la hora de corte la tienda aún tenía sin enviar la reposición de un viaje anterior (líneas en su
    diario de salida sin número de traspaso), el job omitía la de hoy («ya en preparación») y solo avisaba a ELMAH. Ahora
    la de hoy se SUMA a la que hay (productos nuevos y más cantidad en los que ya estaban; nunca se baja nada) y se apunta
    una cabecera nueva del job con el corte de hoy, enlazada a la de la preparación que completa.

    Columna nueva en ReposicionesTraspasos:
      - IdCompletada int NULL: en la cabecera de un corte que se ha sumado a una preparación sin enviar, el Id de la
        PRIMERA cabecera de esa preparación (la del viaje que no se envió). NULL en todas las demás.
    Al terminar la reposición (POST api/Reposiciones/EnPreparacion/Terminar) el número de traspaso va a la cabecera
    abierta y a todas las de su misma preparación (Id = IdCompletada de la abierta, o IdCompletada = ese Id).

    ORDEN: da igual antes o después del deploy de la API. La columna NO va en el EDMX (la API la lee y la escribe con SQL
    tras COL_LENGTH): sin este script la de hoy se suma igual, pero su cabecera nace sin enlace y al terminar solo se numera
    la más reciente (la del viaje anterior se queda abierta, sin número, como las que termina Nesto viejo).

    Sin GRANT: es una columna nueva en una tabla que la API ya lee y escribe.

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
-- 1. Columna (las filas que ya hay quedan en NULL: no completan ninguna)
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.ReposicionesTraspasos', 'IdCompletada') IS NULL
BEGIN
    ALTER TABLE dbo.ReposicionesTraspasos ADD IdCompletada int NULL;
    PRINT 'Columna IdCompletada añadida.';
END
ELSE
    PRINT 'La columna IdCompletada ya existía.';
GO

------------------------------------------------------------------------------------------------
-- 2. Índice para numerar al terminar (las que completan una preparación)
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReposicionesTraspasos_IdCompletada'
               AND object_id = OBJECT_ID('dbo.ReposicionesTraspasos'))
BEGIN
    EXEC sp_executesql N'CREATE INDEX IX_ReposicionesTraspasos_IdCompletada ON dbo.ReposicionesTraspasos (IdCompletada)
        WHERE IdCompletada IS NOT NULL';
    PRINT 'Índice IX_ReposicionesTraspasos_IdCompletada creado.';
END
GO

------------------------------------------------------------------------------------------------
-- 3. Comprobación: las cabeceras abiertas (sin número) de los últimos días
------------------------------------------------------------------------------------------------
EXEC sp_executesql N'
SELECT Id, NumTraspaso, Origen, Destino, Herramienta, FechaCreacion, FechaCorte, IdCompletada, Omitida
FROM dbo.ReposicionesTraspasos
WHERE NumTraspaso IS NULL AND FechaCreacion >= DATEADD(day, -7, CAST(GETDATE() AS date))
ORDER BY Id';
GO

SET NOEXEC OFF;
GO
