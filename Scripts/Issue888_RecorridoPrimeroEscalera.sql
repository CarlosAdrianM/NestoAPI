/*
    NestoAPI#888 (sugerencia 1418, Santiago; Carlos 23/11/26: sí, configurable por almacén): orden «primero la escalera».

    Añade Almacenes.PrimeroEscalera (bit NULL), junto a Almacenes.FilaAMano (Scripts/Issue641_RecorridoCalles.sql). Con 1, dentro de
    cada columna de una calle se recorre primero la escalera (filas 1 hasta FilaAMano - 1, de arriba abajo) y después lo que se coge a
    mano (de la más alta que se alcanza hacia el suelo, como siempre). NULL o 0: el orden de siempre (a mano y después la escalera,
    subiendo 3, 2, 1). Vale para picking, reposiciones, pendiente de ubicar y huecos alternativos (la regla única OrdenRecorrido).

    No está en el EDMX: la API lo lee y lo escribe con SQL a mano. SIN ESTE SCRIPT LA API VA COMO HOY (y un PUT api/Almacen/Recorrido
    con PrimeroEscalera = true da 503). Se puede lanzar antes o después del deploy. Idempotente.

    Ejecutar en SSMS contra NV como sa (ALTER TABLE). Sin GRANT nuevo: la API ya escribe en dbo.Almacenes (FilaAMano).

    Activarlo en Algete DESPUÉS de publicar la API: desde el mantenimiento de calles de Nesto (casilla, Nesto#528) o con
    PUT api/Almacen/Recorrido { ..., "PrimeroEscalera": true }, o aquí (descomentando el UPDATE de abajo).
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.Almacenes', 'PrimeroEscalera') IS NULL
BEGIN
    ALTER TABLE dbo.Almacenes ADD PrimeroEscalera bit NULL;
    PRINT 'Añadida Almacenes.PrimeroEscalera';
END
ELSE
    PRINT 'Ya estaba Almacenes.PrimeroEscalera';
GO

-- Activarlo en Algete (empresas 1 y 3: es el mismo almacén físico), cuando esté publicada la API:
-- UPDATE dbo.Almacenes SET PrimeroEscalera = 1 WHERE [Número] = 'ALG' AND Empresa IN ('1', '3');

SELECT Empresa, [Número], FilaAMano, PrimeroEscalera FROM dbo.Almacenes WHERE [Número] IN ('ALG', 'REI', 'ALC');
GO
