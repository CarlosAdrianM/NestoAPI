/*
    Novedades de la versión 1.10.26.0 (10/09/2026).

    Tanda completa: NestoAPI + Nesto (ClickOnce 1.10.26.0). Se sube la tercera cifra porque hay
    funcionalidad nueva visible: variantes para la web en la ficha del producto y los días que
    sirve el cliente en la ficha de cliente.

    ORDEN: 1) SQL como sa (Issue477_ProductosVariantes, Issue480_prdCrearRemesaIso20022_TodoRCUR,
    Issue478_Familias_VentaPausadaEnTienda); 2) publicar NestoAPI; 3) publicar la ClickOnce;
    4) ejecutar ESTE script.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - Nesto#340 4A.5: los comandos de Nesto.ViewModels pasan a RelayCommand (modernización).
      - NestoAPI#478 (parte API): interruptor de familia para pausar la venta en la tienda. Sin
        pantalla en Nesto todavía y sin soporte en el módulo de PrestaShop; se anunciará cuando
        se pueda usar.
      - NestoAPI#477 paso 1 (tabla ProductosVariantes y campo Variante en el mensaje del bus):
        el usuario ve el bloque de la ficha (sí se anuncia), no la tubería.
      - NestoAPI#481: el usuario de auditoría de las ofertas autorizadas de familia se grababa
        como la cuenta del servidor (RDS2016$); ahora graba quién fue. Solo se ve en la BD.
      - Tests, EDMX, scripts.

    Ejecutar en SSMS contra NV.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.26.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-10';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Nuevo', 'Variantes para la web en la ficha del producto',
 'En la ficha del producto hay un bloque nuevo, "Variantes para la web", para agrupar referencias hermanas (el mismo artículo en varios colores o tapizados) bajo una ficha principal. En la tienda online se verán como UNA ficha con selector de color, en vez de una ficha por color. De momento solo se cargan los datos: la tienda empezará a usarlos cuando se instale la nueva versión del módulo de sincronización.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Nuevo', 'Días que sirve el cliente, en la ficha de cliente',
 'En la página de contacto del asistente de cliente hay cinco casillas, de lunes a viernes, para marcar qué días puede recibir mercancía. El picking ya no saca un pedido cuya entrega caería en un día que el cliente cierra; hasta ahora ese dato solo se podía cambiar a mano en la base de datos.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', 'No se puede anular un cliente con productos pendientes de servir ni con deuda',
 'Al pasar un cliente a anulado desde la ficha comercial (o al dejar de visitarlo), si tiene líneas pendientes de servir o apuntes vivos en el extracto, se rechaza el cambio y se indican los pedidos o el importe. Antes se anulaba sin más y el pendiente se quedaba colgado (cliente 40445).', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Mejorado', 'Al guardar un cliente, los rechazos de la base de datos se ven con su motivo',
 'Cuando la base de datos rechaza guardar un cliente o una cuenta bancaria (IBAN mal formado, CIF repetido), sale el motivo tal cual en vez de un error genérico.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Mejorado', 'El correo "Pedidos sin picking: el cliente cierra el día de la entrega" va a almacén y solo una vez',
 'El aviso de los pedidos que no salen porque el cliente cierra el día de la entrega ahora se envía con almacén en copia (antes iba administración) y una sola vez por pedido y día de entrega: antes se repetía en cada pasada del picking.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', 'La remesa se contabiliza igual que la abona el banco',
 'Todos los recibos de la remesa viajan al banco como recurrentes (RCUR) y la contabilización agrupa solo por fecha de cobro. Antes, cuando la remesa llevaba algún mandato "primer cobro" (FRST), se contabilizaban tres asientos y el banco hacía dos abonos (remesas 10930, 10931 y 10937), y había que cuadrarlos a mano.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Editar una línea en el detalle del pedido daba un error en cada tecla',
 'Desde la versión anterior, al modificar una celda del detalle del pedido saltaba un error ("Overload resolution failed") con cada pulsación y los totales no se actualizaban. Corregido.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Al crear un cliente ya no se cierra la ventana por la dirección de entrega',
 'Al terminar el asistente de cliente, si la dirección de entrega no aparecía en la lista (o aparecía con espacios de relleno), la ventana se cerraba con un error. Ahora se selecciona la dirección correcta y, si no está, se avisa.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', 'En Agencias, teclear un pedido de la empresa espejo ya no da error',
 'Al teclear el número de un pedido que solo existe en la empresa 3, la ventana de Agencias cambiaba de empresa y fallaba al elegir la agencia ("Sequence contains no matching element"). Ahora elige la misma agencia en esa empresa.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Las ofertas autorizadas de familia guardan el nombre de la familia tal como está en Nesto',
 'Si se tecleaba la familia en minúsculas ("staleks"), así se quedaba en la oferta. Ahora se guarda como figura en la tabla de familias ("Staleks").', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', 'La ventana de Agencias volvía a cargar las empresas (publicado esta mañana)',
 'Desde ayer por la tarde el desplegable de empresa de Agencias salía vacío y no se podían sacar etiquetas de ASM ni Correos Express. Se publicó la corrección en el servidor a las 09:34 de hoy, antes de la hora de impresión.', 'NestoAPI', 1, 'sa');
