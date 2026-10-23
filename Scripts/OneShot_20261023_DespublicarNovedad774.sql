/*
    23/10/26: la novedad 774 de Ariadna 2.17 («Cámara: lee la etiqueta siguiente…») repite la sugerencia 758, que ya está
    marcada como Implementada 2.17. Se despublica la 774 (no se borra). Ejecutar en NV como sa. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
UPDATE Novedades SET Publicada = 0
WHERE Id = 774 AND Ambito = 'Ariadna' AND Version = '2.17' AND SugeridaNombre IS NULL;
SELECT @@ROWCOUNT AS Despublicadas;  -- debe ser 1
SELECT Id, Version, Publicada, SugeridaNombre, Titulo FROM Novedades WHERE Id IN (758, 774);
