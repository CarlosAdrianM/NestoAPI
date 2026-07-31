-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.18.2
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.18.2). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.18.2.
-- Se omiten a propósito (internas o sin efecto visible todavía para el usuario):
--  - PutPedidoVenta devuelve 404 en vez de 500 si el pedido no existe (#377): interno.
--  - Endpoint api/Clientes/PorTelefono y búsqueda de cliente de Amazon sin EF (Nesto#340): interno.
--  - DeducirPais reconoce los sufijos "ae"/"sa" en el cuadre de facturas: interno.
--  - Cuentas contables 555.87/555.88 de Amazon.sa: infraestructura contable.

DECLARE @version VARCHAR(23) = '1.10.18.2';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Canales Externos: activado el marketplace Amazon Arabia Saudí (amazon.sa)',
 'Los pedidos de amazon.sa ya aparecen en Canales Externos → Pedidos (Amazon) y sus facturas se reconocen y liquidan como las del resto de marketplaces. El primer pedido de Arabia Saudí no salía en el listado porque el marketplace no estaba dado de alta.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'La carga de pedidos de Amazon es mucho más rápida',
 'Al cargar los pedidos de Amazon en Canales Externos ya no se consulta la dirección de los pedidos que ya están registrados en Nesto, y las direcciones ya descargadas se reutilizan al recargar. Antes se pedía la dirección de todos los pedidos uno a uno a Amazon (que limita mucho esas consultas) y la pantalla tardaba minutos.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Facturar y subir a Amazon ya funciona con pedidos sin facturar',
 'En Canales Externos → Pedidos (Amazon), el botón de facturar y subir la factura daba "No hay líneas para facturar" si el pedido aún no estaba facturado (típico en pedidos FBA, que no pasan por picking). Ahora crea el albarán y la factura automáticamente antes de subirla.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'No se podía crear un cliente con dirección extranjera',
 'Al crear (o modificar) un cliente con dirección fuera de España, si su código postal no existía todavía en Nesto el alta fallaba con un error técnico y había que crear el código postal a mano. Ahora el código postal extranjero se da de alta automáticamente en la ruta de fuera de Madrid.', 'NestoAPI', SUSER_SNAME());
