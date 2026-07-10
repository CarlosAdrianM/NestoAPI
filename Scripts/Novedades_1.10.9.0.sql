-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.9.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.9.0). Ambito: Nesto / NestoAPI. Publicada = 1.
-- Ejecutar contra la BD NV al publicar Nesto 1.10.9.0.
-- OJO: las novedades de la 1.10.8.5 (que no llegó a publicarse) ya están insertadas y
-- saltarán también en este popup: no duplicarlas aquí.
-- Se omiten a propósito (el usuario no las percibe todavía):
--  - Ofertas combinadas por filtro de familia/nombre (NestoAPI#282): se anunciará al dar
--    de alta la primera oferta real (Lisap) y tener la UI de mantenimiento.
--  - Extracto contable de Cajas con motor QuestPDF (Nesto#340): va detrás del parámetro
--    MotorPdfExtractoContable, se anunciará al activarlo.
--  - Flag de regalos Ganavisiones en el GET de pedidos (NestoAPI#279): es para Nesto#397.
--  - Migración interna a IClienteApiFactory (Nesto#369) y motivos en el aviso del poll
--    de agencias (NestoAPI#266): internos.

DECLARE @version VARCHAR(23) = '1.10.9.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Productos que comisionan por otro grupo según quién vende',
 'En la ficha del producto (pestaña Comisiones, visible para Compras) se puede indicar un grupo alternativo por el que puede comisionar un producto. Por ejemplo, unos guantes de cosmética que también vende el vendedor de peluquería: si el pedido lo mete el vendedor de ese grupo del cliente, la línea comisiona por su grupo. Un grupo con vendedor propio no pierde nunca la línea; solo se puede tomar de un grupo sin vendedor o con el vendedor genérico.', 'Nesto', SUSER_SNAME()),
-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Ya no se pierden pedidos al guardar dos a la vez',
 'Si dos personas guardaban un pedido en el mismo instante, ambos podían recibir el mismo número y el segundo se perdía con un error. Ahora cada pedido recibe siempre un número distinto.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Mover seguimientos entre clientes ya no da error',
 'Al copiar seguimientos a otro cliente eliminando el origen (mover), fallaba si algún seguimiento estaba marcado como leído. Ahora el movimiento funciona; en el cliente de destino aparecen como no leídos.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Contabilizar ya no falla cuando coinciden dos personas',
 'Al contabilizar cobros o cierres a la vez que otro compañero, a veces saltaba un error de bloqueo y había que repetir la operación. Ahora se reintenta automáticamente.', 'NestoAPI', SUSER_SNAME()),
-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Unir pedidos: el error muestra la causa real',
 'Cuando falla la unión de dos pedidos, el mensaje ahora indica la causa concreta en vez del genérico "Se anuló la transacción".', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Facturación: aviso claro si la ruta del pedido no existe',
 'Al facturar un pedido cuyo cliente tiene una ruta que no existe, ahora se muestra un aviso claro indicando la ruta, en vez de un error interno que además dejaba la conexión en mal estado.', 'NestoAPI', SUSER_SNAME());
GO

-- Comprobación
-- SELECT * FROM dbo.Novedades WHERE [Version] = '1.10.9.0' ORDER BY Id;
