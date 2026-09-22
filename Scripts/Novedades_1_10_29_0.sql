/*
    Novedades de la versión 1.10.29.0 (22/09/2026, segunda publicación del día).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.29.0). SÍ se sube la tercera cifra: la plantilla de
    venta estrena dos cosas que se ven (el modo de entrega preseleccionado con su motivo y el
    aviso de ofertas no aplicadas) y el alta de cliente cambia de comportamiento (Nesto#480).

    ORDEN:
      1) Publicar NestoAPI.
      2) Publicar la ClickOnce (1.10.29.0).
      3) ESTE script.

    El parámetro ModoServicioPorDefecto ya se pasó a '0' esta misma mañana con
    Scripts/Issue506_ModoServicioSegunStock.sql: no hay que repetirlo.

    SCRIPTS PENDIENTES, con su condición:
      - Scripts/Odoo_RepublicarProductosDLQ_263.sql: LISTO. Odoo desplegó sus arreglos el 22/09
        (odoo#19 y odoo#21) y NestoAPI#511 se publicó esta mañana. Solo falta lanzarlo FUERA DE
        HORARIO, que es lo acordado con ellos.
      - Scripts/Issue499_ExigirDireccionVerificadaAlta.sql: con Nesto#480 (esta versión) y
        NestoApp#180 (2.20.4) el código ya está. Lo que queda es que NO QUEDEN VERSIONES VIEJAS
        instaladas: quien siga con una NestoApp anterior a la 2.20.4 no manda DireccionVerificada
        y sus altas empezarían a fallar. Mejor mañana, con todo el mundo actualizado, que esta
        noche. Se apaga con UPDATE ... SET Valor = '0'.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - Que el POST a api/Clientes empiece a mandar DireccionVerificada (es el fontanero de #480).
      - NestoAPI#515 en sí (el cálculo del color con la cantidad): se cuenta por su efecto, no por
        cómo está hecho.
      - El retardo con el que la plantilla pregunta al servidor y el que las llamadas fallidas no
        bloqueen el pedido.
      - Tests, DTOs, issues.

    Ejecutar en SSMS contra NV. Los literales de texto van con N'...': las columnas son nvarchar y sin
    el prefijo cualquier carácter fuera de Windows-1252 se graba como '?'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.29.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-22';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Nuevo', N'La plantilla avisa de las ofertas que el pedido podría llevar',
 N'Hasta ahora había que saberse las ofertas de memoria: si nadie caía en que faltaba una unidad para el 6+1, el cliente se quedaba sin ella. Ahora, mientras se montan las líneas, aparece debajo del pedido un aviso con las ofertas que se podrían aplicar y no se están aplicando: las de cantidad («este producto tiene un 2+1 y no lo estás aplicando», «con 1 unidad más te llevas otra de regalo»), los regalos por importe de pedido («añadiendo 12,00 € más entra el regalo») y los descuentos por volumen. En las de cantidad hay un botón «Aplicar» que pone las unidades solo, sin teclear nada. Es solo un aviso: se puede ignorar y el pedido se cierra igual. Los mismos avisos salen ya en la app de vendedores.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Nuevo', N'El modo de entrega viene propuesto según el stock, y dice por qué',
 N'El desplegable «Servir» de la plantilla proponía siempre «Tras reponer de tiendas», hubiera stock o no. Ahora lo propone el servidor mirando el stock real de las líneas del pedido, y debajo explica el motivo: «Hay stock de todo en el almacén del pedido: sale todo junto», «2 líneas hay que traerlas de las tiendas: se espera a la reposición»... Se recalcula al cambiar las líneas y al pasar de paso, pero si se elige un modo a mano ya no se toca. Es la misma regla que aplican la tienda online, la app y los pedidos que entran solos, así que a partir de ahora todos proponen lo mismo.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Corregido', N'El modo de entrega que se propone ya cuenta las unidades que se piden',
 N'El cálculo que se estrenó esta mañana miraba si quedaba stock del producto, pero no cuántas unidades se estaban pidiendo: con 7 unidades en Algete y 4 en Reina, pidiendo 8, decía «hay stock de todo». Bastaba una unidad de cada referencia para que un pedido entero saliera como «Todo junto». Ahora se descuenta lo que pide cada línea, y las dos líneas de un mismo producto (la normal y la de regalo de una oferta) cuentan juntas, porque salen del mismo almacén. Afecta también a los pedidos de la tienda online y de la app de clientes, que nacen con ese modo.', 'Nesto', 1, 'sa'),

(@version, @fecha, 'Mejorado', N'En el alta de cliente, la dirección se elige de Google y ya no se puede retocar',
 N'Lo normal era elegir la dirección que proponía Google y luego añadirle cosas al final (el portal, el local, «junto a...»), con lo que la dirección dejaba de ser la que Google había validado y la ficha quedaba mal escrita; eso es justo lo que luego no encuentra la agencia de transporte. Ahora, al elegir una dirección de la lista, el campo se pone verde y queda bloqueado: para cambiarla hay que borrarla con el botón de al lado y buscar otra. Todo lo demás (portal, piso, nombre del centro, referencias) va en «Dirección (resto de información)», que sigue siendo libre. Y ya no se puede terminar el alta con una dirección escrita a mano: el botón Finalizar se queda apagado y explica por qué. Si alguna vez Google no encuentra una dirección, avisa y se da de alta desde el Nesto viejo. En la app de vendedores funciona igual desde la versión 2.20.4.', 'Nesto', 1, 'sa');
