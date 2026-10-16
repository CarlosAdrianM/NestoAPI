/*
    Novedades de la versión 1.10.43.0 de Nesto (16/10/2026).
    Versión de la ClickOnce: 1.10.43.0 (pubxml 1.10.43.* en revisión 0: propuestas con filtros y «Potenciar ventas», fechas en
    las ofertas, etiqueta de producto, ficha = Excel, cheque regalo de la tienda).
    Ariadna NO entra aquí: la 2.12 irá más tarde con su propio script (y la 2.11 tiene el suyo, Novedades_Ariadna_2_11.sql).

    Tanda: NestoAPI (conciliación: fichero de tarjetas con el botón equivocado da un mensaje claro, #647 múltiplos de las ofertas
    combinadas por producto, #611/#612/#613 cupones de eventos + señal ya usada [interruptores APAGADOS], #654 etiqueta de
    código de barras de producto, #649 muestras en pedidos por ruta solo con lo que hay en Algete, #653 UrlFoto en reposiciones
    [para Ariadna], #652 cheque regalo en la tienda PrestaShop, sugerencia 643 caducidad en las ofertas por familia, mensajes de
    las agencias de usted, propuestas de stock [sugerencia 644 filtros y consecuencia, #655 vida útil por grupo, Potenciar ventas
    y ObjetivoVentaSugerido, #630 máximo sin múltiplo de compra, regenerar un mes y calibración, #656 pendiente vencido y servido
    con retraso], #628 ficha = Excel (mínimo y máximo sugeridos de ALG), incidencia 645 CantidadDisponibleTotal para la tienda,
    Novedades: recordatorios de respuesta, estado «Cerrada» y «le ha mencionado», #552 GLS fase 2 [interruptor APAGADO])
    + Nesto (Nesto#535 Potenciar ventas, sugerencia 644 filtros en Propuestas de stock, tooltip del mínimo/máximo en la ficha,
    Recoger producto conserva el retorno, sugerencia 643 fechas en ofertas, #533 cheque regalo de la tienda, #531 muestras de
    Algete en la plantilla, #534 etiqueta de producto, #490 4C.5 tramo 2 sin cambio visible).

    ORDEN:
      1) Scripts sa ANTES de publicar la API:
         - Issue656_ServidoConRetraso.sql            (OBLIGATORIO: columna PropuestasStock.ServidoConRetraso; sin ella falla el
                                                      job nocturno y la pantalla de Propuestas de stock)
         - Issue630_RegenerarPropuestasStock.sql     (GRANT DELETE para Generar?regenerar=true)
         - NovedadesRecordatoriosRespuesta.sql       (OBLIGATORIO: columnas de espera + CHECK de Estado con «Cerrada»)
         - Issue612_FacturacionCursosEventos.sql     (interruptores apagados: antes o después, da igual)
         - Issue613_Cupones.sql                      (interruptores apagados: antes o después, da igual)
         - (Ya lanzado el 15/10: Issue655_PotenciarVentas.sql. Sin él, «Potenciar ventas» daría 404.)
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.43.0.
      4) DESPUÉS:
         - ESTE script (idempotente: compara por Versión + Título).
         - OneShot_20261016_NovedadesEsperandoRespuesta.sql (backlog 579, 584, 605, 621 y 648 esperando al autor).
         - OneShot_20261016_Incidencia646AManuel.sql (si no se lanzó ya).
      5) Post-deploy:
         - POST api/PropuestasStock/Generar?mes=2026-09&regenerar=true (Dirección; queda programado a las 04:30) y revisar ELMAH.
         - Encolar en Nesto_sync las 61 referencias del comentario de NestoAPI#656 (sin 90029 ni 90056) y después todos los
           productos con stock (mensaje Productos con CantidadDisponibleTotal).
         - Avisar a la tienda en prestashop-nestosync#35 (contrato CantidadDisponibleTotal) y comprobar 45939 y 43992 en el
           webservice.
         - Avisos en Novedades: 546 (Paloma, etiqueta de producto), 643 (Manuel, fechas en las ofertas), 644 (propuestas: filtros
           y «Si se aplica»), 646 (Manuel, retorno en Recoger producto), 645 y 647 (Enrique, stock de la tienda online y múltiplos
           de las ofertas combinadas): versión 1.10.43.0 y comentario en cada hilo.
         - Cerrar issues (comentario con la versión):
             NestoAPI #647 #649 #652 #653 #654 #656 #628; comentar #655 (Potenciar ya en Nesto) y #630 (regenerar/calibración).
             Nesto #531 #533 #534 #535.
             NO cerrar: #611 #612 #613 (cupones, interruptores apagados), #552 (GLS fase 2, apagado), Nesto#490 (4C.5 sigue).
      ENTRARON antes de publicar: Excel por exclusivas (filas G: en IndicadoresStock) con «Liquidar → estado 4» (contado en la
      novedad de Propuestas de stock) y la señal del cupón tras la rectificativa (interruptores de cupones apagados: se omite).
      POST-DEPLOY extra: POST api/IndicadoresStock/Reconstruir?meses=36 (Dirección; espera sola a las 22:00), ANTES de las 04:30.

    SE OMITE A PROPÓSITO:
      - Cupones de eventos (#611, #612, #613, señal ya usada): interruptores apagados; se contará al encenderlos.
      - #653 (foto en las reposiciones): es para Ariadna; irá en las novedades de la 2.12.
      - #552 GLS fase 2: interruptor Agencias:GlsTramitarPorApi apagado; sin cambio visible.
      - #490 4C.5 tramo 2: sin cambio visible.
      - #630 calibración y regenerar un mes, #656 y #655 (vida útil por grupo): cambian las cifras del Excel y de las propuestas,
        que se cuentan juntas en la novedad de Propuestas de stock; el regenerar es interno (Dirección por la API).
      - Ariadna: va en su propio script.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.43.0';
DECLARE @fecha DATE = '2026-10-16';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Imprimir la etiqueta de código de barras de un producto',
     N'En la ficha del producto hay un botón «Imprimir etiqueta» en el que puede elegir cuántas copias quiere (de 1 a 100). En Recibir reposición, cada línea tiene también su botón «Etiqueta». La etiqueta es pequeña (30 × 20 mm): lleva el nombre, el subgrupo y el tamaño del producto, el código de barras y la referencia en grande. Sale por su impresora de etiquetas, la misma de las etiquetas de hueco.',
     N'Almacén,Tiendas,Compras'),
    ('Nuevo', N'Ofertas por Familia y de Producto: fechas Desde y Hasta, y «Permanente»',
     N'Las ofertas por familia también tienen ahora las columnas Desde y Hasta (como las de producto), así que puede ponerles fecha de fin y ya no tendrá que acordarse de borrarlas. Las que están fuera de fechas se ven apagadas, y las que no tienen «Hasta» indican «Permanente». «Solo activas» oculta también las caducadas. Las fechas se guardan con el botón Guardar de la fila. Si «Hasta» es anterior a «Desde», le avisa antes de guardar.',
     N'Compras,Administración'),
    ('Nuevo', N'Propuestas de stock: «Potenciar ventas» con objetivo y fecha de revisión',
     N'En Propuestas de stock, además de aplicar, rechazar, posponer, silenciar o liquidar, ahora puede elegir «Potenciar ventas»: pone un objetivo de venta al mes (se propone uno que puede cambiar) y una fecha de revisión (de entrada, dentro de seis meses). La lista enseña el objetivo en la columna «Objetivo» y, al revisarla, si se ha cumplido o no.',
     N'Compras'),
    ('Mejorado', N'Propuestas de stock: filtros, familia y grupo, y qué pasa «Si se aplica»',
     N'La lista tiene ahora las columnas Familia y Grupo, filtros por familia, grupo y acción, y un buscador por código o nombre (pulse Intro); el contador de arriba cuenta solo lo filtrado. «Quitar filtros» vuelve a la lista entera. En el panel de decisión, «Si se aplica» le dice exactamente qué cambiará si aplica la propuesta (también al pasar el ratón por la columna Acción). Además, las cifras son más ajustadas: los máximos ya no suben por el múltiplo de compra (eso se respeta al hacer el pedido al proveedor), los aparatos y accesorios ya no salen como «Caduca antes de venderse», y se tiene en cuenta lo que está pendiente de servir con la fecha ya pasada y lo que se ha servido con retraso. Y aplicar «Liquidar» con el canal Outlet pasa el producto a estado 4 (deja de reponerse en todos los almacenes) y, si tiene ficha en la tienda, entra en el Outlet esa misma noche; con Ganavisiones no cambia el estado. Los productos propios nunca se liquidan.',
     N'Compras'),
    ('Mejorado', N'Ficha del producto: el mínimo y el máximo sugeridos de Algete salen del mismo cálculo que las propuestas de stock',
     N'En la ficha del producto, el mínimo y el máximo sugeridos de Algete se calculan ahora igual que en las propuestas de stock, así que ya no le saldrán cifras distintas en la ficha y en las propuestas. Si pasa el ratón por encima, verá de dónde sale cada cifra: la fórmula, la acción propuesta y la explicación. En las tiendas sigue siendo la media de los últimos 24 meses, y también se explica.',
     N'Compras,Almacén'),
    ('Mejorado', N'Pedidos por ruta desde Algete: las muestras, solo con lo que hay en Algete',
     N'En un pedido de ruta (AT, 16, 00 o FW) que sale de Algete, las muestras (material promocional) solo pueden llevar las unidades que haya en Algete, porque para una muestra no se espera a una reposición de las tiendas. Al crear el pedido desde la plantilla, si alguna muestra pide más de lo que hay, le enseña la lista (cuántas pide y cuántas hay) y le pregunta si quiere dejarlas en lo que hay; las que no tengan ninguna unidad se quitan. Si contesta que no, el pedido no se graba y puede cambiarlas usted.',
     N'Vendedores,Tiendas,Administración'),
    ('Mejorado', N'Los pedidos de la tienda online pagados con cheque regalo entran con la línea del cheque',
     N'Cuando un cliente usa su cheque regalo en la tienda online, el pedido entra en Nesto con la línea del cheque regalo (como si se hubiera canjeado aquí) y no como un cupón de descuento. Si algo no cuadra (el cheque no es de ese cliente, ya se usó, el importe no coincide…), el pedido entra con un cupón «CHEQUE REGALO NO APLICADO: REVISAR» y un aviso en los comentarios para que lo revise.',
     N'Administración'),
    ('Mejorado', N'Novedades: recordatorios cuando le hacemos una pregunta y estado «Cerrada»',
     N'Si le preguntamos algo sobre una sugerencia o incidencia suya y no contesta, le llegará un recordatorio cada dos días laborables. Si después de dos recordatorios sigue sin respuesta, la sugerencia pasa a «Cerrada». Usted la seguirá viendo, y si escribe un comentario se vuelve a abrir sola. Además, cuando alguien le menciona, el aviso indica «… le ha mencionado en Novedades».',
     NULL),
    ('Corregido', N'Ofertas combinadas: los múltiplos se cuentan por producto, aunque esté en varias líneas',
     N'Si un producto de una oferta combinada estaba repartido en varias líneas del pedido (por ejemplo, unas cobradas y otras de un 6+1), cada línea se comprobaba por separado y se rechazaba un regalo que sí correspondía. Ahora se suman todas las líneas del producto: con 14 mascarillas de una oferta de 7 + 1 limpiador le corresponden 2 limpiadores, y «Aplicar oferta» los ofrece.',
     N'Vendedores,Tiendas,Administración'),
    ('Corregido', N'La tienda online ya no vende lo que está comprometido para otros pedidos',
     N'La tienda online sumaba lo disponible de cada almacén por separado y, si un almacén no tenía stock pero sí pedidos pendientes, esos pedidos no restaban del stock de los otros. Así podía vender unidades que ya estaban comprometidas. Ahora la web recibe el stock de toda la empresa menos todo lo que está pendiente de entregar.',
     N'Vendedores,Tiendas,Administración'),
    ('Corregido', N'Agencias: al teclear el peso de un «Recoger producto» de CTT ya no se pierde el retorno',
     N'Al abrir en Agencias un pedido con la etiqueta de CTT pendiente que tenía retorno, al teclear el peso se volvía a poner «sin retorno» y el servicio por defecto, y la etiqueta se imprimía sin retorno. Ahora se conserva lo que había en pantalla mientras no cambie la agencia ni el pedido. Además, los mensajes de las agencias le tratan de usted.',
     N'Almacén,Administración'),
    ('Corregido', N'Conciliación: si carga el fichero de tarjetas con el botón del banco, le dice qué botón usar',
     N'Al cargar el fichero de liquidación de tarjetas con el botón de los movimientos del banco salía un error sin explicación. Ahora le avisa de que ese fichero se carga con el botón del fichero de tarjetas (o de que el fichero no es de movimientos del banco).',
     N'Administración');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
