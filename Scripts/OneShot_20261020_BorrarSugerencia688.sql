-- 20/10/26 (lanzar como sa): borrar la sugerencia 688 de Lidia («pedidos en Global»), a petición de Carlos.
-- La aclara él por teléfono. Ya estaba Descartada (oculta). Se borra con sus comentarios, votos y adjuntos.
USE NV;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.Novedades WHERE Id = 688 AND Version IS NULL AND SugeridaNombre = 'Lidia')
BEGIN
    RAISERROR('La 688 no es la sugerencia de Lidia que se esperaba: no se borra nada.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END

DELETE FROM dbo.NovedadesComentarios WHERE NovedadId = 688;
SELECT @@ROWCOUNT AS ComentariosBorrados;   -- 1 (el del asistente)
DELETE FROM dbo.NovedadesVotos WHERE NovedadId = 688;
SELECT @@ROWCOUNT AS VotosBorrados;
DELETE FROM dbo.NovedadesAdjuntos WHERE NovedadId = 688;
SELECT @@ROWCOUNT AS AdjuntosBorrados;
DELETE FROM dbo.Novedades WHERE Id = 688;
SELECT @@ROWCOUNT AS SugerenciasBorradas;   -- 1

COMMIT TRANSACTION;
SELECT COUNT(*) AS Quedan FROM dbo.Novedades WHERE Id = 688;   -- 0
