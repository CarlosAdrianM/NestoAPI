-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.8.2
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.8.2). Ambito: Nesto / NestoAPI. Publicada = 1.
-- Ejecutar contra la BD NV al publicar Nesto 1.10.8.2.
-- Release de estabilización (solo correcciones) previa al viaje.

DECLARE @version VARCHAR(23) = '1.10.8.2';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Seguimiento de envíos de GLS restablecido',
 'El seguimiento automático de los envíos de GLS había dejado de actualizar entregas e incidencias (la pestaña de Incidentados se quedaba vacía y nada pasaba a Entregado). Ya vuelve a funcionar; los envíos entregados e incidentados aparecen de nuevo en sus pestañas.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Pasar pedidos con muestras aunque haya poco stock',
 'Al quitar «servir junto» en un pedido con una muestra (material promocional), a veces daba error diciendo que la muestra se quedaría pendiente, aunque en pantalla apareciera con stock. Se estaba contando la propia línea del pedido como si fuera otra reserva. Corregido: ahora se puede pasar el pedido cuando hay stock suficiente.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Envío diario de facturas por correo más robusto',
 'El envío automático de las facturas del día por correo podía fallar entero por culpa de una sola factura (por ejemplo, con una línea sin albarán) y entonces no se enviaba ninguna. Corregido: esa factura ya no da error y, si alguna fallara, el resto se envían igual.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Crear pedidos de compra con descripciones largas',
 'Crear un pedido de compra con una línea cuya descripción superaba los 50 caracteres daba error y no se guardaba. Corregido: el texto se ajusta y el pedido se guarda correctamente.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Crear seguimientos de clientes sin error',
 'Al crear un seguimiento de un cliente o contacto que no estaba en la ficha, daba un error y no se guardaba. Corregido.', 'Nesto', SUSER_SNAME());
GO

-- Comprobación
-- SELECT * FROM dbo.Novedades WHERE [Version] = '1.10.8.2' ORDER BY Id;
