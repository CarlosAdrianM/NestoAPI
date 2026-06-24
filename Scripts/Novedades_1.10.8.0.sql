-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.8.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.8.0). Ambito: Nesto / NestoAPI. Publicada = 1.
-- Ejecutar contra la BD NV al publicar Nesto 1.10.8.0.

DECLARE @version VARCHAR(23) = '1.10.8.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Estado de los envíos a la vista',
 'En la ventana de Agencias y en el detalle del pedido se ve el estado de cada envío (tramitado, entregado, incidentado o devuelto a origen) con un color. Hay además una pestaña nueva de Incidentados para vigilar los que tienen alguna incidencia.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Seguimiento automático de los envíos',
 'El estado de los envíos se actualiza solo de forma periódica, consultando a la agencia (GLS e Innovatrans), sin tener que comprobarlo a mano.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Nuevo', 'Sello Madrid Excelente en facturas y documentos',
 'Nueva Visión es empresa certificada Madrid Excelente y su sello aparece ya en las facturas, albaranes, pedidos y presupuestos en PDF.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Nuevo', 'Enlace de seguimiento de Innovatrans',
 'Los envíos de Innovatrans muestran ya su enlace de seguimiento, igual que el resto de agencias.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Mejor elección de agencia para Portugal y la Unión Europea',
 'El comparador tiene en cuenta el país real del destino: para Portugal elige Innovatrans y para la Unión Europea usa la tarifa internacional de GLS, en lugar de aplicar por error una tarifa nacional.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Aviso si la agencia no cubre el destino',
 'Antes de tramitar, si la agencia elegida no tiene tarifa para esa zona, se cambia automáticamente a otra que sí cubra el destino (o avisa si ninguna lo cubre).', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'La agencia se calcula por la dirección de entrega real',
 'La agencia y la tarifa se eligen por el código postal del destino real del envío, no por el del contacto del pedido. Esto corrige casos como un envío a Portugal que se calculaba con tarifa nacional.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'El peso es obligatorio antes de tramitar',
 'Para calcular bien la tarifa, ahora se pide el peso del envío antes de tramitarlo, y se guarda también en las etiquetas pendientes.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Aviso mientras se tramita el envío',
 'Aparece un indicador mientras se está tramitando el envío con la agencia, para saber que la operación está en curso.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Scroll en la pestaña de Pago',
 'Cuando un pedido tiene muchos efectos, la pestaña de pago permite desplazarse para verlos todos.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Impresión de etiquetas de GLS a la primera',
 'Corregido que la primera impresión de una etiqueta de GLS/ASM fallaba y había que repetirla; ahora imprime bien a la primera.', 'Nesto', SUSER_SNAME());
GO

-- Comprobación
-- SELECT * FROM dbo.Novedades WHERE [Version] = '1.10.8.0' ORDER BY Id;
