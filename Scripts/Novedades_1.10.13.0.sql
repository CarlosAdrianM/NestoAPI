-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.13.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.13.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.13.0.
-- Se omiten a propósito (el usuario no las percibe o son internas):
--  - Casilla "Recoger producto" que añade el aviso a comentarios (#310): red de seguridad.
--  - Coherencia de NIF y ClientePrincipal al crear clientes (#263), reintentos de red en
--    consultas (#288.3), usuario del JWT en el perfil de vendedores (#307), volcado de
--    errores de validación y de bloqueos a ELMAH (#309/#312 parte interna), envío diario
--    de facturas testeable (#261), tests rescatados (#242): internos.
--  - Pegar un pedido de ELMAH en la plantilla (#397 Parte 1): herramienta de administración
--    del sistema; se explicará a quien la necesite.

DECLARE @version VARCHAR(23) = '1.10.13.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Al escribir la dirección de un cliente nuevo, salen sugerencias de Google',
 'En el alta de clientes, al teclear la dirección aparece un desplegable con las direcciones reales que sugiere Google (como en la web de cualquier tienda). Se puede elegir con las flechas y Intro o con el ratón. Al elegir una, la dirección, el código postal, la población y la provincia se rellenan solos y dejan de dar el error de "código postal incorrecto" que obligaba a reintentar el alta. Si se prefiere escribir a mano, todo sigue como antes.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Aviso por correo con el importe cuando un pedido coge picking',
 'En el pedido de venta y en la plantilla hay una casilla nueva "Avisar con importe cuando coja picking". Si se marca, al salir picking del pedido llega un correo al vendedor y al usuario que lo marcó con el importe de lo que se va a servir en esa tanda.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'La ficha de clientes ya permite gestionar las cuentas bancarias',
 'En la ficha de clientes se pueden crear, modificar y activar cuentas bancarias (CCC) y generar un mandato nuevo, sin salir a otras ventanas. Si el cliente tiene otro CCC activo, se avisa antes de cambiarlo.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'El selector de clientes puede mostrar los contactos desplegados y avisa de cuántos hay',
 'Cada usuario puede elegir (en la ruedita de opciones del propio selector) si el panel de contactos/direcciones sale desplegado al seleccionar un cliente o cerrado como hasta ahora. Además, cuando está cerrado, un pequeño indicador muestra cuántos contactos tiene el cliente.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'La conciliación de Stripe reconoce las devoluciones',
 'Cuando un abono de Stripe incluye una devolución (por ejemplo: dos cobros y un reembolso en el mismo ingreso), ahora se puede contabilizar la devolución por separado, seleccionar los movimientos implicados y el botón de contabilizar se activa con el cuadre exacto. Antes esos ingresos no cuadraban de ninguna manera.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Si algo se queda "bloqueado", el mensaje dice quién lo está bloqueando',
 'Cuando una operación falla por un bloqueo de otro usuario (el típico "no se pudo contabilizar" en cadena), el mensaje ahora indica qué usuario y programa lo está bloqueando y desde cuándo, para poder avisarle en vez de reintentar a ciegas.', 'NestoAPI', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Regularizar un descuadre metiendo la cuenta a mano ya no da error',
 'En la conciliación bancaria, al regularizar una diferencia introduciendo la cuenta contable a mano fallaba con "gasto sin centro de coste, delegación o departamento". Ahora el apunte se crea imputado a la delegación del usuario y contabiliza a la primera.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Al modificar un pedido con la plantilla, las líneas ya facturadas quedan protegidas',
 'Al cargar un pedido en la plantilla para modificarlo, las líneas que ya están en albarán o factura no se cargan (no se pueden cambiar) y se avisa de cuántas se han dejado fuera. Además, si mientras se editaba el pedido el almacén cogió picking de alguna línea, al guardar se avisa y no se pierde nada.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'El botón "Abrir seguimiento" de los envíos incidentados ya funciona',
 'En la pestaña de incidentados de agencias, el enlace de seguimiento salía siempre desactivado. Ahora se abre el seguimiento del envío con su agencia real.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Un doble clic en Contabilizar ya no puede crear el asiento dos veces',
 'En la conciliación bancaria, los botones de contabilizar y regularizar quedan desactivados mientras se está contabilizando, para que un doble clic o un momento de lentitud no generen asientos duplicados.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Los pedidos de la tienda online se pueden facturar sin permisos especiales',
 'Facturar o sacar albarán de un pedido con líneas de la tienda online pedía pertenecer a un grupo concreto o tener almacén propio aunque el pedido fuera de la web. Ya no hace falta.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'El extracto de cuenta en PDF ya no falla si se imprime dos veces seguidas',
 'Imprimir el extracto de una cuenta contable dos veces seguidas (o desde dos ventanas) fallaba con "el archivo está en uso". Corregido.', 'Nesto', SUSER_SNAME());
GO
