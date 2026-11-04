/*
    Novedades de la versión 1.10.55.0 de Nesto (04/11/2026).
    Nesto #608 (behavior de productos en «Hay que comprar»), #609 (copiar el Nº Envío), #610 (botón −1 al recibir), #611 (concepto
    del enlace de pago), #612/#613 (cheque regalo en la plantilla; ampliar respeta el modo de entrega), #614 (asistentes por
    inscripción), #615 (reembolso del reparto propio desfasado al facturar), Cajas 10 días;
    NestoAPI #800 (cuadre de agencias), #801/#807 (cobros del reparto propio a la empresa de la factura), #802 (cierre de caja del
    repartidor), #803/#804 (etiqueta de reparto propio desde el packing; transportista de PrestaShop), #805 (picking colgado por una
    falta), #808 (aviso de cheque sin usar), #809 (reembolso de entregados), #810, #811 (asistentes), #812 (picking entre dos),
    #813 (cliente sin vendedor) y #806 (impresión en el servidor, con el interruptor APAGADO: no se anuncia todavía).

    ORDEN:
      1) Scripts sa ANTES de publicar la API: Issue800_prdCuadreAgenciasContabilidad_ReembolsosDiferidos.sql, Issue779_Festivos.sql
         (tabla Festivos con 2024-2026 como el código y los NACIONALES de 2027; autonómicos y locales de 2027 los mete
         Administración desde Nesto: Carlos 04/11),
         Issue811_EventosInscripcionesAsistentes.sql, Issue806_ImpresoraDocumentos.sql («(defecto)» = HP LaserJet 600 M601 M602
         M603 PCL6, la de Algete, para todos los de Ariadna; el interruptor Facturacion:ImprimirDocumentosEnServidor va ENCENDIDO
         en el Web.config del repo: Carlos 04/11 17:05; comprobar que la impresora existe con ese nombre en RDS2016),
         Issue699_EnviosAgenciaReenvios.sql (tabla de decisiones de los devueltos: Reenviar/Abonar/EntregadoPorLaAgencia; versión
         con EnvioAnulado y GRANT UPDATE), Issue786_NotasCobro.sql (nota de cobro NC: CHECK de Tipo, ImporteCobro y NotasEntregaCobroEfectos),
         Issue786b_NotaCobroRepartidor.sql (después del anterior: NotasEntregaCabecera.Repartidor, la NC se asigna a un repartidor)
         e Issue814_ReposicionesTraspasos_LaLleva.sql (columna LaLleva: puntual que se lleva un compañero; necesita el de Issue581).
         YA HECHO el 04/11 a las 14:00 por la API: ParámetrosUsuario FacturarAlImprimirEtiqueta = 1 para «(defecto)», Alfredo, Andre,
         Pedro y Santiago (la etiqueta desde Ariadna albarana y factura; antes estaba a 0 y no facturaba: sugerencia 1017).
      2) Publicar la API (master) → ClickOnce (Clean + Rebuild) → 1.10.55.0 → flujos de Ariadna 2.29.
      3) DESPUÉS: ESTE script, Novedades_Ariadna_2_29.sql, OneShot_20261104_GlobosFichaProducto.sql (globos de la ficha) y OneShot_20261104_CerrarSugerencias_1_10_55_0.ps1 (marca Implementadas las
         sugerencias y contesta en cada hilo). Luego AvisarNuevaVersionNesto.ps1 -Version 1.10.55.0 solo con el OK de Carlos.
      4) Rol «Tiendas» en Identity para Paloma, Reina, Pilar y LauraC (Ariadna_RolAlmacen.ps1 -Rol Tiendas …), para que impriman
         etiquetas desde Ariadna (sugerencia 1009).
      NO duplicar: las sugerencias 969, 970, 979, 1003, 1005, 1006, 1010, 794, 1020 (#814, puntual con compañero) y 1013 (#815,
      reembolso diferido no retiene) las marca Implementadas en 1.10.55.0 el OneShot (la sugerencia ES la novedad); aquí no llevan fila.

    Ejecutar en SSMS contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.55.0';
DECLARE @fecha DATE = '2026-11-04';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Gracias, Paloma, Reina y Alfredo',
     N'Casi todo lo que sale en esta versión viene de lo que nos habéis contado hoy por «Novedades»: el botón que no se veía al recibir, el cheque que no se ofrecía, las dos personas de un mismo centro en un evento, el picking que no se dejaba terminar y el reembolso que no cuadraba. Gracias por contarlo con detalle y con captura: así se arregla en el día. Y gracias a Aida, Andre y Miguel, que siguen probándolo todo.',
     NULL),
    ('Nuevo', N'Caja del repartidor: Miguel la cierra desde Ariadna y Administración la recibe como la de una tienda',
     N'Miguel cierra su caja desde Ariadna tecleando lo que lleva; si coincide con lo que dice la contabilidad (empresa 1 y empresa 3 por separado), se hacen los dos traspasos a la caja pendiente de recibir (57000016), uno por empresa, y sus dos cajas quedan a cero. Si no coincide, no se cierra y tiene que verlo con Administración. En Cajas → Traspasos, Administración recibe el traspaso de Miguel igual que el de Paloma o el de Laura Camacho, sin tener que cuadrar los cobros uno a uno.',
     N'Administración,Dirección,Almacén'),
    ('Corregido', N'Cobros del reparto propio: a la empresa de la factura',
     N'Lo que cobra Miguel al entregar (en efectivo o con tarjeta) se apunta ahora en la empresa de la factura pendiente del cliente, aunque el envío figure en la empresa 1: el cobro de un pedido facturado en la empresa 3 (GB) liquida esa factura en la 3 y entra en la caja de la 3. Antes se quedaba a cuenta en la empresa 1 y había que deshacerlo a mano. Lo «a cuenta» de «Cobrar una deuda» va a la empresa de los efectos marcados. Si la factura pendiente es de la empresa 3, no se puede cobrar con enlace de tarjeta: se cobra en efectivo.',
     N'Administración,Dirección'),
    ('Corregido', N'Aviso de descuadre en agencia: ya no cuenta los reembolsos que aún no se han entregado',
     N'El correo «Aviso descuadre en Agencia» daba descuadre por los envíos tramitados cuyo reembolso se contabiliza al entregar (desde el 29/10): mientras están en camino no tienen apunte. Ya no los cuenta.',
     N'Administración'),
    ('Nuevo', N'Al grabar un pedido, aviso si el cliente tiene un cheque regalo sin usar',
     N'Si el cliente tiene un cheque regalo disponible, el pedido supera los 250 € de producto que cuentan y no lleva la línea del cheque, al grabar o modificar el pedido Nesto avisa: «El cliente tiene un cheque regalo de 50,00 € sin usar…». No bloquea nada: se añade desde el detalle del pedido si se quiere.',
     N'Vendedores,Tiendas,Dirección'),
    ('Mejorado', N'Ficha del cliente: el enlace de pago sin efectos marcados pide el concepto',
     N'Al generar un enlace de pago desde la ficha del cliente sin marcar ningún efecto (o cuando el importe no cuadra con los marcados), Nesto pide el concepto en pantalla («Pago pedido 123456», «Pago señal curso…») en vez de mandar la petición y devolver un error. Reina lo intentó cinco veces el martes.',
     N'Administración,Tiendas,Vendedores'),
    ('Mejorado', N'Eventos: la casilla «Hay que comprar» con el buscador de productos',
     N'La casilla «Hay que comprar» del evento usa el mismo buscador que las líneas del pedido: referencia, código de barras leído con la pistola (añade directamente) o escribir parte del nombre y elegir de la lista.',
     N'Vendedores,Tiendas,Dirección'),
    ('Corregido', N'Facturar un cliente sin vendedor ya no da el error de SQL',
     N'Al albaranar o facturar en tienda un pedido de un cliente sin vendedor en la ficha, Nesto daba «No se puede insertar el valor NULL en la columna Vendedor». Ahora se usa el vendedor de la cabecera del pedido y, si tampoco hay, un mensaje claro para ponérselo en la ficha. Le pasó a Paloma el martes y a Reina hoy.',
     N'Tiendas,Administración'),
    ('Mejorado', N'Ficha de producto: buscar desde la referencia o teclear la referencia en el filtro de nombre',
     N'En la ficha de producto, el campo de la referencia admite ahora teclear la referencia, leer el código de barras o escribir parte del nombre y elegir de la lista. Y si en el filtro de nombre teclea una referencia (solo cifras) e Intro, se abre directamente su ficha sin pasar por la lista. Así vale tanto para quien busca por nombre como para quien se sabe la referencia.',
     NULL),
    ('Nuevo', N'Festivos: se mantienen desde Nesto (Herramientas › Recursos Humanos › Festivos)',
     N'Los festivos que usan las remesas, las fechas de entrega, el picking de nuestras rutas y los avisos ya no van dentro del programa: se dan de alta por año (nacional, autonómico, de empresa o de cada tienda) desde Herramientas › Recursos Humanos › Festivos, para Administración, Dirección e Informática. Si a 1 de diciembre no están los del año siguiente, Nesto avisa a Dirección.',
     N'Administración,Dirección'),
    ('Mejorado', N'Correo «Incidencia de la agencia en el pedido»: con el enlace de seguimiento también en nuestra ruta',
     N'El correo que avisa de una incidencia de la agencia lleva el número de seguimiento como enlace a la web de la agencia (GLS, CTT, Innovatrans) y, en los envíos de nuestra ruta, el enlace público de seguimiento del reparto.',
     N'Administración,Vendedores,Almacén'),
    ('Mejorado', N'Tienda online: los pedidos que van por nuestra ruta se confirman en PrestaShop',
     N'Los envíos de la tienda online que van por el reparto propio ya se pueden confirmar en PrestaShop desde Agencias (antes decía que la agencia no tenía transportista). Llevan el enlace público de seguimiento del reparto.',
     N'Administración,Dirección'),
    ('Nuevo', N'Agencias: pestaña «Devueltos» para volver a enviar la misma caja cuando el cliente sigue queriendo el pedido',
     N'Cuando la agencia devuelve un envío (o lo deja incidentado), el vendedor recibe un correo y un aviso en la app preguntando si el cliente lo sigue queriendo. En Agencias › Devueltos, el almacén o el vendedor eligen: «El cliente lo sigue queriendo: volver a enviar» (se corrige la dirección si hace falta, con la casilla de cobrar los portes desmarcada) crea un envío nuevo del mismo pedido, sin abono, sin picking y sin factura, y la caja sale con la etiqueta nueva en cuanto llega; o «No lo quiere: abonar», que es lo de siempre con Copiar factura. Y en Copiar factura hay una casilla «El cliente lo sigue queriendo: rehacer el pedido» (abono + pedido nuevo pendiente del mismo cliente) para cuando ya se abonó y ubicó. Lo pedían los vendedores desde hace tiempo; Alfredo nos ayudó a montarlo para que ninguna caja se quede dando vueltas.',
     N'Administración,Almacén,Vendedores,Dirección'),
    ('Nuevo', N'Nueva nota de cobro: una parada del reparto solo para cobrar',
     N'Desde el detalle del pedido o la ficha del cliente, «Nueva nota de cobro» crea una parada del reparto propio solo para cobrar (NC + número): se marcan los efectos pendientes del extracto (empresas 1 y 3), el importe, el día, el repartidor que irá y un comentario para él. No pasa por picking ni packing. Miguel la ve en Ariadna como «COBRO · NC…», cobra en efectivo (liquida esos efectos) o con tarjeta y el cliente firma «Confirmo que he pagado…». Antes había que hacer una nota de entrega de solo texto y cobrar la deuda a mano en la parada (pedido 929609).',
     N'Administración,Dirección,Vendedores'),
    ('Corregido', N'Cajas: cobrar a la vez una deuda de la empresa 1 y otra de la 3 ya no deja líneas colgadas',
     N'Al cobrar en una caja varias deudas de distintas empresas (una factura NV y una GB), solo se contabilizaba la empresa de la primera línea; las líneas de la otra se quedaban en la precontabilidad y los cobros siguientes de ese cliente fallaban con «No se ha podido liquidar…» (le pasó a Laura Camacho el 28/10 con la GB2601662). Ahora se hace un asiento por empresa, cada cobro liquida su efecto y entra en la caja de su empresa, y si uno falla no se graba nada. La lista de deudas lleva la columna «Empresa».',
     N'Administración,Tiendas'),
    ('Mejorado', N'Detalle del pedido: las fotos del packing con miniatura y en grande dentro de Nesto',
     N'En la pestaña del seguimiento de la agencia, cada bulto y cada foto del packing de Ariadna llevan su miniatura; al pincharla se abre en grande dentro de Nesto, con «Descargar», «Abrir con el visor» y «Copiar enlace para el cliente».',
     NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
