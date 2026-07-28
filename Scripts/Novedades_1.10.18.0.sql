-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.18.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.18.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.18.0.
-- Se omiten a propósito (internas o sin efecto visible todavía para el usuario):
--  - Fases 2 y 3 del polimorfismo de agencias (#258): refactor interno, mismo comportamiento.
--  - Sugerencia de país para NIF-IVA intracomunitarios (#354): el servidor ya la calcula,
--    pero la pantalla de NIF incorrectos aún no la muestra.
--  - Tabla y job de seguimiento de las facturas subidas a Amazon (#366): infraestructura.

DECLARE @version VARCHAR(23) = '1.10.18.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Canales Externos: subir la factura de un pedido de Amazon con un clic',
 'En Canales Externos → Pedidos (Amazon), cada pedido tiene ahora un botón "Subir Factura": factura el pedido de Nesto (si no lo estaba) y sube el PDF de la factura a Amazon. También hay un botón "Subir facturas pendientes" que lo hace en lote para todos los pedidos cargados, y una columna Factura que muestra el estado (pendiente ⏳, aceptada ✔, rechazada ✖). Las ventas sin datos del comprador (factura simplificada) no se suben: la columna lo indica y el lote las salta.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Alta de clientes: direcciones de otros países',
 'Al crear o modificar un cliente ya se pueden poner direcciones de fuera de España. Junto a la dirección hay un selector de país (por defecto, el país fiscal del cliente, pero se puede cambiar: un cliente con datos fiscales de Alemania puede vivir en Francia). El buscador de direcciones de Google ofrece direcciones del país elegido y la ficha se crea con la población y el código postal correctos, sin colar "España" en la dirección ni dar el error de código postal inexistente.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Ampliar un pedido de compra al stock máximo daba error de tiempo de espera',
 'Con proveedores de muchos productos, el botón de ampliar el pedido de compra al stock máximo tardaba tanto que daba "Tiempo de espera agotado" y había que reintentar varias veces. Se ha rehecho el cálculo y ahora responde en un par de segundos.', 'Nesto', SUSER_SNAME());
