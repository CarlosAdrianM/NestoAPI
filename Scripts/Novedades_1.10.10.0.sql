-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.10.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.10.0). Ambito: Nesto / NestoAPI.
-- Ejecutar contra la BD NV al publicar Nesto 1.10.10.0.
-- Se omiten a propósito (el usuario no las percibe todavía):
--  - Picking y Packing en PDF con QuestPDF (Nesto#340): van detrás del parámetro por
--    usuario MotorPdfPicking, se anunciarán al extenderlo a todos.
--  - [Authorize] en controllers (NestoAPI#186/#189), CONTEXT_INFO (#286), contador de
--    asientos en ContadoresEmpresa (#287): internos (la mejora percibida va en "menos
--    bloqueos al contabilizar").
--  - GET ParaPlantilla (NestoAPI#279/Nesto#397 backend): lo percibe vía "Modificar con plantilla".

DECLARE @version VARCHAR(23) = '1.10.10.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Modificar un pedido desde la plantilla de ventas',
 'En la lista de pedidos hay un botón nuevo que carga el pedido en la plantilla de ventas para modificarlo como si se estuviera creando: añadir o quitar productos, cambiar cantidades y volver a guardar sobre el mismo número de pedido. Las líneas que ya están preparadas en el almacén no se pueden modificar ni quitar, para que lo enviado coincida siempre con lo facturado.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Ofertas combinadas más flexibles: filtros y "2+1 entre referencias"',
 'En el mantenimiento de ofertas combinadas las líneas de la oferta ya no tienen que ser referencias concretas: pueden ser filtros por familia, por principio del nombre o por subgrupo (con un desplegable). Además, la casilla "Regalo menor imp." permite ofertas tipo 2+1 entre referencias de precios distintos: el sistema comprueba que la unidad regalada sea siempre la más barata del conjunto y que las demás se cobren a tarifa.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Múltiplos de compra junto al stock mínimo en la ficha del producto',
 'En la ficha del producto, junto al stock mínimo de cada almacén, ahora se ve y se edita también el múltiplo de compra, sin tener que ir a otra pantalla.', 'Nesto', SUSER_SNAME()),
-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Facturar avisa claro si una línea tiene una forma de pago inválida',
 'Al facturar un pedido con líneas cuya forma de pago no existe para el cliente, antes fallaba con un error interno confuso; ahora se muestra un aviso claro indicando la línea y la forma de pago para poder corregirla.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Errores confusos al dejar de visitar clientes y al validar algunos NIF',
 'Dejar de visitar un cliente que ya no existe y validar un NIF escrito solo con guiones o espacios provocaban errores internos; ahora se responde con un aviso claro.', 'NestoAPI', SUSER_SNAME()),
-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Menos bloqueos al contabilizar y facturar a la vez que otros compañeros',
 'El número de asiento contable se reserva ahora de forma que una facturación larga no deja bloqueados al resto de usuarios que contabilizan o consultan datos de la empresa mientras tanto.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Menos avisos de envíos GLS en estado "Desconocido"',
 'Cuando el servicio de seguimiento de GLS está saturado (suele pasar a media mañana), la consulta se reintenta automáticamente un rato después, así que ya no llegan avisos de envíos sin estado por caídas puntuales.', 'NestoAPI', SUSER_SNAME());
GO

-- Comprobación
-- SELECT * FROM dbo.Novedades WHERE [Version] = '1.10.10.0' ORDER BY Id;
