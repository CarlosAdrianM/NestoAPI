/*
    Novedades de la versión 1.10.61.0 (12/11/2026): API + Nesto. Sube la tercera cifra: lleva ventanas nuevas (Recorrido del
    almacén, Recogidas solicitadas).

    ORDEN:
      1) Scripts de tablas (sa), YA lanzados el 12/11: Issue855_RecogidasSolicitadas, Issue859_RecogidasImprevistas,
         Issue858_PedidosRetenidosPorFalta, Issue857_ComprasCorreosSeguimiento, Issue590_Facturas_BloqueoNestoViejo (sombra),
         Issue858_NoEsta_Interruptor. Issue641_RecorridoCalles estaba desde el 15/10.
      2) Publicar la API (master). 3) Publicar Nesto. 4) ESTE script. 5) Novedades_Ariadna_2_35.sql cuando la 2.35 llegue a las PDA.

    SOLO SI SE PUBLICA NESTO: todas las de abajo menos las marcadas «(solo API)», que valen aunque Nesto no salga.

    Se omiten a propósito (nada perceptible todavía o va por otro lado):
      - «No está» = hueco a cero y la recogida imprevista del reparto: van en las novedades de Ariadna 2.35.
      - Correos de seguimiento de compras@ en sombra (NestoAPI#857) y su histórico: nada visible.
      - DELETE api/Inventarios/Rellenar (deshacer el rellenado de una tienda): solo por API, Dirección e Informática.
      - El asunto del correo interno del recordatorio de reposición; los festivos de 2099/2100; el registro de Nesto viejo (#590).
      - Novedades por «última novedad vista» en Nesto (#495): es el propio mecanismo del aviso.
      - El arreglo del fallo al elegir una agencia antigua en Agencias (no cambia nada para las agencias en uso).
      - La parte de NestoApp (recogidas solicitadas #259, disquete del rapport #276, decidir retenidos #279): va con la app.
    NO duplicar: 1181 (Aida), 1215 (Sancho), 1227/1234 (Alfredo) y 1233 (Reina) se contestan en su hilo con la versión.

    Ejecutar en SSMS contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.61.0';
DECLARE @fecha DATE = '2026-11-12';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Recogidas solicitadas por los vendedores',
     N'Ventana nueva (Almacén y envíos › Recogidas solicitadas): el vendedor pide desde NestoApp que se recoja mercancía en un cliente y aquí el almacén la confirma (corrigiendo bultos o texto si hace falta) o la rechaza con su motivo. Puede recogerla el reparto propio o el propio vendedor; en los dos casos se crea la nota de recogida y se cierra leyendo su código al llegar a Algete. La lista se actualiza sola y el aviso de la campana abre la ventana. Si una solicitud lleva un día laborable sin contestar, se avisa al almacén y a quien la pidió.',
     NULL),
    ('Nuevo', N'Recorrido del almacén: ordenar las calles',
     N'Ventana nueva (Almacén y envíos › Recorrido del almacén) para decidir el orden en que la PDA manda recoger: las calles, su sentido, los pasillos de cada una y la fila que se coge a mano. Tiene «Serpentina» para alternar el sentido y «Vista previa» para ver el recorrido resultante antes de guardar. Guardan Almacén y Dirección; los demás la ven sin poder cambiarla.',
     NULL),
    ('Nuevo', N'Facturado en tienda: acumulado del año y comparativo con otros años',
     N'En «Facturado en tienda» se puede elegir, además de la semana de siempre, el acumulado del año mes a mes y el comparativo con los años anteriores (dos por defecto), con la variación en euros y en porcentaje. Se ve en pantalla y se puede guardar en Excel.',
     NULL),
    ('Nuevo', N'Casilla «Recoge en tienda» en la plantilla y en el pedido',
     N'En los pedidos de Algete se puede marcar que el cliente recoge en tienda: el pedido pasa a la ruta de recogida y no lleva portes ni comisión contra reembolso. En Alcobendas y Reina sale marcada y fija. No se puede cambiar si el pedido ya tiene picking, albarán o factura.',
     NULL),
    ('Mejorado', N'Plantilla de venta: el color de cada línea sigue al stock del almacén elegido',
     N'El color de las líneas (y el texto de unidades disponibles) se calcula ahora con el stock del almacén que tiene elegido, también al cambiar de almacén. Antes se usaba el del primer almacén de su lista, normalmente Algete, y una línea podía salir en verde sin haber stock en su almacén. Es posible que vea en otro color líneas que antes salían en verde: ahora el color es el correcto.',
     NULL),
    ('Mejorado', N'Si el almacén no encuentra un producto de su pedido, se le avisa (solo API)',
     N'Cuando al preparar un pedido el almacén marca que un producto no está, quien pasó el pedido recibe un aviso en la campana (y en NestoApp) con lo que falta, lo que hay en otros almacenes, si hay compra pendiente y, si el pedido es «al completar», que no se facturará mientras esa línea siga pendiente. Si lo que falta es una muestra o material de regalo, la línea se quita del pedido para que no lo bloquee.',
     NULL),
    ('Mejorado', N'Alquileres: nombre del equipo y del cliente en la relación',
     N'Al buscar los alquileres de un cliente, la relación enseña una columna «Equipo» con la referencia y el nombre de cada máquina; y siempre aparece el nombre del cliente junto a su número.',
     NULL),
    ('Mejorado', N'Inventario: las filas sin contabilizar de días anteriores se pueden borrar (solo API)',
     N'Una fila de inventario que se quedó sin contabilizar otro día ya no deja bloqueado el inventario ni el picking: en las comprobaciones aparece con «Borrar esta fila». Borrarla no pone el producto a cero: lo deja sin contar.',
     NULL),
    ('Corregido', N'Picking parado por un inventario abierto: ahora dice de qué producto (solo API)',
     N'Si no se puede sacar el picking porque hay inventario sin contabilizar, el mensaje indica los productos, el almacén, el día y quién los contó, en vez del error genérico.',
     NULL),
    ('Nuevo', N'Pedidos «todo junto»: si falta algo de pago, el almacén espera a que usted decida',
     N'Cuando a un pedido que debe servirse completo le falta en el almacén un producto de pago, ya no se empaqueta lo que hay: queda retenido y quien pasó el pedido recibe un aviso. Al abrir el pedido verá una banda roja con lo que falta y dos botones: «Servir lo que hay» o «Esperar a tenerlo completo». Si no decide en dos horas laborables, se espera a tenerlo completo. Si lo que falta es una muestra, no se retiene.',
     NULL),
    ('Mejorado', N'Eventos: al poner sin coste un evento con inscritos, las reservas sin pagar pasan a inscritas',
     N'Si un evento se dio de alta con señal por error y se corrige a sin coste, las reservas que no han pagado nada pasan solas a inscritas, se anulan sus enlaces de pago y, si caben, se recuperan las que habían caducado. Nesto le dice cuántas ha cambiado. Si alguna ya tiene un pago, una factura o un cupón, no se cambia nada y se explica el motivo. Además, al crear un curso que se gana comprando productos, Nesto avisa si se queda con señal.',
     NULL),
    ('Mejorado', N'Inventario en Algete: se cuenta por huecos',
     N'En Productos › Inventarios, en Algete cada producto se cuenta en su hueco: hay que indicar pasillo, fila y columna (se quedan puestos de un producto al siguiente). Así no quedan filas sin ubicación, que no se podían contabilizar y paraban el picking. En las tiendas no cambia nada.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
