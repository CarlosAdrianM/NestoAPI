/*
    Novedades de Ariadna 2.21 (28/10/26). SUSTITUYE al script Novedades_Ariadna_2_20.sql: la 2.20 no se promocionó al canal
    cerrado y la 2.21 lleva todo lo de la 2.20 más la ficha de producto que pidió Andre (sugerencia 824) para mañana.
    - 822 (Santiago): un código que se parece a uno del producto se relee dos veces antes de añadirlo y se avisa a Compras; la
      cámara exige dos detecciones de un código que no conoce.
    - NestoAPI#720 (Andre): la lista de salidas: una entrega con todas las etiquetas hechas desde Nesto desaparece.
    - NestoAPI#552: F8 «Etiqueta de agencia» del packing imprime también GLS, por la impresora de agencia del usuario.
    - 824 (Andre): «Producto (0)» en el inicio: ficha del producto leyendo o tecleando un código.
    Necesita la API publicada con 752b4f5c (ficha) o posterior. Sin la API nueva, la ficha dice «no encontrado».
    Va primero al canal INTERNO (Carlos). Ejecutar en SSMS contra NV como sa cuando la 2.21 llegue a la prueba cerrada.
    Idempotente (compara por Ámbito + Título).
    Al publicar: OneShot_20261028_CerrarSugerencias_1_10_51_0.ps1 -Ariadna221 (822 → Implementada 2.21, aviso a @Santiago; 824 →
    Implementada 2.21, aviso a @Andre @Alfredo @Santiago; lista de salidas y GLS a @Andre).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Ficha del producto desde el inicio: «Producto (0)»',
 N'En el inicio hay un botón nuevo, «Producto» (tecla 0). Lea un código o teclee la referencia y sale la ficha: foto, nombre, familia, referencia del proveedor, los códigos de barras con sus unidades y, por cada almacén, el stock, lo pendiente de entregar y los huecos con sus unidades (su almacén primero). Leer otro código carga el siguiente. F2 «Algo está mal» avisa a Tienda online de un dato incorrecto; F3 «Añadir código» hace que la siguiente lectura sea un código nuevo del producto (con la relectura de seguridad si se parece a uno que ya tiene); F4 imprime la etiqueta del producto (tecle antes el número de copias); F5 recarga; Esc vuelve al inicio.',
 N'Almacén,Tiendas'),
(2, N'Mejorado', N'Un código que se parece a uno del producto se relee antes de añadirlo',
 N'Si lee un código que no es de ningún producto pero se diferencia en una o dos cifras de uno del producto que tiene delante, Ariadna no lo añade a la primera: pide leerlo dos veces más (con el lector o con la cámara). Si las tres lecturas coinciden, se añade y se avisa a Compras para que lo compruebe; si no, se descarta. Así no entran códigos mal leídos como el del 28893.',
 N'Almacén,Tiendas'),
(3, N'Mejorado', N'La cámara pide dos detecciones de un código que no conoce',
 N'Con la cámara, un código que no es de ningún producto tiene que detectarse dos veces seguidas para darse por leído. Los códigos conocidos siguen leyéndose a la primera.',
 N'Almacén,Tiendas'),
(4, N'Mejorado', N'Lista de salidas: las entregas etiquetadas desde Nesto desaparecen',
 N'Una entrega con todos sus pedidos etiquetados desde Nesto cuenta como empaquetada y su picking deja de salir en la lista de salidas, aunque no tenga cajas con foto.',
 N'Almacén'),
(5, N'Nuevo', N'Etiqueta de agencia del packing también para GLS',
 N'F8 «Etiqueta de agencia» en el packing, con toda la entrega dentro y las cajas con foto, ya imprime también las etiquetas de GLS (antes solo CTT e Innovatrans), en la impresora de agencia de su usuario. El envío queda grabado con sus cajas y se tramita al cerrar el día.',
 N'Almacén');
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.21', '2026-10-28', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
