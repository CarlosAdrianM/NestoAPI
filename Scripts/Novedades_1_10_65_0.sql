/*
    Novedades de la versión 1.10.65.0 (19/11/2026): API + Nesto. Va con Ariadna 2.39 (Scripts/Novedades_Ariadna_2_39.sql).

    ORDEN:
      1) Scripts (sa), ANTES de publicar la API (idempotentes; sin ellos la API funciona como antes y esa parte queda apagada):
           Scripts/Issue857f_ComprasCorreosEstadoEnvio.sql          (correos de compras: estado del envío y origen de la entrega)
           Scripts/Issue857g_ComprasConfirmacionesProveedor.sql     (confirmaciones de pedido del proveedor)
         Ya lanzados hoy: Issue874_trgClientesUpd_Direccion2.sql y OneShot_20261119_MetaHarmonia_FormularioNuevo.sql.
         APARTE, cuando Carlos decida: OneShot_20261119_OfertasBimestrales_Fechas.sql e Issue590_PlazosPagoPedido_BloqueoNestoViejo.sql.
      2) Publicar la API (master). 3) Publicar Nesto (1.10.65.0). 4) Lanzar los flujos de Ariadna 2.39 (la API y Ariadna van JUNTAS:
         «Solo entrego los leídos» y el reembolso parcial dependen de las dos). 5) ESTE script.

    NO van aquí porque son SUGERENCIAS/INCIDENCIAS que se contestan en su propio hilo al publicar:
      1320 y 1361 (Dirección 2 a la vista), 1341, 1355 y 1359 (etiqueta de producto: el cursor vuelve a la referencia y el mensaje sale
      junto al botón), 1356 (devuelto en la pestaña Entrega), 1357 (Ariadna: preguntas encima de la pantalla), 1360 (cambiar el cliente de
      un pedido con prepago), 1362 (inscritos: separador e «Imprimir lista»), 1364 (inventario: fila de un día anterior), 1365 (nota de
      entrega: cambios de cabecera), 1366 (reenvío que sobra), 1367 (campana más grande), 1368 (dosier de más de 4 MB).

    Se omiten a propósito (nada perceptible o interno):
      - Correos de seguimiento de compras en dos niveles (formato del proveedor y agencia), Nacex, MBE, Weelko, Schenker a 72 h laborables.
      - Devueltos y demás consultas de envíos cerradas a los clientes de la tienda. Dirección 2 en el checkout de Aplázame y campos
        nuevos para la tienda online y NestoApp. Dirección 2 en el correo del disparador de clientes.
      - PUT de una fila de inventario cambiando la fecha (400 en vez de 500). Teléfono de la ficha en los inscritos.
      - Aviso «curso vendido a mano». El job de las 22:00 cierra los bultos de envíos ya entregados.

    Ejecutar contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.65.0';
DECLARE @fecha DATE = '2026-11-19';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Alta de cliente: la dirección de entrega, en la misma alta',
     N'Al crear un cliente puede marcar «La entrega es en otra dirección» y rellenar allí mismo la dirección de entrega, con su «¿Qué es?» (piso, local, casa, nave…), su teléfono y sus comentarios. Nesto crea las dos direcciones a la vez y deja la de entrega como la de por defecto; ya no hace falta entrar dos veces. Además, el código postal del alta lo pone la dirección de Google: solo se puede teclear si Google no lo da.',
     NULL),
    ('Nuevo', N'Ficha de cliente: pestaña «Direcciones» y entrega por defecto',
     N'En la ficha del cliente hay una pestaña «Direcciones» con todas sus direcciones. Se ve cuál es la de facturación y cuál la de entrega por defecto, y puede cambiar esta última con un clic.',
     NULL),
    ('Nuevo', N'La Dirección 2 (piso, local, rótulo…) se ve en todas partes y se puede cambiar',
     N'Al modificar un cliente aparece también «¿Qué es?», con lo que tenga guardado, para corregirlo. Y la dirección completa (con el piso o el rótulo del local) sale ya en el selector de clientes, en la cabecera y la lista de pedidos, en el correo del pedido, en el packing, en las paradas del reparto, en las etiquetas de recogida y en los documentos. En las facturas, solo en las que se emitan desde hoy.',
     NULL),
    ('Nuevo', N'Pedido: «Dónde está lo que falta»',
     N'En el detalle del pedido hay un apartado plegable, «Dónde está lo que falta», que dice de cada producto que no está listo en Algete dónde está y cuándo llega: en qué reposición viene, de qué tienda, quién la ha recogido y a qué hora, o qué pedido al proveedor lo trae. También está en «Pedidos con la fecha en riesgo».',
     NULL),
    ('Nuevo', N'Cajas: la señal de un evento se cobra como cualquier cobro',
     N'En Cajas › Cobros, al elegir un cliente con una plaza reservada sin pagar, aparece la reserva del evento. Al cobrarla (efectivo o tarjeta en tienda; transferencia en Administración) Nesto hace lo mismo que cuando el cliente paga el enlace: deja la plaza pagada, hace la factura y le manda su cupón. La señal se cobra entera. «Marcar pagada» a mano queda solo para Administración y Dirección.',
     N'Tiendas,Administración,Dirección'),
    ('Nuevo', N'Reparto propio: entregas incompletas a la vista',
     N'Si el repartidor entrega un envío sin alguno de sus bultos (porque se quedó en el almacén o no lo llevaba), el envío queda como «Entregado: 4 de 5 bultos. Falta…», con el bulto exacto y el motivo. Se avisa a Almacén, a quien hizo el pedido y a su vendedor, y se ve marcado en Agencias › Tramitados y en la pestaña «Entrega» del pedido. La prueba de entrega dice además qué bultos se leyeron («Leídos 6 de 7»).',
     NULL),
    ('Mejorado', N'Modos de entrega: solo se ofrecen los que hacen algo distinto',
     N'Nesto compara lo que haría cada modo de entrega con ese pedido en concreto y ya no ofrece dos que acabarían haciendo lo mismo. Por ejemplo, si todo lo que falta viene de una tienda, «Tras reponer de tiendas» no aparece porque es igual que «Todo junto»; y sí aparece cuando la tienda solo puede traer una parte. El modo que no se ofrece indica a cuál equivale.',
     NULL),
    ('Mejorado', N'Fecha de entrega: lo que ya viene de camino cuenta antes que lo de otra tienda',
     N'Si una reposición ya terminada trae a Algete lo que le falta a un pedido, la fecha y los avisos cuentan con ella («viene de camino en la reposición 81463 de Alcobendas») en vez de decir que tiene que venir de otra tienda. El correo de promesas en riesgo solo lleva lo recién recalculado.',
     NULL),
    ('Mejorado', N'Correo del pedido: se explica cuando manda la fecha de entrega',
     N'Si un pedido podría salir antes pero tiene puesta una fecha de entrega posterior, la explicación del correo lo dice: «No sale antes del jueves 26/11/2026, que es la fecha de entrega del pedido».',
     NULL),
    ('Mejorado', N'Reparto propio: el reembolso se cobra entero o con autorización',
     N'Si el repartidor va a cobrar en efectivo menos de lo que marca el reembolso, tiene que pedir autorización, como cuando entrega sin cobrar. En la ventana de autorización verá cuánto cobra y cuánto falta.',
     N'Administración,Dirección'),
    ('Mejorado', N'Devueltos: quién decide qué se hace con un paquete',
     N'«Reenviar» y «Abonar» de un envío devuelto los decide quien hizo el pedido, su vendedor, Almacén o Dirección. «Ya está aquí» lo marca Almacén.',
     NULL),
    ('Mejorado', N'Novedades: copiar los datos de una sugerencia',
     N'En la ventana de Novedades se ve el número de cada sugerencia o incidencia y, con el botón derecho, «Copiar datos» copia su número, quién la puso y el texto.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo, Perfiles FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
