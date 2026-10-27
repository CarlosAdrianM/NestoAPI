/*
    Novedades de la versión 1.10.50.0 de Nesto (27/10/2026).
    Versión de la ClickOnce: 1.10.50.0 (pubxml 1.10.50.* en revisión 0).

    Tanda: NestoAPI (#705 stock de la tienda al grabar pedidos, #706 UTM, #707 recomendaciones post-compra, #708 promesa en
    riesgo ACTIVA, #709 push, #710 SignalR con la lectura, #711 vídeo en Novedades, #712 fecha de entrega lejana, #713 tope de
    las paradas, #695 fecha de recepción, #717 muestras sin stock, GLS fases 4-5 apagadas, ML.NET en sombra #714/#715/#716,
    #671e, #618, #696 cheque+cupón no acumulables, #704 origen del modo de servicio, #703 ámbito de sugerencias, #701 reintento Pub/Sub, #657 versión mínima Ariadna en sombra, #698 retener pedido) + Nesto (783, 787, 790, 733, Nesto.Models a .NET 8, #490 panel lateral tramos 1-2, #711, #712, #558, #546 pantalla fecha en riesgo).

    ORDEN:
      1) Scripts sa ANTES de publicar la API: Issue707_CorreosPostCompraRecomendaciones, Issue709_PedidosOrigenPush,
         Issue711_NovedadesVideo, Issue713_PreparacionEscaneosParada, Issue671e_SugerenciasProductosSombra,
         Issue714_ClientesRiesgoAbandono, Issue715_PedidosAnomalias, Issue716_DatosParaModelos (su backfill FUERA de horario),
         Nesto552_ParametroGlsPorApi, Issue704_PedidosModoServicioOrigen, Issue698_PedidosRetenidos. Nesto490_NavegacionLateral YA LANZADO (13:46).
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.50.0.
      4) DESPUÉS: ESTE script, y OneShot_20261027_CerrarSugerencias_1_10_50_0.ps1 (las sugerencias 783, 784, 787, 788, 789,
         790 y 733 pasan a implementadas en esta versión: la sugerencia ES la novedad, no se duplica aquí).
      5) Subir el vídeo del diseño nuevo a la novedad del panel lateral: POST api/Novedades/{id}/Video (scratchpad
         hyperframes/panel-lateral.mp4, 26 s). Es el único vídeo de la versión (regla: uno por versión).
      6) Fuera de horario: POST api/Clientes/RiesgoAbandono/Ejecutar y el etiquetado de anomalías (primera pasada).

    SE OMITE A PROPÓSITO:
      - Todo lo de ML.NET en sombra (#714 abandono, #715 anomalías, #716 datos, #671e, #618): nadie ve nada todavía.
      - UTM de los correos (#706), recomendaciones del correo del sábado (#707), atribución de push (#709): es para medir.
      - GLS por la API (#552 fases 4 y 5): apagado.
      - Nesto.Models a .NET 8, el tope de las paradas (#713), el registro del origen del modo de servicio (#704), el ámbito de
        las sugerencias (#703) y la versión mínima de Ariadna en sombra (#657): internos.
      - Lo de Ariadna 2.19 (#56, #713, 733 F4): va en su propio script de Novedades de Ariadna.
      - Correo del cheque regalo de usted y con botón a la tienda: lo ven los clientes, no los usuarios de Nesto.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.50.0';
DECLARE @fecha DATE = '2026-10-27';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Diseño nuevo de Nesto: menú lateral en lugar de la cinta',
     N'Ya puede probar el diseño nuevo: un menú a la izquierda, ordenado por áreas de trabajo (Ventas, Clientes, Almacén y envíos, Tiendas y reposición, Compras y proveedores, Administración y contabilidad, Tienda online, Informes), en lugar de la cinta de arriba. Para activarlo, abra Parámetros (en el menú de la cinta, arriba a la izquierda), ponga «Diseño nuevo: menú lateral en lugar de la cinta» a Sí y cierre y vuelva a abrir Nesto. El menú sale recogido, solo con iconos: con el botón ☰ lo deja abierto y Nesto lo recordará. Truco: pulse Ctrl+K desde cualquier pantalla, escriba parte del nombre (por ejemplo «remesa» o «verifactu») y pulse Intro; con las flechas elige otro resultado y con Esc cancela. Si prefiere la cinta de siempre, puede volver a ella desde Parámetros. Dentro de unas semanas el diseño nuevo será el que salga por defecto. Vea el vídeo de 26 segundos.',
     NULL),
    ('Nuevo', N'Novedades con vídeo',
     N'Algunas novedades llevan un vídeo corto (unos 20 segundos, sin sonido) que enseña cómo se hace. Si una novedad tiene vídeo, verá el botón «▶ Ver vídeo»: solo se descarga al pulsarlo y se reproduce dentro de Nesto. Como mucho hay uno por versión, para no quitarle tiempo.',
     NULL),
    ('Nuevo', N'«Retener pedido»: dejar un pedido parado hasta nuevo aviso sin tocar la fecha de entrega',
     N'Hasta ahora, para que un pedido no saliera se le ponía la fecha de entrega a un año vista: nadie lo veía, parecía un error y contaba como promesa incumplida. Ahora, en el pedido, el botón «Retener…» pregunta el motivo y, si quiere, hasta qué fecha; el pedido queda marcado en amarillo («Retenido por Alfredo desde el 27/10 hasta el 15/11: motivo») y no sale en ningún picking hasta que alguien pulse «Liberar». Si puso fecha, se libera solo ese día y le avisa por la campana (a usted y al vendedor); si no la puso, cada semana le recuerda que sigue retenido. La fecha de entrega no se toca: cuando se libera, el pedido vuelve a su modo de servir de antes.',
     N'Vendedores,Tiendas,Almacén,Administración'),
    ('Nuevo', N'Pantalla «Pedidos con la fecha en riesgo»',
     N'En Clientes › Ventas (y en el menú lateral, sección Ventas) hay un botón «Fecha en riesgo» con la lista de pedidos que no van a llegar a la fecha que se le dijo al cliente: pedido, cliente, vendedor, fecha prometida, fecha nueva (o «Sin fecha»), qué falla y por qué. Cada uno ve los suyos: el vendedor sus pedidos, el jefe de equipo los de su equipo y Dirección todos. Con doble clic se abre el pedido y con «Copiar texto para el cliente» tiene un mensaje listo para avisarle. Los avisos de la campana de este tipo abren esta pantalla con el pedido ya elegido. El filtro «Resueltos» enseña los de los últimos 30 días.',
     N'Vendedores,Dirección'),
    ('Nuevo', N'Aviso cuando un pedido no va a llegar a la fecha prometida',
     N'Cuando un pedido se retrasa respecto a la fecha de entrega que se le dijo al cliente (o se queda sin fecha), el vendedor, su jefe de equipo y Dirección reciben un aviso en la campana con el motivo (por ejemplo, un producto que tiene que venir de una tienda o no tiene stock) y un correo resumen a las 9:00 y a las 15:00. En los pedidos de la web avisa a quien los grabó; en los de peluquería, a su vendedor. Llevaba una semana en pruebas sin avisar a nadie: desde hoy avisa.',
     N'Vendedores,Dirección'),
    ('Nuevo', N'«¿Es correcta?»: aviso al grabar un pedido con fecha de entrega lejana',
     N'Si al grabar o modificar un pedido alguna línea tiene fecha de entrega a más de 60 días, Nesto pregunta antes de grabar («La fecha de entrega de las líneas es el miércoles 27/10/2027, dentro de 365 días. ¿Es correcta?»). Con Sí se graba igual; con No vuelve a la edición. Un pedido se quedó una semana sin salir porque se tecleó 2027 en vez de 2026.',
     N'Vendedores,Tiendas,Administración'),
    ('Mejorado', N'Las muestras sin stock ya no retienen el pedido',
     N'Si en un pedido lo único que no tiene stock es una muestra, la muestra se quita del pedido y el pedido sale con el resto, en cualquier modo de servicio (antes, en «Todo junto», se quedaba retenido entero). El usuario que grabó el pedido recibe un aviso en la campana con la muestra que se ha quitado, por si quiere avisar al cliente.',
     N'Vendedores,Tiendas,Almacén'),
    ('Mejorado', N'Las sugerencias de la plantilla aprenden de sus respuestas: pulse ✓ o ✗',
     N'Las sugerencias de productos de la plantilla ya se ordenan por la probabilidad de que el cliente compre, y estamos preparando un modelo que aprenda de lo que ustedes responden. Para que aprenda pronto necesitamos que, cuando salga una sugerencia, pulsen ✓ («Añadido») o ✗ («No le interesa»): desde el 20 de octubre solo hay 21 respuestas sobre más de 3.000 sugerencias. Cada respuesta cuenta.',
     N'Vendedores,Tiendas'),
    ('Mejorado', N'Recibir reposición: ya no se atasca por un inventario hecho después del envío',
     N'Las entradas de una reposición se dan de alta en la tienda o en Algete con la fecha en que se reciben, no con la del envío. Así ya no sale «Se ha realizado un inventario posterior a esta fecha» cuando alguien regularizó un producto entre el envío y la recepción (nos pasó tres veces en una semana). Si eso ocurre, al abrir la reposición sale un aviso: «El 42267 se regularizó por inventario el 26/10 a las 14:19: compruebe que no se cuenta dos veces».',
     N'Tiendas,Almacén'),
    ('Nuevo', N'Rapport: el resultado de la llamada y el próximo contacto',
     N'En el rapport, debajo de los comentarios, puede marcar con un solo clic cómo ha ido: Pedido, Pedirá más tarde, No necesita, Precio, Competencia, Cerrado/Traspaso, Sin stock, No decide, Solo información u Otro; y, si lo acordó, la fecha del próximo contacto. Es opcional, pero cuanto más lo marque, mejor sabrá Nesto a quién proponerle llamar y por qué. La lista de rapports enseña las dos columnas. Al dar de baja un cliente (o quitarlo de su listado), Nesto pregunta el motivo: también es opcional.',
     N'Vendedores,Tiendas'),
    ('Mejorado', N'«Puede ofrecer» sale al elegir el cliente en la lista de rapports, no al crear el rapport',
     N'Las sugerencias de productos para el cliente («Puede ofrecer», con sus botones ✓ y ✗) ya no aparecen en la ficha de crear el rapport, sino encima de la lista de rapports del cliente en cuanto lo elige: ahí es donde se consulta antes o durante la conversación. Se pliega con Alt+O y Nesto lo recuerda. Lo propuso Sancho.',
     N'Vendedores,Tiendas'),
    ('Mejorado', N'Novedades: las revisiones de la misma versión salen juntas',
     N'Si una versión sale con varias revisiones el mismo día (como la 1.10.47.0, .1 y .2), al actualizar ya no ve solo la última: la ventana de Novedades enseña todas las de esa versión en una sola página, de la más nueva a la más antigua, con su revisión en pequeño al lado del título.',
     NULL),
    ('Mejorado', N'Eventos: Nesto pregunta una sola vez si quiere anunciarlo en las apps',
     N'Al guardar un evento que ya tiene todos los datos (lugar, descripción, horario e imagen) y no se ha anunciado, Nesto pregunta si quiere anunciarlo a los vendedores y a las clientas con la app, que pueden reservar plaza desde ella. Si contesta que no, no vuelve a preguntar en cada guardado: siempre puede hacerlo después con «Anunciar otra vez…».',
     N'Tiendas,Administración'),
    ('Mejorado', N'Guardar los textos web de un producto ya no falla si la tienda tarda en responder',
     N'Al guardar la ficha web de un producto, si el aviso a la tienda online tarda más de la cuenta, Nesto guarda igual y la tienda se actualiza sola en unos minutos (lo reintenta varias veces). Si al final no lo consigue, avisa por correo a Tienda online. Antes salía un error y había que volver a guardar.',
     N'Tienda online,Administración'),
    ('Mejorado', N'El cheque regalo y el cupón de evento no se pueden usar en el mismo pedido',
     N'Si un pedido lleva el cheque regalo de la campaña y además un cupón de evento, Nesto no lo graba y explica el motivo («El cupón EV-… y el cheque regalo no son acumulables: quite uno de los dos»). También pasa con los pedidos de la tienda online que se importan: si llega uno con los dos, no entra en silencio, lo ve quien lo importa. La tienda ya no deja aplicar el cupón si el cheque está en el carrito.',
     N'Tiendas,Administración');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
SELECT Id, Titulo FROM Novedades WHERE Version = @version AND Titulo LIKE N'Diseño nuevo%'; -- el Id para subir el vídeo
