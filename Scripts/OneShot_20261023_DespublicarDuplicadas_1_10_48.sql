/*
    23/10/26: Novedades_1_10_48_0.sql metió tres entradas que ya son sugerencias implementadas en la 1.10.48.0
    (717 Manuel, 761 Paloma, 756 Sancho). La novedad es la propia sugerencia (conserva quién la pidió y su hilo), así que
    estas tres filas del script se despublican. No se borra nada. Ejecutar en NV como sa. Idempotente.
*/
SET NOCOUNT ON;
USE NV;

SELECT Id, Version, Publicada, Titulo FROM Novedades
WHERE Version = '1.10.48.0' AND Ambito = 'Nesto' ORDER BY Id;

UPDATE Novedades SET Publicada = 0
WHERE Version = '1.10.48.0' AND Ambito = 'Nesto' AND SugeridaNombre IS NULL
  AND Id NOT IN (717, 761, 756)
  AND Titulo IN (N'Pendientes de entregar: todas las líneas pendientes de todos los almacenes',
                 N'Rapport: el cuadro de comentarios ya se ve bien',
                 N'Ganavisiones al ampliar un pedido desde la plantilla');
SELECT @@ROWCOUNT AS Despublicadas;  -- deben ser 3

SELECT Id, Version, Publicada, SugeridaNombre, Titulo FROM Novedades
WHERE Version = '1.10.48.0' AND Ambito = 'Nesto' ORDER BY Id;
