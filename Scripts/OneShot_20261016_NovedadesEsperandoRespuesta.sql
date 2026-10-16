/*
    Recordatorios de respuesta en Novedades (16/10/26): backlog inicial.

    Marca como «esperando respuesta del autor» las sugerencias e incidencias abiertas cuyo ÚLTIMO comentario es del
    asistente y le pregunta algo al autor, sin respuesta posterior. La fecha de espera es la de esa pregunta, así que
    el primer job (L-V 9:00) mandará ya el recordatorio a las que lleven 2 días laborables o más.

    Comprobado el 16/10/26 con SELECT en producción: son 4, más la 648 (Paloma, pregunta de las 11:18 sobre el informe de cifras).
        579  Marta      13/10 10:12  «¿nos dices aquí si te ha ido bien?»
        584  MariaJose  14/10 08:31  «¿es el Enlace de pago…, el Cobro NestoPago…?»
        605  Marta      14/10 18:14  «¿Puedes volver a intentar borrarla ahora?»
        621  Manuel     15/10 13:33  «¿se refiere a los pedidos de compra al proveedor?…»
    (583, 598, 616-620 también acaban en un comentario del asistente, pero no le preguntan nada al autor.)

    Ejecutar como sa DESPUÉS de Scripts/NovedadesRecordatoriosRespuesta.sql. Idempotente: solo toca las que
    siguen igual (último comentario del asistente, abiertas y sin espera marcada).
*/

USE NV;
GO

-- 1) Vista previa: las que se marcarían (genérico: último comentario del asistente con una pregunta).
WITH ultimo AS (
    SELECT c.NovedadId, c.Usuario, c.Fecha, c.Texto,
           ROW_NUMBER() OVER (PARTITION BY c.NovedadId ORDER BY c.Fecha DESC, c.Id DESC) AS rn
    FROM dbo.NovedadesComentarios c
    WHERE c.Borrado = 0
)
SELECT n.Id, n.SugeridaNombre, n.Estado, u.Fecha AS FechaPregunta, n.EsperandoRespuestaDesde,
       LEFT(u.Texto, 200) AS Pregunta
FROM dbo.Novedades n
JOIN ultimo u ON u.NovedadId = n.Id AND u.rn = 1
WHERE n.Version IS NULL AND n.Publicada = 1 AND n.Estado IN ('Pendiente', 'Aceptada')
  AND u.Usuario = N'Claude' AND u.Texto LIKE N'%?%'
ORDER BY n.Id;
GO

-- 2) Marcar las 5 revisadas a mano, con la fecha real de la pregunta.
WITH ultimo AS (
    SELECT c.NovedadId, c.Usuario, c.Fecha,
           ROW_NUMBER() OVER (PARTITION BY c.NovedadId ORDER BY c.Fecha DESC, c.Id DESC) AS rn
    FROM dbo.NovedadesComentarios c
    WHERE c.Borrado = 0
)
UPDATE n SET
    EsperandoRespuestaDesde = u.Fecha,
    RecordatoriosRespuesta = 0,
    UltimoRecordatorioRespuesta = NULL
FROM dbo.Novedades n
JOIN ultimo u ON u.NovedadId = n.Id AND u.rn = 1
WHERE n.Id IN (579, 584, 605, 621, 648)
  AND n.Version IS NULL AND n.Estado IN ('Pendiente', 'Aceptada')
  AND u.Usuario = N'Claude'
  AND n.EsperandoRespuestaDesde IS NULL;

SELECT @@ROWCOUNT AS MarcadasEsperandoRespuesta;
GO

-- Comprobación
SELECT Id, SugeridaNombre, Estado, EsperandoRespuestaDesde, RecordatoriosRespuesta, UltimoRecordatorioRespuesta, Titulo
FROM dbo.Novedades
WHERE EsperandoRespuestaDesde IS NOT NULL
ORDER BY Id;
