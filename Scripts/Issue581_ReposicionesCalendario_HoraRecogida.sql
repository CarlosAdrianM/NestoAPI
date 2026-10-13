/*
    Sugerencia 581 (Paloma; decisión de Carlos 13/10/26, «hasta la recogida»): la reposición puntual de una tienda a Algete
    solo se puede hacer hasta la hora a la que pasa el repartidor a recogerla ese día. Después, la API la rechaza con 409
    («Ya ha pasado el repartidor de hoy (hh:mm): la reposición puntual ya no sale hoy.») y Nesto lo dice antes de empezar.

    Columna nueva ReposicionesCalendario.HoraRecogida time(0) NULL: a qué hora pasa el repartidor a recoger la reposición
    en el ORIGEN el día de esa fila (DiaSemana). NULL = sin límite (como hasta ahora). Se mantiene desde Nesto
    (Productos › Reposición › Calendario, columna «Recogida»).

    ORDEN: da igual antes o después del deploy de la API. La columna NO está en el EDMX (la API la lee y la escribe con SQL
    y, si no existe, trata todo como «sin límite»); sin este script, guardar una hora de recogida desde Nesto avisa de que
    falta el script.

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
-- 1. Columna (las filas que ya hay quedan en NULL: sin límite)
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.ReposicionesCalendario', 'HoraRecogida') IS NULL
BEGIN
    ALTER TABLE dbo.ReposicionesCalendario ADD HoraRecogida time(0) NULL;
    PRINT 'Columna HoraRecogida añadida.';
END
ELSE
    PRINT 'La columna HoraRecogida ya existía.';
GO

------------------------------------------------------------------------------------------------
-- 2. (OPCIONAL, a mano) Poner la hora del repartidor de las tiendas. Carlos: descomentar con la hora real de cada ruta.
--    Hoy tienda → Algete se cierra a las 9:00 y llega a Algete a las 13:30 (ALC lunes a viernes; REI lunes, miércoles y viernes).
------------------------------------------------------------------------------------------------
/*
UPDATE dbo.ReposicionesCalendario SET HoraRecogida = '11:30', Usuario = 'Issue581', FechaModificacion = GETDATE()
WHERE Empresa = '1' AND AlmacenOrigen = 'ALC' AND AlmacenDestino = 'ALG' AND Activo = 1;

UPDATE dbo.ReposicionesCalendario SET HoraRecogida = '11:00', Usuario = 'Issue581', FechaModificacion = GETDATE()
WHERE Empresa = '1' AND AlmacenOrigen = 'REI' AND AlmacenDestino = 'ALG' AND Activo = 1;
*/
GO

------------------------------------------------------------------------------------------------
-- 3. Comprobación
------------------------------------------------------------------------------------------------
SELECT Id, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, HoraRecogida, Activo
FROM dbo.ReposicionesCalendario
ORDER BY AlmacenOrigen, AlmacenDestino, DiaSemana;
GO

SET NOEXEC OFF;
GO
