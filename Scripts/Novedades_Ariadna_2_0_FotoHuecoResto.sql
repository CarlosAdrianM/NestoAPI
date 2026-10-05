/*
    Novedades de Ariadna 2.0 (05/10/2026, tarde): la foto del producto en todas las pantallas, fijar el hueco al
    ubicar, teclear un hueco con «*», «#» para coger todo lo que queda, «¿de qué pedido es este producto?» en el
    packing y «No está» con el producto en otro hueco (Ariadna#9, #10, #11 y #12).

    Se omite a propósito (no lo nota el mozo):
      - El componente único de la tarjeta del producto (Ariadna#9): se cuenta lo que se ve, la foto.
      - Que la API manda ahora la ficha y la foto en las líneas del packing.
      - Las issues abiertas para más adelante: etiqueta de agencia desde la PDA (Ariadna#13), una cubeta por pedido
        (Ariadna#14) y centralizar las ubicaciones (NestoAPI#594).

    Ejecutar en SSMS contra NV como sa, DESPUÉS de publicar la API y la app. Idempotente.
    Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @novedades TABLE (Categoria varchar(20), Titulo nvarchar(400), Descripcion nvarchar(max));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
('Nuevo', N'La foto del producto, en todas las pantallas',
 N'La foto del producto ya no sale solo en el recorrido: también al ubicar, al recibir (compras y reposiciones) y en el packing. Tócala o pulsa F6 para verla a pantalla completa; Intro o Esc la cierran. Con la foto abierta, Intro solo la cierra: no ubica ni termina nada. En el packing también puedes avisar de un «Dato mal» del producto con «*».'),
('Nuevo', N'Ubicar: fija el hueco para varios productos seguidos',
 N'Si vas a ubicar varios productos en el mismo hueco, ubica el primero como siempre y pulsa «Fijar hueco (F4)». Desde ahí cada producto es leerlo e Intro, sin volver a leer la etiqueta del hueco. Arriba verás siempre a qué hueco va todo. Se suelta con F4, al leer otro hueco (lo cambia), al salir de la pantalla y solo, si pasan 2 minutos sin ubicar nada.'),
('Mejorado', N'Huecos sin etiqueta: tecléalos con «*»',
 N'Un hueco que no tiene etiqueta (o la tiene escrita a mano) se puede teclear con «*» o «#» entre los números, que en la PDA salen con una sola tecla: 2*4*1 es el hueco 002/004/001. Después, «Etiqueta del hueco (F3)» imprime su etiqueta para que la próxima vez se pueda leer.'),
('Nuevo', N'«#» coge todo lo que queda',
 N'Para no leer doce veces el mismo producto: lee una unidad (así se comprueba que es el producto) y después teclea «#» e Intro para coger todo lo que queda, o la cantidad e Intro para coger esas. Vale en el recorrido y en el packing. Los productos sin código no necesitan la primera lectura. «Deshacer» lo quita todo de una vez.'),
('Nuevo', N'Packing: lee un producto para saber de qué pedido es',
 N'En la lista de pedidos del packing puedes leer un producto del montón y Ariadna te dice de qué pedido es. Si solo lo espera uno, abre su caja y apunta esa unidad; si lo esperan varios, te dice cuáles. Y si te sobra una unidad que ya consta metida en todos los pedidos, te dice qué cajas revisar: las unidades no aparecen de la nada, así que en alguna falta.'),
('Nuevo', N'«No está»: si hay en otro hueco, Ariadna te lo dice',
 N'Al pulsar «No está» en un picking, si hay de ese producto en otro hueco, Ariadna te lo dice antes de darlo por falta («Hay 6 en 003/001/002»). Con Intro pasas a cogerlo de ese hueco y el pedido no se queda sin él; con «No está» otra vez lo das por falta como siempre; con Esc sigues buscando. Sin wifi no pregunta: es una falta como hasta ahora.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.0', '2026-10-05', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: las seis nuevas al final.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Ambito = 'Ariadna' ORDER BY Id;
