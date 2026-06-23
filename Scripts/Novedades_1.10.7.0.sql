-- Nesto#372: novedades para la ventana "Qué hay nuevo" de la versión 1.10.7.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.7.0). Ambito: Nesto / NestoAPI. Publicada = 1.
-- Ejecutar contra la BD NV al publicar Nesto 1.10.7.0.

DECLARE @version VARCHAR(23) = '1.10.7.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Nueva agencia de transporte: Innovatrans',
 'Ya se pueden tramitar e imprimir envíos con la agencia Innovatrans (incluido Portugal) desde la ventana de Agencias, con su enlace de seguimiento.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Ventana de Mantenimiento de Agencias',
 'Permite dar de alta y editar las agencias de transporte y su recargo de combustible (fuel) sin tener que actualizar el programa.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Agrupar pedidos por P.O. en una sola factura, aunque sean de direcciones distintas',
 'Un mismo pedido del cliente (P.O.) con entregas a varias direcciones se puede facturar junto en una sola factura, detallando a qué dirección fue cada albarán.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Nuevo', 'Ofertas combinadas: permitir cantidad menor por línea',
 'Nueva casilla en las ofertas combinadas para admitir una cantidad menor de la habitual en una línea concreta.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Elección de la agencia más barata teniendo en cuenta el combustible',
 'Al crear un envío, el sistema compara las agencias con sus tarifas actualizadas (GLS 2026 incluida) y el recargo de combustible de cada una para sugerir la más económica.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Facturas y pedidos en PDF más claros',
 'El descuento por pronto pago se muestra aparte (en el pie), separado del descuento comercial de las líneas. En facturas con varias direcciones de entrega se indica a qué dirección fue cada albarán.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'El P.O. del pedido aparece en el correo',
 'El correo de creación o modificación de un pedido incluye el P.O. (referencia del cliente) cuando el pedido lo tiene.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Cambiar la cuenta de cobro de un pedido ya en albarán',
 'Se puede cambiar la cuenta de cobro (CCC) de la cabecera de un pedido aunque ya esté en albarán.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Aviso claro al asignar la cuenta de cobro por Recibo',
 'Al asignar la cuenta de cobro mediante Recibo se muestra un mensaje claro de lo que ha ocurrido.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Terminal de TPV configurable por usuario',
 'El cobro con tarjeta usa el terminal de TPV configurado para cada usuario, y se ha corregido un terminal que estaba mal asignado.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Se recuerda "Recoger producto" en los borradores de plantilla',
 'Al guardar un borrador de plantilla de venta se conserva la marca de "Recoger producto".', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Unir pedidos ya no falla por una línea correcta con la tarifa subida después',
 'Al unir pedidos ya no se bloquea por una línea que no se está tocando y cuyo precio era el correcto cuando se metió, aunque la tarifa de ese producto haya subido más tarde.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Al unir pedidos se muestra el error real',
 'Cuando unir pedidos no es posible, se indica el motivo real en vez de un mensaje confuso.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Cierre inesperado al actualizar clientes con probabilidad de venta',
 'Corregido un cierre del programa al actualizar clientes que tenían informada la probabilidad de venta.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Error al copiar al portapapeles sin un pedido cargado',
 'Corregido un error al usar "Copiar al portapapeles" en el detalle de pedido cuando no había ningún pedido abierto.', 'Nesto', SUSER_SNAME());
GO

-- Comprobación
-- SELECT * FROM dbo.Novedades WHERE [Version] = '1.10.7.0' ORDER BY Id;
