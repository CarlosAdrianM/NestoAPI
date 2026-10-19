-- 19/10/26: Andre recibe en Algete la 81026 (desde Alcobendas) y el bulto no trae la etiqueta R81026-1 pegada. Sin bultos,
-- la recepción no pide etiqueta y se recibe como antes (igual que la 81029 el 16/10). Solo borra el bulto si no se ha leído.
-- Lanzar como sa.
USE NV;
DELETE FROM ReposicionesBultos
 WHERE NumTraspaso = 81026 AND Codigo = 'R81026-1' AND FechaLectura IS NULL;
SELECT @@ROWCOUNT AS Borradas;   -- debe dar 1
SELECT COUNT(*) AS Quedan FROM ReposicionesBultos WHERE NumTraspaso = 81026;   -- debe dar 0
