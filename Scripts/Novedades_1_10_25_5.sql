/*
    Novedades de la versión 1.10.25.5 (07/09/2026).

    Solo sube la última cifra: son correcciones. Las dos que se cuentan salen de casos reales de
    esta semana — un compañero que se quedó sin ventana de Agencias al elegir una agencia vieja, y
    los Bizum de la tienda que llevaban desde el 27/08 entrando sin su prepago.

    SE OMITE TODO LO DEMÁS A PROPÓSITO (nada de esto lo nota el usuario de Nesto):
      - El envío pendiente del pedido y los plazos de pago ahora se piden a la API en vez de leerse
        con Entity Framework (Nesto#340 A3, NestoAPI#459): se comporta igual.
      - Los GET de plazos y formas de pago pasan a exigir credencial, pero DETRÁS DE UN
        INTERRUPTOR QUE NACE APAGADO (NestoAPI#459): hasta que se encienda no cambia nada.
      - Listado de envíos que la agencia no ha dado por entregados (NestoAPI#173): de momento solo
        el endpoint; la pestaña en Nesto es otra issue.
      - Un pedido que se validaba mientras otro usuario validaba el suyo podía caerse con
        "Colección modificada" (NestoAPI#461), y copiar los datos del contacto principal fallaba al
        crear un contacto sin banco (NestoAPI#462): pasaba muy de tarde en tarde y ya no pasa.
      - Se retiró el cobro directo por MIT del pedido de la app de clientes (NestoAPI#181) y se
        arregló un error del bus de sincronización con Odoo (NestoAPI#463): interno.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.25.5', '2026-09-07', 'Corregido', 'La ventana de Agencias ya no se cierra al elegir una agencia antigua',
 'Al seleccionar en el desplegable una agencia que ya no usamos (Sending, OnTime, Glovo o CTT), la ventana de Agencias se cerraba de golpe. Ahora el desplegable solo ofrece las agencias con las que se puede trabajar de verdad, y si al abrir un envío antiguo aparece una de las otras, se puede consultar sin que se cierre nada. Los envíos históricos se siguen viendo igual.', 'Nesto', 1, 'sa'),

('1.10.25.5', '2026-09-07', 'Corregido', 'Los pedidos pagados con Bizum en la tienda vuelven a traer su prepago',
 'Desde que se actualizó la tienda online, los pedidos pagados con Bizum entraban como transferencia y sin el prepago, así que el cobro no quedaba contabilizado. La tienda había cambiado el nombre con el que identifica ese pago y Nesto ya no lo reconocía. Ya vuelve a entrar como pago con tarjeta y con su prepago en la cuenta de siempre. Los pedidos del 27 de agosto al 1 de septiembre que se quedaron sin prepago hay que repasarlos a mano.', 'Nesto', 1, 'sa');

SELECT Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.25.5';
