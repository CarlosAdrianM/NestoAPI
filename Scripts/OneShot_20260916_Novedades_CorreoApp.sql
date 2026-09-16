-- Novedades 1.10.28.0: la entrada del correo de confirmación decía «tienda online o la app». Solo es la
-- app de clientes (POST api/Pedidos/Cliente); los pedidos de la tienda de PrestaShop entran por
-- CanalesExternos y ya reciben el correo de la propia tienda. Ejecutar en SSMS contra NV.
UPDATE dbo.Novedades
SET Titulo = N'El cliente que compra desde la app recibe un correo de confirmación',
    Descripcion = N'Hasta ahora, al confirmar un pedido en la app de clientes, el carrito se vaciaba y el cliente no recibía nada (en la tienda online de PrestaShop ya lo mandaba la propia tienda; eso no cambia). Ahora le llega "Gracias por tu pedido nº X" con las líneas, la base, los portes si los hay, el IVA y el total, la situación del pago (cobrado con tarjeta, pendiente en la pasarela, o con sus condiciones habituales) y el aviso de que le avisaremos cuando salga. Sale desde tiendaonline@ y va al correo con el que el cliente ha iniciado sesión en la app. El correo interno de "Pedido nuevo" sigue llegando a las mismas personas.'
WHERE Version = '1.10.28.0' AND Titulo LIKE N'El cliente que compra desde la tienda online%';

SELECT Titulo FROM dbo.Novedades WHERE Version = '1.10.28.0' AND Titulo LIKE N'El cliente que compra%';
