/*
    Novedades de Ariadna 2.37 (16/11/26): «Ha vuelto un paquete» (NestoAPI#870, reenvíos) y foto del mozo en «Dato mal en la ficha»
    (NestoAPI#868). Necesita la API de hoy publicada y sus scripts (Issue870_EnviosAgenciaHaVuelto, Issue868_AvisosFichaFotoMozo).
    Ejecutar en SSMS contra NV como sa cuando la 2.37 llegue a las PDA. Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Ha vuelto un paquete: avisar de que la caja de un envío devuelto está aquí',
 N'En Entradas (F6 o en el menú «⋯») hay una opción nueva, «Ha vuelto un paquete». Cuando llega al almacén un paquete que la agencia nos devuelve, lea su etiqueta (la de la agencia o la nuestra), o teclee el número de envío. Ariadna dice de qué envío y de qué cliente es, y si tiene un reenvío esperando: desde ese momento se puede imprimir su etiqueta y volver a enviarlo. Hasta que no se marca que ha vuelto, el reenvío no puede salir.',
 NULL),
(2, N'Mejorado', N'Dato mal en la ficha: con una foto del producto',
 N'Al avisar de que algo está mal en la ficha de un producto, puede hacer una foto del producto que tiene en la mano con «📷 Foto del producto» (o F6; otra vez para repetirla, F7 o «Quitar» para quitarla). La foto llega en el correo junto a la foto de la tienda, para que se vea qué está mal. Es opcional.',
 NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.37', '20261116', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Ambito = 'Ariadna' AND Version = '2.37' ORDER BY Id;
