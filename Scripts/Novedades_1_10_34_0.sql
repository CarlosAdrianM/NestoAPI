/*
    Novedades de la versión 1.10.34.0 (29/09/2026).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.34.0).

    ORDEN:
      1) ANTES de publicar la API (como sa, en NV): Issue522_VerifactuEnviadaProvisional.sql,
         Issue558_IncidenciasNovedades.sql, Issue564_OfertasPermitidasSubGrupo.sql, Issue392_DeclararSimplificada.sql.
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce 1.10.34.0 (Clean + Rebuild).
      4) DESPUÉS: Issue551_DescartarCV2600484y485.sql, Issue564_DenegarDesechablesGenericos.sql y ESTE script
         (idempotente: compara por Versión + Título).
      5) PUT api/Novedades/Sugerencias/417 con Version = 1.10.34.0 (enlace de pago desde la ficha del cliente,
         sugerencia de Paloma: NO se repite aquí) y avisar a Paloma.

    SE OMITE A PROPÓSITO (el usuario de Nesto no lo nota o aún está apagado):
      - #547: precios medios incrementales (apagado; tope por producto y facturas de compra deshechas).
      - #563: sugerencia del modo de servicio por causas (sombra apagada).
      - #553: propuesta de traspaso (solo lectura en la API).
      - #522: reenvío de la definitiva tras el justificante provisional (Verifactu aún no obligatorio).
      - #392 punto 1: las rectificativas de las simplificadas van como R5 (dato fiscal, invisible).
      - #551: CV2600484/485 descartadas; #554: seguridad de DiariosProductos; #542: vuelta atrás del índice.
      - Nesto#490 (4C.2): diálogos de Agencias, Clientes, Remesas, Cajas, PedidoVenta... (sin cambio visible).
      - Tests, issues, refactorizaciones.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.34.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-29';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'«Algo no funciona»: avisar de un fallo desde Novedades',
     N'Junto a «Sugerir una mejora» hay un botón nuevo, «Algo no funciona», para contarnos un fallo con el mismo formulario (texto y, si queréis, una captura). Nesto añade solo la versión, la pantalla en la que estáis y los últimos errores que haya registrado, así que basta con explicar qué ha pasado. Os contestaremos aquí mismo.'),
    ('Nuevo', N'Más avisos en la campana (y en NestoApp)',
     N'Tres avisos que hasta ahora solo llegaban por correo llegan también a la campana de Nesto y como notificación a NestoApp: cuando un pedido con «Avisar con importe» coge picking (al pulsar el aviso se abre el pedido), cuando al crear un pedido el NIF del cliente es incorrecto para Verifactu, y cuando un cliente paga un enlace de pago que habéis creado vosotros.'),
    ('Nuevo', N'Informar a los vendedores de una oferta nueva',
     N'Al guardar una oferta combinada, por familia o escalonada, Nesto pregunta «¿Desea informar a los vendedores de la oferta que se acaba de autorizar?». Si contestáis que sí, a los vendedores les llega un aviso en NestoApp que abre la oferta. Si contestáis que no, la oferta se guarda igual y no se avisa a nadie.'),
    ('Nuevo', N'Administración: consulta de enlaces de pago',
     N'En Contabilidad, grupo Cobros, el botón «Enlaces de pago» abre una consulta de todos los enlaces de NestoPago: quién lo creó y cuándo, para qué cliente, por qué importe, en qué estado está y a qué correo o móvil se envió. Se puede buscar por fechas, cliente, usuario, estado o por el identificador del enlace. Solo para Administración y Dirección.'),
    ('Nuevo', N'Facturas Verifactu: declarar como simplificada',
     N'En la ventana «Facturas Verifactu», una factura de hasta 400 € cuyo NIF es incorrecto y no hay forma de conseguir el bueno se puede declarar como factura simplificada (sin destinatario), indicando el motivo. Sus rectificativas se declaran igual. Solo para Administración y Dirección.'),
    ('Mejorado', N'«Servir»: sin ninguna unidad en stock, todo junto',
     N'Si ninguna línea del pedido tiene ni una unidad disponible en el almacén, el pedido sale «Todo junto» cuando llegue la mercancía: ya no se puede elegir «Ahora lo que hay», que solo habría mandado los portes. Si alguna línea tiene parte del stock (por ejemplo, 1 de 2), se puede seguir sirviendo lo que hay.'),
    ('Mejorado', N'Confirmar un envío a nuestra propia dirección',
     N'Si al registrar un envío la dirección de entrega es la nuestra (Río Tiétar, 11, Algete), Nesto pide confirmarlo antes de seguir, para que no salga un paquete hacia nosotros mismos por error.'),
    ('Mejorado', N'Avisos de facturas vencidas: esperan si el cliente acaba de pagar',
     N'El correo de facturas vencidas no se manda si el cliente ha hecho un pago en los últimos 7 días, y cuando parte del recibo ya está pagada lo dice («ya hemos recibido X €»).'),
    ('Mejorado', N'Remesas: se retienen los recibos de envíos con retorno',
     N'En la remesa, los recibos de facturas cuyo envío lleva retorno esperan hasta saber si habrá rectificativa (retorno recibido y unos días de margen, con un tope). Se pueden forzar a mano si hace falta.'),
    ('Mejorado', N'Ofertas por familia: «Denegar» se respeta y se puede limitar a un subgrupo',
     N'En las ofertas por familia, una fila marcada como «Denegar» prohíbe de verdad esa oferta (y sus múltiplos) en los productos a los que aplica; antes no se tenía en cuenta. Además se puede limitar a un subgrupo: por ejemplo, el 6+1 de Genéricos no vale para los desechables, pero otra oferta autorizada (10+1) sí.'),
    ('Corregido', N'El selector de cuenta contable funciona con teclado y ratón',
     N'En los prepagos y en las líneas de cuenta del pedido, la cuenta contable se puede escribir tras pulsar Tab, elegir con el ratón en la lista, y «572.13» se completa como 57200013.'),
    ('Corregido', N'El picking ya no saca pedidos solo con los portes',
     N'Un pedido que se sirve por partes ya no coge picking cuando ninguno de sus productos tiene stock: antes salía una entrega con solo los portes.'),
    ('Corregido', N'Correo de presupuesto: las líneas sin stock ya no salen en verde',
     N'En el correo de un presupuesto, el color de cada línea tiene en cuenta la cantidad pedida: las que no tienen stock suficiente ya no salen en verde.'),
    ('Corregido', N'Conciliación: reglas que pedían un apunte que no usaban',
     N'Cuatro reglas de la conciliación bancaria (entre ellas la de adelantos de nómina) exigían tener seleccionado un apunte de contabilidad aunque no lo usaban. Ya no hace falta.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 14 filas, sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
