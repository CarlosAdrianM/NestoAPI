/*
    Novedades de la versión 1.10.28.0 (16/09/2026).

    Tanda completa: NestoAPI + Nesto (ClickOnce 1.10.28.0). Se sube la tercera cifra porque hay
    funcionalidad nueva visible: el selector de modo de entrega del pedido (que sustituye a la
    casilla "Servir junto"), la confirmación al cliente de la tienda y varias preguntas nuevas.

    ORDEN: 1) publicar NestoAPI; 2) deshabilitar la tarea "Nesto_sync Clientes" del Task Scheduler
    de RDS2016 (la sincronización pasa a Hangfire); 3) publicar la ClickOnce; 4) ejecutar ESTE
    script. Los tres SQL de la tanda (trigger de modo de servicio, parámetro por defecto y SP de
    reposición) YA se ejecutaron el 16/09 por la tarde.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - Regla interna "ServirJunto marcado = todo junto" y trigger que mantiene coherente la columna
        cuando el Nesto viejo toca la casilla (NestoAPI#482).
      - prdRellenarReposicionStock reparte por fecha de modificación como el picking (NestoAPI#486).
      - El traspaso a espejo y los pedidos de marketplaces conservan/fijan el modo (barrido #482).
      - Nesto#340: Agencias sin EF (ampliación y factura por API), CanalesExternos a ObservableObject.
      - NestoAPI#402: la sincronización de clientes pasa a Hangfire (misma función, otro planificador).
      - Nombres de la tienda en formato oración (NestoAPI#479): se nota en la web, no en Nesto.
      - Tests, EDMX, scripts, issues de NestoApp.

    Ejecutar en SSMS contra NV. Los literales de texto van con N'...': las columnas son nvarchar y sin
    el prefijo cualquier carácter fuera de Windows-1252 se graba como '?'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.28.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-16';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Nuevo', N'La casilla "Servir junto" del pedido se convierte en un selector de modo de entrega',
 N'En el detalle del pedido y en la plantilla de venta, donde estaba la casilla "Servir junto" ahora hay un selector con cuatro modos: "Todo junto" (lo que era la casilla marcada), "Según vaya entrando" (la casilla desmarcada), "Tras reponer de tiendas" (espera a que la reposición habitual traiga de Reina y Alcobendas el stock que le corresponda al pedido y, cuando ya no queda nada que traer, sale lo que hay) y "Ahora lo que hay, el resto de una vez" (sale ya lo que hay y lo que falte se entrega en una única entrega más). El picking y los portes respetan cada modo. Los pedidos antiguos se muestran con el modo que corresponde a cómo tenían la casilla.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Nuevo', N'Los pedidos nuevos nacen en "Tras reponer de tiendas"',
 N'Al elegir el cliente en la plantilla (y al crear un pedido nuevo desde el detalle), el selector arranca en "Tras reponer de tiendas" y ya no se arrastra la casilla "Servir junto" de la ficha del cliente: esa casilla dejaba pedidos sin servir nunca cuando faltaba una referencia agotada o anulada. Se puede cambiar el modo en cada pedido, y quien quiera otro modo por defecto puede pedirlo (parámetro de usuario "ModoServicioPorDefecto"). Los pedidos de la tienda online y de la app del cliente también nacen así; los de Amazon, Miravia y PrestaShop siguen siendo "Todo junto".', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', N'Los avisos al salir de "Todo junto" y el correo del pedido hablan del modo de entrega',
 N'Cuando no se puede cambiar el modo porque una muestra o un regalo se quedarían pendientes, el mensaje dice a qué modo se intentaba pasar ("No se puede pasar el pedido a «Según vaya entrando»...") en vez de hablar de una casilla que ya no existe. Y el correo de "Pedido nuevo" o "Pedido modificado" lleva siempre una línea "Modo de entrega: ..." (antes solo avisaba si la casilla estaba desmarcada).', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Nuevo', N'El cliente que compra desde la tienda online o la app recibe un correo de confirmación',
 N'Hasta ahora, al confirmar un pedido en la tienda o en la app, el carrito se vaciaba y el cliente no recibía nada. Ahora le llega "Gracias por tu pedido nº X" con las líneas, la base, los portes si los hay, el IVA y el total, la situación del pago (cobrado con tarjeta, pendiente en la pasarela, o con sus condiciones habituales) y el aviso de que le avisaremos cuando salga. Sale desde tiendaonline@ y va al correo con el que el cliente ha iniciado sesión. El correo interno de "Pedido nuevo" sigue llegando a las mismas personas.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Nuevo', N'La plantilla pregunta si se borra el borrador al crear el pedido',
 N'Cuando un pedido se crea a partir de un borrador cargado, al terminar la plantilla pregunta "El pedido X se ha creado a partir del borrador «...». ¿Quieres borrar el borrador?". Si se dice que sí, desaparece de la lista; si no, se queda para reutilizarlo. Así dejan de acumularse borradores de pedidos que ya existen.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Nuevo', N'El rapport pide confirmar cuando no se indican los empleados del centro',
 N'En los clientes de Madrid a los que se pregunta el número de empleados, si al guardar el rapport la casilla se ha dejado vacía, Nesto avisa y pide confirmar que no se rellena porque no se sabe. Si no se confirma, vuelve al formulario. Es el dato de la campaña de contratación del SEPE: desde que existe la casilla nadie lo había rellenado.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'En las ofertas combinadas, vaciar la celda "Grupo alt." bloqueaba la pantalla',
 N'Al borrar el número de grupo alternativo de una línea para convertirla en obligatoria, la celda se quedaba con un borde rojo y no se podía tocar ninguna otra línea. Ahora la celda vacía se guarda como "sin grupo" (línea obligatoria), igual que ya hacían "Precio base" y "Precio fijo".', 'Nesto', 1, 'sa');
