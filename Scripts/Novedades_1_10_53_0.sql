/*
    Novedades de la versión 1.10.53.0 de Nesto (30/10/2026).
    Versión de la ClickOnce: 1.10.53.0 (pubxml 1.10.53.* en revisión 0).

    Tanda: lo que salió en silencio en la 1.10.52.1 (sin Novedades) + lo de hoy. NestoAPI (#737 WhatsApp, #745/#746 eventos,
    #748 seguimiento del reparto propio, #752 foto del aviso de ficha, #753 cursos, #754 capturas, #756/#757/#758/#759/#760/#765
    notas de entrega y recogida (épica #755), #761 retorno, #762 código de barras interno, #763 horario preferible, #764 día de
    reparto por código postal, #751 resumen de sugerencias, fin de mes) + Nesto (#570, #572, #573, #574, #575, #576, #577, #578,
    #579, #580, #581, #582, #583, #584, #585, #586, #588, #589).

    ORDEN:
      1) Scripts sa ANTES de publicar la API: Issue763_HorarioPreferible.sql (+ reciclar el pool) y después
         Issue763_CargaPropuestasHorario.sql (@Aplicar=0 para revisar, luego 1). Ya lanzados: Issue757, Issue758, Issue759, Issue767.
      2) Publicar NestoAPI y reciclar el pool.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.53.0.
      4) DESPUÉS: ESTE script y Novedades_Ariadna_2_26.sql. Luego AvisarNuevaVersionNesto.ps1 -Version 1.10.53.0 (con el OK de Carlos).

    SE OMITE A PROPÓSITO:
      - El picking automático de las 9 de las rutas internas (#764): nace apagado (Issue764_PickingRutasInternas.sql cuando se decida).
      - Lo de Ariadna (paradas, cobro libre, orden sugerido, escáner, recepción escaneando): va en Novedades_Ariadna_2_26.sql.
      - La numeración duplicada de las notas de entrega (#756): el usuario no la veía; solo se nota en que ya no hay huecos ni repetidos.
      - El cambio de contrato de la agencia propuesta (#576) y el envío que el packing crea para las notas (#758): se nota en que
        las notas de nuestra ruta salen solas en el reparto.
      - TNV / NestoApp: salen con su versión.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.53.0';
DECLARE @fecha DATE = '2026-10-30';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Notas de entrega y de recogida',
     N'En el detalle del pedido la nota de entrega tiene tipo (Entrega o Recogida), bultos y, si procede, la factura original. Con «Nueva nota de recogida» se crea en un paso. Facturar rutas imprime las notas con su código de barras (NE o NR más el número de pedido) y, en las recogidas, las etiquetas de los bultos; en el detalle hay «Imprimir nota» y «Etiquetas de recogida». Las notas de nuestra ruta entran solas en el reparto de Ariadna como una parada más.',
     N'Almacén,Administración,Dirección'),
    ('Nuevo', N'Abono por recogida leyendo la etiqueta',
     N'Cuando llega una recogida, lea la etiqueta NR con el lector (o Ctrl+Mayús+R y tecléela) en el detalle del pedido: Nesto crea el pedido de abono del cliente en la serie rectificativa, vinculado a la factura original si la hay, y lo abre con el cursor en Producto y la cantidad en −1 para que solo tenga que leer los productos que vuelven. El abono al contado vence hoy, como en «Copiar factura».',
     N'Almacén,Administración,Dirección'),
    ('Nuevo', N'Horario preferible de entrega del cliente',
     N'En la ficha del cliente, pestaña «Horario preferible», puede indicar por contacto los días y las franjas (mañana y tarde) en que prefiere recibir. En Clientes > «Horarios por revisar» están las propuestas sacadas de los comentarios de las fichas, para aceptarlas o descartarlas. Es una preferencia: lo intentaremos, pero no hay compromiso de respetarla.',
     NULL),
    ('Nuevo', N'El móvil como lector de códigos de barras de Nesto',
     N'Con «Escanear para Nesto» en Ariadna, lo que lea con la cámara del móvil llega al Nesto en el que tenga el cursor, en el campo que acepte códigos (el producto de una línea, la referencia de la ficha de producto, la lectura de una reposición…), con su Intro. Si el cursor no está en un campo de esos, sale una ventanita con el último código leído y un botón «Copiar». Basta con entrar en Ariadna con su mismo usuario.',
     NULL),
    ('Nuevo', N'Enviar el pedido o la factura por WhatsApp',
     N'En el detalle del pedido y en la lista de facturas hay un botón que abre WhatsApp Web con el mensaje preparado y un enlace de descarga del PDF, que caduca a los pocos días. Se elige el teléfono de la ficha o se teclea otro.',
     NULL),
    ('Nuevo', N'Reposiciones: todo con código de barras y recibidas leyendo',
     N'Al enviar una reposición, Nesto imprime una etiqueta EAN-13 (3×2) por cada producto que no tenga código de barras, con un código interno nuevo que ya queda en su ficha. Al recibirla en la tienda se lee todo con el lector; lo que llegue sin etiqueta se apunta con «Sin etiqueta», unidad a unidad, y se avisa al origen y a Compras.',
     N'Tiendas,Almacén,Dirección'),
    ('Nuevo', N'Eventos: reservas sin pagar mientras haya plazas',
     N'Se puede inscribir a un cliente sin cobrarle todavía: la reserva avisa del plazo para pagar y, si el evento se llena, se anula sola la que no se haya pagado. Dirección puede inscribir en un evento por compra aunque el cliente no haya comprado el producto.',
     NULL),
    ('Nuevo', N'Nuestra ruta: la fecha de entrega sale del código postal',
     N'Para los pedidos que van por nuestra ruta (Reparto propio), la fecha de entrega es el próximo día en que la furgoneta pasa por el código postal del cliente; el día de su ruta, los pedidos de antes de la hora de corte salen ese mismo día. Si el cliente cierra ese día, Nesto avisa al grabar. Las agencias siguen como siempre.',
     N'Almacén,Administración,Dirección'),
    ('Mejorado', N'Inscritos del evento en tarjetas',
     N'Los inscritos de un evento se ven en tarjetas legibles que se despliegan al hacer clic, en vez de una rejilla apretada.',
     NULL),
    ('Mejorado', N'Fin de mes: sin fecha de corte y también para Facturación',
     N'El fin de mes ya no pide fecha de corte: reúne y factura lo que está en albarán. Lo puede lanzar también el grupo Facturación. Si un cliente tiene «facturar al completar», se le factura lo albaraneado y se le vuelve a poner la marca; si algo falla, el aviso lo dice.',
     N'Administración,Dirección'),
    ('Mejorado', N'Notas de entrega de solo texto por picking y packing',
     N'Las notas de entrega que solo llevan texto («Preparar: …») pasan por el picking y el packing como cualquier pedido: se les ponen los bultos y la etiqueta, y si llevan reembolso sale en la etiqueta. «Facturar al imprimir» ya no procesa las notas de nuestra ruta: las procesa el packing.',
     N'Almacén,Administración'),
    ('Mejorado', N'Agencias: «Abrir seguimiento» también en nuestra ruta',
     N'En los envíos del reparto propio el botón «Abrir seguimiento» abre la página de seguimiento que ve el cliente.',
     N'Almacén,Administración'),
    ('Mejorado', N'Cursos en la plantilla: la forma de pago manda',
     N'Si el pedido con un curso se paga por transferencia o en efectivo, no se manda enlace de tarjeta. Al grabar, Nesto pregunta si se envía el correo del curso al cliente.',
     NULL),
    ('Mejorado', N'Novedades: varias capturas y la ventana se puede apartar',
     N'En una sugerencia o un comentario de Novedades se pueden pegar varias capturas y verlas después. La ventana de Novedades se puede minimizar para hacer la captura y volver.',
     NULL),
    ('Mejorado', N'Rapports: Resultado y Próximo contacto al final',
     N'En la rejilla de rapports, Resultado y Próx. contacto van al final y son más estrechas, para que el comentario tenga sitio.',
     NULL),
    ('Mejorado', N'Ficha de producto: buscar por código de barras',
     N'En la casilla de referencia de la ficha de producto se puede leer o teclear un código de barras y Nesto abre el producto.',
     NULL),
    ('Mejorado', N'Sugerencias de productos: qué gana con cada respuesta',
     N'Junto a «💡 Puede ofrecer» hay una frase con lo que le aportan sus respuestas ✓ y ✗ a las sugerencias (mejores sugerencias para usted y sus clientes).',
     N'Vendedores'),
    ('Mejorado', N'El correo del pedido dice si hay retorno',
     N'En el correo interno del pedido, la fila del modo de entrega indica si el envío lleva retorno, no lo lleva o es una recogida en origen.',
     N'Almacén,Administración'),
    ('Corregido', N'Recoger producto: ya no se pierde el retorno de la etiqueta pendiente de CTT',
     N'Al imprimir la etiqueta de un pedido con recogida pendiente, el retorno se conserva aunque el cliente lo hubiera quitado sin pedirlo.',
     N'Almacén,Administración'),
    ('Corregido', N'Aviso de ficha: el correo conserva la foto',
     N'El correo de aviso de ficha de producto ya no pierde la foto cuando la tienda la cambia después.',
     NULL),
    ('Corregido', N'Pedido de compra: error al guardar sin haberlo creado',
     N'Guardar un pedido de compra nuevo (por ejemplo, de STALEKS) daba «Sequence contains no matching element» y no dejaba enviarlo por correo. Corregido.',
     N'Compras,Dirección');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
