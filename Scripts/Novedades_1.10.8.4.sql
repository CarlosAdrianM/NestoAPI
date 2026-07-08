-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.8.4
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.8.4). Ambito: Nesto / NestoAPI. Publicada = 1.
-- Ejecutar contra la BD NV al publicar Nesto 1.10.8.4.
-- Release de correcciones (sin módulos nuevos): solo se sube la 4ª cifra.
-- NOTA: se omite a propósito la corrección de bultos de Innovatrans (NestoAPI#270): el
-- cambio está desplegado pero PENDIENTE de verificación en vivo, así que no se anuncia
-- todavía como corregido para no crear expectativas si hubiera que ajustarlo.

DECLARE @version VARCHAR(23) = '1.10.8.4';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Guardar la ficha de cliente ya no cierra la aplicación',
 'En algunos casos, al pulsar Guardar en la ficha de un cliente, la aplicación se cerraba inesperadamente. Corregido.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Coste de transporte de la tienda online al destino real',
 'El coste de transporte de los envíos pendientes de la tienda online se calcula ahora con el código postal del destino real del envío, no con la dirección de la ficha del cliente. Así las zonas y los importes de portes salen correctos.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Unidad de medida en la orden de compra',
 'En la orden de compra a proveedores (PDF y Excel), la unidad de medida (ml, g, uds...) aparece ahora junto al Tamaño del producto y no junto a la Cantidad pedida. Además, en el Excel la Cantidad queda como número (se puede sumar).', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Abono con los datos fiscales de la factura original',
 'Al anular una factura con un abono + cargo, el abono se emite con los datos fiscales (nombre y dirección) que constaban en la factura original, no con los de la ficha actual del cliente. El cargo mantiene los datos correctos actuales.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Aviso claro al facturar si la ruta no existe',
 'Si se intenta facturar un pedido cuyo cliente tiene asignada una ruta que ya no existe, ahora se avisa con un mensaje claro (para corregir la ruta) en vez de dar un error confuso de base de datos.', 'NestoAPI', SUSER_SNAME());
GO

-- Comprobación
-- SELECT * FROM dbo.Novedades WHERE [Version] = '1.10.8.4' ORDER BY Id;
