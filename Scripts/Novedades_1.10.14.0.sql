-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.14.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.14.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.14.0.
-- Se omiten a propósito (el usuario no las percibe o son internas):
--  - Verifactu (job de estados y reintentos #329, guarda de extranjeros #339): sigue en
--    SOMBRA contra el entorno de pruebas de la AEAT. Se contará cuando entre en producción.
--  - Impagados por API en Remesas (1C.14 slices 4-5) y refactor de agencias (#258 b.2):
--    migración interna, la ventana se ve igual.
--  - Bloqueo de bots en IIS y cabeceras ocultas (#336 fase 2): seguridad interna.
--  - Los reportes de error del cliente ahora llevan la versión ClickOnce real (Nesto#423):
--    diagnóstico interno.
--  - Auto-fix de porcentajes de IVA incoherentes (#342): el usuario solo percibe que la
--    factura que antes daba "descuadre" ahora sale; no merece entrada propia.

DECLARE @version VARCHAR(23) = '1.10.14.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Ventana Extracto de Cliente',
 'En Clientes → Cartera ya está disponible el Extracto de Cliente: consulta de los movimientos de cartera del cliente y liquidación de efectos (los pares cargo/abono que se compensan entre sí) sin salir de Nesto. Es el primer paso para jubilar la ventana equivalente del programa antiguo.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Crear la remesa de recibos desde Nesto',
 'La ventana de Remesas tiene una pestaña nueva "Crear Remesa": muestra los efectos candidatos a remesar, retiene automáticamente los que no conviene cobrar todavía (entrega sin confirmar por la agencia, envíos con incidencia o devueltos, clientes con el estado bloqueado) explicando el motivo de cada retención, y al confirmar crea la remesa SEPA con su asiento contable en un solo paso.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Copiar cualquier dato desde Agencias con el botón derecho',
 'En la ventana de Agencias (Incidentados y demás pestañas de envíos), el botón derecho sobre una celda ahora ofrece: copiar el campo exacto bajo el cursor (teléfono, dirección, población...), copiar el nº de envío, copiar el nº de pedido, o copiar el envío completo con formato para pegarlo en un correo.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Aviso de NIF incorrecto al facturar (preparación Verifactu)',
 'Al meter un pedido se comprueba el NIF del cliente contra el censo de la AEAT. Si es incorrecto, al facturar salta un aviso claro y el vendedor recibe un correo (con copia a administración) pidiendo el NIF correcto. De momento la factura sale igualmente, pero a partir del 01/12/2026, con Verifactu, un NIF incorrecto impedirá facturar. Corregir el NIF en la ficha lo arregla para el cliente y todos sus contactos de una vez.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Nuevo', 'Aviso al modificar un pedido con reembolso desajustado',
 'Si al modificar un pedido se quita la comisión de reembolso pero el envío de agencia sigue teniendo importe contra reembolso, Nesto avisa y ofrece restar el reembolso del envío en un clic (solo si el envío aún no está tramitado; si ya lo está, indica que hay que abonar la comisión aparte).', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Facturar ya no se bloquea por el visto bueno',
 'Al facturar un pedido, las líneas pendientes de visto bueno lo reciben automáticamente en ese momento, en vez de parar la facturación con el error "Hay líneas que no tienen el visto bueno dado".', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Si la factura falla después de crear el albarán, la pantalla lo cuenta',
 'Antes, si la facturación fallaba justo después de crear el albarán, la pantalla del pedido se quedaba desactualizada y parecía que no había pasado nada. Ahora el pedido se recarga solo y el mensaje explica que el albarán sí se creó y qué hacer a continuación.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'La ventana de Planes de Ventajas vuelve a abrir',
 'La lista de planes de ventajas daba un error del servidor al cargar y la ventana se quedaba vacía. Corregido.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Error al hacer doble clic en la lista de ventas del cliente',
 'En la ficha comercial del cliente, un doble clic que caía en la cabecera o en el hueco vacío de la lista de pedidos provocaba un error. Ahora simplemente no hace nada.', 'Nesto', SUSER_SNAME());
