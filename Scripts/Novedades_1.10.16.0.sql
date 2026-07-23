-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.16.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.16.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.16.0.
-- Se omiten a propósito (el usuario no las percibe o son internas):
--  - Verifactu (país de la UE declara con IdOtro 02 #354, facturas del camino viejo sin datos
--    fiscales fuera del ciclo #348): sigue en SOMBRA contra el entorno de pruebas.
--  - CIF ampliado a 20 caracteres para NIF-IVA extranjeros (#356): interno; sin efecto visible
--    hasta re-marcar esos clientes.
--  - Refinamientos de la ventana de NIF incorrectos (botones español/extranjero, selector de
--    país #417): mejoras de una ventana ya anunciada en 1.10.15.0.
--  - ClientesViewModel sin Entity Framework (1C.8), validación previa de gastos sin centro de
--    coste (#343), "sin stock para picking" como aviso de negocio, filtros anti-bots (#336),
--    diagnóstico de bloqueos robusto al saturarse el pool (#357): internos.

DECLARE @version VARCHAR(23) = '1.10.16.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Cartera viva: la remesa muestra los recibos pendientes al abrirse',
 'La ventana de remesas estrena la pestaña "Cartera viva" como primera pestaña y carga sola los recibos candidatos al abrirse, sin tener que buscarlos a mano. Así se ve de un vistazo la cartera pendiente de remesar.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'El alta de cliente pide el país',
 'Al dar de alta un cliente ahora se indica el país (con un selector). Para los clientes extranjeros el NIF no se valida contra el censo de la AEAT española y sus facturas se declararán a Verifactu con el identificador correcto según el país.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Imprimir el informe de la remesa',
 'Se puede imprimir un informe de la remesa —con el IBAN completo de cada recibo y subtotales por fecha de cargo— tanto al crearla como después desde el listado de remesas.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'La remesa con vencimientos en varias fechas descuadraba el asiento',
 'Una remesa que agrupaba recibos con distintas fechas de cargo metía todos los importes en el asiento contable de la primera fecha. Corregido: cada fecha de cargo lleva su importe al banco.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'La ventana de pedidos podía quedarse colgada si había un bloqueo',
 'Al abrir o seleccionar un pedido, si otro usuario tenía la base de datos bloqueada, la ventana podía quedarse sin responder. Corregido. Además, cuando algo se queda bloqueado, el aviso indica qué usuario lo está bloqueando incluso cuando el sistema está muy cargado, para poder avisarle directamente.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Planes de Ventajas podía quedarse en blanco',
 'La pantalla de Planes de Ventajas podía quedarse vacía si fallaba la carga de datos. Ahora muestra el error en vez de una pantalla en blanco.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Crear un pedido con una línea de inmovilizado daba error',
 'Añadir a un pedido una línea de inmovilizado podía provocar un error al guardarlo. Corregido.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'En Agencias, si la factura no se creaba tras el albarán, ahora lo dice claro',
 'Al facturar desde la ventana de Agencias, si el albarán se creaba pero la factura no, no quedaba claro qué había pasado. Ahora se avisa de que el albarán se creó correctamente pero la factura no se pudo crear, indicando el motivo.', 'Nesto', SUSER_SNAME());
