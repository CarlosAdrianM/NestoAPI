/*
    Novedades de la versión 1.10.47.0 de Nesto (22/10/2026).
    Versión de la ClickOnce: 1.10.47.0 (pubxml 1.10.47.* en revisión 0).

    Tanda: NestoAPI (#614/#615/#690 cupones y eventos, #691 cursos desde la plantilla + CodigoCupon en TNV, #692 anunciar
    eventos en las apps, #693 fechas prometidas en riesgo [en Sombra], #532/#662 recordatorio de reposición Activo con bajas
    por tipo, Novedad 716, CCC en blanco del pedido 929031, imagen del evento solo Tienda online) + Nesto (#517/#518 eventos y
    cupones en la ficha del cliente, #543 cursos en la plantilla, #544 sugerencias en el rapport, #545 anunciar eventos,
    #542 Klarna, Novedades 731/732/734/735).

    ORDEN:
      1) Scripts sa ANTES de publicar la API (TODOS YA LANZADOS el 22/10): Issue532_RecordatorioReposicionActivo,
         Issue532_BajasPorTipo, Issue690_EventosHorarioModalidadAsistencia, Issue614_CuponesPrestashop,
         Issue615_PropuestasSenalesEventos, Issue691_CursosPlantilla, Issue692_AnunciarEventos, Issue693_PedidosPromesaRiesgo.
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.47.0.
      4) DESPUÉS: ESTE script.

    SE OMITE A PROPÓSITO:
      - #693 (avisos de pedidos con la fecha prometida en riesgo): nace en Sombra; se anunciará al encenderlo.
      - #532/#662 (recordatorio de reposición a clientes) y «Correos/Notificaciones que recibe»: es para los clientes (TNV).
      - CCC en blanco (pedido 929031) e imagen del evento solo para Tienda online: internos.
      - Cupones en la tienda online (#614 espejo apagado, #694 pendiente).

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.47.0';
DECLARE @fecha DATE = '2026-10-22';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Eventos y cupones en la ficha del cliente',
     N'En la ficha del cliente hay dos pestañas nuevas. En «Eventos» puede inscribir al cliente en un curso o evento, marcar la plaza como pagada y ver sus inscripciones. En «Cupones» ve los cupones de descuento que ha ganado al asistir (con su importe, su mínimo y hasta cuándo valen) y puede usarlos en el pedido. Al terminar el evento, la tienda pasa lista: quien pagó y no vino no recibe el cupón, y Nesto se lo recuerda con los nombres antes de guardar.',
     N'Vendedores,Tiendas,Administración'),
    ('Nuevo', N'Eventos: presencial u online, referencia del curso y anuncio en las apps',
     N'Cada evento indica si es presencial u online (webinar) y siempre lleva su tienda, Reina o Alcobendas, que es la de la factura. La referencia del curso puede ser una que ya exista (por ejemplo, 90077) o crearse con el botón «Crear referencia». Cuando el evento está completo, con su imagen incluida, Nesto pregunta si quiere anunciarlo en las apps: los vendedores lo reciben en NestoApp y los clientes en su app (los presenciales, solo los de la zona de Madrid). Más adelante puede volver a anunciarlo con un reclamo, como «¡Últimas plazas disponibles!».',
     N'Tiendas,Administración'),
    ('Nuevo', N'Vender cursos desde la plantilla',
     N'Los cursos con un evento próximo ya salen al buscar en la plantilla. Al añadir uno, Nesto le pide el evento (si hay varios) y la modalidad («Con cupón de productos» o «Sin cupón»), y pone el precio de esa modalidad. Los cursos van siempre con prepago y en un pedido aparte de los productos, porque se facturan por separado: su importe no cuenta para portes, Ganavisiones ni cheques. La clienta queda apuntada al evento y recibe un correo para pagar su plaza.',
     N'Vendedores,Tiendas'),
    ('Nuevo', N'«Puede ofrecer» también en el rapport',
     N'Al crear un rapport de un cliente aparecen las mismas tres sugerencias de productos que en la plantilla, para ofrecérselas durante la llamada. Con «✓» el producto se añade a la plantilla de ese cliente: a la que ya tenga abierta o a una nueva, que se abre sola con el cliente y el producto puestos.',
     N'Vendedores'),
    ('Mejorado', N'Añadir productos a un pedido pendiente',
     N'Si el cliente tiene un pedido pendiente, al elegirlo en la plantilla Nesto le ofrece «Modificar el pedido». Así el cheque regalo, los portes y los Ganavisiones cuentan todo el pedido y no solo lo nuevo. Gracias, Paloma, por el aviso.',
     N'Vendedores,Tiendas'),
    ('Mejorado', N'Bajo «Servir» se ve claro qué es la recomendación',
     N'Cuando lo que ha elegido en «Servir» no coincide con lo que recomienda Nesto, la frase gris empieza por «Recomendado:». Gracias, María José.',
     N'Vendedores,Tiendas'),
    ('Mejorado', N'Las incidencias de agencia de la tienda online llegan a tiendaonline@',
     N'Los avisos de incidencia de la agencia en pedidos de la tienda online y de Amazon llegan ahora también a tiendaonline@nuevavision.es. Gracias, Laura.',
     NULL),
    ('Mejorado', N'Bancos: los pagos con Klarna se contabilizan solos',
     N'La conciliación reconoce los cobros de Stripe pagados con Klarna y con las tarjetas premium a la tarifa actual, y ya no se queda pensando con muchos cargos en el mismo ingreso.',
     N'Administración'),
    ('Corregido', N'«Copiar seguimientos a otro cliente»: el botón se ve sin maximizar',
     N'El botón «Copiar» quedaba fuera de la ventana si no se maximizaba. Gracias, Sancho.',
     NULL),
    ('Corregido', N'Etiqueta del producto: las copias vuelven a 1',
     N'Al pasar de una referencia a otra en la ficha del producto, el número de etiquetas a imprimir vuelve a 1. Gracias, Reina.',
     N'Tiendas,Compras');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
