-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.11.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.11.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.11.0.
-- Se omiten a propósito (el usuario no las percibe todavía o son internas):
--  - Informes de picking/packing QuestPDF (#293): siguen en piloto con un usuario (flag
--    MotorPdfPicking); se anunciarán al extenderlos a todo el almacén.
--  - Informe de rapports QuestPDF, ficha de cliente sin EF (1C.8), Polly (#288),
--    rollback seguro (#291), applock de rapports (#294): internos (el usuario solo nota
--    que los duplicados ya no ocurren, recogido abajo como corrección).

DECLARE @version VARCHAR(23) = '1.10.11.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Ofertas combinadas 3+2, 2+2 o las que haga falta',
 'En el mantenimiento de ofertas combinadas ahora se indica cuántas unidades se cobran y cuántas se regalan (por ejemplo 3+2: se compran 5, se cobran las 3 más caras a su tarifa y se regalan las 2 más baratas). Basta poner las cobradas y las regaladas: la cantidad de la fila se ajusta sola, y una columna muestra el resumen de la oferta (3+2) de un vistazo.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'La plantilla de venta puede cargar solo el stock de su almacén',
 'Nueva casilla "Ver stock de los tres almacenes" en la plantilla: desmarcándola solo se consulta el stock del almacén propio y la plantilla carga bastante más rápida. La preferencia se guarda por usuario; por defecto se siguen viendo los tres almacenes. Además, la carga de stock es ahora mucho más rápida en todos los casos.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Ofertas puntuales del proveedor en los pedidos de compra',
 'Si un proveedor ofrece algo solo para un pedido ("si me pides 20 te regalo 4") sin ser una oferta habitual, ya se puede reflejar a mano: las columnas Cobradas y Regalo del pedido de compra son editables, y al crear el pedido se generan las dos líneas (cobrada y regalo a precio 0) como con las ofertas de tabla. Vaciar el regalo vuelve al automático.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Office ya no pide iniciar sesión cada vez que se abre Nesto',
 'Al crear tareas de Planner, citas de Outlook o usar cualquier función de Office 365, el navegador de inicio de sesión solo se abre la primera vez: la sesión se recuerda entre arranques de Nesto y se renueva sola.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'El concepto de los enlaces de pago ahora identifica el pago',
 'Al enviar un enlace de pago sin marcar los efectos que paga el cliente, hay que escribir un concepto real (por ejemplo "Pago pedido 123456" o "Pago señal curso quiromasaje"): es el texto que queda en el extracto del cliente al contabilizar el cobro. Con efectos marcados, el asunto por defecto se pone solo. Las mayúsculas se normalizan automáticamente.', 'Nesto', SUSER_SNAME()),
-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Ya no se crean rapports duplicados al pulsar dos veces Guardar',
 'Si el guardado tardaba y se pulsaba el botón otra vez, se creaban dos rapports idénticos del mismo cliente y día. Ahora el botón se desactiva mientras guarda y, si aun así llegan dos intentos, el segundo recibe el aviso de que ya existe.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'La cantidad de oferta ya no se queda bloqueada en la plantilla',
 'Al recuperar un borrador con una oferta puesta (por ejemplo un 2+1), el campo de cantidad de oferta podía quedar inactivo y no había forma de quitar o ampliar la oferta. Ahora, con una oferta puesta, siempre se puede modificar o quitar.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Mensajes claros al liquidar movimientos y al editar el extracto',
 'Al liquidar movimientos del extracto que no se pueden liquidar (importes del mismo signo o a cero), el motivo real quedaba enterrado entre errores técnicos; ahora se ve claro. Y al poner una cuenta bancaria a un impagado, en vez de un error técnico sale el aviso de que un impagado no puede llevar CCC.', 'NestoAPI', SUSER_SNAME());
GO

-- VERIFICACIÓN (deben salir las 8 filas de la 1.10.11.0):
SELECT [Version], Categoria, Titulo FROM dbo.Novedades WHERE [Version] = '1.10.11.0';
GO
