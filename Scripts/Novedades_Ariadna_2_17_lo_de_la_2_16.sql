/*
    Novedades de Ariadna 2.16 que llegan a la prueba cerrada con la 2.17 (23/10/26). La 2.16 solo estuvo en la pista
    interna y su script nunca se lanzó, así que lo que trajo se publica ahora con la versión 2.17.
    Fuera a propósito respecto a Novedades_Ariadna_2_16.sql:
      - «Recibir: descartar lo leído en este equipo (F7)»: lo supera «Recibir: todos ven lo mismo, también Nesto» (2.17).
      - «Enviar reposición: "No está" deja el stock a 0»: va por la sugerencia 753 de Paloma (PUT, Implementada 2.17).
    NO lanzar Novedades_Ariadna_2_16.sql. Ejecutar en SSMS contra NV como sa. Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Corregido', N'La cámara vuelve a leer todas las etiquetas',
 N'Con la cámara del móvil, las etiquetas antiguas de Nesto no se leían y las nuevas costaban mucho, sobre todo con luz. Ahora se leen las dos, y a la primera. El lector de la PDA no cambia.',
 NULL),
(2, N'Mejorado', N'Recibir: lo ya recibido sigue en la lista',
 N'En una reposición recibida en parte, lo que ya se dio de alta ya no desaparece: sale al final de la lista completa (F5) como «Recibido el 22/10 (Alfredo)». Si se vuelve a leer, Ariadna avisa de que ya se dio de alta y no lo suma.',
 N'Almacén'),
(3, N'Mejorado', N'Recibir: terminar cuando lo que queda no ha llegado',
 N'Si una reposición ya se recibió en parte y lo que queda no ha llegado, ya se puede terminar sin leer nada: lo que falta queda como diferencia y se avisa a quien la preparó.',
 N'Almacén'),
(5, N'Nuevo', N'«No está»: cogerlo de otro hueco',
 N'Al dar «No está» en una parada del recorrido, si el producto está en otro hueco Ariadna lo dice y ofrece «Cogerlo de…» (Intro): la parada pasa a ese hueco y, al acabar, se vuelve a la que tocaba. Ahora también en las reposiciones.',
 N'Almacén');
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.17', '2026-10-23', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
