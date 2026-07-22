-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.15.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.15.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.15.0.
-- Se omiten a propósito (el usuario no las percibe o son internas):
--  - Verifactu (OSS #347, subsanación fuera de plazo #346, pasaportes #339, auditoría de
--    envíos): sigue en SOMBRA contra el entorno de pruebas. Se contará al entrar en producción.
--  - Manifiesto de agencia migrado al servidor (último informe RDLC): se imprime igual.
--  - Remesas: generación de fichero, impagados y tareas Planner por API (1C.14): la ventana
--    se ve igual.
--  - Filtros anti-bots ampliados (#336): seguridad interna.

DECLARE @version VARCHAR(23) = '1.10.15.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Ventana de clientes con NIF incorrecto',
 'En Clientes → Mantenimiento hay una ventana nueva con los clientes cuyo NIF no pasa la comprobación del censo de la AEAT (los que tienen pedido pendiente van primero, porque su factura se bloqueará con Verifactu). Desde la propia ventana se corrige el NIF: se valida contra la AEAT al momento y se actualizan la ficha, los contactos y las facturas aún sin declarar. Administración y dirección ven todos los clientes; los jefes de venta, los de su equipo; y cada vendedor, los suyos.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Clientes extranjeros con pasaporte, sin falso aviso de NIF',
 'Si un cliente se identifica con pasaporte u otro documento extranjero, ya no se trata como "NIF incorrecto": en la ventana de NIF incorrectos se marca "¿Es extranjero?" indicando el tipo de documento y el país, sale de la lista y sus facturas se declararán correctamente a Verifactu.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'La remesa puede respetar el vencimiento de cada recibo',
 'Al crear una remesa, la casilla "Respetar el vencimiento de cada efecto" (marcada por defecto) hace que cada recibo se cargue en su fecha de vencimiento real en vez de cargarse todos el mismo día; los ya vencidos se cargan hoy. La fecha de la cabecera ahora es el límite de selección ("efectos vencidos hasta el día X") y se propone sola con el siguiente día laborable. Si se desmarca la casilla, todo se carga en una única fecha, como hasta ahora.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'El correo de recibos domiciliados avisa de la fecha de cargo',
 'El correo que se envía a los clientes con recibos domiciliados ahora indica la fecha prevista de cargo cuando es posterior al día del aviso, para que el cliente sepa cuándo se le cargará cada recibo.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'El aviso de NIF incorrecto llega a la persona adecuada',
 'El correo de NIF incorrecto se envía al vendedor del cliente si tiene correo propio; si el vendedor es genérico o no tiene correo, se envía al usuario que metió el pedido; y en último caso, solo a administración (que siempre va en copia).', 'NestoAPI', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Crear la remesa daba error',
 'La pestaña "Crear Remesa" estrenada en la versión anterior fallaba al confirmar (error de clave duplicada). Corregido: la remesa se crea con su asiento contable completo.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Los pedidos de Canarias y extranjero ya no avisan de NIF incorrecto',
 'Los pedidos sin IVA (que se facturan por la empresa espejo, fuera de Verifactu) lanzaban el aviso de NIF incorrecto sin motivo. Ya no se comprueba el censo en esos pedidos.', 'NestoAPI', SUSER_SNAME());
