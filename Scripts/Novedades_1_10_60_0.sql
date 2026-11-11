/*
    Novedades de la versión 1.10.60.0 de Nesto (11/11/2026).
    Alquileres por cliente (1181), menciones en la campana (1183), «Mostrar Sólo Retenidos» (1184), dejar una referencia a cero en
    todas sus ubicaciones (1185), borrar una fila del inventario (incidencia 1186), consulta de reembolso urgente con «Contestar…»,
    aviso de contado en las campañas (feria online 12-16/11), el teórico del inventario sin las reservas de picking (el 37215),
    «No declarar…» en Verifactu, la campana abre la ficha del lead (Nesto#634), cliente/contacto en los correos de incidencia de
    agencia y el estado «De camino a punto CTT».

    ORDEN:
      1) Scripts sa ANTES de publicar la API: Issue849_PagosTPV_Pedido.sql (la API lee las columnas nuevas de PagosTPV),
         Issue784_InventarioBorrarFilas.sql (enciende «InventarioBorrarFilas» a Alfredo y Santiago) y EventosCandidatosSombra.sql
         (NestoAPI#850; sin él, los candidatos a eventos no se guardan y nada falla). Antes o después: FechasEntregaFotos.sql
         (NestoAPI#853, medir en sombra el error de las fechas de entrega; sin él, el job no hace nada).
         DESPUÉS de publicar la API: EmbudoBloqueDiario_Interruptor.sql (enciende el bloque diario del embudo, 50 clientes a las 7:00).
         Cuando se quiera: OneShot_20261111_Clientes_ComentariosHtmlHistoricos.sql (deja la transacción abierta) y los centros de
         códigos postales (CentrosCodigosPostales_GeoNames.ps1, para la distancia de los eventos en sombra).
         Ya lanzados por Carlos el 11/11: Sugerencia1183_AvisoEmergenteMenciones.sql, FeriaOnline_CampanasCondicionContado.sql y
         UbicacionesDeshacerAlbaranEspejo_prdDeshacerExtractoProducto.sql.
         NO lanzar OneShot_20261111_Feria_AplicarDto_Activar.sql ni OneShot_20261117_Feria_AplicarDto_Restaurar.sql: lo hace el job
         «campanas-aplicar-dto».
      2) Publicar la API (master) → ClickOnce (Clean + Rebuild) → 1.10.60.0 → Ariadna 2.34.
      3) DESPUÉS de publicar la API y la ClickOnce: ESTE script, Novedades_Ariadna_2_34.sql y el PUT de las sugerencias implementadas.
      NO duplicar (la sugerencia ES la novedad; van por PUT api/Novedades/Sugerencias/{id} con Titulo, Descripcion y Version, y aquí no
      llevan fila): 1181 (Aida, todos los alquileres de un cliente), 1183 (Magán, la campana resalta las menciones y, si se enciende,
      las avisa con una ventanita; lleva el vídeo de la versión), 1184 (Paloma, «Mostrar Sólo Retenidos»), 1185 (Alfredo, «Dejar a
      cero…» una referencia en todas sus ubicaciones), 1186 (Alfredo, «Borrar esta fila» del inventario), 1167 (Paloma, los correos
      enviados a mano salen a nombre de quien los envía), 780 (Laura, Pack Regalo solo como categoría principal y sin otras, salvo
      las de Outlet: decisión de Carlos del 11/11) y 1192 (Paloma, el precio con IVA en la plantilla para el cliente de público final).
      Las incidencias 1189 y 1190 (Miguel, la furgoneta) y la 1169 (Santiago, etiquetas cruzadas) van con Ariadna 2.34.
      Se omiten a propósito (nada perceptible para el usuario o va por otro lado): la barrera frente al eco de Odoo en la
      sincronización de clientes, el job «ubicaciones-huerfanas», el arreglo del traspaso a la empresa espejo y de
      prdDeshacerExtractoProducto (37573/38114/38146), el pedido guardado en el enlace de pago (NestoAPI#849, corte 1), la historia
      del envío recortada (envío 250766), el recordatorio y el escalado de las consultas de reembolso sin contestar, «Aplicar Dto»
      de las campañas por Hangfire, la validación por día solo para TiendaOnline/Dirección, el auto-curado de los portes en las
      rectificativas (RV2600129), rehacer una rectificativa en su serie normal (RV2600154/155) y que el pronto pago cuente como
      contado (va dentro de la fila del aviso de contado), el bloque diario del embudo de Odoo y el ajuste automático de su
      ritmo (jobs embudo-bloque-diario y embudo-ajuste-ritmo), los candidatos a eventos en modo sombra (NestoAPI#850) y el
      aviso a Almacén cuando un repartidor quita un bulto de su furgoneta sin leerlo (llega solo por la campana).

    Ejecutar en SSMS contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.60.0';
DECLARE @fecha DATE = '2026-11-11';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Gracias, Aida, Magán, Paloma, Laura y Alfredo',
     N'Esta versión sale casi entera de lo que nos han contado por «Novedades»: ver todos los alquileres de un cliente (Aida), que la campana destaque cuando alguien le menciona (Magán), la casilla «Mostrar Sólo Retenidos» y que los correos que usted envía salgan a su nombre (Paloma), que un Pack Regalo no pueda estar en otra categoría (Laura), y dejar una referencia a cero en todas sus ubicaciones y borrar una fila del inventario que sobra (Alfredo). Muchas gracias: sigan contándonos.',
     NULL),
    ('Mejorado', N'Consulta de reembolso: se contesta desde el propio aviso',
     N'Cuando un vendedor o el almacén consulta si un pedido puede salir contra reembolso, a Administración le sale ahora un aviso urgente (la ventana roja con pitido) con el botón «Contestar…», que abre directamente la consulta: ya no hay que buscarla en la campana. Si nadie contesta, Nesto lo recuerda a media mañana y a media tarde, y cuando lleva más de un día laborable sin respuesta avisa también a Dirección.',
     N'Administración,Dirección'),
    ('Nuevo', N'Campañas al contado: Nesto propone cambiar los plazos',
     N'Algunas campañas (como la feria online del 12 al 16 de noviembre) solo valen si el pedido se paga al contado. Al crear un pedido, en el detalle o en la plantilla, con productos de una campaña así y unos plazos que no son de contado, Nesto avisa y propone cambiarlos a contado. Un pedido con descuento por pronto pago cuenta como contado y no avisa.',
     NULL),
    ('Corregido', N'Inventario: lo recogido para un pedido ya no cuenta como teórico del hueco',
     N'Al inventariar una ubicación, Nesto proponía como teórico también las unidades ya recogidas para un pedido que aún no se ha albaranado: no están en el hueco, se contaban como 0 y al contabilizar se daban de baja del stock. Ahora el teórico es solo lo que está libre en el hueco, y si la ubicación tiene unidades reservadas Nesto lo avisa al rellenarla. Las comprobaciones antes de contabilizar avisan si alguna fila se rellenó con reservas, y no deja contabilizar hasta arreglarla.',
     N'Almacén,Dirección'),
    ('Mejorado', N'Inventario: Almacén ya puede contabilizar',
     N'El botón «Contabilizar inventario» lo ven ahora también los usuarios de Almacén, para cerrar su propio recuento sin esperar a Administración.',
     N'Almacén'),
    ('Mejorado', N'Picking: el mensaje de «ubicación descuadrada» dice qué pasa de verdad',
     N'Cuando un producto no puede salir en el picking porque todas sus unidades ubicadas están ya reservadas para otros pedidos, el mensaje ya no habla de una ubicación descuadrada: dice que no queda stock libre del producto y para qué pedidos están reservadas las unidades.',
     N'Almacén,Dirección'),
    ('Nuevo', N'Verifactu: «No declarar…» una factura',
     N'En la ventana de facturas pendientes de Verifactu, el botón «No declarar…» aparta una factura anterior a la entrada en vigor que no se va a enviar (por ejemplo, una que no tiene arreglo y se rehízo por otro lado). Deja de salir en la lista de pendientes y queda anotado quién la apartó.',
     N'Administración,Dirección'),
    ('Corregido', N'Reparto propio: no se reimprime la etiqueta de una recogida o de una nota de cobro',
     N'La parada de una nota de recogida o de cobro no lleva bulto que etiquetar (los bultos de una recogida llevan su propia etiqueta NR). Si se intenta reimprimir su etiqueta RP, Nesto avisa del motivo en vez de imprimirla: esa etiqueta acababa leyéndose al cargar y dejaba en la furgoneta un bulto que no existía.',
     N'Almacén'),
    ('Corregido', N'Agencias: si la etiqueta no llega a la impresora, Nesto lo dice',
     N'Cuando la impresora de etiquetas estaba dormida (pasaba sobre todo con la primera etiqueta del día), la etiqueta no salía y Nesto no avisaba. Ahora Nesto vuelve a intentarlo una vez y, si sigue sin poder enviarla, le avisa con el nombre de la impresora para que la reimprima. Vale para las etiquetas de todas las agencias y para las de alquileres.',
     N'Almacén,Administración'),
    ('Corregido', N'Agencias: «Borrar Envío» explica por qué no se puede borrar',
     N'Al borrar un envío que la base de datos no deja borrar (por ejemplo, el que nació de volver a enviar un envío devuelto), Nesto enseñaba un error en inglés. Ahora explica el motivo en castellano.',
     N'Almacén'),
    ('Mejorado', N'La campana abre la ficha del cliente en los avisos de lead',
     N'Al pulsar en la campana un aviso de lead nuevo o de lead sin llamar, Nesto abre la ficha de ese cliente.',
     NULL),
    ('Mejorado', N'Agencias: los correos de incidencia dicen cliente y contacto',
     N'Los avisos de incidencia de un envío llevan ahora el cliente con su contacto (12345/0) en vez de solo el cliente, para saber a qué dirección se refiere. Además, el estado de CTT «De camino a punto CTT» ya sale con su nombre en el seguimiento.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
