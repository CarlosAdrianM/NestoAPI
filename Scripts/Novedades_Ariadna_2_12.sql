/*
    Novedades de Ariadna 2.12 (16/10/26): Ariadna#35, #42, #30, #28 (sin cambio: ya estaba desde la 2.7), #29, #33 y #40.
    Va primero al canal INTERNO (Carlos). Ejecutar en SSMS contra NV como sa cuando la 2.12 llegue a la prueba cerrada.
    Idempotente (compara por Ámbito + Título). Necesita la API publicada el 16/10 (UrlFoto de reposiciones, NestoAPI#653).
    SE OMITE: #28 (ya estaba), el confirmar de F7 en Diferencias va dentro de la de usted.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Ariadna se actualiza sola',
 N'Al abrir Ariadna (y al volver a ella al cabo de un rato), si hay una versión nueva en Google Play, sale la pantalla de actualización de Play y, al terminar, Ariadna se vuelve a abrir ya actualizada. No hay que esperar a que Play la actualice por su cuenta. Esta vez, si su dispositivo tiene una versión anterior a la 2.12, actualícela a mano desde Play Store; a partir de esta, se encargará Ariadna.',
 NULL),
(2, N'Nuevo', N'F9: avisar de un fallo o hacer una sugerencia desde cualquier pantalla',
 N'Arriba, en todas las pantallas, tiene «⚠ Avisar F9». Púlselo (o F9) y elija «Algo no funciona» o «Sugerencia»: se manda con una foto de la pantalla en la que estaba y su nombre, y al enviar vuelve a esa pantalla tal como la dejó. En Avisar: F1 enviar, F2 foto, F3 quitarla. La campana (F10) sigue llevando a sus avisos.',
 NULL),
(3, N'Mejorado', N'Novedades como en Nesto: PDF, me gusta y comentarios',
 N'En Novedades, los PDF adjuntos (como las guías) salen como un botón y se abren al tocarlos o con F4. Puede marcar 👍 o 👎 (F1 y F2) y ver y escribir comentarios, también con foto (F3 abre los comentarios; F5 comentar, F6 foto). Si una sugerencia suya sale «Cerrada» porque no nos contestó, comente en ella y se vuelve a abrir.',
 NULL),
(4, N'Mejorado', N'La foto del producto en todas las pantallas',
 N'En el recorrido, el packing, Recibir, Ubicar, Enviar reposición, Diferencias y Reposición a tienda sale la foto del producto, siempre igual. Toque la foto de una línea para verla en grande (o F6 en la del producto que tiene delante).',
 N'Almacén,Tiendas'),
(5, N'Mejorado', N'Lista «Pendientes / Completa» en salidas y entradas',
 N'En el recorrido, Enviar reposición y Recibir, la lista enseña de entrada solo lo que queda pendiente («Pendientes: 3 de 10»): lo ya cogido, mandado o dado por falta desaparece, y en Recibir lo que ya cuadra. F5 (o el botón) cambia a la lista completa y vuelve. Si deshace algo, vuelve a la lista.',
 N'Almacén,Tiendas'),
(6, N'Mejorado', N'Ariadna le habla de usted, y F7 en Diferencias pide confirmación',
 N'Todos los textos y mensajes de Ariadna van ahora de usted. En Diferencias de reposición, F7 «Error al recibir» pregunta antes de corregir el stock; Intro, F7 o «Sí» lo confirman, y Esc o «No» lo cancelan.',
 NULL);
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.12', '2026-10-16', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
