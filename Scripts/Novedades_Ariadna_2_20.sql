/*
    Novedades de Ariadna 2.20 (28/10/26): sugerencia 822 (Santiago): un código de barras leído mal (28893 leído como
    9… en vez de 8…) se daba de alta solo. Ahora, si el código no es de ningún producto pero se parece a uno del
    producto que se tiene delante (una o dos cifras distintas), hay que leerlo dos veces más antes de añadirlo y se
    avisa a Compras; la cámara exige dos detecciones seguidas de un código que no conoce. También (NestoAPI#720,
    Andre) la lista de salidas: una entrega con todas las etiquetas hechas desde Nesto desaparece de la lista; y
    (NestoAPI#552) F8 «Etiqueta de agencia» del packing imprime también GLS, por la impresora de agencia del usuario.
    Necesita la API publicada hoy (28/10). Sin la API nueva, Ariadna funciona como en la 2.19.
    Va primero al canal INTERNO (Carlos). Ejecutar en SSMS contra NV como sa cuando la 2.20 llegue a la prueba cerrada.
    Idempotente (compara por Ámbito + Título).
    Al publicar: comentario a @Santiago en la 822 (marcar Implementada 2.20), a @Andre en la 824/720 (lista de salidas)
    y a @Andre @Alfredo por las etiquetas de GLS desde el packing.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Mejorado', N'Un código que se parece a uno del producto se relee antes de añadirlo',
 N'Si lee un código que no es de ningún producto pero se diferencia en una o dos cifras de uno del producto que tiene delante, Ariadna no lo añade a la primera: pide leerlo dos veces más (con el lector o con la cámara). Si las tres lecturas coinciden, se añade y se avisa a Compras para que lo compruebe; si no, se descarta. Así no entran códigos mal leídos como el del 28893.',
 N'Almacén,Tiendas'),
(2, N'Mejorado', N'La cámara pide dos detecciones de un código que no conoce',
 N'Con la cámara, un código que no es de ningún producto tiene que detectarse dos veces seguidas para darse por leído. Los códigos conocidos siguen leyéndose a la primera.',
 N'Almacén,Tiendas'),
(3, N'Mejorado', N'Lista de salidas: las entregas etiquetadas desde Nesto desaparecen',
 N'Una entrega con todos sus pedidos etiquetados desde Nesto cuenta como empaquetada y su picking deja de salir en la lista de salidas, aunque no tenga cajas con foto.',
 N'Almacén'),
(4, N'Nuevo', N'Etiqueta de agencia del packing también para GLS',
 N'F8 «Etiqueta de agencia» en el packing, con toda la entrega dentro y las cajas con foto, ya imprime también las etiquetas de GLS (antes solo CTT e Innovatrans), en la impresora de agencia de su usuario. El envío queda grabado con sus cajas y se tramita al cerrar el día.',
 N'Almacén');
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.20', '2026-10-28', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
