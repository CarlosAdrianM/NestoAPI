/*
    Novedades de Ariadna 2.38 (18/11/26): cantidades dudosas con confirmación (Ariadna#103, incidencia 1335) y foto del producto
    obligatoria al avisar de la foto en «Dato mal en la ficha» (NestoAPI#868). No necesita nada nuevo de la API.
    Ejecutar en SSMS contra NV como sa cuando la 2.38 llegue a las PDA. Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Mejorado', N'Cantidades raras: Ariadna pregunta antes de apuntarlas',
 N'Si en el inventario o al recibir teclea una cantidad que parece la referencia o un código, o que es demasiado grande para lo que hay, Ariadna no la apunta: le pregunta «¿Seguro que hay…?». Intro o Esc dicen que no y no se apunta nada. Si de verdad es así, pulse F1 otra vez.',
 NULL),
(2, N'Mejorado', N'Dato mal en la ficha: si avisa de la foto, haga una foto del producto',
 N'Al marcar «Foto» se abre la cámara: haga una foto del producto que tiene en la mano, porque sin ella no se puede enviar el aviso. Si avisa de otra cosa (precio, nombre…), al enviar le preguntará si quiere hacer la foto: Intro o F6 la hacen, y F1 envía sin foto.',
 NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.38', '20261118', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Ambito = 'Ariadna' AND Version = '2.38' ORDER BY Id;
