/*
    Novedades de la versión 1.10.46.0 de Nesto (21/10/2026).
    Versión de la ClickOnce: 1.10.46.0 (pubxml 1.10.46.* en revisión 0).

    Tanda: NestoAPI (#669/#671 modelo de sugerencias activo + #682 + «Quitar» por familia + informe de uso, #683 estado del
    cliente en el modelo [se entrena esta noche], #677 llamadas por probabilidad y cobertura, #684 fecha de entrega >= 2000,
    #685 etiquetas ZPL/EPL y reimprimir reposiciones, #686 contado + «Mantener junto» y reembolso de toda la factura, #687
    unir pedidos, #688 servir lo pendiente en cuanto haya stock + aviso, #632 máximos de stock, portes al modificar un pedido
    ya albaranado, #343 diagnóstico, Ganavisiones con cheque regalo, recorrido de Ariadna en tiendas por familia) + Nesto (#688 corte 3, #685, #686, #684,
    tarjeta de sugerencias 714, #490 4C.4 tramo 4 sin cambio visible).

    ORDEN:
      1) Scripts sa ANTES de publicar la API: Issue688_ServirPendienteAlHaberStock.sql (YA LANZADO 21/10 16:59),
         Issue683_SugerenciasPopularidadEstado.sql (YA LANZADO), Issue547_ApagarSPDomingo.sql (YA LANZADO).
      2) Publicar NestoAPI (antes de las 23:30: el entrenamiento de esta noche corre dentro de la API).
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.46.0.
      4) DESPUÉS: ESTE script.
      5) POST api/SugerenciasProductos/ReentrenarModelo (→ hoy 23:30) y regenerar el Excel de septiembre de #632 de noche.

    SE OMITE A PROPÓSITO:
      - #683 (estado del cliente en el modelo): entra solo si el modelo de esta noche pasa el examen.
      - #343: solo diagnóstico interno. #547: proceso nocturno de los domingos apagado (nadie lo ve).
      - #490 4C.4 tramo 4: sin cambio visible.
      - Lo de Ariadna (recorrido de las tiendas por familia, etiquetas de bulto): en su script si sale versión nueva.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.46.0';
DECLARE @fecha DATE = '2026-10-21';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'«Servir lo pendiente en cuanto haya stock»',
     N'Con la facturación «Todo ahora, lo pendiente se entrega después» aparece una casilla nueva, marcada por defecto: lo que se quede en carpeta se servirá solo, todo junto, en cuanto haya stock de todo. Si el cliente prefiere que se lo vayamos mandando poco a poco, desmárquela: entonces la fecha la pone usted, como hasta ahora, y Nesto le avisará (y al vendedor) cuando ya haya stock de todo lo pendiente. También se puede cambiar después en la nota de entrega, mientras no tenga picking. Y con «Todo junto» ya no se muestra la facturación, porque va implícita. Gracias, Marta, por la idea.',
     N'Vendedores,Tiendas,Almacén'),
    ('Mejorado', N'«Puede ofrecer»: sugerencias más acertadas y que cambian según añade productos',
     N'Las tres tarjetas las elige ahora un modelo entrenado con años de pedidos, y se actualizan a medida que mete productos en la plantilla (cambia como mucho una cada vez, y nunca las que ya ha contestado). Si quita con «🚫» dos productos de la misma marca a un cliente, deja de proponerle esa marca. Le pedimos un favor: ofrézcalas al cliente y pulse «✓» si se lo lleva o «✗» si no le interesa. Con esas respuestas las sugerencias aprenden y aciertan cada vez más.',
     N'Vendedores,Tiendas'),
    ('Mejorado', N'Clientes para contactar: ordenados por probabilidad de pedido',
     N'La lista ya no va por Máxima/Alta/Media/Baja, sino por la probabilidad de que el cliente haga un pedido: arriba, los que más probablemente compren. Además, cada día se reservan unos huecos («Cobertura del mes») para los clientes que le compran y con los que todavía no ha hablado este mes. A principio de mes son pocos y van aumentando; el último día laborable salen todos los que falten, para que a fin de mes haya hablado con todos.',
     N'Vendedores'),
    ('Nuevo', N'Enviar reposición: reimprimir bultos e impreso',
     N'Debajo de la lista, en «Enviadas, pendientes de recibir», están las reposiciones que ha enviado y aún no se han recibido, con dos botones: «Reimprimir bultos» y «Reimprimir impreso». Ya no hace falta pedirlo si una etiqueta sale mal.',
     N'Tiendas,Almacén'),
    ('Corregido', N'Etiquetas de bulto en blanco en Alcobendas',
     N'Las etiquetas de bulto (y las de hueco) salían en blanco en la impresora Zebra de Alcobendas. Ahora Nesto manda cada etiqueta en el idioma que entiende cada impresora.',
     N'Tiendas,Almacén'),
    ('Mejorado', N'Pedidos al contado: no se pueden «Mantener junto»',
     N'Un pedido al contado ya no se puede facturar «Al completar», porque se cobra en cada entrega. Y si un pedido así llega a salir en varias entregas, o se factura «Todo ahora», el envío que lleva la factura cobra toda la factura y no solo lo que va en esa caja. Gracias, Alfredo, por el aviso.',
     N'Vendedores,Almacén,Administración'),
    ('Corregido', N'Fecha de entrega con el año mal escrito',
     N'Nesto ya no deja grabar una fecha de entrega anterior al año 2000 (pasó con un 1926 en vez de 2026) y avisa con un mensaje claro para que revise el año.',
     NULL),
    ('Corregido', N'Modificar un pedido ya albaranado con portes',
     N'Al cambiar algo de un pedido ya albaranado (por ejemplo, el número de pedido del cliente) que llevaba portes, Nesto daba error y no guardaba. Ya se puede.',
     NULL),
    ('Corregido', N'Unir pedidos fallaba a veces',
     N'Al añadir productos a un pedido que el cliente ya tenía creado, a veces fallaba por un error interno y no se ampliaba. Ya está corregido. Gracias, Marta.',
     N'Vendedores'),
    ('Corregido', N'La tarjeta de «Puede ofrecer» se veía cortada',
     N'Con nombres largos, el motivo de la sugerencia quedaba cortado detrás de los botones. Ahora los botones van a la derecha del motivo.',
     N'Vendedores,Tiendas'),
    ('Corregido', N'Ganavisiones con el cheque regalo',
     N'Al usar el cheque regalo, la plantilla contaba los Ganavisiones como si no se hubiera descontado (por ejemplo, 71 en vez de 66) y luego no dejaba grabar el pedido. Ahora el cheque resta de la base, y si los regalos elegidos dejan de caber, Nesto le avisa antes de grabar.',
     N'Vendedores,Tiendas'),
    ('Mejorado', N'Ficha de control de stock: máximos más ajustados',
     N'Los máximos y mínimos propuestos pesan más lo vendido en los últimos meses, descuentan la temporada y no suben en los productos que van a la baja. Cada propuesta explica sus cuentas.',
     N'Compras');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
