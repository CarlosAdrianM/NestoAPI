-- 25/09/26: dos vídeos duplicados (mismo VideoId de YouTube insertado dos veces). Lo avisó Laura Villacieros.
--   · hl4g0UBQ-k8 «El error de usar PDRN, NAD y exosomas por separado»: se queda el 1980 (con sus 2 productos);
--     se borra el 1981 (sin productos).
--   · VLnM8mWuP4M «Gel Activador al 15 %…»: se queda el 1982; se borra el 1983 (su único producto, Gel Activador
--     24357, es idéntico al del 1982 y se va con él por el ON DELETE CASCADE de VideosProductos).
-- Ejecutar como sa. Si el recuento no es exactamente 2 vídeos, no borra nada.
SET NOCOUNT ON;
BEGIN TRAN;

-- Solo si siguen siendo duplicados de otro vídeo con el mismo VideoId que se queda
DELETE v
FROM dbo.Videos v
WHERE v.Id IN (1981, 1983)
  AND EXISTS (SELECT 1 FROM dbo.Videos q WHERE q.VideoId = v.VideoId AND q.Id IN (1980, 1982));

IF @@ROWCOUNT <> 2
BEGIN
    ROLLBACK;
    RAISERROR('No son exactamente 2 vídeos: no se ha borrado nada', 16, 1);
END
ELSE
    COMMIT;

-- Comprobación: ya no debe haber ningún VideoId repetido
SELECT VideoId, COUNT(*) AS Veces FROM dbo.Videos GROUP BY VideoId HAVING COUNT(*) > 1;
SELECT v.Id, v.VideoId, LEFT(v.Titulo, 60) AS Titulo, (SELECT COUNT(*) FROM dbo.VideosProductos vp WHERE vp.VideoId = v.Id) AS Productos
FROM dbo.Videos v WHERE v.Id IN (1980, 1981, 1982, 1983);
