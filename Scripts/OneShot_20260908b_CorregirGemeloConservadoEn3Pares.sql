/*
    OneShot 08/09/26 (b) - Corregir CUÁL de los dos gemelos se conservó en 3 de los 7 pares.

    QUÉ PASÓ
    --------
    El script de deduplicación de esta misma mañana (OneShot_20260908_DeduplicarVideosPorVideoId)
    conservó de cada par la fila CON CONTENIDO (protocolo y transcripción), que era el único criterio
    visible desde Nesto. En 4 de los 7 pares esa fila resultó ser la del Id bajo, y bien.

    Pero en 3 pares el contenido estaba en el Id ALTO, así que se conservó ese y se borró el bajo. Y
    eso, desde la tienda, es justo lo contrario de lo que hace falta (equipo de SEO, 08/09/26):

        cuando llegó el segundo gemelo, la URL limpia ya estaba ocupada, así que la tienda le puso
        el Id detrás. En TODOS los pares:
            Id bajo  -> /video/tutoria-diciembre-2025          <- la indexada en Google
            Id alto  -> /video/tutoria-diciembre-2025-1838     <- la fea

    Conservar el Id alto deja viva la URL fea y manda la buena a 410. Hay que conservar el bajo.

    LA SOLUCIÓN NO ES BORRAR EL OTRO
    --------------------------------
    Si nos limitáramos a borrar ahora el Id alto, perderíamos el protocolo y la transcripción, que
    es exactamente lo que llevamos todo el día evitando. Así que se hacen las dos cosas: se recrea
    la fila con el Id BAJO (el de la URL buena) llevándose el CONTENIDO del alto, y después se borra
    el alto.

        Id que se recupera <- contenido que se lleva     (vídeo de YouTube)
              1825         <-        1826                 H2qPRrWYn34
              1833         <-        1834                 E0lyUhj-Z9M
              1837         <-        1838                 04-wYIZ-Hig

    Los otros 4 pares (1746, 1827, 1835, 1852 vivos) YA están bien: no se tocan.

    POR QUÉ SE PUEDE RECREAR EL ID
    ------------------------------
    Los dos gemelos tenían el mismo VideoId, la misma FechaPublicacion, el mismo Titulo y la misma
    Descripcion: solo se diferenciaban en el contenido. Así que la fila nueva con el Id bajo es,
    para cualquiera que la mire, la que había, y además con el protocolo que a aquella le faltaba.
    Se usa IDENTITY_INSERT; no toca la semilla de la identidad.

    URGENCIA
    --------
    El sync de la tienda corre a las 04:00. Si esto se ejecuta antes, la tienda no llega a ver
    nunca el estado incorrecto: de un tirón dará de baja los 3 Id altos y dejará vivas las 3 URL
    limpias. Si se ejecuta después, la tienda habrá dado de baja las 3 buenas y habrá que esperar
    al siguiente sync para que las reponga.

    RED DE SEGURIDAD
    ----------------
    Las filas originales de los 7 borrados siguen en el backup bcp de esta mañana, en
    repos/videos_dedup_backup_20260908/videos_backup_20260908.dat.
*/

USE NV;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Pares a corregir: Bajo = el que hay que dejar vivo (URL limpia), Alto = del que sale el contenido.
DECLARE @Pares TABLE (Bajo int PRIMARY KEY, Alto int NOT NULL);
INSERT INTO @Pares (Bajo, Alto) VALUES (1825, 1826), (1833, 1834), (1837, 1838);

-- ---------------------------------------------------------------------------
-- Comprobaciones previas: el estado tiene que ser EXACTAMENTE el esperado.
-- ---------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM @Pares p JOIN Videos v ON v.Id = p.Bajo)
BEGIN
    RAISERROR('Algún Id bajo YA existe: el script ya se ejecutó o el estado no es el previsto. Se aborta.', 16, 1);
    RETURN;
END

