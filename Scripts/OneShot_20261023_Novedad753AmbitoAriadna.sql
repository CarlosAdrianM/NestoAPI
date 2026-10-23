/*
    URGENTE 23/10/26: la sugerencia 753 (Paloma) se creó desde Nesto (Ambito='Nesto') y se marcó Implementada en
    Ariadna 2.17. Con Version='2.17' y ámbito Nesto, el popup de Nesto la pone como la versión más nueva (2.17 > 1.10.48)
    y abre esa página en vez de la de la 1.10.48.0. Se pasa a Ambito='Ariadna'. Ejecutar en NV como sa. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
UPDATE Novedades SET Ambito = 'Ariadna' WHERE Id = 753 AND Version = '2.17' AND Ambito = 'Nesto';
SELECT @@ROWCOUNT AS Corregidas;  -- debe ser 1
SELECT Id, Version, Ambito, Publicada, Titulo FROM Novedades WHERE Id = 753;
-- Ninguna otra novedad de Nesto con versión que no sea 1.x (debe salir vacío):
SELECT Id, Version, Ambito, Titulo FROM Novedades
WHERE Version IS NOT NULL AND Version NOT LIKE '1.%' AND ISNULL(Ambito, '') NOT IN ('Ariadna', 'NestoApp');
