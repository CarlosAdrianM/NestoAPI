/*
    Novedades de Ariadna 2.13 (19/10/26): Ariadna#43, #44, #45, #46, #47 y NestoAPI#675 (parte de Ariadna); en la API, #593
    (cheques y cupones fuera del recorrido y del packing).
    Va primero al canal INTERNO (Carlos). Ejecutar en SSMS contra NV como sa cuando la 2.13 llegue a la prueba cerrada.
    Idempotente (compara por Ámbito + Título). Necesita la API del 19/10 publicada y Ariadna43_LecturasYMotivosTecleo.sql lanzado.
    SE OMITE: las sugerencias 674 (código recién corregido) y 675 (nombre completo) de Andre van por la propia sugerencia
    (PUT con Version '2.13' cuando llegue a la prueba cerrada).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Productos que se confunden: las últimas cifras del código y lo que los distingue',
 N'Cuando en un hueco hay productos muy parecidos, la tarjeta del producto enseña en grande las últimas 5 cifras de su código de barras, para compararlas con la caja sin escanear, y resalta en amarillo lo que lo distingue de los demás del hueco (por ejemplo, «MÁSCARA» frente a «SERUM», o el tono «5/55»).',
 N'Almacén,Tiendas'),
(2, N'Nuevo', N'Al teclear una referencia, confirme con la foto',
 N'Si teclea la referencia en lugar de escanear, Ariadna le enseña la foto y el nombre del producto en grande y le pregunta si es el que tiene en la mano: Intro, sí; Esc, no. Si el producto tiene código de barras, la primera vez le pregunta por qué lo ha tecleado: F1 «No se lee», F2 «No lleva código» o F3 «Otro». Con «No se lee» le ofrece imprimir nuestra etiqueta de producto para pegarla y que la próxima vez se lea. Leer nuestra etiqueta con la referencia cuenta como escanear.',
 N'Almacén,Tiendas'),
(3, N'Nuevo', N'Recibir una reposición por partes',
 N'En Recibir, F4 «Solo lo leído» da de alta lo que ha leído y deja el resto de la reposición pendiente para recibirlo después, sin contar como falta. La reposición se queda en la lista con «Recibido en parte».',
 N'Tiendas,Almacén'),
(4, N'Mejorado', N'La cámara lee mejor',
 N'La cámara solo acepta los códigos que usamos (EAN, UPC, Code 128 e ITF) y da la lectura por buena cuando la lee dos veces igual. Así ya no se «inventa» códigos al apuntar a una caja o a un reflejo.',
 NULL),
(5, N'Mejorado', N'La etiqueta del bulto se lee siempre',
 N'La etiqueta del bulto (R + número de reposición + bulto) se reconoce aunque el lector cambie el guion por otro signo.',
 N'Almacén,Tiendas'),
(6, N'Corregido', N'Los cheques regalo y los cupones ya no salen en el recorrido',
 N'El cheque regalo o el cupón de un pedido no es un producto: es un descuento del cliente. Antes salía en el recorrido como algo que recoger, sin ubicación, y si se marcaba «No está» el cliente se quedaba sin su descuento. Ya no sale ni en el recorrido ni en el packing, y llega al albarán como siempre.',
 N'Almacén');
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.13', '2026-10-19', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
