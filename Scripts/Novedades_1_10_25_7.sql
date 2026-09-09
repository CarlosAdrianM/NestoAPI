/*
    Novedades de la versión 1.10.25.7 (09/09/2026).

    Si al final se publica como 1.10.26.0 (hay funcionalidad nueva: empleados en el rapport, campañas
    por marca, servir junto en un paso), cambiar @version antes de ejecutar. La Version tiene que ser
    <= a la versión ClickOnce publicada, si no reaparece en cada actualización.

    SE OMITE A PROPÓSITO (el usuario de Nesto no lo nota):
      - Los comandos de casi todos los módulos pasan de Prism a CommunityToolkit (Nesto#340 4A.5):
        se comportan igual.
      - Las empresas de Agencias se leen de la API en vez de EF (Nesto#340).
      - NestoAPI#454 (momento del producto en los vídeos) y #458 (150 € por efecto con el total del
        pedido): son de la tienda online.
      - El endpoint de grupos bonificables (NestoAPI#466): el usuario solo nota que PEL ya no cuenta,
        que sí va abajo.

    Ejecutar en SSMS contra NV DESPUÉS de publicar la API y la ClickOnce.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.25.7'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-09';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Nuevo', 'El rapport pregunta cuántos empleados tiene el centro (clientes de Madrid)',
 'Al meter un rapport de un cliente con código postal de Madrid aparece un desplegable "Empleados" (Sin empleados, 1, 2, 3, 4, 5 o más). Si ya se sabía, sale relleno; si se cambia, se guarda en la ficha del cliente con la fecha. Sirve para localizar centros que podrían contratar alumnos del SEPE. Los clientes de fuera de Madrid no lo ven.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Nuevo', 'Campañas por marca y categoría (Outlet)',
 'En la pestaña Campañas se puede indicar, además de la familia y el grupo, el subgrupo (categoría). Así una campaña como el Outlet se define por marca y categoría en vez de producto a producto, y se aplica a los productos que tienen esa categoría como principal o como secundaria. El Outlet ya está cargado así y la tienda online vuelve a aplicar sus descuentos.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', 'Servir junto: si hay muestras que lo impiden, Nesto ofrece borrarlas y desmarcar de una vez',
 'Hasta ahora, al desmarcar "Servir junto" con muestras que se quedarían pendientes, el aviso decía qué líneas había que borrar y había que ir a buscarlas, borrarlas una a una y volver a desmarcar. Ahora el aviso lista esas líneas y pregunta "¿Quieres borrarlas del pedido y desmarcar Servir junto?". No se borra nada sin decir que sí, y si aun borrándolas no se pudiera desmarcar, no se toca el pedido y se explica el motivo.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', 'Herramientas → Productos: el precio público se elige por modo, sin teclear el -1',
 'El precio público de la tienda tiene tres modos: precio fijo, descuento por defecto (30 %) o el mismo precio que el profesional. Antes había que saberse que "el mismo que el profesional" era teclear -1, y en la lista salía como "-1,00 €". Ahora se elige el modo en un desplegable, la casilla del importe solo aparece en "Precio fijo" (propone el precio público actual) y la lista de Revisar enseña el modo con palabras.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', 'El selector de direcciones de entrega enseña el número de contacto',
 'Al elegir la dirección de entrega de un pedido, cada dirección lleva ahora su número de contacto, para distinguir a simple vista direcciones parecidas del mismo cliente.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', 'En la plantilla de venta, la línea seleccionada vuelve a verse con el color del stock',
 'Al seleccionar una línea de las que se van metiendo en la plantilla, el texto perdía el color y no se sabía si había stock o no. Ahora la línea seleccionada mantiene su color sobre un fondo claro.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Un rapport nuevo que falla al guardarse ya no se queda en la lista',
 'Si al guardar un rapport nuevo daba error, el rapport aparecía igualmente en la lista como si se hubiera guardado. Ahora, si falla, no se añade.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Los comentarios largos del rapport tienen barra de desplazamiento',
 'En los rapports con comentarios largos no se podía bajar para leerlos enteros. Ahora la caja de comentarios tiene su barra de desplazamiento.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Abrir un pedido de compra ya no da error al cargar las ofertas',
 'Al abrir ciertos pedidos de compra (con líneas de texto, o con productos que ya no existen para ese proveedor) saltaba un error al cargar las ofertas y descuentos. Ahora esas líneas se saltan y el resto se carga con normalidad.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Los pedidos con portes de clientes sin vendedor vuelven a poder guardarse',
 'Al guardar un pedido con línea de portes para un cliente que no tiene vendedor en la ficha, el pedido se rechazaba con un error de servidor y no llegaba a crearse. Ahora la línea de portes se contabiliza al centro de coste de administración (CA) y el pedido se guarda.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Personas de mi centro: cada persona sale una sola vez',
 'En la pestaña Personas de mi centro, una persona que estaba en varios contactos del cliente salía repetida. Ahora sale una vez (por correo electrónico) y, al cambiarle el cargo, el cambio se aplica en todos sus contactos.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Mejorado', 'Los productos de peluquería (grupo PEL) ya no generan Ganavisiones',
 'Solo los grupos de cosmética (COS) y accesorios (ACC) suman para los Ganavisiones. La plantilla de venta, la app de vendedores y la app de clientes lo aplican igual porque la lista de grupos la manda el servidor.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', 'La etiqueta de recogida lleva el importe del reembolso cuando el pago es en efectivo',
 'Al crear la etiqueta de "Recoger producto" de un pedido que se paga en efectivo, el reembolso salía a 0. Ahora lleva el importe del pedido.', 'NestoAPI', 1, 'sa');

SELECT Version, Categoria, Titulo FROM Novedades WHERE Version = @version ORDER BY Id;
