/*
    Novedades de la versión 1.10.28.5 (22/09/2026).

    Tanda completa: NestoAPI + Nesto (ClickOnce 1.10.28.5). NO se sube la tercera cifra: son
    correcciones y mejoras menores; lo más gordo del día (el refactor de PedidoVenta y
    ControlesUsuario a CommunityToolkit) es interno y el usuario no debe notarlo.

    ORDEN:
      1) Publicar NestoAPI.
      2) Publicar la ClickOnce (1.10.28.5).
      3) Scripts/Issue506_ModoServicioSegunStock.sql  (el parámetro ModoServicioPorDefecto pasa
         de '3' a '0' = "según el stock"). Es indiferente ejecutarlo antes o después de publicar:
         la API vieja lee '0' como el modo de siempre y la nueva lee '3' como modo forzado.
      4) ESTE script.

    NO EJECUTAR TODAVÍA (esperan a otros):
      - Scripts/Issue499_ExigirDireccionVerificadaAlta.sql: hasta que Nesto#480 y NestoApp#180
        estén desplegados, o se rompen todas las altas de clientes.
      - Scripts/Odoo_RepublicarProductosDLQ_263.sql: hasta que el equipo de Odoo avise de que ha
        desplegado sus arreglos.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - Nesto#340 (4A.3): PedidoVenta, ControlesUsuario y 9 ViewModels pasan de Prism a
        CommunityToolkit. Cambio mecánico, misma pantalla.
      - NestoAPI#493: que el error de CTT se pueda leer cuando viene en otro formato, y el test que
        obliga a toda agencia nueva a declarar sus datos de canal.
      - NestoAPI#499: el guardarraíl de la dirección verificada nace APAGADO.
      - NestoAPI#511: el endpoint para republicar productos y que las fichas sin nombre no salgan
        a la tienda.
      - NestoAPI#457: el motor que calcula las ofertas escalonadas que un pedido podría aplicar
        (todavía no hay pantalla que lo enseñe).
      - El filtro de ELMAH para las sondas de los escáneres y el 400 de las búsquedas cortas.
      - Tests, scripts, issues.

    Ejecutar en SSMS contra NV. Los literales de texto van con N'...': las columnas son nvarchar y sin
    el prefijo cualquier carácter fuera de Windows-1252 se graba como '?'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.28.5'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-22';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Mejorado', N'El pedido de compra al proveedor enseña el pronto pago aparte',
 N'Hasta ahora, si el plazo de pago del proveedor tenía descuento por pronto pago, ese descuento se sumaba al de cada línea y el proveedor veía un porcentaje que no reconocía (por ejemplo un 47,75 % que en realidad era un 45 % más un 5 %). Ahora cada línea muestra su descuento de verdad y el pronto pago va al pie del documento, con su importe, igual que en los pedidos y facturas de venta. También se ve en el detalle del pedido de compra, y los pedidos que se hagan desde Nesto ya lo guardan solos a partir del plazo elegido.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', N'Los pedidos que entran solos eligen el modo de entrega según el stock',
 N'Los pedidos que llegan de la tienda online o de la app de clientes nacían siempre como «Tras reponer de tiendas», aunque no hubiera nada que reponer. Ahora se mira el stock real: si hay de todo nace «Todo junto»; si falta algo que no está en ninguna tienda, «Ahora lo que hay, el resto de una vez»; y solo si hay algo que traer de las tiendas, «Tras reponer de tiendas». En la plantilla de Nesto no cambia nada todavía: sigue proponiendo el modo de siempre hasta que se conecte con este cálculo.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', N'Menos correos marcados como «Financiación a revisar»',
 N'Desde que los pedidos nacen con modo de entrega, casi cualquier pedido a 30 y 60 días salía marcado para administración aunque no hubiera nada que revisar. Ahora se mira de verdad si el pedido se va a partir en varias facturas: los de fin de mes nunca se parten, y los de «Tras reponer de tiendas» solo si falta algo que no hay en ningún almacén. Lo que sí sale bajo por su importe se sigue marcando igual que antes.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Los presupuestos ya no se quedan a medias ni cogen los portes dos veces',
 N'Al pasar un pedido a presupuesto podían quedarse líneas sin pasar, mezcladas con las que sí, y cada vez que se modificaba o se aceptaba el presupuesto se añadía otra línea de portes. Ahora pasan todas las líneas que corresponde, las que se añaden a un presupuesto nacen como presupuesto, los portes se ponen una sola vez y, si un guardado fuera a dejar el pedido mezclado, se avisa en vez de guardarlo así.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Cambiar la fecha de entrega cuando ya hay picking avisa en vez de callarse',
 N'Si el pedido ya tenía picking y se cambiaba la fecha de entrega, la fecha no cambiaba en las líneas preparadas pero el correo del pedido salía con la fecha nueva, así que parecía que sí se había cambiado. Ahora Nesto solo cambia la fecha de las líneas que todavía se pueden tocar y dice cuántas se han quedado como estaban; si no se puede cambiar ninguna, lo avisa y explica que hay que quitar el picking primero. Además, la línea de portes que se añade al modificar un pedido ya no se queda con una fecha distinta a la de la mercancía.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Confirmar el envío de un pedido de Amazon o de la web con una agencia nueva',
 N'Al confirmar el envío de un pedido de canales externos, Nesto reconocía la agencia por el enlace de seguimiento y tenía la lista escrita por dentro, así que con CTT Express no sabía qué decirle a Amazon y fallaba sin dejar ningún rastro. Ahora el transportista lo dice siempre el servidor, y si algo falla el motivo queda registrado y se ve en pantalla. Una agencia nueva ya no puede quedarse a medias sin que nos enteremos.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'El reembolso de un envío ya tramitado no se puede cambiar sin avisar a la agencia',
 N'En Agencias, pestaña En curso, se podía cambiar el importe del reembolso de un envío que ya estaba registrado en la agencia: cambiaba en Nesto pero no en la agencia, que seguía cobrando el importe antiguo. Ahora ese cambio se rechaza y se explica que hay que borrar el envío (que lo anula también en la agencia) y volver a tramitarlo. Por otro lado, al crear una etiqueta ya no se propone como reembolso una entrada que el cliente había pagado por adelantado.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'Odoo ya no se queda sin el comercial del cliente',
 N'Cuando Nesto publicaba un cliente y no conseguía el correo de su vendedor, Odoo entendía que había que quitarle el comercial y lo borraba sin avisar a nadie. Ahora, si el vendedor no tiene correo en su ficha, el dato sencillamente no viaja y Odoo conserva el que tenía; solo se le quita el comercial cuando el cliente está de verdad en el vendedor general. Además, las republicaciones nocturnas vuelven a llevar las personas de contacto.', 'Nesto', 1, 'sa');
