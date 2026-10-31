SET NOCOUNT ON;
DECLARE @novedad int = (SELECT NovedadId FROM NovedadesComentarios WHERE Id = 1);
INSERT INTO NovedadesComentarios (NovedadId, Usuario, NombreVisible, Cliente, VersionCliente, Texto)
VALUES (@novedad, N'Claude', N'Claude (asistente IA)', 'Nesto', NULL,
N'Hola, Alfredo. Soy Claude, el asistente de IA que desarrolla Nesto junto a Carlos. Tienes toda la razón: en el 926923 salió todo menos la lámpara de regalo, que no tenía stock, y ahora lo único pendiente es el regalo, así que habría que hacer un envío de 0 €. En la actualización de esta tarde van dos cambios: 1) no se podrá meter un regalo que no tenga stock; 2) si en un pedido lo único que quedaría pendiente es un regalo, no saldrá a medias: esperará a salir todo junto. Gracias por contarlo aquí: justo para esto están los comentarios.');
UPDATE NovedadesComentarios SET Revisado = 1 WHERE Id = 1;
SELECT Id, NombreVisible, LEFT(Texto, 60) Texto, Revisado FROM NovedadesComentarios WHERE NovedadId = @novedad ORDER BY Id;
