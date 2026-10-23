/*
    Novedades de Ariadna 2.16 (23/10/26): Ariadna#52 (la cámara vuelve a leer las etiquetas de Nesto viejo y lee a la
    primera), Ariadna#51 (recibir en parte: lo ya recibido sigue en la lista, borradores viejos fuera, F7 descartar y
    terminar sin nada leído), Ariadna#12 (al dar «No está», otro hueco con stock y «Cogerlo de…» también en reposiciones)
    y sugerencia 753 de Paloma (lo dado por «No está» al enviar desde una tienda queda a 0 de stock).
    Lo de #51, #12 y 753 necesita la API publicada con 0b4ab902, 75ca0b38 y 22799494.
    Va primero al canal INTERNO (Carlos). Ejecutar en SSMS contra NV como sa cuando la 2.16 llegue a la prueba cerrada.
    Idempotente (compara por Ámbito + Título).
    Al publicar: comentario con @Andre @Pedro @Santiago @Alfredo (cámara, recibir en parte, otro hueco) y en la sugerencia
    753 a @Paloma (stock a 0), y marcar la 753 Implementada 2.16.
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
(4, N'Nuevo', N'Recibir: descartar lo leído en este equipo (F7)',
 N'Lo leído se guarda en cada equipo hasta que se termina. Si en un equipo queda algo sin enviar, Ariadna lo dice («los demás no lo ven»), y con F7 «Descartar lo leído» se empieza de cero. Las lecturas guardadas de lo que ya se dio de alta se quitan solas al abrir.',
 N'Almacén'),
(5, N'Nuevo', N'«No está»: cogerlo de otro hueco',
 N'Al dar «No está» en una parada del recorrido, si el producto está en otro hueco Ariadna lo dice y ofrece «Cogerlo de…» (Intro): la parada pasa a ese hueco y, al acabar, se vuelve a la que tocaba. Ahora también en las reposiciones.',
 N'Almacén'),
(6, N'Mejorado', N'Enviar reposición: «No está» deja el stock a 0',
 N'Al enviar una reposición desde la tienda, lo que haya marcado como «No está» se pone a 0 de stock en la tienda, para que la reposición no lo vuelva a pedir. Al terminar le dice qué productos se han puesto a 0. Lo pidió Paloma.',
 N'Tiendas');
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.16', '2026-10-23', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
