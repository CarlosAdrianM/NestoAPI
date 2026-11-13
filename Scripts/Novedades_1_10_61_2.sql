/*
    Novedades de la revisión 1.10.61.2 (13/11/2026): API + Nesto. Revisión menor: tanda de mejoras y correcciones.

    ORDEN:
      1) Scripts (sa) YA lanzados hoy: Issue860_Prepagos_FechaCobro, Issue860_Prepagos_AsientoAparte, Issue861_FechasOrientativasSinPedir,
         Novedades1263_OfertaAcumulaPrecioEspecial, Novedades1261_PlantillaMantenerAbierta.
         Issue864_PagoAPlazos.sql (ya lanzado) y, tras publicar, Issue864_PagoAPlazos_Activar.sql (lo enciende para todos).
      2) Publicar la API (master). 3) Publicar Nesto (1.10.61.2). 4) ESTE script.
      5) Novedades1261_GloboPlantillaMantenerAbierta.sql (el globo de la casilla; solo con Nesto 1.10.61.2 publicado).

    NO van aquí porque son SUGERENCIAS de usuarios que pasan a «Implementada» en su propio hilo (se marcan por la API con su versión):
      1256 (nota de cobro a cualquier cliente), 1257 (manifiesto de nuestro reparto separado por ruta),
      1261 (dejar una plantilla nueva abierta al grabar el pedido) y 1263 (packs de la feria: precio especial y 6+1).

    Se omiten a propósito (nada perceptible todavía):
      - Medición de las fechas orientativas de lo no pedido (NestoAPI#861): solo recoge datos por la noche.
      - Fecha real del cobro en el prepago (NestoAPI#860): la API ya la guarda, pero nadie la rellena todavía.
      - Embudo de Odoo: los 7 días entre subidas cuentan desde el primer bloque.
      - Los globos esperan a que su control se vea; los servicios de Cajas usan la fábrica común de la API.

    Ejecutar contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.61.2';
DECLARE @fecha DATE = '2026-11-13';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Pago a plazos de un pedido ya creado',
     N'En el detalle del pedido, pestaña Cabecera, debajo de «Mandar enlace pago», hay un botón nuevo «Pago a plazos…». Sirve para que el cliente financie ese pedido con la financiera (el nombre sale en el propio botón de la ventana). Se puede mandar a la app del cliente, que recibe un aviso y termina el pago desde su móvil, o generar un enlace para copiarlo y enviárselo. Debajo del botón se ve cómo va: pendiente del cliente, aprobado (con la fecha límite para que salga el pedido), cobrado, rechazado, caducado o cancelado; y mientras esté pendiente o aprobado se puede cancelar indicando el motivo. Si el pedido no se puede financiar, el botón sale apagado y al pasar el ratón dice por qué. Cuando el cliente termina, a quien pasó el pedido y a su vendedor les llega un aviso a la campana. Es la primera versión: si ve algo raro, cuéntelo en este hilo.',
     NULL),
    ('Mejorado', N'Enviar reposición: solo sale de la tienda lo que se lee',
     N'Al preparar la reposición para Algete hay que leer el código de barras de cada unidad que se mete en la caja, igual que se hace en Algete al recibirla. Cada línea empieza en «0 de N» en la columna nueva «Leídas» y cada lectura suma una unidad; si el código no es de la reposición o ya se han leído todas las de ese producto, suena el error y esa unidad se queda en la tienda. Teclear la referencia no cuenta como lectura. Al terminar solo se manda lo leído: lo que falte se lo dice antes de confirmar y se queda en la tienda. Un producto sin código de barras se lee con la etiqueta que se le imprime, así que hay que pegarla antes de leerlo. Las muestras sin código se siguen tecleando como hasta ahora.',
     NULL),
    ('Mejorado', N'Nesto avisa cuando otro usuario tiene bloqueada la base de datos',
     N'Si una operación no se puede completar porque otro usuario tiene algo a medias (por ejemplo, un albarán o un asiento sin terminar), aparece una banda en la parte de abajo de Nesto que dice quién lo tiene bloqueado y desde cuándo, para poder avisarle. La banda no interrumpe lo que se está haciendo y se cierra sola. Además, en Cajas y Bancos el mensaje de error incluye ahora ese mismo dato.',
     NULL),
    ('Mejorado', N'Bancos: la regla de Aplázame admite varios ingresos de una misma venta',
     N'En el cuadre de bancos, si Aplázame liquida una venta en más de un ingreso (por ejemplo, tras cancelar una operación y rehacerla con otro importe), ya se pueden seleccionar todos los ingresos junto con el apunte de contabilidad: el botón «Contabilizar apunte» se habilita con la regla «Aplázame» y contabiliza la comisión del total.',
     NULL),
    ('Mejorado', N'El cobro de un prepago se contabiliza en su propio asiento',
     N'Al facturar un pedido con prepago, el cobro ya no va dentro del asiento de la factura: se contabiliza en un asiento aparte, justo a continuación, con el mismo número de documento y el concepto «Prepago…». La factura queda liquidada igual que antes. Es el primer paso para que el cobro lleve su fecha real y el cuadre del banco case por fechas.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
