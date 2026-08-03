-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.19.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.19.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.19.0.
-- Se omiten a propósito (internas o sin efecto visible todavía para el usuario):
--  - Endpoints GET/PUT api/CodigosPostales (#378): interno (la ventana es lo visible).
--  - Búsqueda de cliente de los pedidos Miravia por la API, sin EF (Nesto#340): interno.
--  - Columna Pais + FK en CódigosPostales y backfill de ~18.000 CPs (#378): ya aplicado
--    directamente en BD el 03/08; lo visible es la ventana y el alta de extranjeros.

DECLARE @version VARCHAR(23) = '1.10.19.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Ventana de mantenimiento de Códigos Postales (Clientes → Códigos Postales)',
 'Para Dirección y Tienda online: busca un código postal por número o población y corrige su población, provincia, ruta, país y vendedor, además de los vendedores por grupo de producto (peluquería, etc.). Los códigos postales sin país salen resaltados: son los extranjeros antiguos que conviene clasificar. Los ~18.000 códigos postales existentes ya se han clasificado por país automáticamente.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Los códigos postales extranjeros nuevos nacen con su país',
 'Al crear o modificar un cliente con dirección fuera de España, el código postal que se da de alta automáticamente queda registrado con el país de la dirección (y si ya existía sin país, se le completa). Así dejan de acumularse códigos postales extranjeros como si fuesen españoles.', 'NestoAPI', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Los pedidos de amazon.sa (Arabia Saudí) ahora sí aparecen',
 'En la versión anterior se activó el marketplace amazon.sa pero el identificador que se registró era erróneo y los pedidos seguían sin salir en Canales Externos → Pedidos (Amazon). Corregido: el pedido de Arabia Saudí pendiente ya debe aparecer en el listado.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Error al crear un cliente cuya persona de contacto solo tiene correo',
 'Si al dar de alta un cliente se añadía una persona de contacto con correo electrónico pero sin nombre, el alta fallaba con un error técnico. Ahora se crea correctamente (sin saludo, al no haber nombre).', 'NestoAPI', SUSER_SNAME());
