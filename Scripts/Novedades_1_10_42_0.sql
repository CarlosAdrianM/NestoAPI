/*
    Novedades de la versión 1.10.42.0 de Nesto (15/10/2026) y de Ariadna 2.9 (la 2.8 no llegó a publicarse).
    Versión de la ClickOnce: 1.10.42.0 (pubxml 1.10.42.* en revisión 0: ventanas nuevas de Diferencias de reposición y de
    Propuestas de stock).

    Tanda: NestoAPI (#635 diagnóstico de bloqueos, #636 sin huecos en la numeración de pedidos, #637 propuestas de stock que
    no se guardaban, #638 mensajes de muestras y del 5 %, Amazon sin cheque regalo, 618 Almacén/Dirección cambian el modo con
    picking, #627 prevista de proveedor en día no laborable, #631 decisiones de Compras sobre las propuestas, #634 diferencias
    de reposición [ajuste del origen ENCENDIDO], #641 recorrido por calles [sin el script, como hoy], #626 regalo de ofertas
    combinadas, #643 Outlet = estado 4 [interruptor APAGADO: solo simulación nocturna], #644 clasificación de más vendidos para
    la tienda, efectos manuales sin forma de pago, incremental de precios medios ENCENDIDO)
    + Nesto (#527 muestras en la plantilla, #529 Propuestas de stock, #526 Diferencias de reposición, #524 aplicar regalo de
    oferta combinada, forma de pago en los efectos manuales, 4C.5 tramo 1 sin cambio visible)
    + NestoAPI#645 / Nesto#530 / Ariadna#31 etiquetas por bulto en las reposiciones
    + Ariadna 2.9 (#26 recorrido al revés / empezar por pasillo, #25 diferencias de reposición en la PDA, #31 etiquetas).

    ORDEN:
      1) Scripts sa ANTES de publicar la API:
         - Issue634_DiferenciasReposiciones.sql      (el ajuste está encendido: sin la tabla, la recepción va como hoy y los GET vacíos)
         - Issue645_ReposicionesBultos.sql           (etiquetas por bulto; sin la tabla, no se piden bultos ni se exige etiqueta)
      2) Scripts sa cuando se quiera (idempotentes):
         - Issue641_RecorridoCalles.sql              (calles de Algete con el recorrido de Carlos; sin él, el orden de siempre)
         - Issue631_MotivosSilencio.sql              (sin él, Nuevo / Estacional / Aparato)
         - (Ya lanzados hoy: Issue622_IndiceUnicoSoloCabeceraPrincipal, Issue631_PropuestasStockDecisiones, AnularChequeAmazon,
           ReservasRepoNegativas_80940_80941)
      3) Publicar NestoAPI.
      4) Publicar la ClickOnce (Clean + Rebuild; recargar antes la solución desde disco: proyecto nuevo Nesto.Tests) → 1.10.42.0.
      5) Ariadna 2.9: flujos de Play interna y Windows.
      6) DESPUÉS: ESTE script (idempotente: compara por Versión + Título).
      7) Adjuntar la guía regenerada a la novedad «Guía de reposiciones actualizada…» de esta versión y a las de reposiciones
         (552, 562, 564, 570, 577, 578, 581, 602, 603):
           .\Scripts\AdjuntarANovedad.ps1 -Titulo "Guía de reposiciones actualizada: léala de nuevo, hay cambios importantes" -Ficheros ".\Documentacion\Guia reposiciones automaticas.pdf"
      8) Encolar POST api/PropuestasStock/Generar?mes=2026-09 (corre de noche) y revisar ELMAH.
      9) Cerrar issues: NestoAPI #635 #636 #637 #638 #627 #631 #634 #641 #626 #643 #644 #645 (corte 1; mezclas pendientes); Nesto #527 #529 #526 #524 #530;
         Ariadna #26 #25 #31. Contestar en Novedades: 618 (Alfredo, ya puede cambiar el modo con picking).

    SE OMITE A PROPÓSITO:
      - #636 (números de pedido), #635 (diagnóstico de bloqueos), #637 (guardado de propuestas): sin cambio visible.
      - #641 (orden por calles): hasta lanzar el script y probarlo, el recorrido es el de siempre. Se contará entonces.
      - #643 (Outlet = estado 4): apagado; solo manda la simulación por correo a Carlos.
      - #644 (clasificación para la tienda): lo usa el equipo de PrestaShop.
      - Incremental de precios medios y 4C.5: sin cambio visible.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.42.0';
DECLARE @fecha DATE = '2026-10-15';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Guía de reposiciones actualizada: léala de nuevo, hay cambios importantes',
     N'Le rogamos que vuelva a leer la guía de reposiciones (va adjunta): hay cambios importantes en cómo se preparan, se envían y se reciben. Lo más importante: cada caja lleva ahora una etiqueta con código de barras y NUNCA se pueden meter en la misma caja productos de dos reposiciones distintas (una reposición puntual va siempre en su propia caja, con su etiqueta). También explica las diferencias de reposición, el nuevo orden del recorrido en Algete y cómo preparar entre dos personas.',
     N'Almacén,Tiendas'),
    ('Nuevo', N'Etiquetas por bulto en las reposiciones',
     N'Al terminar de preparar una reposición (también la puntual) se pregunta cuántos bultos lleva y se imprime una etiqueta con código de barras por cada caja, en la impresora de etiquetas. Al recibir, lo primero es leer la etiqueta de la caja: así cada caja se recibe en su reposición, y si la etiqueta es de otra se avisa. Si no sale la etiqueta, se puede reimprimir. Las reposiciones enviadas antes de hoy se reciben como siempre. NUNCA mezcle en una caja productos de dos reposiciones distintas.',
     N'Almacén,Tiendas'),
    ('Nuevo', N'Diferencias de reposición: lo recibido que no coincide con lo enviado',
     N'Nuevo botón «Diferencias» en Productos › Reposición. Cuando una tienda o Algete recibe una reposición y lo leído no coincide con lo enviado, queda apuntado aquí, se avisa en la campana a quien la preparó y a Almacén, y el stock del almacén de origen se corrige solo. En la ventana se ve qué pasó, en qué hueco hay que mirar y dónde hay stock; quien comprueba la diferencia la resuelve con un motivo («Comprobada», «No estaba», «Sobraba» o «Error al recibir»). El botón enseña cuántas quedan por comprobar.',
     N'Almacén,Tiendas'),
    ('Nuevo', N'Propuestas de stock: Compras decide cada propuesta',
     N'Nueva ventana «Propuestas de stock» (Productos › Compras), solo para Compras y Dirección. Cada mes el sistema propone ajustar máximos y mínimos, pasar productos a estado 1 o liquidarlos, con la explicación de cada cifra. Desde aquí se aplica (pudiendo cambiar los valores, con motivo), se rechaza, se pospone, se silencia hasta una fecha o se liquida (Outlet o Ganavisiones). Todo queda registrado: quién, cuándo y el valor de antes y de después. Cuando hay propuestas nuevas, avisa en la campana.',
     N'Administración'),
    ('Mejorado', N'Plantilla: al elegir «Según vaya entrando» con muestras sin stock, le ofrece quitarlas',
     N'Si el pedido lleva muestras (material promocional) que no hay en el almacén, ya no le sale solo un error: le pregunta si quiere quitarlas del pedido y pasarlo al modo elegido. Si dice que no, el pedido se queda en «Todo junto» y se lo dice junto al selector. Los mensajes explican que el motivo es que no hay stock de esas muestras.',
     N'Vendedores,Tiendas,Administración'),
    ('Mejorado', N'«Aplicar oferta» añade el regalo de una oferta combinada',
     N'En la plantilla, las sugerencias de ofertas ahora también avisan cuando a una oferta combinada le falta su regalo (por ejemplo, el limpiador de la oferta del PDRN) y, al pulsar «Aplicar oferta», añaden la línea del regalo al 100 %. Si el regalo puede ser uno de varios productos, le pregunta cuál.',
     N'Vendedores,Tiendas,Administración'),
    ('Mejorado', N'El material promocional que supera el 5 % sale en un solo mensaje',
     N'Si varias muestras sueltas pasan del 5 % del pedido, el aviso ya no repite la misma frase por cada una: sale un solo mensaje con la lista de productos y le dice qué hacer (quitar alguna o ampliar el pedido).',
     N'Vendedores,Tiendas,Administración'),
    ('Mejorado', N'Almacén puede cambiar el modo de entrega aunque el pedido tenga picking',
     N'Cuando un pedido ya tiene picking, los vendedores siguen sin poder cambiar el modo de entrega (le piden el cambio a almacén con el correo de siempre), pero las personas de Almacén y de Dirección ya sí pueden hacerlo desde el pedido. El picking ya hecho no se deshace solo: revise lo que ya está preparado.',
     N'Almacén'),
    ('Mejorado', N'Fecha de entrega: una recepción prevista en fin de semana o festivo cuenta como el siguiente día laborable',
     N'Si un producto del pedido depende de una compra a proveedor con la fecha prevista en sábado, domingo o festivo, la fecha de entrega se calcula desde el siguiente día laborable, que es cuando de verdad puede llegar.',
     N'Vendedores,Tiendas,Administración'),
    ('Corregido', N'Ya se pueden guardar los pedidos con vencimientos puestos a mano',
     N'Al añadir vencimientos a mano en un pedido, el primero se creaba sin forma de pago y el pedido no se podía guardar. Ahora llevan la forma de pago del pedido.',
     N'Vendedores,Administración');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;

-- Ariadna 2.9 (Ariadna#26, #25 y #31; la 2.8 no llegó a publicarse)
DECLARE @ariadna TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @ariadna (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Etiquetas por bulto: al terminar se imprimen y al recibir se leen primero',
     N'Al terminar una reposición en Algete (y al enviar la de la tienda), Ariadna pregunta cuántos bultos lleva (Intro solo = 1) e imprime una etiqueta por caja; F5 reimprime. Al recibir, lo primero es leer la etiqueta de la caja («LEA LA ETIQUETA DEL BULTO»): hasta entonces no acepta productos, y si la etiqueta es de otra reposición lo dice. En Entradas, leer una etiqueta abre directamente su reposición. Nunca mezcle en una caja productos de dos reposiciones distintas.'),
    ('Nuevo', N'Preparar entre dos o más: recorrido al revés o empezando por un pasillo',
     N'En el recorrido de un picking o una reposición, F8 (o el botón «⇅ Orden») abre «Orden del recorrido»: # e Intro lo pone al revés y un número de pasillo e Intro empieza por ese pasillo (0 vuelve a «donde toca»). Así, si dos personas preparan la misma reposición, una va en orden normal y la otra al revés, y cada una empieza por una punta. Cada PDA recuerda su elección. Ojo con las faltas: que las marque solo una persona, al final.'),
    ('Nuevo', N'Diferencias de reposición en la PDA',
     N'En el inicio sale «Diferencias de reposición por comprobar» (tecla 8) cuando lo recibido en una reposición no coincide con lo enviado. Para cada una se ve qué pasó y en qué hueco mirar: lea el producto y pulse Intro si está («Comprobada»), F3 si no estaba, F4 si sobraba o F7 si fue un error al recibir. F1 para añadir un comentario. El aviso del buzón también abre esta pantalla.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.9', @fecha, a.Categoria, a.Titulo, a.Descripcion, 'Ariadna', 1, 'sa'
FROM @ariadna a
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = a.Titulo);

SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
