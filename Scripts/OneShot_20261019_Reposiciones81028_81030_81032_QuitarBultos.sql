-- 19/10/26: Andre recibe en Algete la 81028 y la 81030 (desde Reina) y la 81032 (desde Alcobendas), y ningún bulto trae la
-- etiqueta pegada. Sin bultos, la recepción no pide etiqueta y se recibe como antes (igual que la 81029 y la 81026).
-- Solo borra los bultos que no se han leído. Lanzar como sa.
USE NV;
DELETE FROM ReposicionesBultos
 WHERE NumTraspaso IN (81028, 81030, 81032) AND Codigo IN ('R81028-1', 'R81030-1', 'R81032-1') AND FechaLectura IS NULL;
SELECT @@ROWCOUNT AS Borradas;   -- debe dar 3
SELECT COUNT(*) AS Quedan FROM ReposicionesBultos WHERE NumTraspaso IN (81028, 81030, 81032);   -- debe dar 0