IF (SELECT COUNT(*) FROM @Pares p JOIN Videos v ON v.Id = p.Alto) <> 3
BEGIN
    RAISERROR('No están vivos los 3 Id altos de los que hay que copiar el contenido. Se aborta.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- 1. Recrear la fila del Id BAJO con el contenido del ALTO.
-- ---------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.Videos ON;

INSERT INTO dbo.Videos (Id, VideoId, Transcripcion, Descripcion, Titulo, FechaPublicacion, Protocolo, EsUnProtocolo, FechaBaja)
SELECT p.Bajo, v.VideoId, v.Transcripcion, v.Descripcion, v.Titulo, v.FechaPublicacion, v.Protocolo, v.EsUnProtocolo, v.FechaBaja
FROM @Pares p
JOIN dbo.Videos v ON v.Id = p.Alto;

-- Hay que guardarlo AQUÍ: el SET de la línea siguiente pone @@ROWCOUNT a cero, y leerlo después
-- hace que el script diga "0 filas recreadas" habiéndolas insertado. Pasó en la ejecución del
-- 08/09/26 y asustó sin motivo.
DECLARE @Recreadas int = @@ROWCOUNT;

SET IDENTITY_INSERT dbo.Videos OFF;

SELECT 'Filas recreadas con el Id bajo = ' + CAST(@Recreadas AS varchar);

-- ---------------------------------------------------------------------------
-- 2. Llevarse los productos asociados, si los hubiera (en estos 3 pares no hay
--    ninguno, comprobado el 08/09/26, pero así el script no depende de eso).
-- ---------------------------------------------------------------------------
UPDATE vp
SET vp.VideoId = p.Bajo
FROM VideosProductos vp
JOIN @Pares p ON p.Alto = vp.VideoId;

SELECT 'Productos reasignados al Id bajo = ' + CAST(@@ROWCOUNT AS varchar);

-- ---------------------------------------------------------------------------
-- 3. Borrar el Id alto, que es el que tiene la URL con el número detrás.
-- ---------------------------------------------------------------------------
DELETE FROM dbo.Videos WHERE Id IN (SELECT Alto FROM @Pares);

SELECT 'Filas borradas (Id alto) = ' + CAST(@@ROWCOUNT AS varchar);

-- ---------------------------------------------------------------------------
-- 4. Verificación antes del commit.
-- ---------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM Videos GROUP BY VideoId HAVING COUNT(*) > 1)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Han vuelto a aparecer VideoId repetidos: ROLLBACK.', 16, 1);
    RETURN;
END

IF (SELECT COUNT(*) FROM Videos WHERE Id IN (1825, 1833, 1837)) <> 3
    OR EXISTS (SELECT 1 FROM Videos WHERE Id IN (1826, 1834, 1838))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('El resultado no es el esperado: ROLLBACK.', 16, 1);
    RETURN;
END

-- El contenido tiene que haber viajado: estas 3 filas deben tener protocolo o transcripción.
IF EXISTS (SELECT 1 FROM Videos
           WHERE Id IN (1833, 1837)
             AND LEN(ISNULL(Protocolo, '')) = 0)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Alguna fila recreada se ha quedado sin protocolo: ROLLBACK.', 16, 1);
    RETURN;
END

COMMIT TRANSACTION;

-- ---------------------------------------------------------------------------
-- 5. Resultado. Los 7 supervivientes deben ser los 7 Id BAJOS.
-- ---------------------------------------------------------------------------
SELECT 'Total de vídeos = ' + CAST(COUNT(*) AS varchar) FROM Videos;

SELECT 'Superviviente Id=' + CAST(v.Id AS varchar)
     + ' | protocolo=' + CAST(LEN(ISNULL(v.Protocolo, '')) AS varchar)
     + ' | transcripcion=' + CAST(LEN(ISNULL(v.Transcripcion, '')) AS varchar)
     + ' | ' + LEFT(ISNULL(v.Titulo, ''), 45)
FROM Videos v
WHERE v.Id IN (1746, 1825, 1827, 1833, 1835, 1837, 1852)
ORDER BY v.Id;

SELECT 'Id altos que deben haber desaparecido (esperado 0) = ' + CAST(COUNT(*) AS varchar)
FROM Videos WHERE Id IN (1747, 1826, 1828, 1834, 1836, 1838, 1853);
GO
