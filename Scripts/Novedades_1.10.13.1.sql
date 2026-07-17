-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.13.1
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.13.1). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.13.1.
-- Se omiten a propósito (el usuario no las percibe o son internas):
--  - Verifactu Fase A (#34): envío de facturas a la AEAT, desactivado por configuración
--    hasta la fase de producción (dic/26).
--  - Remesas lee las empresas del API (Nesto#340 1C.14.1) y datos por-agencia del servidor
--    para confirmar envíos de Amazon/Prestashop (#258a): internos, sin cambio visible.
--  - Endpoints de modificar envío en la agencia (#317): la edición en la ventana llegará
--    en una versión próxima; de momento el flujo es anular + corregir + reimprimir.
--  - Traspaso de albaranes entre empresas (#318): corrección de integridad interna.

DECLARE @version VARCHAR(23) = '1.10.13.1';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Ya se puede borrar un envío de Innovatrans aunque esté registrado en la agencia',
 'En la ventana de Agencias, el botón Borrar ahora funciona también con envíos de Innovatrans ya registrados (con albarán): primero se anula el envío en la agencia y, solo si la agencia lo acepta, se borra. El envío queda como etiqueta pendiente, así que se puede corregir la dirección y volver a imprimir con un albarán nuevo. Ojo: la agencia solo permite anular hasta el cierre del día (sobre las 15:00-17:00); pasada la recogida, el mensaje lo indicará y habrá que gestionarlo como incidencia por teléfono.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Los pedidos con productos que comisionan por otro grupo ya se pueden guardar',
 'Al meter un pedido con ciertos productos que comisionan a peluquería o estética según quién los vende (bobinas de aluminio, guantes...), el pedido daba un error y no se podía guardar. Ya funciona: la línea queda en el grupo correcto con su subgrupo general.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Los usuarios de tienda online ya ven los botones de crear albarán y factura',
 'En los pedidos con líneas de tienda online (Amazon, web...), los usuarios que no son de almacén ni de tiendas no veían los botones de Crear Albarán / Crear Factura aunque tenían permiso para usarlos. Ya aparecen y se activan según el estado del pedido.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'El punteo automático de Bancos ya no puede cerrar el programa',
 'Si el punteo automático de la conciliación bancaria fallaba (por ejemplo, sin los apuntes cargados), el programa podía cerrarse de golpe. Ahora muestra el motivo del error y se puede seguir trabajando. También se ha corregido un error intermitente al cambiar de fechas o de banco con la carga de apuntes en marcha.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Al reimprimir una etiqueta de Innovatrans que falla, el mensaje dice cómo solucionarlo',
 'Cuando un envío tiene un código de barras que no es un albarán real de la agencia (por ejemplo, heredado de una etiqueta de otra agencia), la impresión fallaba una y otra vez con un mensaje que no ayudaba. Ahora el mensaje indica el albarán y el paso a dar: borrar el código de barras del envío para que se registre de nuevo.', 'NestoAPI', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Los interbloqueos también dicen quién está bloqueando',
 'El aviso de "quién te está bloqueando" que salía en los errores por bloqueo ahora aparece también cuando el error es un interbloqueo entre dos operaciones (el típico "quedó en interbloqueo... fue elegida como sujeto"). Así se puede avisar directamente al compañero en vez de reintentar a ciegas.', 'NestoAPI', SUSER_SNAME());
