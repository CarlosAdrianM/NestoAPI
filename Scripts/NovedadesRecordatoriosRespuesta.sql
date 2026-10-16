/*
    Recordatorios de respuesta en Novedades (Carlos, 16/10/26).

    Se acumulaban sugerencias e incidencias porque el asistente le pregunta algo al usuario y el usuario no contesta.
    Ahora:
      - POST api/Novedades/{id}/Comentarios/Asistente acepta "EsperaRespuesta": true → la sugerencia queda esperando
        la respuesta de su autor (EsperandoRespuestaDesde = ahora, RecordatoriosRespuesta = 0).
      - Si comenta el AUTOR (SugeridaPor), se borra la espera. Los demás comentarios no la tocan.
      - Job de Hangfire «novedades-recordatorios-respuesta» (L-V 9:00, interruptor Novedades:RecordatoriosRespuesta):
        cada 2 días laborables sin respuesta, recordatorio del asistente con @mención (dos veces); tras el 2.º,
        comentario de cierre y Estado = 'Cerrada'. El autor la sigue viendo y, si comenta, vuelve a 'Pendiente'.

    Ejecutar en SSMS contra NV (NestoConnection), como sa, ANTES de publicar la API. Idempotente: se puede volver a lanzar.
    (Si se publicara antes, las Novedades salen igual; solo fallan, sin romper nada y con aviso en ELMAH, marcar la
    espera y el job.)

    QUÉ HACE
      1) Columnas nuevas en dbo.Novedades:
           EsperandoRespuestaDesde      datetime NULL      desde cuándo se espera al autor (NULL = no se espera)
           RecordatoriosRespuesta       tinyint NOT NULL 0 recordatorios mandados desde la pregunta (0, 1, 2)
           UltimoRecordatorioRespuesta  datetime NULL      cuándo se mandó el último (los 2 días cuentan desde aquí)
      2) El CHECK de Estado admite 'Cerrada'.
      3) GRANT a la cuenta de la API (ya los tenía sobre la tabla; se repite por si acaso).

    Después de este, el backlog inicial: Scripts/OneShot_20261016_NovedadesEsperandoRespuesta.sql.
*/

USE NV;
GO

IF COL_LENGTH('dbo.Novedades', 'EsperandoRespuestaDesde') IS NULL
    ALTER TABLE dbo.Novedades ADD EsperandoRespuestaDesde datetime NULL;
GO

IF COL_LENGTH('dbo.Novedades', 'RecordatoriosRespuesta') IS NULL
    ALTER TABLE dbo.Novedades ADD RecordatoriosRespuesta tinyint NOT NULL
        CONSTRAINT DF_Novedades_RecordatoriosRespuesta DEFAULT (0);
GO

IF COL_LENGTH('dbo.Novedades', 'UltimoRecordatorioRespuesta') IS NULL
    ALTER TABLE dbo.Novedades ADD UltimoRecordatorioRespuesta datetime NULL;
GO

-- Estado 'Cerrada' (sin respuesta del autor). Se rehace el CHECK solo si aún no lo admite.
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_Novedades_Estado' AND parent_object_id = OBJECT_ID('dbo.Novedades')
             AND definition NOT LIKE '%Cerrada%')
    ALTER TABLE dbo.Novedades DROP CONSTRAINT CK_Novedades_Estado;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_Novedades_Estado' AND parent_object_id = OBJECT_ID('dbo.Novedades'))
    ALTER TABLE dbo.Novedades ADD CONSTRAINT CK_Novedades_Estado
        CHECK ([Estado] IS NULL OR [Estado] IN ('Pendiente', 'Aceptada', 'Implementada', 'Descartada', 'Cerrada'));
GO

-- Sin índice: la tabla es pequeña (unos cientos de filas) y el job corre una vez al día.

-- La API corre con la cuenta de máquina del servidor.
GRANT SELECT, UPDATE ON dbo.Novedades TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT c.name, t.name AS tipo, c.is_nullable
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.Novedades')
  AND c.name IN ('EsperandoRespuestaDesde', 'RecordatoriosRespuesta', 'UltimoRecordatorioRespuesta');
SELECT definition FROM sys.check_constraints WHERE name = 'CK_Novedades_Estado';
