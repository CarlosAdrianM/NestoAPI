/*
    Novedades de la versión 1.10.39.0 de Nesto (08/10/2026) y de la 2.3 de Ariadna.
    Versión de la ClickOnce: 1.10.39.0 (pubxml 1.10.39.* en revisión 0). Ariadna: ApplicationDisplayVersion 2.3.

    Tanda: NestoAPI (reposiciones automáticas #577 c3a-3d, fecha de entrega a la agencia #606, comparador y cuarentena #607,
    sugerencias de contacto #603, motivo del picking #608, corrector del concepto de los enlaces de pago #609, adjuntos en
    Novedades #616, quick wins 541/542/544 y Novedades descartadas visibles, estado 95 «Una sola compra, por Amazon»)
    + Nesto (Enviar reposición con permiso #577, #606 plantilla y detalle, Recibir reposición 543/545, Novedades listas
    separadas y adjuntos Nesto#519, campana Nesto#516, corrector #609, Amazon 548, Nesto#515, Rapports una sola llamada,
    modernización #490 6.º tramo)
    + Ariadna 2.3 (F7/F8 solo con permiso #577 c3c, fuera los fallbacks de #600, cámara en «Dato mal» Ariadna#19).

    ORDEN:
      1) Scripts sa ANTES de publicar la API (TODOS LANZADOS el 08/10/26): Issue577_LinPedidoVtaFechaCreacion,
         Issue577_PropuestaReposicionSinBloqueo, Issue577_ReposicionesTraspasos, Issue577_UsuariosRellenarReposicionManual,
         Issue606_FechaEntregaAgenciaPrometida, Issue577_CalendarioCierreAntelacion, Issue577_CalendarioRutasAlgeteTiendas,
         Issue609_GlosarioConceptosPago, Issue616_NovedadesAdjuntos, OneShot_20261008_ClientesAmazonUnaCompraEstado95,
         OneShot_20261008_prdCrearFacturaVta_Estado95.
      2) Publicar NestoAPI (hecho 08/10 ~20:40) y Ariadna 2.3 (Play interna y Windows).
      3) Publicar la ClickOnce (Clean + Rebuild).
      4) DESPUÉS: ESTE script (idempotente: compara por Versión/Ámbito + Título).
      5) Marcar implementadas por la API (PUT api/Novedades/Sugerencias/{id}), NO se insertan aquí para no duplicarlas:
         542 (Manuel, ofertas 6+1 y 10+1), 543 (Paloma, colores en Recibir), 545 (Paloma, buscador y cualquier código de
         barras en Recibir), 544 (Aida, reembolso de un envío devuelto), 547 (Alfredo, motivo del picking), 548 (Enrique,
         Amazon con cantidad 0) → 1.10.39.0. 541 (Marta, comisiones) → 2.23.0 de NestoApp (hecho 08/10).
         Tras publicar la API: `UPDATE AgenciasTransporte SET EsSombra = 0 WHERE Numero = 12` (sa) para que Innovatrans vuelva
         a medirse en sombra; la cuarentena ya la respeta el comparador (#607).

    SE OMITE A PROPÓSITO:
      - Lo que va por las sugerencias de arriba (542, 543, 544, 545, 547, 548).
      - Estado 95 «Una sola compra, por Amazon» (460 clientes): interno; en Rapports se nota solo en que ya no salen.
      - Modernización #490 (6.º tramo): sin cambio para el usuario.
      - Ariadna: los fallbacks de #600 quitados y el orden de los errores pendientes: interno.
      - NestoApp: la sugerencia 541 ya está marcada implementada en la 2.23.0.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.39.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-10-08';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Las reposiciones de las tiendas se rellenan solas a su hora',
     N'Ya no hay que calcular la propuesta de reposición: cada reposición del calendario se rellena sola a su hora de corte (Reina y Alcobendas hacia Algete a las 10:00 el mismo día; de Algete a Reina y a Alcobendas, a las 13:00 del día anterior). En «Enviar reposición» se ve cuándo se rellena cada una («se rellena sola el viernes a las 13:00») y solo Manuel, Alfredo y Carlos pueden rellenar una a mano; el resto trabaja sobre las que ya están rellenas. Además, la propuesta ya no se bloquea porque la tienda tenga una reposición sin recibir: lo que ya va de camino no se vuelve a proponer.'),
    ('Nuevo', N'La plantilla dice qué día se entrega el pedido a la agencia',
     N'Al hacer un pedido, la plantilla enseña «Se entrega a la agencia el jueves 15/10» (y, si va en varias entregas, cuándo sale completo), calculado con el stock de Algete y de las tiendas, las reposiciones del calendario y lo que viene del proveedor; el motivo sale al pasar el ratón. Si el pedido está en «Todo junto» y falta algo, avisa de que no saldrá hasta que esté todo y qué día saldría la primera parte con otro modo. El detalle del pedido enseña la misma fecha y, en naranja, la que se prometió al crearlo si ya no coincide. Si la fecha prevista de un pedido a proveedor ya pasó, se avisa a Compras en la campana y por correo cada mañana.'),
    ('Nuevo', N'Documentos adjuntos en las novedades',
     N'Una novedad o una sugerencia puede llevar ficheros (PDF e imágenes): salen como chips bajo el texto con su nombre y tamaño, y se abren con un clic. Dirección e Informática pueden adjuntarlos y quitarlos. El primero será el PDF con las normas de los cupones de descuento para eventos.'),
    ('Nuevo', N'Antes de mandar un enlace de pago, Nesto propone corregir el concepto',
     N'Al reclamar una deuda con un concepto escrito a mano, Nesto revisa la ortografía y los nombres de cursos y productos («micronileng» → «Microneedling», «nv2613646» → «NV2613646») y, si hay algo que cambiar, pregunta «¿Quisiste decir…?» con los cambios resaltados. Tú eliges «Usar la corrección» o «Dejar el mío»; nunca cambia nada sin que lo veas. Los números, fechas e importes no se tocan.'),
    ('Mejorado', N'La campana te lleva al comentario, no solo lo resalta',
     N'Al abrir un aviso de la campana, Novedades se coloca en el comentario al que se refiere, lo deja a la vista y con el foco, aunque esté al final de una novedad larga o dentro de una sugerencia o un aviso de fallo. Antes solo se resaltaba y había que buscarlo.'),
    ('Mejorado', N'«Sugerir una mejora» y «Algo no funciona» enseñan cada uno lo suyo',
     N'Entrando por «Sugerir una mejora» solo se ven las sugerencias; por «Algo no funciona», solo los avisos de fallos. Al abrir Novedades sin elegir ninguno, se ven las novedades de la versión. Además, si tu sugerencia se descarta, la sigues viendo 30 días con la respuesta, para que no se pierda.'),
    ('Mejorado', N'Clientes para contactar: las llamadas al mes se calculan mejor',
     N'Las llamadas al mes de cada cliente se calculan con los días del mes y su cadencia real (un cliente que compra cada semana son 4 al mes), el objetivo de hoy se reparte solo entre los días laborables que quedan, la lista del día no encoge al ir llamando y los pedidos del cliente se cuentan por pedido y su fecha (uno servido en dos entregas contaba dos veces). Los clientes que solo han comprado una vez por Amazon ya no salen en la lista.'),
    ('Mejorado', N'El comparador de agencias respeta la cuarentena',
     N'Una agencia en cuarentena (hoy Innovatrans) ya no sale como la más económica ni en las etiquetas pendientes ni en la propuesta de envío, aunque su coste se siga calculando para compararlo.'),
    ('Corregido', N'Rapports cargaba dos veces la lista de clientes para contactar',
     N'Al abrir Rapports, el panel de clientes para contactar se pedía dos veces al servidor. Ahora una.'),
    ('Corregido', N'Recibir reposición: los textos de confirmación vienen del servidor',
     N'Los mensajes al terminar de recibir una reposición (y el aviso de diferencias) los manda el servidor y ya no se duplicaban.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;

-- Ariadna 2.3
DECLARE @ariadna TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @ariadna (Categoria, Titulo, Descripcion) VALUES
    ('Mejorado', N'Las reposiciones a las tiendas se rellenan solas; F7 y F8 solo con permiso',
     N'En Salidas, las reposiciones a Reina y Alcobendas aparecen ya rellenas: se rellenan solas el día anterior a las 13:00. F7 y F8 (rellenar una a mano) solo las tienen Manuel, Alfredo y Carlos.'),
    ('Nuevo', N'«Dato mal en la ficha» tiene botón de cámara',
     N'En el móvil, junto al hueco del código de barras hay un botón de cámara para leerlo sin teclearlo. Lo leído con la cámara cuenta como leído con el escáner, igual que en la PDA.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.3', @fecha, a.Categoria, a.Titulo, a.Descripcion, 'Ariadna', 1, 'sa'
FROM @ariadna a
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = a.Titulo);

SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
