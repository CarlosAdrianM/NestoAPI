/*
    Novedades de la versión 1.10.36.3 de Nesto (05/10/2026) y de Ariadna.
    Versión de la ClickOnce: 1.10.36.3 (pubxml en revisión 3).
    

    Tanda: NestoAPI (etiquetas de hueco b0c348a0, #578 e52e7f02, #579 640e19bd) + Nesto (ClickOnce) + Ariadna
    (Windows y Play interna: F3 en Ubicar, Android 7.1).

    ORDEN:
      1) Publicar NestoAPI (no hay scripts sa previos).
      2) Etiqueta de prueba: Ariadna › Ubicar › F3 (o, ya publicada la ClickOnce, Productos › Almacén › Etiquetas de hueco).
      3) Publicar la ClickOnce (Clean + Rebuild) y Ariadna Windows.
      4) DESPUÉS: ESTE script (idempotente: compara por Versión/Ámbito + Título).

    SE OMITE A PROPÓSITO:
      - Modernización Nesto#490 (Producto, Rapports, Plantilla, menú, extracto, 347, mayor… sin Prism): no cambia
        nada para el usuario. Si algo se ve raro en esas pantallas, es por esto.
      - #578 (dirección desde coordenadas) y #579 (push de versión de NestoApp): son para NestoApp; se contarán
        en su versión.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.36.3'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-10-05';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Mandar facturas por correo desde la ficha del cliente',
     N'En la ficha comercial del cliente, pestaña Facturas: marca las facturas, escribe el correo (se propone el de facturas del cliente; para varios, sepáralos con punto y coma) y pulsa «Enviar por correo». Salen todas juntas en un correo, sin tener que descargarlas.'),
    ('Nuevo', N'Recibir las reposiciones en la tienda desde Nesto',
     N'En Productos › Reposición › «Recibir» salen las reposiciones pendientes de tu tienda. Abre una, lee con el lector lo que llega (los productos sin código se teclean en «Leído») y pulsa «Terminar»: entra lo que has leído y, si no coincide con lo enviado, se avisa a quien hizo la reposición.'),
    ('Nuevo', N'Etiquetas de hueco del almacén desde Nesto',
     N'En Productos › Almacén › «Etiquetas de hueco» se imprimen las etiquetas de los huecos (pasillo, fila y columna, con su código de barras) en vuestra impresora de etiquetas de producto. Se puede imprimir un pasillo entero, un rango de filas y columnas, solo los huecos que tienen producto, o huecos sueltos tecleados o leídos con el lector. «Ver» dice cuántas saldrán antes de imprimir.'),
    ('Corregido', N'Clientes con dirección de fuera de España aunque Google no tenga su código postal',
     N'Al crear un cliente, algunas direcciones (por ejemplo de Portugal) salen en la lista de Google sin código postal, y Nesto decía que la dirección se había escrito a mano. Ahora la dirección elegida de la lista vale y solo hay que escribir el código postal.'),
    ('Corregido', N'Cobros en caja con un abono que no cuadraban por un céntimo',
     N'Al cobrar a la vez facturas y un abono del mismo cliente, a veces salía «Error en el algoritmo de cobros» y no se contabilizaba. Ahora el abono se descuenta primero y el cobro cuadra; si aun así no cuadrara, sale un aviso claro en vez del error.'),
    ('Corregido', N'Agencias: la etiqueta de un envío pendiente sale con su agencia',
     N'Al imprimir la etiqueta de un envío pendiente (los de la tienda online o Amazon), Nesto usaba la agencia elegida arriba en la ventana en vez de la del envío, y podía salir «La agencia 1 no tiene gestión remota en el servidor». Ahora grabar, imprimir y borrar usan siempre la agencia del envío.'),
    ('Corregido', N'«Pegar» con el botón derecho ya adjunta la captura en Novedades',
     N'Al escribir una sugerencia, un «Algo no funciona» o un comentario, si lo copiado era una imagen, «Pegar» del botón derecho salía desactivado y solo funcionaba Ctrl+V. Ahora sale activo y adjunta la captura; con texto copiado pega el texto como siempre.'),
    ('Mejorado', N'Agencias avisa si el pedido ya tiene algo pagado por adelantado',
     N'Al hacer la etiqueta de un pedido contra reembolso que tiene un prepago, junto al reembolso sale «Descontar X € prepagados» y, al imprimir, Nesto pregunta si se descuenta. Si dices que no, el reembolso se queda como estaba.'),
    ('Mejorado', N'Los avisos con mucho texto se leen enteros',
     N'Los avisos y las preguntas de Nesto ya no tienen un tamaño fijo: el texto largo se reparte en varias líneas, la ventana se puede agrandar y el texto se recoloca, y si no cabe aparece la barra para desplazarse.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;

-- Ariadna (mismo ámbito y versión que las de la 2.0)
DECLARE @tituloAriadna nvarchar(400) = N'Imprimir la etiqueta de un hueco desde Ubicar';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.0', @fecha, 'Nuevo', @tituloAriadna,
       N'Si un hueco no tiene etiqueta y tecleas su número (por ejemplo 002/004/001), al ubicar Ariadna te propone imprimirla: pulsa F3 (o «Etiqueta del hueco») y sale por tu impresora de etiquetas.',
       'Ariadna', 1, 'sa'
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = @tituloAriadna);

SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;

-- Comprobación: 5 filas de Nesto con esta versión y la de Ariadna al final de las suyas.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version OR (Ambito = 'Ariadna' AND Fecha = @fecha) ORDER BY Id;
