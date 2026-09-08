/*
    OneShot 08/09/26 - Quitar los vídeos duplicados de la tabla Videos.

    QUÉ PASA
    --------
    Hay 7 pares de filas en Videos que son el MISMO vídeo de YouTube: mismo VideoId, misma
    FechaPublicacion, mismo Titulo. La tienda online publica una ficha por fila, así que son 7
    URLs /video/{slug} distintas apuntando al mismo vídeo: contenido duplicado.

    POR QUÉ ESTÁN
    -------------
    NVIA (ConsolaNVIA, GestorTranscripciones.ProcessYouTubeData) inserta UNA FILA POR PISTA DE
    SUBTÍTULOS: el `foreach (string captionId in captionIds)` termina en InsertTranscripcion. Un
    vídeo con dos pistas (la manual y la automática, o español e inglés) entra dos veces. Por eso
    en casi todos los pares uno tiene protocolo y transcripción y el otro está vacío: la segunda
    pista se descargó vacía. También duplicaba la push notification de protocolo nuevo.

    El bucle ya está arreglado en ConsolaNVIA (se queda con la primera pista que traiga texto y
    inserta una sola vez). Este script limpia lo que quedó de antes.

    CRITERIO DE CUÁL SE QUEDA
    -------------------------
    De cada par se conserva la fila con contenido (protocolo, transcripción y/o productos) y se
    borra la vacía. En los pares en que las dos son iguales se conserva la de Id menor, salvo
    H2qPRrWYn34, donde la de Id mayor es la única con transcripción.

    Comprobado en producción el 08/09/26: ninguna de las filas que se borran tiene productos que
    no tenga su gemela, así que el ON DELETE CASCADE de VideosProductos no se lleva nada único.

    EFECTO EN LA TIENDA
    -------------------
    Las 7 URLs sobrantes dejan de venir en el listado de la API, la tienda las da de baja sola en
    el sync de las 04:00 y pasan a responder 410. Es lo que se busca.

    Los índices de Lucene se quedan con 7 entradas muertas hasta el reindexado de las 20:30; no
    molesta, porque los resultados se rellenan leyendo la base de datos y esos Id ya no existen.
*/

USE NV;
GO

SET NOCOUNT ON;

-- ---------------------------------------------------------------------------
-- 1. Copia de seguridad ANTES de tocar nada. Si algo sale mal, de aquí se
--    reponen las filas (con SET IDENTITY_INSERT Videos ON).
-- ---------------------------------------------------------------------------
IF OBJECT_ID('dbo.Videos_Backup_20260908', 'U') IS NOT NULL
BEGIN
    RAISERROR('La tabla Videos_Backup_20260908 ya existe: el script ya se ejecutó. Se aborta.', 16, 1);
    RETURN;
END

DECLARE @ABorrar TABLE (Id int PRIMARY KEY);

INSERT INTO @ABorrar (Id)
VALUES (1837),  -- 04-wYIZ-Hig  se queda 1838 (protocolo 2689 + transcripción)
       (1836),  -- 5-LStZhI918  se queda 1835 (protocolo 2878 + transcripción)
       (1828),  -- 93AnEsoMf5s  se queda 1827 (protocolo 2196 + 5 productos)
       (1833),  -- E0lyUhj-Z9M  se queda 1834 (protocolo 4113 + transcripción)
       (1747),  -- e-hCF5sNTRA  gemelas idénticas (11 productos cada una): se queda la 1746
       (1825),  -- H2qPRrWYn34  se queda 1826, que es la única con transcripción
       (1853);  -- wpmfMDqoTU0  gemelas idénticas: se queda la 1852

-- Red de seguridad: que no se borre nada cuyo VideoId se quede sin ninguna fila.
IF EXISTS (
    SELECT 1
    FROM Videos v
    JOIN @ABorrar b ON b.Id = v.Id
    WHERE NOT EXISTS (SELECT 1 FROM Videos g
                      WHERE g.VideoId = v.VideoId AND g.Id <> v.Id)
)
BEGIN
    RAISERROR('Alguna fila a borrar no tiene gemela: se dejaría el vídeo sin ficha. Se aborta.', 16, 1);
    RETURN;
END

SELECT *
INTO dbo.Videos_Backup_20260908
FROM Videos v
WHERE v.Id IN (SELECT Id FROM @ABorrar);

SELECT 'Filas guardadas en la copia' AS Paso, COUNT(*) AS Filas FROM dbo.Videos_Backup_20260908;

-- ---------------------------------------------------------------------------
-- 2. El borrado. VideosProductos cae por el ON DELETE CASCADE de la FK.
-- ---------------------------------------------------------------------------
BEGIN TRANSACTION;

DELETE FROM Videos WHERE Id IN (SELECT Id FROM @ABorrar);

SELECT 'Filas borradas' AS Paso, @@ROWCOUNT AS Filas;

-- ---------------------------------------------------------------------------
-- 3. Verificación: no debe quedar ningún VideoId repetido y el total baja a 771.
-- ---------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM Videos GROUP BY VideoId HAVING COUNT(*) > 1)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Siguen quedando VideoId repetidos: no se ha hecho el commit.', 16, 1);
    RETURN;
END

COMMIT TRANSACTION;

SELECT 'Vídeos que quedan' AS Paso, COUNT(*) AS Filas FROM Videos;
SELECT 'VideoId repetidos'  AS Paso, COUNT(*) AS Filas
FROM (SELECT VideoId FROM Videos GROUP BY VideoId HAVING COUNT(*) > 1) t;
GO
