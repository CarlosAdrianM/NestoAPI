-- 16/10/26: el bulto R81029-1 lo creó Claude a mano (la 81029 se terminó con una Ariadna vieja, sin bultos). Desde
-- entonces la recepción en Alcobendas exige leer la etiqueta y a Paloma no se la reconoce. Sin bultos, la recepción no
-- pide etiqueta y se recibe como antes. Solo borra el bulto si no se ha leído. Lanzar como sa.
USE NV;
DELETE FROM ReposicionesBultos
 WHERE NumTraspaso = 81029 AND Codigo = 'R81029-1' AND FechaLectura IS NULL;
SELECT @@ROWCOUNT AS Borradas;   -- debe dar 1
SELECT COUNT(*) AS Quedan FROM ReposicionesBultos WHERE NumTraspaso = 81029;   -- debe dar 0
