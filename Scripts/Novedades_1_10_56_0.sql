/*
    Novedades de la versión 1.10.56.0 de Nesto (05/11/2026).
    Nesto #620 (no imprimir en local lo impreso en el servidor), #622 (ventana de inventario y PDF temporal), #623 (campana → Ariadna y
    NestoApp), #624 (cambiar estado de productos), #625 (gestión en tránsito de CTT), #626 (eventos mixtos);
    NestoAPI #726 (Management API de CTT), #818 (stock de la web al momento), #819 (alta de cliente: textos recortados y 400 claro),
    #820 (Hangfire robusto), #821 (CP españoles nuevos con dirección de Google), #823 (una persona, una identidad), #824 (enlace pagado
    y reembolso por cliente; céntimo de redondeo), #825 (packing por pedido en factura simplificada), #827 (una sola grafía por persona),
    #829 (forma de asistencia en eventos mixtos), #830 (la ruta del pedido es la de la empresa 1), #735 (fuera RDLC), #738 (estado de
    productos por la API), #822 (campana por persona en Nesto, Ariadna y NestoApp; GET api/Novedades/{id}), #826 (direcciones extranjeras
    sin reordenar) y #623 cortes 1 y 5 (origen del cliente: leads de Meta Harmonia, llamada < 24 h laborables; Nesto#523).

    ORDEN:
      1) Scripts sa ANTES de publicar la API: Issue818_ExtractoProducto_EncolarSyncTienda.sql (disparador que encola en Nesto_sync cada
         apunte de ExtractoProducto que no sume cero: el stock de la web al momento) e Issue829_EventosInscripcionesModalidad.sql
         (formato «Mixto» en la CHECK de Eventos y columna EventosInscripciones.FormaAsistencia) e Issue623_OrigenesCliente.sql (tablas
         OrigenesCliente con la fila META-HARMONIA-0926 y ClientesOrigen, GRANT a RDS2016$).
         YA HECHOS hoy por Carlos: OneShot GrantExecute_ApiSPs (EXECUTE a RDS2016$ en 10 SPs), REVOKE INSERT/UPDATE de CódigosPostales a
         «Eva Visnú», borrado de los rellenos de inventario (REI 2512, ALG 8, ALC 3206) y de las 44 filas antiguas de Andre.
      2) Publicar la API (master) → ClickOnce (Clean + Rebuild) → 1.10.56.0 → flujos de Ariadna 2.30.
      3) DESPUÉS de publicar la API (en este orden): Issue823_UsuariosAriadnaADominio.sql (4.701 filas: el usuario sin dominio de
         Ariadna pasa a su gemelo de dominio), Issue823b_FechasPermitidas_UnaFilaPorPersona.sql (borra «Andre» sin dominio, crea
         NUEVAVISION\pedro, deja «Miguel»), Issue827_UsuariosUnaGrafia.sql (FUERA DE HORARIO: ~22.000 filas, 20 índices de
         SeguimientoCliente; no toca lo anterior a 2025), ESTE script, Novedades_Ariadna_2_30.sql y
         OneShot_20261105_CerrarSugerencias_1_10_56_0.ps1 (marca Implementadas las sugerencias y contesta en cada hilo).
         Luego AvisarNuevaVersionNesto.ps1 -Version 1.10.56.0 solo con el OK de Carlos.
      4) OPCIONALES: OneShot_20261105_Clientes_RutaEmpresa3_ComoEmpresa1.sql (iguala la ruta de 58 fichas de la empresa 3 a la de la 1;
         con #830 la API ya usa la de la 1, el script es para Nesto viejo e informes) y OneShot_20261105_CP48430_Ortuella.sql (SOLO
         cuando Paloma haya corregido el cliente 27593/91).
      5) Tras publicar: avisar en el hilo 1057 a Algete de que ya pueden hacer la prueba del inventario (Ariadna y Nesto) y a Reina y
         Paloma de la prueba en Ariadna; contabilizar la fila de Alcobendas (37533) cuando Paloma la cuente.
      NO duplicar: las sugerencias 793 (Nesto#624, cambiar estado de productos), 959 (#827, informe de rapports), 1049 (#819, alta de
      cliente), 1064 (Nesto#626, eventos mixtos), 1065 (#830, ruta de la empresa 1), 1054 (Ariadna#96) y 1056 (#825, packing) las marca
      Implementadas el OneShot (la sugerencia ES la novedad); aquí no llevan fila.
      Se omiten a propósito (nada perceptible o en sombra): #735 (RDLC fuera de la API), #820 (Hangfire: timeout de Pub/Sub y colas
      separadas), Nesto#621 (restos de Prism), NestoAPI#827 en la API (la grafía se corrige sola al grabar), el interruptor de impresión
      en servidor para los usuarios de Nesto (sigue solo para Ariadna hasta que todos tengan la 1.10.56).

    Ejecutar en SSMS contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.56.0';
DECLARE @fecha DATE = '2026-11-05';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Gracias, Manuel, Alfredo, Santiago, Reina, Paloma, Sancho y Enrique',
     N'Esta versión sale de lo que habéis contado por «Novedades»: el cambio de estado de muchos productos a la vez (Manuel), el alta de cliente que decía «la solicitud no es válida» (Alfredo), los pedidos de Amazon y de la tienda online mezclados en el packing (Santiago), los eventos mixtos (Reina), los pedidos de la empresa 3 que salían a diario (Alfredo), la familia y el tamaño recortados en Ariadna (Paloma) y el informe de rapports que partía a Daniel en dos (Sancho). Seguid contándonos.',
     NULL),
    ('Nuevo', N'Agencias: parar en vuelo, crear retorno y cambiar la fecha o la dirección de entrega de un envío de CTT',
     N'En Agencias › Tramitados, Incidentados y Retrasados, los envíos de CTT tienen cuatro botones nuevos: «Parar en vuelo» (la agencia deja de intentar la entrega), «Crear retorno» (nos lo devuelven y queda como envío de retorno en la pestaña Retornos), «Cambiar fecha de entrega» y «Cambiar dirección de entrega» (con teléfono y correo del destinatario, que CTT exige). Cada operación queda en la historia del envío y avisa por correo a logística. Para las demás agencias los botones salen desactivados con el motivo.',
     N'Administración,Almacén,Dirección'),
    ('Mejorado', N'La campana abre también las novedades y sugerencias de Ariadna y de NestoApp',
     N'Al pinchar en un aviso de la campana que viene de una sugerencia o incidencia puesta desde Ariadna o desde la app de vendedores, Nesto la abre (antes decía que no la encontraba). Se ve etiquetada como «Sugerencia de Ariadna» o «Novedad de NestoApp».',
     NULL),
    ('Corregido', N'Facturar rutas y detalle del pedido: no se vuelve a imprimir lo que ya imprimió el servidor',
     N'Cuando la factura o el albarán ya han salido por la impresora del servidor (lo que ocurre al facturar desde la etiqueta de Ariadna), Nesto ya no los vuelve a imprimir en local ni pregunta por ellos. Si no se imprimieron en el servidor, desde el detalle del pedido pregunta e imprime en local.',
     N'Administración,Almacén,Tiendas'),
    ('Corregido', N'Inventario de tienda: la ventana ya no se cierra al cambiar de pestaña',
     N'Al cambiar de pestaña con el inventario abierto, Nesto daba un error y se cerraba la ventana (le pasó a Reina). Y los informes en PDF del menú se abren ahora en un fichero nuevo cada vez: si uno estaba abierto, ya no da «el fichero está en uso».',
     N'Tiendas,Almacén'),
    ('Nuevo', N'Alta de cliente: un código postal español que no existe se da de alta solo si la dirección viene del buscador de Google',
     N'Si al dar de alta o modificar un cliente el código postal no está en Nesto, se crea solo con la población y la provincia que trae la dirección elegida en el buscador de Google, con la ruta más habitual de su provincia. Dirección recibe el correo de siempre para revisarlo. Si la dirección no se eligió en Google, Nesto pide que se elija en vez de fallar.',
     N'Vendedores,Tiendas,Administración,Dirección'),
    ('Corregido', N'Ariadna y Nesto: la misma persona con usuario de dominio y sin él comparte fechas permitidas, buzón y parámetros',
     N'Quien entra en Ariadna con su usuario (Alfredo, Santiago, Andre…) ya no recibe «Fecha no permitida» al contabilizar: la API reconoce que es la misma persona que NUEVAVISION\Alfredo y usa sus fechas, su buzón y sus parámetros de Nesto. No hace falta duplicar nada por usuario.',
     N'Almacén,Tiendas,Administración'),
    ('Corregido', N'Enlace de pago cobrado mientras la mercancía va en otro envío del cliente contra reembolso',
     N'Si el cliente paga un enlace y su mercancía va en un envío de OTRO pedido suyo que sale contra reembolso (como el 929852 con el envío del 929323), el aviso urgente a logística y Administración ya sale igualmente, con el envío y el pedido, para que la agencia entregue sin cobrar. Al tramitar un envío contra reembolso también avisa si el cliente tiene un enlace pendiente de otro pedido. Y una diferencia de hasta 5 céntimos entre lo cobrado y la factura ya no deja el efecto pendiente: se liquida y la diferencia va a Regularización de saldo.',
     N'Administración,Dirección,Almacén'),
    ('Mejorado', N'Tienda online: el stock de la web se actualiza al momento',
     N'Cada movimiento de stock (venta, recepción, traspaso, inventario, regularización) manda el producto a la web en cuanto se apunta, sin esperar a la sincronización de la noche. Los movimientos que suman cero (meter y sacar de una carpeta, cambiar de ubicación) no la molestan.',
     N'Dirección,Compras,Tiendas'),
    ('Nuevo', N'Origen del cliente: los leads de la campaña de Meta de Harmonia, con llamada en menos de 24 h',
     N'En «Crear cliente» hay un desplegable «Origen» (por ahora «Lead de Meta · campaña Harmonia»); también se ve y se cambia en la ficha del cliente. Un cliente con ese origen sale el primero en «clientes para contactar» de su vendedor con «Lead de Meta: llamar antes de las hh:mm del dd/MM» (24 horas laborables desde el alta) hasta que tenga un rapport; si se pasa el plazo, aviso en la campana al vendedor y a su jefe de ventas. Importar el fichero de leads de Meta y el informe por origen vienen después.',
     N'Vendedores,Tiendas,Administración,Dirección'),
    ('Mejorado', N'La campana es la misma persona en Nesto, Ariadna y NestoApp',
     N'Los avisos que llegan a una persona se ven en cualquiera de las tres aplicaciones, y leer o borrar uno en una se refleja en las demás. Un mismo aviso ya no sale dos veces aunque se mande a Nesto y a la app.',
     NULL),
    ('Corregido', N'Alta de cliente extranjero: la dirección se deja como viene',
     N'Para los clientes de fuera de España, la dirección ya no se reordena al formato español (número detrás de la calle con coma): se deja como la teclea el usuario. Lo contó Enrique con una dirección francesa. Cuando la dirección se elige en el buscador de Google, Nesto todavía la monta a la española; queda pendiente.',
     N'Vendedores,Tiendas,Administración');
    ('Mejorado', N'Eventos: formato «Mixto» y, en cada inscrita, si viene presencial u online',
     N'Un evento puede ser presencial, online o mixto. En los mixtos, la tarjeta de cada inscrita lleva un desplegable presencial / online que se guarda al elegirlo, y la lista de inscritos tiene un contador «N presencial · M online», un filtro y la opción de ordenar por eso. Lo pidió Reina.',
     N'Tiendas,Vendedores,Dirección');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
