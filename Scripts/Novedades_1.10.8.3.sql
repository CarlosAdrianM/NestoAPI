-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.8.3
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.8.3). Ambito: Nesto / NestoAPI. Publicada = 1.
-- Ejecutar contra la BD NV al publicar Nesto 1.10.8.3.
-- Release de estabilización (solo correcciones) previa al viaje.
-- NOTA: la 1.10.8.2 ya está publicada y sus novedades ya están insertadas; aquí solo
-- van las correcciones NUEVAS de la .3. El seguimiento de GLS se describe en tono
-- prudente: el #264 se corrigió, pero sigue pendiente revisar la uid de seguimiento (#266).

DECLARE @version VARCHAR(23) = '1.10.8.3';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Mejoras en el seguimiento de envíos',
 'Seguimos afinando el seguimiento automático: las incidencias de los envíos de Innovatrans ahora aparecen correctamente en la pestaña de Incidentados, y el seguimiento aguanta mejor las caídas puntuales de la web de las agencias.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Confirmar envíos de Amazon con Innovatrans',
 'Al confirmar el envío de un pedido de Amazon cuyo seguimiento era de Innovatrans, la aplicación daba error. Corregido: ya se confirma con Innovatrans igual que con las demás agencias.', 'Nesto', SUSER_SNAME());
GO

-- Comprobación
-- SELECT * FROM dbo.Novedades WHERE [Version] = '1.10.8.3' ORDER BY Id;
