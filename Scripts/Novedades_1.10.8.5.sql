-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.8.5
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.8.5). Ambito: Nesto / NestoAPI. Publicada = 1.
-- Ejecutar contra la BD NV al publicar Nesto 1.10.8.5.
-- Release de correcciones (sin módulos nuevos): solo se sube la 4ª cifra.
-- NOTA 1: se omite a propósito el ajuste del centro de coste de los portes (NestoAPI#277):
-- es un cambio de imputación contable pendiente de verificar en vivo, no se anuncia todavía.
-- NOTA 2: se omite la migración interna de seguridad (JWT en las llamadas, Nesto#369): no la percibe el usuario.

DECLARE @version VARCHAR(23) = '1.10.8.5';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Agencias: ya no se cierra la aplicación al rehusar o crear envíos',
 'En la ventana de Agencias, al Rehusar un envío o al crear un envío pendiente (por ejemplo al registrar una etiqueta) la aplicación podía cerrarse inesperadamente si la lista de la agencia aún no se había cargado. Corregido.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Abrir Picking ya no cierra la aplicación',
 'En el detalle del pedido, al pulsar Abrir Picking sin un pedido cargado la aplicación se cerraba. Corregido.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Descuentos que se rechazaban por error',
 'Algunos productos rechazaban por error un descuento que sí estaba autorizado, cuando el precio autorizado no estaba ligado a un cliente concreto. Ahora el descuento se acepta correctamente.', 'NestoAPI', SUSER_SNAME()),
-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Mensaje claro al guardar un cliente o un mandato',
 'Cuando falla el guardado de un cliente o de un mandato bancario, ahora se muestra el motivo real del error (por ejemplo un dato duplicado o incorrecto) en vez de un mensaje genérico. Además, el error queda registrado para poder diagnosticarlo.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Aviso claro al crear un pedido con un producto inexistente',
 'Al crear un pedido con un producto que no existe en la empresa, ahora se avisa con un mensaje claro indicando el producto, en vez de un error interno confuso.', 'NestoAPI', SUSER_SNAME());
GO

-- Comprobación
-- SELECT * FROM dbo.Novedades WHERE [Version] = '1.10.8.5' ORDER BY Id;
