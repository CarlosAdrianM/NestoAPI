/*
    Novedades de Ariadna 2.11 (incluye lo de la 2.10, que solo llegó al canal interno y a Windows: la prueba cerrada pasa
    de la 2.9 a la 2.11). Ariadna#36, #37, #38 y #39.
    Ejecutar en SSMS contra NV como sa cuando la 2.11 llegue a la prueba cerrada. Idempotente (compara por Ámbito + Título).
    Después, adjuntar la guía a la de packing:
      .\Scripts\AdjuntarANovedad.ps1 -Titulo "Packing: leer comprueba el producto y lo tecleado es el total" -Ambito Ariadna -Ficheros "Documentacion\Guia packing Ariadna.pdf"
*/
SET NOCOUNT ON;
USE NV;

DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Mejorado', N'Packing: leer comprueba el producto y lo tecleado es el total',
 N'El packing funciona ya igual que el recorrido. Lea un producto del pedido (en cualquier orden): Ariadna pasa a su línea y la comprueba. Si solo queda una, la cuenta. Si quedan más, el cuadro pasa a CANTIDAD (naranja): teclee cuántas mete o pulse Intro para todas. Lo tecleado es el TOTAL: leer y teclear 5 son 5 (antes eran 6). Para contar unidad a unidad, F1 «Contar leyendo». Intro sin nada, cuando no hay ninguna línea esperando cantidad, sigue cerrando la caja con su foto. La línea en la que está sale destacada arriba, con lo que falta. Tiene todos los detalles en la guía adjunta.',
 N'Almacén'),
(2, N'Mejorado', N'Packing: F7 nombra el producto, Deshacer va por pedido y no toca cajas cerradas',
 N'Si un código no es de esta caja, «Es un código de este producto» (F7) dice ahora a qué producto lo añadiría, y en el packing solo se ofrece después de elegir usted la línea del producto que tiene en la mano (con las flechas o tocándola): así no se añade el código a otro producto por error. Deshacer (F2) quita lo último de ESTE pedido y no deshace nada de una caja ya cerrada con su foto. Los productos sin código de barras piden «Teclee la referencia», como en el recorrido.',
 N'Almacén'),
(3, N'Mejorado', N'Los errores salen en rojo y los avisos en azul',
 N'En el recorrido, el packing y la reposición que envía una tienda, un error (lo leído no vale) sale en rojo con su tono, y un aviso o una indicación (no hay que corregir nada) sale en azul. Lo hecho sale en verde. También encima de la cámara del móvil.',
 N'Almacén,Tiendas'),
(4, N'Mejorado', N'En el móvil, la parte de arriba ya no tapa la lista',
 N'En un móvil, el cliente, el producto y los botones ocupan menos y siempre quedan a la vista unas cuatro líneas de la lista. En el packing, el cliente sale en dos líneas; la dirección y las observaciones se ven tocándolo o con F5 (si el pedido tiene observaciones, sale ya desplegado).',
 N'Almacén,Tiendas'),
(5, N'Mejorado', N'En el recorrido, la cámara del móvil se cierra sola',
 N'Leyendo con la cámara del móvil en el recorrido, cuando la lectura es buena y pasa a la siguiente parada, la cámara se cierra sola y ve el siguiente producto y su hueco, como con el lector de la PDA. Sigue abierta si quedan unidades del mismo producto o si la lectura no vale.',
 N'Almacén');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.11', '2026-10-15', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;

SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
