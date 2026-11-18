/*
    Novedades de la versión 1.10.64.0 (18/11/2026): API + Nesto.

    ORDEN:
      1) Scripts (sa), ANTES de publicar la API (todos idempotentes; sin ellos la API funciona como antes y esa parte queda apagada):
           Scripts/Issue1328_EnviosAgenciaPickings.sql            (qué picking lleva cada envío)
           Scripts/PromesaRiesgo_CausaOriginal.sql                (promesas en riesgo: causa original y observación)
           Scripts/Issue857e_ComprasCorreosRemitentesPdf.sql      (correos de compras: PDF del proveedor y remitentes a ignorar)
           Scripts/ParametroUsuario_TamanoLetraSelectorCliente.sql
           Scripts/Issue875_EventosEnlaceOnline.sql               (enlace de la conexión online)
           Scripts/EventosDosier.sql                              (dosier del evento)
           Scripts/AmpliacionesPedidoEnPreparacion.sql            (pedir a Almacén que meta lo añadido en el envío de hoy)
           Scripts/Sugerencia1340_ModoJuntoConLoPendiente.sql     (modo 6: amplía la CHECK de ModoServicio)
         APARTE y sin prisa (probar antes en una copia): Scripts/Issue872_LinPedidoVta_EncolarSyncTienda.sql
      2) Publicar la API (master). 3) Publicar Nesto (1.10.64.0). 4) ESTE script.

    NO van aquí porque son SUGERENCIAS/INCIDENCIAS que se contestan en su propio hilo al publicar:
      1013 (prepago: deuda de otra empresa), 1328 (aplazar líneas con un envío ya entregado; Modificar con plantilla y las líneas con
      picking), 1329 (Retrasados y Devueltos), 1331 (tamaño de letra del cliente), 1339 (tamaño del producto en el buscador),
      1342 (forzar la validación al modificar con la plantilla). 1332 (dosier) y 1340 (modo nuevo) van TAMBIÉN aquí por ser para todos.

    Se omiten a propósito (nada perceptible o interno):
      - Embudo: numeración de bloques que no se repite. Sincronización de productos troceada. Republicaciones a Odoo y a la tienda.
      - Correos de seguimiento de compras: aviso de salida del proveedor con PDF, boletines y remitentes ignorados (falta el botón en Nesto).
      - Vigencia con un día de margen para los pedidos de la tienda (Black Friday). N+M sin fechas en las sugerencias de llamadas.
      - Dirección 2 y tipo de lugar en el mensaje a Odoo. Desglose en ELMAH del pedido retenido por prepago.
      - El bloqueo antiguo de «insertar líneas» que no saltaba nunca (sustituido por el pedido en preparación).

    Ejecutar contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.64.0';
DECLARE @fecha DATE = '2026-11-18';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Añadir productos a un pedido que ya está en preparación',
     N'Si añade productos a un pedido que ya tiene picking (o del que hoy ha salido parte), Nesto ya no lo guarda en silencio para que salga otro día sin portes. Le pregunta qué quiere hacer: «Hacer un pedido nuevo con lo añadido» (con sus propios portes; Nesto le dice el número y le ofrece abrirlo) o, si la agencia todavía no se lo ha llevado, «Pedir a Almacén que lo meta en el envío de hoy». En ese caso a Almacén le sale una ventana roja con «Sí, lo metemos» / «No llega»; si dicen que no, o nadie contesta en una hora, lo añadido pasa a un pedido nuevo y se le avisa a usted. Desde NestoApp, de momento, un pedido en preparación no se puede ampliar: hay que hacer un pedido nuevo.',
     NULL),
    ('Nuevo', N'Nuevo modo de entrega: «Junto con lo pendiente del cliente»',
     N'Si el cliente tiene algo pendiente para la misma dirección (por ejemplo, una nota de entrega que espera un producto), puede elegir que el pedido nuevo espere y salga en el mismo envío. El modo solo aparece cuando hay algo con lo que juntarlo, y debajo se indica con qué saldrá. La fecha que se da al cliente es la de lo pendiente, con el motivo. Si a los 15 días sigue esperando, Nesto le avisa para que decida si lo sirve ya. Si lo pendiente se anula o ya ha salido, el pedido sale solo, como un «Todo junto». Los portes se calculan sobre la suma de lo que viaja junto y se cobran una sola vez.',
     NULL),
    ('Nuevo', N'Eventos: el dosier se envía solo',
     N'En la ficha del evento hay un apartado «Dosier» para subir un PDF. Nesto lo manda por correo a todas las inscritas con la plaza pagada (o gratuita) unas 2 horas antes de empezar, y al momento a quien se inscriba o pague después. Puede abrirlo, sustituirlo o quitarlo, y en la ficha se ve cuándo sale y a cuántas ha llegado. Si lo sustituye cuando ya se ha enviado, Nesto le pregunta si quiere reenviar el nuevo a las que ya lo tienen.',
     N'Tiendas,Administración,Dirección'),
    ('Nuevo', N'Eventos: enlace de la conexión online en su propio campo',
     N'Los eventos online y mixtos tienen un campo propio, «Enlace de la conexión online», que solo ven las tiendas, Administración y Dirección. Las inscritas online lo reciben en el recordatorio de 2 días antes; si se inscriben más tarde, al momento; y si lo cambia, les llega un correo con el enlace nuevo. Si el evento no tiene enlace cuando alguien lo necesita, se le avisa en la campana para que lo ponga. Además, la hora de inicio del evento pasa a ser obligatoria.',
     N'Tiendas,Administración,Dirección'),
    ('Mejorado', N'Promesas en riesgo: frases cortas y solo lo que importa',
     N'El aviso de una promesa en riesgo dice ahora qué falta, cuándo llega y cuándo sale, sin enumerar lo que ya está en Algete, y con la fecha siempre escrita («el jueves 19/11»). Ya no avisa de un pedido cuando lo que faltaba acaba de llegar: si antes se avisó de la mala noticia, le llega una sola vez la buena («Faltaba X; llegó el martes 17/11»). Y si un pedido lo tiene todo en Algete y sigue sin salir al día siguiente, se le avisa para que lo revise.',
     NULL),
    ('Mejorado', N'Correo del pedido: el color de las líneas y la explicación de abajo ya coinciden',
     N'En el correo de cada pedido, el color de cada línea (verde, rosa o rojo) sale del mismo cálculo que la explicación de abajo («Entrega a la agencia… Por qué»). Una línea solo sale en rojo si a ese pedido le falta de verdad; ya no se pone roja porque otro pedido sin fecha tenga pedidas esas unidades. Las reservas que se piden a las tiendas son también las que dice esa explicación.',
     NULL),
    ('Mejorado', N'Fecha de entrega: los packs que se montan en Algete',
     N'Un pack que no está montado pero cuyos componentes sí están en Algete cuenta ya como disponible para la fecha de entrega. Si falta algún componente, la fecha es la de ese componente y la explicación lo nombra.',
     NULL),
    ('Mejorado', N'Unir pedidos: se ve que está trabajando',
     N'Al unir dos pedidos, la lista muestra «Uniendo los pedidos…» hasta que termina (con pedidos grandes puede tardar) y no deja lanzar otra unión mientras tanto.',
     NULL),
    ('Corregido', N'Ficha de cliente: al cambiar de cliente no queda nada del anterior',
     N'Al pasar de un cliente a otro ya no se queda nada del anterior en la ficha: ni el correo o el móvil del enlace de pago, ni sus facturas, pedidos o deudas, ni los datos de la reclamación. Si un cliente no se puede leer, la ficha se vacía y lo indica en la pestaña. El botón de cobrar un pedido funciona ya sin pasar antes por la pestaña de Deudas.',
     NULL),
    ('Corregido', N'Modificar con plantilla: se puede aceptar un pedido que no pasa la validación',
     N'Al modificar un pedido desde la Plantilla de Ventas, si el pedido no pasaba la validación Nesto lo rechazaba sin más, también a quien tiene permiso para aceptarlo. Ahora pregunta «¿Desea modificarlo de todos modos?», igual que al crear un pedido.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
