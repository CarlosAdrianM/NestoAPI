/*
    Novedades de la versión 1.10.52.0 de Nesto (29/10/2026).
    Versión de la ClickOnce: 1.10.52.0 (pubxml 1.10.52.* en revisión 0).

    Tanda: NestoAPI (#728 Apple/Google Pay, #730 stock por antigüedad en la fecha a la agencia, #731/#732 eventos (persona que
    asiste, gratuitos por compra, todos o alguno), #733 traspaso solo de lo albaraneado, #734 consulta de reembolso, #736/#532
    recordatorio de reposición, #741 inscribir cada comercial, #611 origen Cobro, #613 mínimo del cupón, #692 anunciar,
    #739 reparto propio + caja del repartidor + carga de furgoneta, Ariadna#63 histórico, SEPA por correo (831)) + Nesto
    (#565, #566, #567, #568, #569, #560, 864, 868, #571, ventana Eventos, inscribir con selectores, cupón manual con mínimo).

    ORDEN:
      1) Scripts sa ANTES de publicar la API (ver la lista del resumen de la sesión).
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.52.0.
      4) DESPUÉS: ESTE script y Novedades_Ariadna_2_22.sql. Luego AvisarNuevaVersionNesto.ps1 -Version 1.10.52.0 (con el OK de Carlos).

    SE OMITE A PROPÓSITO:
      - #736 y #532 (recordatorio de reposición): lo ve el cliente, no el usuario de Nesto; el modelo va en Sombra.
      - El reparto del stock por antigüedad en la fecha a la agencia (#730): se nota en que las fechas y los avisos de riesgo aciertan.
      - La carga de la furgoneta, el histórico (Ariadna#63) y la entrega con firma: van en el script de Ariadna 2.22.
      - TNV (Apple Pay / Google Pay en la app, eventos «Todos»): sale con su versión.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.52.0';
DECLARE @fecha DATE = '2026-10-29';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Reparto propio como una agencia más',
     N'Nuestra ruta propia ya es una agencia en Nesto: «Reparto propio». Se elige como cualquier otra, la etiqueta sale por la Zebra con su código de bulto y el repartidor entrega desde Ariadna con el nombre de quien recoge, su DNI o su firma, y el cobro del reembolso. En Tramitados, «Ver prueba de entrega» enseña quién lo recogió, cuándo, cómo se cobró y la firma. El efectivo cobrado se apunta en la caja del repartidor.',
     N'Almacén,Administración,Dirección'),
    ('Nuevo', N'Pedido contra reembolso con enlace de pago pendiente: retener y consultar a Administración',
     N'Si en Agencias va a tramitar un pedido contra reembolso que tiene un enlace de pago sin pagar, Nesto ofrece «Retener y consultar a Administración»: el pedido queda retenido y Administración contesta desde la campana si sale con reembolso o sin él. Lo mismo ocurre en el packing de Ariadna.',
     N'Almacén,Administración'),
    ('Nuevo', N'Eventos gratuitos por compra',
     N'En el mantenimiento del evento puede indicar los productos que dan derecho a la inscripción gratuita y si basta con uno o hacen falta todos. Si después se quita del pedido un producto que justifica una inscripción, Nesto pregunta qué hacer con ella.',
     NULL),
    ('Nuevo', N'Eventos: la persona que asiste',
     N'En los inscritos del evento se ve la persona que asiste (una persona de contacto del cliente), con el correo y la dirección de la ficha. Al inscribir puede elegirla, si quiere, en el mismo desplegable.',
     NULL),
    ('Nuevo', N'Cada comercial inscribe a sus clientes en los eventos',
     N'Cada vendedor puede inscribir en un evento a sus propios clientes, y el jefe de equipo a los de su equipo. Administración, Tiendas y Dirección pueden inscribir a cualquiera.',
     NULL),
    ('Mejorado', N'Inscribir en un evento: con el selector de clientes y el enlace de pago copiado',
     N'La ventana de inscribir usa el selector de clientes de siempre y un desplegable con los correos del cliente (puede escribir otro). Al terminar, el enlace de pago queda copiado para pegarlo donde quiera. En los inscritos y en la ficha del evento hay un botón «Copiar enlace».',
     NULL),
    ('Mejorado', N'Pedido mínimo de los cupones',
     N'Cada evento define el pedido mínimo de sus cupones (0 si no se indica). Al emitir un cupón a mano, Nesto pide el pedido mínimo, 200 € por defecto.',
     NULL),
    ('Mejorado', N'Ventana de Eventos en pantallas pequeñas',
     N'La ventana de Eventos se adapta a pantallas y ventanas pequeñas, con columnas de un ancho más cómodo.',
     NULL),
    ('Mejorado', N'Anunciar un evento: Tienda online o Dirección',
     N'El botón para anunciar un evento a los clientes de la app solo aparece a Tienda online y a Dirección.',
     NULL),
    ('Mejorado', N'Datos de la transferencia también con transferencia al contado',
     N'Los datos para hacer la transferencia se ofrecen también cuando la forma de pago es transferencia al contado, y se proponen al terminar la plantilla.',
     NULL),
    ('Mejorado', N'Enlaces de pago con Apple Pay y Google Pay',
     N'Los enlaces de pago que se mandan a los clientes admiten Apple Pay y Google Pay, además de la tarjeta y Bizum.',
     NULL),
    ('Mejorado', N'Comentarios del rapport más cómodos',
     N'La casilla de comentarios del rapport crece mientras escribe, hasta unas quince líneas, y después se desplaza.',
     NULL),
    ('Mejorado', N'Panel lateral: la misma letra que con la cinta',
     N'Con el panel lateral, las pantallas tienen el mismo tamaño de letra y el mismo alto de fila que con la cinta.',
     NULL),
    ('Mejorado', N'Reembolsos de CTT: el número de cliente ya viene puesto',
     N'Al contabilizar los reembolsos de CTT, el número de cliente sale ya puesto.',
     N'Administración'),
    ('Corregido', N'Pedidos de la tienda con Google Pay o Apple Pay: pago con tarjeta ya cobrado',
     N'Los pedidos de la tienda online pagados con Google Pay o Apple Pay entraban como transferencia sin prepago. Ahora entran como pago con tarjeta ya cobrado.',
     N'Administración,Tiendas'),
    ('Corregido', N'Enviar por correo el mandato SEPA',
     N'Enviar por correo el mandato SEPA (y algunas facturas) daba error porque no encontraba el informe. Corregido.',
     NULL),
    ('Corregido', N'Inscripción creada desde un cobro: con su forma de pago',
     N'La inscripción que se crea al cobrar la señal de un evento toma la forma de pago del cobro (por ejemplo, tarjeta si se pagó con el enlace) y su origen es «Cobro». Antes salía siempre como transferencia.',
     NULL),
    ('Corregido', N'Traspaso a la otra empresa: solo lo albaraneado',
     N'Al traspasar un pedido a la otra empresa para facturarlo, solo se mueven las líneas ya albaraneadas. Lo pendiente se queda en su empresa con su cabecera, para servirlo después.',
     N'Administración,Almacén');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
