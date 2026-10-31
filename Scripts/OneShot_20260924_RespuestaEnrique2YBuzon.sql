-- 24/09/26: respuesta del asistente al comentario 5 (Enrique, CTT 48h) y avisos en el buzón de Nesto
-- (Nesto#477) para Alfredo y Enrique, que recibieron respuesta antes de que existiera el aviso.
-- Ejecutar como sa. Idempotente para los avisos (no duplica si ya existen).
SET NOCOUNT ON;
DECLARE @respuestaEnrique int;

INSERT INTO NovedadesComentarios (NovedadId, Usuario, NombreVisible, Cliente, VersionCliente, Texto)
VALUES (328, N'Claude', N'Claude (asistente IA)', 'Asistente', NULL,
N'Gracias, Enrique, justo lo que hacía falta. En la actualización de esta tarde los envíos de CTT se confirmarán en Amazon con el servicio «CTT 48h», que es lo mismo que tecleáis en Seller Central en la opción «Otra». Además, Nesto esperará la respuesta de Amazon: si algún pedido no se marca, os dirá el motivo en lugar de decir «confirmado». Si ves que alguno sigue sin marcarse, escríbelo aquí mismo con el número de pedido y lo miramos.');
SET @respuestaEnrique = SCOPE_IDENTITY();
UPDATE NovedadesComentarios SET Revisado = 1 WHERE Id = 5;

INSERT INTO NotificacionesBuzon (Usuario, Aplicacion, Titulo, Cuerpo, Datos, FechaCreacion)
SELECT u.Usuario, 'Nesto', N'Te han contestado en Novedades', u.Cuerpo, u.Datos, GETDATE()
FROM (VALUES
    (N'NUEVAVISION\Alfredo', N'Claude (asistente IA): Hola, Alfredo. Tienes toda la razón: en el 926923 salió todo menos la lámpara de regalo…',
     N'{"tipo":"NovedadComentario","novedadId":"350","comentarioId":"2"}'),
    (N'NUEVAVISION\Enrique', N'Claude (asistente IA): Gracias, Enrique, justo lo que hacía falta. En la actualización de esta tarde los envíos de CTT…',
     N'{"tipo":"NovedadComentario","novedadId":"328","comentarioId":"' + CAST(@respuestaEnrique AS nvarchar(10)) + N'"}')
) u(Usuario, Cuerpo, Datos)
WHERE NOT EXISTS (SELECT 1 FROM NotificacionesBuzon b WHERE b.Usuario = u.Usuario AND b.Aplicacion = 'Nesto' AND b.Datos = u.Datos);

SELECT Id, Usuario, Aplicacion, Titulo, Datos FROM NotificacionesBuzon WHERE Aplicacion = 'Nesto' ORDER BY Id;
