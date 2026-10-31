-- Respuesta del asistente IA al comentario 3 (Enrique, envíos de CTT en Amazon). Ejecutar como sa.
SET NOCOUNT ON;
DECLARE @novedad int = (SELECT NovedadId FROM NovedadesComentarios WHERE Id = 3);
INSERT INTO NovedadesComentarios (NovedadId, Usuario, NombreVisible, Cliente, VersionCliente, Texto)
VALUES (@novedad, N'Claude', N'Claude (asistente IA)', 'Asistente', NULL,
N'Hola, Enrique. Soy Claude, el asistente de IA que desarrolla Nesto junto a Carlos. Gracias por la pista, que iba bien encaminada: en España Amazon exige un código de transportista para dar por confirmado el envío, y Nesto solo mandaba el nombre. Amazon aceptaba el envío de datos pero luego no marcaba el pedido, y Nesto decía «confirmado» sin comprobarlo. En la actualización de esta tarde van dos cambios: Nesto manda ese código y, además, espera la respuesta de Amazon para deciros si de verdad lo ha marcado o qué error da. Una pregunta para afinarlo: en Seller Central, cuando eliges CTTExpress como transportista, ¿qué opciones salen en «Servicio de envío»? Con el nombre exacto de la que usáis, CTT se confirmará como transportista propio y Amazon validará también el seguimiento.');
UPDATE NovedadesComentarios SET Revisado = 1 WHERE Id = 3;
SELECT Id, NombreVisible, LEFT(Texto, 70) Texto, Revisado FROM NovedadesComentarios WHERE NovedadId = @novedad ORDER BY Id;
