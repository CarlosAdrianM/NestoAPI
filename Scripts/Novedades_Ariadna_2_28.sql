/*
    Novedades de Ariadna 2.28 (03/11/26): Ariadna#79 (pantalla de Inventario, NestoAPI#795/#784), #78 (Salidas: reimprimir),
    #80 (código interno al imprimir, NestoAPI#791), #77 (Sin etiqueta sin duplicados), #82 (recoger desde la parada), #84 (muestras MMP sin código), #83 (pedido dentro de Empaquetar) y el agradecimiento a Miguel y Andre.
    Necesita la API del 03/11 (1dff4f39) y los scripts Issue784_* (ya lanzados).
    Ejecutar en SSMS contra NV como sa cuando la 2.28 llegue a la prueba cerrada. Idempotente (compara por Ámbito + Título).
    NO duplicar: la sugerencia 960 (Carlos, reimprimir desde Salidas) ya está marcada Implementada en 2.28 por la API.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Gracias, Miguel y Andre',
 N'La furgoneta bulto a bulto, la firma, el cobro con tarjeta, las reposiciones que se recogen leyendo y el packing con fotos funcionan porque Miguel y Andre los han probado desde el primer día, nos han contado cada cosa que no cuadraba y han vuelto a probar cada arreglo. Gracias de corazón a los dos: sois nuestros «key users» de este mes. Y gracias a todos los que escribís por «Novedades»: lo leemos todo.',
 NULL),
(2, N'Nuevo', N'Inventario (en pruebas para el 21 de noviembre)',
 N'Pantalla nueva «Inventario». En Algete va por ubicaciones, como la Ariadna de siempre: lee la etiqueta del hueco, lee cada producto y teclea la cantidad; si lees el mismo producto dos veces en el hueco, se suma. En las tiendas va por producto: primero «Rellenar» trae todo lo que tiene stock, y después lees el código y tecleas la cantidad (o la referencia a mano, aunque Ariadna apunta que se tecleó). «Lo contado» enseña lo que llevas y «Comprobar» dice si falta algo antes de contabilizar. Antes del inventario la probaremos con una sola referencia en cada almacén.',
 N'Almacén,Tiendas'),
(3, N'Mejorado', N'Salidas: reimprimir las etiquetas de una reposición ya enviada',
 N'Si tecleas el número de una reposición que ya salió (no está en la lista) e Intro, Ariadna te ofrece «Reimprimir etiquetas de bulto», para cuando la impresora falló al terminarla.',
 N'Almacén'),
(4, N'Mejorado', N'Etiquetas de producto: siempre EAN-13',
 N'Las etiquetas de producto llevan siempre el código de barras EAN-13 de la unidad; si el producto no tiene, Ariadna le da uno interno (empieza por 29) al imprimirla y lo apunta al momento en la ficha, de modo que la siguiente lectura ya lo reconoce. Ya no hay etiqueta con la referencia en Code128.',
 N'Almacén,Tiendas'),
(5, N'Corregido', N'Recepción «Sin etiqueta»: sin avisos repetidos',
 N'Si al dar una unidad «Sin etiqueta» la PDA pierde la cobertura y reintentas, el aviso al origen y a Compras ya no se manda dos veces.',
 N'Almacén'),
(6, N'Mejorado', N'Reparto: recoger la reposición desde la propia parada',
 N'Si en «Entregar» lee un bulto R de la reposición que se recoge en esa misma tienda (o una NR del cliente de la parada), Ariadna ya no le dice solo «no es de esta parada»: le pregunta «¿Recogerlo ahora?» e Intro lo sube a la furgoneta sin salir de la parada, igual que desde «En la furgoneta». Gracias, Miguel.',
 N'Almacén'),
(7, N'Mejorado', N'Muestras sin código de barras: se cuentan tecleando',
 N'Las muestras (subgrupo MMP) que no traen código de barras del fabricante ya no hay que etiquetarlas. En el recorrido, el packing y Enviar reposición, Ariadna dice «Muestra sin código de barras: teclee su referencia (o la cantidad y F1) para contarla» y no pide imprimir etiqueta. Al recibir una reposición, en esas líneas teclear la referencia o la cantidad cuenta, sin pasar por «Sin etiqueta». Lo pidió Almacén.',
 N'Almacén,Tiendas'),
(8, N'Mejorado', N'Packing: el número del pedido le lleva a su entrega',
 N'Dentro de Empaquetar, teclee o lea el número de un pedido (6 cifras) y Ariadna le lleva a su entrega y a su caja («Pedido 929582: entrega de X, caja 1»). Si el pedido es de otro picking, le pregunta si quiere abrirlo. Las entregas siguen agrupadas por cliente, porque los pedidos del mismo cliente van en la misma caja. Gracias, Alfredo.',
 N'Almacén');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.28', '2026-11-03', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
