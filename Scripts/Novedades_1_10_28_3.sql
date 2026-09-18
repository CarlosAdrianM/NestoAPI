/*
    Novedades de la versión 1.10.28.3 (18/09/2026).

    Tanda completa: NestoAPI + Nesto (ClickOnce 1.10.28.3). NO se sube la tercera cifra: son correcciones y
    mejoras menores; lo único nuevo que ve el usuario es el país fiscal en la ficha comercial. Lo grande del
    día (Agencias sin Entity Framework) va detrás de interruptores y nadie lo nota hasta encenderlos.

    ORDEN:
      1) Publicar NestoAPI.
      2) Publicar la ClickOnce.
      3) Scripts/Nesto340_GrantSpsModificarEnvio.sql (GRANT de prdDesliquidar y prdModificarEfectoCliente).
      4) Scripts/Nesto415_ParametroPagarReembolsosPorApi.sql y Scripts/Nesto340_ParametroModificarEnvioPorApi.sql
         ((defecto) = EF, piloto Carlos = API).
      5) ESTE script.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - Nesto#340 A4.2: «Recibir retorno» guarda por la API (misma pantalla, mismo resultado).
      - Nesto#340 A4.3/A4.4 y Nesto#415: pago de reembolsos y modificar/rehusar envíos por la API, detrás de
        los parámetros PagarReembolsosPorApi y ModificarEnvioPorApi (solo el piloto).
      - Nesto#479: las llamadas de la ficha de producto llevan el usuario (se nota solo en ELMAH).
      - NestoAPI#457 (corte 2): ofertas sugeridas por importe de pedido; todavía sin pantalla.
      - NestoAPI#401: la consulta ya no admite texto inyectado en el vendedor (seguridad).
      - Tests, scripts, issues.

    Ejecutar en SSMS contra NV. Los literales de texto van con N'...': las columnas son nvarchar y sin
    el prefijo cualquier carácter fuera de Windows-1252 se graba como '?'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.28.3'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-18';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Nuevo', N'El país fiscal del cliente se puede cambiar desde la ficha comercial',
 N'En la cabecera de la ficha comercial, junto a los vendedores, hay un nuevo desplegable «País fiscal» con el país al que pertenece el cliente a efectos de impuestos. Se guarda con el mismo botón «Guardar» de los vendedores. No es el país de la cuenta bancaria, que sigue apareciendo junto al banco. Hasta ahora solo se podía cambiar desde «Editar cliente»; todos los clientes antiguos figuran como España, así que conviene ir corrigiéndolo al trabajar con cada cliente extranjero.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', N'El correo de «Pedidos sin picking» explica qué días cierra el cliente',
 N'Cuando un pedido no coge picking porque la entrega caería en un día que el cliente tiene cerrado, el correo mostraba los días de servir como un código («01111») que nadie sabía leer. Ahora dice en palabras qué día cierra el cliente y enseña una columna por día, de lunes a viernes, con «Abierto» o «Cerrado», resaltando el día en que caería la entrega.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', N'La lista de pedidos de la app ya no se queda cargando sin vendedor asignado',
 N'Para los usuarios de la app sin vendedor asignado (administración), la lista de pedidos pendientes tardaba tanto que acababa en error de tiempo agotado. Ahora se carga mucho más rápido. Además, algunos pedidos en los que participaban varios vendedores por grupo de producto mostraban el importe duplicado en esa lista; ya sale el importe correcto.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', N'La lista de clientes a llamar o visitar debería cargar más rápido',
 N'La lista de clientes recomendados para llamar o visitar, la primera que abre el comercial por la mañana, podía agotar el tiempo de espera con los vendedores que tienen muchos clientes. Se ha rehecho la consulta para que tarde bastante menos y, si aun así tarda, espera más antes de dar error.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Al montar un kit sin cantidad suficiente se explica el motivo',
 N'Al montar un kit desde la ficha del producto, si no había unidades suficientes de algún componente el aviso decía solo «No se han podido traspasar los movimientos de producto», sin el motivo. Ahora añade «No hay cantidad suficiente para montar el kit» (y, en general, el motivo que dé el servidor).', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'El doble clic en la fila vacía del pedido de compra ya no da error',
 N'En el pedido de compra, hacer doble clic en la última fila (la vacía, para añadir una línea nueva) mostraba un error técnico. Ya no pasa nada raro al hacerlo.', 'Nesto', 1, 'sa');
