/*
    Novedades de la versión 1.10.51.0 de Nesto (28/10/2026).
    Versión de la ClickOnce: 1.10.51.0 (pubxml 1.10.51.* en revisión 0).

    Tanda: NestoAPI (#698 retener con picking + fix CK modo 5, #722 aplazar con picking, #693 avisos de pedidos en riesgo por
    usuario, #720 lista de salidas, 822 código parecido, 830 resumen de rapports, #723 P.O. obligatorio, Nesto#555 alumna y
    público, 929498 IVA del contacto, 832 prometida al adelantar, 249990 retorno, #725 cambios confirmados con la agencia,
    #724 enviar por correo, #727 P.O. unidad de facturación + fin de mes por la API, GLS por la API ENCENDIDO (#552),
    impresora de agencia en las etiquetas del servidor) + Nesto (Nesto#555, #562, #564, #563, ventana fin de mes, plantilla IVA
    del contacto y selector de fecha, aviso al retener, desplegable de avisos, resumen de rapports).

    ORDEN:
      1) Scripts sa ANTES de publicar la API: Issue727_prdAgruparAlbaranesVta_PO (fin de mes: P.O. y fecha). Issue693 e Issue723 YA
         LANZADOS (12:05). Opcional: Nesto552_ParametroGlsPorApi (solo para que Nesto tramite GLS por la API).
      2) Publicar NestoAPI (lleva Agencias:GlsTramitarPorApi=true y Facturacion:FinDeMesPorApi=true en el Web.config del repo).
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.51.0. Ariadna 2.20 por sus flujos (su script Novedades_Ariadna_2_20).
      4) DESPUÉS: ESTE script. Luego AvisarNuevaVersionNesto.ps1 -Version 1.10.51.0 (con el OK de Carlos).
      5) Marcar Chanel: PUT api/Clientes/1/27593/SuPedidoObligatorio { "Exige": true } (OK de Carlos 28/10).
      6) Avisos en Novedades: @Paloma (P.O. de Chanel), @Alfredo @Santiago (fin de mes: lo que tenga P.O. se queda como albarán),
         797 (Alfredo, retener), 822 (Santiago), 830 (Javier), 831 (MariaJose), 832 (Lidia), 833 (hilo CTT), 770 (Laura/Sancho).

    SE OMITE A PROPÓSITO:
      - ReglaIvaContacto, AdelantoFechaEntrega, DatosQueViajaronALaAgencia, ImpresorasAgencia: internos (se cuentan por su efecto).
      - El sondeo del sandbox de CTT (#726), el script del procedimiento de fin de mes, el parámetro AvisosPedidosEnRiesgo.
      - Lo de Ariadna 2.20 (822): va en su propio script.
      - La corrección del código parecido en la API sin Ariadna: no se ve.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.51.0';
DECLARE @fecha DATE = '2026-10-28';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Retener un pedido que ya tiene picking: el almacén se entera y el pedido no sale',
     N'Al retener un pedido que ya está en un picking, el almacén recibe el aviso por la campana de Nesto y por el buzón de Ariadna, con los pickings afectados, y el pedido queda bloqueado: no se puede tramitar ni imprimir su etiqueta hasta que se libere. Al liberarlo vuelve a su modo de servicio anterior.',
     NULL),
    ('Nuevo', N'Aplazar la fecha de entrega de un pedido con picking',
     N'Si cambia la fecha de entrega de una línea que ya tiene picking y la agencia todavía no tiene el envío, Nesto lo acepta, avisa al almacén y el pedido no sale hasta la fecha nueva. Si el envío ya está tramitado en la agencia, lo rechaza y lo explica: la única opción es que el cliente lo rechace a la entrega o avisar a logística.',
     NULL),
    ('Nuevo', N'Pedidos en riesgo: cada usuario elige qué avisos recibe',
     N'En la ventana de pedidos en riesgo (Clientes › Fecha en riesgo) hay un desplegable «Avisos» con cuatro opciones: Todo (campana y correo), Solo correo, Solo campana o Nada. Lo que elija vale solo para usted.',
     NULL),
    ('Nuevo', N'Crear cliente: alumnas y público final',
     N'En el asistente de crear cliente, en «Datos Comisiones», hay dos casillas nuevas: «¿Es alumna?» (cursos y eventos) y «¿Es público final?» (profesional de otro sector que pide factura). Con una de ellas marcada no hace falta elegir estética ni peluquería: el estado y el vendedor los pone Nesto. Si una alumna tiene centro y hay que visitarla, créela como estética o peluquería.',
     N'Administración,Dirección,Tiendas'),
    ('Nuevo', N'P.O. obligatorio por cliente',
     N'En la ficha comercial del cliente hay una casilla nueva, «Exige P.O. en todos los pedidos» (solo Administración, Dirección e Informática). Con ella marcada, Nesto no graba ningún pedido de ese cliente sin «Su pedido» (la plantilla y el detalle lo resaltan en rojo si falta) y no factura ninguno que no lo tenga. Y en la barra de facturación del pedido hay un botón «Poner P.O.» para poner o corregir el P.O. de una factura ya emitida sin abonarla: la factura reimpresa sale con él.',
     N'Administración,Dirección'),
    ('Nuevo', N'El P.O. manda en la facturación',
     N'Todos los pedidos de un cliente con el mismo P.O. se facturan juntos, en una sola factura con ese P.O., en cuanto todos están entregados, da igual el contacto, el periodo de facturación o «Mantener junto». Dos P.O. distintos nunca van en la misma factura, y un pedido con P.O. no se factura suelto antes de tiempo.',
     N'Administración,Dirección'),
    ('Nuevo', N'Facturar el fin de mes desde Nesto',
     N'Botón nuevo «Facturar Fin de Mes» en el detalle del pedido (Administración, Dirección e Informática). Pide la fecha de corte (por defecto el último día del mes anterior) y, si quiere, un cliente y un contacto; reúne los albaranes de fin de mes de cada cliente en un pedido y los factura, y enseña las facturas creadas, los clientes saltados con su motivo y los errores. Los pedidos con P.O. no entran: se facturan por P.O. Lo que no se pueda facturar se queda como albarán. Conviene lanzarlo el último día del mes: la factura sale con la fecha del día.',
     N'Administración,Dirección'),
    ('Nuevo', N'Cambiar un envío ya registrado en CTT o Innovatrans: solo con la confirmación de la agencia',
     N'Si modifica desde «Modificar Envío» (pestaña En curso) un dato que ya viajó a la agencia (reembolso, retorno, dirección, bultos, peso…) de un envío que CTT o Innovatrans ya tienen registrado, Nesto avisa en rojo: ese cambio se haría en nuestro sistema pero no en el de la agencia. Solo se guarda si confirma dos veces y escribe con quién ha hablado en la agencia y cuándo; queda anotado en la historia del envío. Para que el cambio llegue a la agencia, use «Modificar datos y reenviar a la agencia» (pestaña Tramitados), que anula el envío y lo registra de nuevo con etiqueta nueva.',
     N'Almacén,Administración,Dirección'),
    ('Nuevo', N'Enviar por correo el pedido, la factura o el mandato SEPA sin descargarlos',
     N'En el detalle del pedido, debajo de «Descargar PDF», hay un botón «Enviar por correo…»; en el menú de «Imprimir Factura» está también para la factura, y en la ficha del cliente, junto a «Imprimir» del mandato SEPA. Se abre un diálogo con los correos del cliente ya puestos (editables, varios separados por «;»), asunto y texto, y Nesto manda el correo con el PDF adjunto y copia a usted. Ya no hace falta descargar, copiar al escritorio y adjuntar.',
     NULL),
    ('Nuevo', N'Etiquetas de GLS desde el packing de Ariadna',
     N'En el packing de Ariadna, con toda la entrega dentro y las cajas con foto, F8 «Etiqueta de agencia» ya imprime también las etiquetas de GLS (hasta ahora solo CTT e Innovatrans), en la impresora de agencia de cada usuario. El envío se graba con sus cajas y se tramita al cerrar el día como siempre.',
     N'Almacén'),
    ('Mejorado', N'Resumir rapports vuelve a funcionar',
     N'El botón «Resumir rapports» del cliente daba un aviso sin explicación: el modelo de inteligencia artificial que usaba lo han retirado. Ya funciona con el modelo actual y, si fallara, dice el motivo.',
     NULL),
    ('Mejorado', N'Lista de salidas de Ariadna: las entregas etiquetadas desde Nesto desaparecen',
     N'Una entrega con todos sus pedidos etiquetados desde Nesto cuenta como empaquetada: el picking deja de salir en la lista de salidas de Ariadna aunque no tenga cajas con foto.',
     N'Almacén'),
    ('Mejorado', N'Códigos de barras parecidos: Ariadna pide releerlos',
     N'Si al leer un código en Ariadna no es de ningún producto pero se parece mucho a uno del producto que tiene delante (se diferencia en una o dos cifras), no se da de alta a la primera: hay que leerlo dos veces más y se avisa a Compras para que lo compruebe. La cámara también exige dos detecciones seguidas de un código que no conoce.',
     N'Almacén,Compras'),
    ('Mejorado', N'Adelantar la fecha de entrega actualiza la fecha prometida',
     N'Si adelanta la fecha de entrega de un pedido que aún no tiene picking, la fecha de entrega a la agencia prometida al cliente (la naranja) pasa a la nueva si es anterior. Aplazarla no la cambia: ese retraso es lo que detecta la pantalla de pedidos en riesgo.',
     NULL),
    ('Corregido', N'Plantilla: el IVA del pedido es el del contacto de entrega',
     N'Al elegir en la plantilla una dirección de entrega de un contacto con otro régimen de IVA (por ejemplo con recargo de equivalencia), el pedido toma el IVA de ese contacto. Antes se quedaba con el del contacto de la página de cliente y el pedido salía sin recargo (pedido 929498).',
     NULL),
    ('Corregido', N'Plantilla: el selector de fecha de entrega ya no da error al modificar un pedido',
     N'Al modificar un pedido con la plantilla podía salir «Specified argument was out of the range of valid values» al cambiar la fecha mínima de entrega. Corregido.',
     NULL),
    ('Corregido', N'Plantilla: el P.O. escrito llega al pedido y al borrador',
     N'El P.O. («Su pedido») que se escribía en la plantilla no llegaba al pedido ni se guardaba en el borrador, y al modificar un pedido con la plantilla se perdía. Corregido.',
     NULL),
    ('Corregido', N'Etiquetas de agencia impresas por el servidor: por la impresora de agencia',
     N'Las etiquetas de agencia que imprime el servidor (reimpresión de CTT e Innovatrans y las del packing de Ariadna) salían por la impresora de etiquetas pequeñas de hueco. Ahora salen por la impresora de agencia de cada usuario, como en Nesto: la de GLS para GLS y la de agencia para el resto.',
     N'Almacén');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
