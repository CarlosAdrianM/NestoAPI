/*
    Novedades de la versión 1.10.38.0 de Nesto (07/10/2026) y de la 2.2 de Ariadna.
    Versión de la ClickOnce: 1.10.38.0 (pubxml 1.10.38.* en revisión 0). Ariadna: ApplicationDisplayVersion 2.2.

    Tanda: NestoAPI (señales de eventos #591, sugerencias de contacto #603 con modelo reentrenado, avisos de ficha #604,
    varios códigos de barras #605, modo de facturación en el correo #598, Verifactu UE #599, rapports 520, facturas por
    correo para la tienda online TNV#81, seguridad #601, calendario de reposiciones #577 c1, textos por tipo #600)
    + Nesto (eventos y señales #591, clientes para contactar #603, códigos de barras #605, Novedades 513/518, fondos 519,
    iconos, control de stock #512, Cajas #514, CP #596, modernización #490 5.º tramo)
    + Ariadna 2.2 (503, permiso de foto #18, versión en la entrada #16, textos del servidor #600, avisos #604, códigos #605).

    ORDEN:
      1) Scripts sa ANTES de publicar la API: Issue577_ReposicionesCalendario.sql, Issue591_EventosSenales.sql,
         Issue603_SugerenciasContacto.sql, Issue603_FrasesRitmoYRecordatorio.sql, Issue604_AvisosFichaCodigoEscaner.sql,
         Issue605_ProductosCodigosBarras.sql.
         Issue596_LimpiezaCodigosPostalesPortugal.sql ANTES de publicar Nesto (primero en simulación).
         Issue538_LimpiezaCabecerasPedidoVacias.sql cuando se quiera (simulación primero).
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce (Clean + Rebuild), los flujos de Ariadna (Play interna y Windows) y TNV (pista interna).
         API y Ariadna deben salir juntas (#604: la Ariadna anterior perdería avisos con la API nueva).
      4) DESPUÉS: ESTE script (idempotente: compara por Versión/Ámbito + Título).
      5) Marcar implementadas las sugerencias 518, 519 y 520 (1.10.38.0) y 503 (2.2) por la API
         (PUT api/Novedades/Sugerencias/{id}); NO se insertan aquí para no duplicarlas.

    SE OMITE A PROPÓSITO:
      - Seguridad #601 (descarga de facturas con token): no cambia nada para el usuario.
      - Calendario de reposiciones #577 (corte 1, solo API) y textos por tipo #600: todavía sin pantalla.
      - Facturas por correo desde la tienda online (TNV#81): no hay ámbito TNV en Novedades.
      - Scripts de limpieza #596 y #538: tarea interna.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.38.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-10-07';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Eventos y señales de cursos reembolsables',
     N'En la pestaña Clientes hay dos ventanas nuevas. «Eventos» (Tienda online): das de alta cada curso o masterclass con su fecha y el importe de la señal, y ya no hace falta mandar las fechas por correo. «Señales de eventos»: la lista de señales cobradas con su estado (pendiente hasta la fecha del evento, liberada desde ese día, sin compra si pasan 15 días y consumida cuando se liquida), con el importe cobrado y lo que queda a favor. En el extracto del cliente, Administración marca un cobro a cuenta como «señal del evento…» y puede quitarlo. El día del evento, Administración recibe un correo con las señales que ya se pueden liquidar y las que llevan 15 días sin compra.'),
    ('Mejorado', N'El correo del pedido dice el modo de facturación',
     N'El correo que se manda al crear o modificar un pedido enseña, junto al modo de entrega, el modo de facturación («Por entregas», «Al completar el pedido» o «Todo ahora y lo pendiente después»), en vez del antiguo «Marcado mantener junto».'),
    ('Mejorado', N'El código postal se escribe como quieras y Nesto lo guarda igual',
     N'Al crear un cliente, el código postal se normaliza al salir del campo (4480 670 o 4480670 quedan 4480-670; un español de cuatro cifras recupera el cero). En el mantenimiento de códigos postales, un código portugués se encuentra escrito de cualquier forma, se ve el formato correcto junto al guardado, y los duplicados por formato salen marcados y no se pueden volver a guardar.'),
    ('Mejorado', N'Iconos propios en la cinta',
     N'Recibir y Enviar reposición, Enlaces de pago, Facturas Verifactu y Pedidos de compra tienen ahora su propio icono: antes repetían el de Diarios, Bancos, NIF incorrectos o Pedidos de venta.'),
    ('Corregido', N'Cobrar a la vez facturas de dos empresas descuadraba el asiento',
     N'En Cajas, al cobrar en un mismo cobro facturas de un cliente que eran de empresas distintas, salía «El asiento está descuadrado» y había que cobrarlas una a una. Ahora cada empresa lleva su contrapartida al banco con lo cobrado de esa empresa.'),
    ('Corregido', N'La ficha de producto ya no falla al guardar el control de stock',
     N'Si el control de stock de un almacén ya existía, al guardar salía «No se ha podido modificar el control de stock» sin más explicación. Ahora lo modifica directamente y, si algo falla, dice el motivo.'),
    ('Corregido', N'Las facturas a clientes de la Unión Europea se declaran con su NIF-IVA completo',
     N'Verifactu rechazaba la factura de un cliente de Portugal porque el NIF se mandaba sin el prefijo del país. Ahora va como «PT311482473»; las facturas rechazadas se reintentan solas.'),
    ('Nuevo', N'Clientes para contactar por prioridad, con el motivo y tu ritmo del mes',
     N'En Rapports, la lista de clientes para contactar ya no la ordena solo el modelo: cada cliente sale con su prioridad (Máxima, Alta, Media o Baja) y el motivo («Compra cada 10 días y lleva 34 sin hablar contigo»). La cadencia es de cada cliente según lo que compra, así que no se repite a quien acabas de llamar. Arriba ves cuántos contactos llevas hoy, esta semana y este mes, el objetivo para cubrir toda tu cartera y una frase que cambia cada día. Los clientes ya atendidos quedan marcados al final. El modelo de probabilidad se ha reentrenado con tres años de llamadas y acierta bastante más.'),
    ('Nuevo', N'Varios códigos de barras por producto',
     N'En la ficha de producto hay una pestaña «Códigos de barras»: un producto puede tener varios (el de cada lote del proveedor, el de la caja de 100…), con uno principal. Compras puede añadir, hacer principal y dar de baja. La plantilla y las líneas de pedido encuentran el producto por cualquiera de sus códigos y, si un código es de varios productos, lo preguntan. En el almacén, Ariadna también casa por cualquiera y los mozos pueden añadir un código nuevo al momento.'),
    ('Mejorado', N'Los avisos de ficha desde el almacén explican el código leído',
     N'Cuando un mozo avisa desde Ariadna de un código de barras mal, el correo a Compras dice si ese código es de otro producto (y cuál), si es el mismo de la ficha (el envase no se ha podido leer) o si no está en ninguna ficha, y si se leyó con el escáner o se tecleó.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;

-- Ariadna 2.2 (lo de pinchar un picking en Salidas es la sugerencia 503 de Alfredo: se marca implementada por la API)
DECLARE @ariadna TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @ariadna (Categoria, Titulo, Descripcion) VALUES
    ('Corregido', N'La foto del bulto en móviles con Android 9 o anterior',
     N'En esos móviles la cámara necesita permiso de almacenamiento para guardar la foto y la app fallaba sin avisar. Ahora pide el permiso y, si se deniega (o se deniega el de la cámara), lo dice claramente para que lo actives en Ajustes.'),
    ('Mejorado', N'La versión instalada también se ve al elegir mozo',
     N'La pantalla de entrada enseña abajo la versión y la compilación instaladas, igual que el inicio, para saber si una PDA está al día sin iniciar sesión.'),
    ('Mejorado', N'«Algo está mal» te avisa antes de mandar el correo',
     N'Si el código que lees es de otro producto, Ariadna te lo dice al momento («Ese código es del 32564 GUANTES… T/P. Comprueba el hueco») sin molestar a Compras; si es el mismo de la ficha, te pide que leas el del envase o hagas una foto. Solo si vuelves a enviar se manda el aviso. Además distingue si el código lo leíste con el escáner o lo tecleaste.'),
    ('Nuevo', N'Un producto puede tener varios códigos de barras, y los añades tú',
     N'Al recoger, empaquetar, recibir o ubicar se acepta cualquiera de los códigos del producto, no solo el de la ficha. Si un código no casa, pulsa F7 (roja + 7) para añadirlo como código del producto de esa parada; si ya lo tiene otro producto, te pregunta antes. También puedes añadirlo desde «Algo está mal» con F2. Así los guantes que cambian de código con cada lote dejan de dar guerra.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.2', @fecha, a.Categoria, a.Titulo, a.Descripcion, 'Ariadna', 1, 'sa'
FROM @ariadna a
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = a.Titulo);

SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;

-- Comprobación: 10 filas de Nesto con esta versión y 4 de Ariadna con la 2.2.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version IN (@version, '2.2') ORDER BY Id;
