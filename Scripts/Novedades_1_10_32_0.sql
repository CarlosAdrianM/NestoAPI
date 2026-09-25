/*
    Novedades de la versión 1.10.32.0 (25/09/2026).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.32.0). SÍ se sube la tercera cifra: se estrena el modo de
    facturación del pedido (selector en el detalle y en la plantilla) y cambiar el cliente de un pedido.

    ORDEN:
      1) YA EJECUTADOS por Carlos el 25/09: Issue542_ModoFacturacion.sql, Issue522_VerifactuIncidencia.sql,
         Issue544_AvisosFacturasVencidas.sql, Issue532_RecordatorioReposicion.sql (Sombra) y el trabajo
         nocturno Issue542_IndicePedidoOrigen_JobNocturno.sql (índice, 26/09 02:30).
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce 1.10.32.0 (Clean + Rebuild).
      4) ESTE script. Idempotente: compara por Versión + Título.

    SE OMITE A PROPÓSITO (el usuario de Nesto no lo nota o aún está apagado):
      - #542 corte 2: la nota de entrega automática (interruptor NotaEntregaAutomatica, apagado).
      - #544 y #534: aviso de facturas vencidas a clientes (sigue en sombra, solo le llega a administración).
      - #532: recordatorio de reposición (en sombra, solo le llega a Carlos).
      - #522: Verifactu, incidencias y documento provisional (Verifactu sigue en sombra).
      - #494: recogidas y retornos de CTT (interruptor CTTRetornosActivos, apagado).
      - #538: cabeceras vacías (script de limpieza sin ejecutar) y el hueco de «copiar factura».
      - #543: referencia en el correo de pedido de la tienda y forma de venta de El Edén (no es de Nesto).
      - Nesto#490 (4C.2): diálogos de CarteraPagos, Inventario, Ganavisiones y Rapports (sin cambio visible).
      - Tests, issues, refactorizaciones.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.32.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-25';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Cada pedido dice cómo se factura: por entregas, al completarlo o todo ahora',
     N'La casilla «Mantener junto» se convierte en un desplegable «Facturación», en el detalle del pedido y en la plantilla, con tres opciones: «Por entregas» (cada albarán lleva su factura), «Al completar el pedido» (una sola factura cuando esté todo servido, lo que antes era marcar Mantener junto) y la nueva «Todo ahora, lo pendiente se entrega después»: el pedido se factura entero con la primera entrega y lo que no hay en ese momento se queda para entregarlo más tarde con una nota de entrega, el «producto en carpeta» de siempre. Cómo se entrega lo pendiente lo sigue diciendo el modo de entrega (todo junto, según vaya entrando…). Si los plazos de pago no son los de la ficha del cliente, «Por entregas» no se puede elegir, igual que antes no se podía desmarcar Mantener junto. En las líneas veréis la columna «A recoger» con las unidades facturadas que quedan por entregar.'),
    ('Nuevo', N'Cambiar el cliente de un pedido que aún no ha salido',
     N'En el detalle del pedido hay un botón «Cambiar cliente…» para pasar un pedido a otro cliente (por ejemplo, a una ficha nueva que se acaba de crear). Solo se puede mientras el pedido no tenga picking, albarán ni factura. Al cambiarlo se ponen las condiciones del cliente nuevo (forma y plazos de pago, cuenta, IVA, vendedor, descuentos y precios) y Nesto os enseña qué ha cambiado. Las líneas con oferta, regalo o descuento puesto a mano se conservan y se vuelven a validar.'),
    ('Nuevo', N'Buscar un cliente por el número de una factura',
     N'En el buscador de clientes podéis escribir el número de una factura (por ejemplo, NV2615541) y os sale el cliente de esa factura. Es útil cuando llega una transferencia que solo trae el número de factura en el concepto.'),
    ('Nuevo', N'CTT urgente 24 h en Agencias',
     N'Al hacer un envío por CTT, en el desplegable de servicio podéis elegir «CTT 24h (urgente)» en lugar del 48 h de siempre. Solo sale urgente si lo elegís a mano: cuesta más, así que usadlo cuando el cliente lo necesite de verdad.'),
    ('Mejorado', N'Si un pedido no sale, lo que ya tiene stock deja de estar pendiente',
     N'Cuando un pedido no puede salir todavía (por ejemplo, uno «todo junto» al que le falta un producto), las líneas que ya tienen su mercancía apartada pasan a «en curso» y solo se quedan en pendiente las que de verdad faltan. Así se ve de un vistazo qué es lo que sigue faltando. Solo cuenta la mercancía que no necesita un pedido anterior, así que una línea que pasa a «en curso» no vuelve a pendiente porque llegue otro pedido.'),
    ('Mejorado', N'Cambiar los días que abre un cliente con pedidos ya en preparación',
     N'Si en la ficha del cliente cerráis un día (por ejemplo, «cierra los lunes») y ese cliente tiene pedidos con picking, Nesto os lo dice y os pregunta si avisamos a almacén. Si decís que sí, se guarda la ficha y almacén recibe un correo con los pedidos afectados; si decís que no, el cambio de días no se guarda.'),
    ('Mejorado', N'Los regalos miran el stock del almacén del pedido',
     N'Al meter un regalo en un pedido que se sirve según vaya entrando, Nesto comprueba que haya stock en el almacén de la línea (antes sumaba el de todas las tiendas y lo que estaba pedido al proveedor, y aceptaba regalos que luego no se podían servir).'),
    ('Corregido', N'Conciliación bancaria: «Puntear seleccionados» ya no se queda parado',
     N'En la conciliación de bancos, después de puntear pasaban varios segundos hasta que se podía seguir. Era el botón «Contabilizar apunte», que comprobaba si había una regla de contabilización con la ventana parada. Ahora lo comprueba en segundo plano y se puede seguir punteando al momento; el botón «Contabilizar apunte» se queda gris un instante mientras lo calcula.'),
    ('Corregido', N'La cuenta del pedido vuelve a verse en la pestaña de pago',
     N'En el detalle del pedido, la cuenta bancaria aparecía en blanco aunque debajo se leía a qué cuenta se iba a cargar. Ya se ve seleccionada, como siempre.'),
    ('Corregido', N'Imprimir un pedido sin líneas dice por qué no se puede',
     N'Si se intentaba imprimir un pedido que no tiene ninguna línea, salía un error sin explicación. Ahora Nesto dice que el pedido no tiene líneas y no hay nada que imprimir.'),
    ('Mejorado', N'Novedades dice la fecha de cada versión',
     N'En la ventana de Novedades, junto al número de cada versión aparece el día en que se publicó (por ejemplo, «Versión 1.10.28.2 · 17/09/26»), también en los resultados del buscador. Así podéis saber si algo que os pasó un día concreto ya estaba arreglado o no.'),
    ('Corregido', N'Confirmar un envío en Amazon vuelve a ser inmediato',
     N'Desde la versión anterior, confirmar un envío de Amazon tardaba cerca de un minuto porque Nesto esperaba a que Amazon lo procesara. Ahora se confirma al momento y la comprobación sigue por detrás: si Amazon no acepta algún pedido, os llega un aviso con el motivo para revisarlo en Seller Central.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 12 filas, sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
