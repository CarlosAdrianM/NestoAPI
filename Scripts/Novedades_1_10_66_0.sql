/*
    Novedades de la versión 1.10.66.0 (20/11/2026): API + Nesto. Va con Ariadna 2.41 (Scripts/Novedades_Ariadna_2_41.sql).

    ORDEN:
      1) Scripts (sa), ANTES de publicar la API. YA LANZADOS el 20/11/26 a las 14:08-14:25:
           Scripts/RepartoLecturasRechazadas.sql, Scripts/RepartoRecogidasNoRecogidas.sql, Scripts/RepartoEnviosInternos.sql,
           Scripts/PedidosAutorizacionesSolicitadas.sql, Scripts/Issue590_FormaPagoYCccPedido_BloqueoNestoViejo.sql (en Sombra) y
           Scripts/Issue857h_ComprasCorreosLineasAplicadas.sql.
      2) Publicar la API (master). 3) Publicar Nesto (1.10.66.0). 4) Lanzar los flujos de Ariadna 2.41 (la lista de «solo lo que
         llevo», «No se ha recogido» y los envíos internos necesitan la API de hoy). 5) ESTE script.
      NO encender `Reparto.CargaObligatoria` hasta que todas las PDA tengan Ariadna 2.41.

    NO van aquí porque son SUGERENCIAS/INCIDENCIAS que se contestan en su propio hilo al publicar:
      1258 (pedir autorización de un pedido), 1277 (envíos internos entre sedes), 1283 (borrar pedido), 1315 (comentario al recoger),
      1369 (panel de ofertas plegable), 1398 (formación en la plantilla), 1400 (ventana del cobro en caja).

    Se omiten a propósito (nada perceptible o interno):
      - Lecturas rechazadas del reparto (registro para diagnóstico) e informe por repartidor (aún sin pantalla).
      - Leads de Meta: columna «nombre_y_apellidos», aviso a quien gestiona y aviso del lead que no se puede importar.
      - Correos de compras: Schenker/DSV, TXT y el aviso de salida de Weelko (lo perceptible va abajo, en una línea).
      - Bloqueo de forma de pago y CCC en Nesto viejo (en sombra). Picking de cierre con solo clientes cerrados.
      - Novedades por Id en la API, interruptor de carga obligatoria (apagado) y «Imprimir lista» sin caducadas (1362, ya contestada).

    Ejecutar contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.66.0';
DECLARE @fecha DATE = '2026-11-20';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Reparto propio: un envío que va en la furgoneta no se puede cerrar ni borrar desde la oficina',
     N'Si un repartidor ha cargado un bulto, el envío es suyo hasta que lo entregue o lo descargue. Al intentar anularlo, borrarlo o cambiarle la agencia o el estado, Nesto lo dice: «No se puede: el envío 251169 lo lleva Andre en la furgoneta desde las 9:42. Que lo descargue o lo entregue primero.» En el reparto propio ya no existe «Dar por cerrado»: los envíos los cierra el repartidor al entregar.',
     NULL),
    ('Nuevo', N'Agencias: «Añadir nota» a un envío',
     N'En Agencias › Tramitados y en la pestaña Entrega del pedido puede añadir una nota al envío (por ejemplo, «entregado con firma en papel»). Queda en su historia con su nombre y la hora, sin cambiar nada del envío.',
     NULL),
    ('Nuevo', N'Recogidas: «Anular recogida» y pestaña «No recogidas»',
     N'Una nota de recogida que ya no hay que hacer se anula con «Anular recogida», indicando el motivo. Y cuando el repartidor marca «No se ha recogido», la recogida ya no se cierra sola: aparece en la pestaña «No recogidas» de Recogidas solicitadas para que Almacén decida si vuelve al reparto otro día (la misma nota) o se cierra del todo.',
     NULL),
    ('Mejorado', N'Borrar un envío deja siempre rastro',
     N'Al borrar un envío de cualquier agencia se guarda quién lo borró, cuándo y todos sus datos e historia, para poder consultarlo después.',
     NULL),
    ('Mejorado', N'La campana abre la novedad al momento',
     N'Al pulsar un aviso de Novedades en la campana, la novedad o el comentario se abren enseguida, sin esperar a que cargue toda la lista ni las capturas.',
     NULL),
    ('Mejorado', N'Ficha de cliente: al cambiar la calle se revisa «¿Qué es?»',
     N'Si cambia la calle de un cliente, Nesto le pide revisar también el piso, local o rótulo: puede dejarlo como está, cambiarlo o quitarlo. Así no se queda el piso de la dirección antigua.',
     NULL),
    ('Mejorado', N'Modo de servicio: «Según vaya entrando» también cuando no hay nada disponible',
     N'Si de un pedido no hay nada ni en Algete ni en las tiendas y tiene varias líneas (que pueden llegar en fechas distintas), ahora se ofrece también «Según vaya entrando», además de «Todo junto».',
     NULL),
    ('Mejorado', N'Compras: la fecha de recepción, solo en las líneas que vienen en el aviso del proveedor',
     N'Cuando el aviso de salida de un proveedor dice qué productos envía, Nesto pone la fecha de recepción solo en esas líneas del pedido de compra, no en todo el pedido, y lo indica en el correo de aviso a Compras (también si el envío es parcial).',
     NULL),
    ('Corregido', N'Reparto propio: cambiar el reembolso de un envío ya no da error',
     N'Al modificar el reembolso de un envío del reparto propio salía «Esta agencia no tiene establecida una cuenta de reembolsos». Ya se puede cambiar.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo, Perfiles FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
