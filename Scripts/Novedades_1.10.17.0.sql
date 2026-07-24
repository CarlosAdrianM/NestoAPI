-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.17.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.17.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.17.0.
-- Se omiten a propósito (internas o sin efecto visible para el usuario):
--  - Fase 1 del polimorfismo de agencias (#258) y generación local del QR de Verifactu
--    (#326): internas, todavía sin efecto visible.
--  - Copia del aviso de NIF incorrecto al usuario del pedido, estado 8 para clientes
--    extranjeros, CIF ampliado a 20 caracteres (#356), rechazo con mensaje claro de líneas
--    con estado/factura incoherente (#360), filtro anti-bots y no-op de la rotación de
--    credenciales de Amazon en ELMAH (#336/#361): internas.

DECLARE @version VARCHAR(23) = '1.10.17.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Remesas: doble clic en un recibo abre el extracto del cliente',
 'En la ventana de remesas, al hacer doble clic sobre un recibo se abre el extracto de ese cliente para ver su situación de un vistazo. Además, el total de la remesa se puede seleccionar y copiar.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'NIF incorrectos: ahora también aparecen los clientes extranjeros',
 'La ventana de NIF incorrectos muestra también los clientes extranjeros cuyo NIF-IVA rechaza Verifactu (por ejemplo un NIF italiano o francés). Así se pueden marcar como extranjeros y corregir su identificación desde la misma ventana, sin tener que tocar nada por detrás.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Contabilizar los impagados de una remesa daba error de fecha',
 'Al contabilizar el fichero de impagados de una remesa, la operación fallaba con un error de fecha no permitida. Corregido.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'El cargo de la remesa se contabiliza en el día en que el banco lo abona',
 'El apunte del banco de una remesa se contabilizaba el mismo día de presentarla. Como el banco no abona los recibos hasta el siguiente día laborable, ahora la fecha contable es esa fecha de valor, para que cuadre con el extracto bancario.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'En Agencias, rehusar un envío ya tramitado daba un error falso',
 'Al rehusar un envío desde la pestaña de tramitados de Agencias podía aparecer un error aunque la operación fuese correcta. Corregido.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Crear un pedido de compra podía dar error de número duplicado',
 'En momentos de mucha concurrencia, dos pedidos de compra podían intentar coger el mismo número y uno de ellos fallaba. Corregido: el número se asigna de forma atómica.', 'Nesto', SUSER_SNAME());
