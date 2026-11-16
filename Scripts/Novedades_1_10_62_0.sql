/*
    Novedades de la versión 1.10.62.0 (16/11/2026): API + Nesto.

    ORDEN:
      1) Scripts (sa) ANTES de publicar la API: Issue866_SugerenciasContactoOfertas.sql, Issue868_AvisosFichaFotoMozo.sql,
         Issue870_EnviosAgenciaHaVuelto.sql, Issue867_ReposicionesCalendarioExcepciones.sql (Issue865 ya lanzado hoy).
      2) Publicar la API (master). 3) Publicar Nesto (1.10.62.0). 4) ESTE script. 5) Ariadna 2.37 al canal interno y su script.

    NO van aquí porque son SUGERENCIAS/INCIDENCIAS que se contestan en su propio hilo al publicar:
      1257 (manifiesto con lo pendiente de días anteriores), 1275 (reposiciones del inventario), 1276 (provincia de Italia),
      1279 (2+1 de aceites de la feria), 1280 (reenvíos), 1284 (furgoneta: descargar deja la parada sin cargar).

    Se omiten a propósito (nada perceptible o solo para medir):
      - Embudo: los clientes de origen AMAZON y WEB entran en el bloque diario.
      - Datos de las sugerencias de llamadas (qué productos en oferta las justifican y si hubo pedido).
      - Reenvío temporal a Compras de los correos de agencia (es para revisar; se apagará).

    Ejecutar contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.62.0';
DECLARE @fecha DATE = '2026-11-16';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Calendario de reposiciones: quitar o añadir un día concreto',
     N'En la ventana del calendario de reposiciones hay una sección nueva, «Excepciones: días concretos». Con «Quitar un día» se elige una ruta y una fecha y ese día no habrá reposición (por ejemplo, la víspera de un inventario); con «Añadir día extra» se programa una reposición un día que normalmente no la tiene. Hay que poner el motivo. Un día quitado no se rellena ni avisa de fuera de plazo, y las fechas de entrega de los pedidos lo tienen en cuenta. Una excepción ya no se puede cambiar una vez pasada la hora de cierre de esa reposición. Con «Borrar excepción» el día vuelve a ser como dice el calendario.',
     NULL),
    ('Nuevo', N'Ficha del producto: cuándo llega la próxima entrada',
     N'En la ficha del producto sale una línea «Próxima entrada» con la fecha en que se espera recibirlo en Algete. Si hay pedido al proveedor, la de ese pedido, con lo segura que es (por ejemplo, «prácticamente segura: ya enviado por UPS» cuando ha llegado el correo de la agencia). Si no hay pedido, una fecha orientativa: cuándo llegaría si se pidiera hoy y cuándo toca el próximo pedido habitual a ese proveedor. Al pasar el ratón por encima se ven las cuentas: proveedor, cada cuánto se le pide y cuánto suele tardar.',
     NULL),
    ('Mejorado', N'Un reenvío no sale hasta que vuelve la caja',
     N'Al pulsar «Reenviar» en un envío devuelto o con incidencia, el envío nuevo queda en Pendientes y no se puede imprimir su etiqueta ni tramitar, tampoco con «Tramitar todos», hasta que la caja original esté de vuelta en el almacén. Cuando llega, se lee su etiqueta en Ariadna («Ha vuelto un paquete») o se pulsa el botón nuevo «Ya está aquí» en Agencias › Devueltos, donde la columna «Caja» dice si se está esperando o cuándo volvió. Si se intenta imprimir antes, Nesto pregunta si la caja ya está aquí. A quien pidió el reenvío le llega un aviso cuando vuelve la caja.',
     NULL),
    ('Mejorado', N'Copiar factura: no se puede abonar a quien no la compró ni abonar dos veces',
     N'Al copiar una factura invirtiendo las cantidades (un abono), Nesto ya no deja abonarla a un cliente distinto del que la compró: para pasar una venta a otro cliente está «Crear abono y cargo». Tampoco deja abonar otra vez lo que ya está abonado (dice en qué abono). Y al facturar una devolución hecha a mano, si el cliente no ha comprado ese producto o ya lo ha devuelto entero, no se factura y dice por qué. Así no quedan abonos que no corresponden a ninguna venta.',
     NULL),
    ('Mejorado', N'Clientes para contactar: las ofertas del momento y el porqué',
     N'En la lista de clientes para contactar suben, como mucho cinco al día, los clientes que compran habitualmente un producto que hoy está en oferta y al que ya le toca volver a comprarlo. El motivo lo explica, por ejemplo: «Compra el 45929 cada 45 días y lleva 64 sin comprarlo; hoy tiene un 20 % de descuento (hasta el 30/11)». Además, en los clientes que salen por probabilidad, el motivo añade las dos razones que más pesan («Sobre todo porque…»).',
     NULL),
    ('Mejorado', N'Correo de proveedores con la fecha pasada: la fecha se recalcula sola',
     N'En el correo diario de pedidos de compra con la fecha de recepción pasada, Nesto pone una fecha nueva a esas líneas y explica de dónde sale: del correo de seguimiento de la agencia si lo hay, o de lo que suele tardar ese proveedor cuando ya va con retraso. Si una línea vence por segunda vez, el correo pide llamar al proveedor. En «Clientes esperando», lo que ya viene de otra tienda en una reposición sale aparte con su fecha de llegada.',
     NULL),
    ('Mejorado', N'Aviso de dato mal en la ficha: con la foto del producto que tiene el mozo',
     N'Cuando desde el almacén se avisa de un dato mal en la ficha de un producto, el correo puede llevar, además de la foto de la tienda que vio el mozo, una foto hecha por él del producto que tiene en la mano («Lo que tiene el mozo en la mano»). Así se ve mejor qué está mal.',
     NULL),
    ('Corregido', N'Alta de clientes de Italia con provincias largas',
     N'Al dar de alta un cliente de Italia, el buscador de direcciones devolvía provincias como «Città Metropolitana di Roma Capitale», demasiado largas, y el alta fallaba. Ahora queda solo el nombre («Roma»).',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
