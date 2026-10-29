-- 29/10/26: el script Novedades_1_10_52_0.sql metió como filas propias seis novedades que son sugerencias implementadas, y
-- la sugerencia ES la novedad (conserva quién la pidió y el hilo). OneShot_20261029_CerrarSugerencias_1_10_52_0.ps1 convierte
-- las sugerencias en novedades de la 1.10.52.0; esto retira las filas duplicadas (nuevavision no tiene UPDATE en Novedades).
--   877 «Eventos: la persona que asiste»                 → va por la sugerencia 862 (Reina)
--   878 «Cada comercial inscribe a sus clientes…»        → 863 (Paloma)
--   881 «Ventana de Eventos en pantallas pequeñas»       → 871 (Pilar)
--   883 «Datos de la transferencia… al contado»          → 864 (Paloma)
--   885 «Comentarios del rapport más cómodos»            → 865 (Iñaki)
--   887 «Reembolsos de CTT: el número de cliente…»       → 868 (Aida)
-- Lanzar como sa en NV DESPUÉS del .ps1 (si no, durante un rato no se ven ni unas ni otras). Idempotente.
USE NV;
SET NOCOUNT ON;

UPDATE Novedades SET Publicada = 0
WHERE Version = '1.10.52.0' AND Publicada = 1 AND Id IN (877, 878, 881, 883, 885, 887);
SELECT @@ROWCOUNT AS Despublicadas;

SELECT Id, Version, Publicada, Estado, LEFT(Titulo, 80) AS Titulo
FROM Novedades
WHERE Id IN (861, 862, 863, 864, 865, 868, 871, 877, 878, 881, 883, 885, 887)
ORDER BY Id;
