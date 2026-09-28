/*
    Novedades de la versión 1.10.33.0 (28/09/2026).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.33.0).

    ORDEN:
      1) YA EJECUTADOS por Carlos el 28/09: PreciosMedios_Paso0/Paso1, Carpeta_20260928_1 (regla del 2022 retirada)
         y Carpeta_20260928_2, GRANT INSERT/UPDATE de Novedades.
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce 1.10.33.0 (Clean + Rebuild).
      4) ESTE script. Idempotente: compara por Versión + Título.
      5) Encender NotaEntregaAutomatica = 1 y avisar a Alfredo en la campana (POST api/Notificaciones/AvisoNesto).

    SE OMITE A PROPÓSITO (el usuario de Nesto no lo nota o aún está apagado):
      - #547: precios medios (paso 0/1 en SQL; corte a = calculadora en C#; corte b = sombra apagada, falta Issue547_PreciosMediosSombra.sql).
      - #546: la API rechaza servicio/horario/retorno que no es de la agencia (guarda del servidor).
      - #494/#548: GLS e Innovatrans en la subasta de retornos; Innovatrans fuera hasta que DataTrans reciba el retorno.
      - #522: reenvío con incidencia = S.
      - Errores que ahora llegan a ELMAH, aviso a Carlos de la actividad en Novedades, POST AvisoNesto.
      - Correo de pedido de tienda sin «¡¡¡ ATENCIÓN !!!» (no es de Nesto).
      - Nesto#490 (4C.2): diálogos de Producto, Cliente y OfertasCombinadas (sin cambio visible).
      - Tests, issues, refactorizaciones.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.33.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-28';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Mejorado', N'Producto en carpeta: la factura va entera, también con los regalos',
     N'Hasta ahora, cuando en un pedido lo único que se quedaba «en carpeta» era un regalo (una línea a 0 €), Nesto lo devolvía a pendiente en vez de facturarlo, y si esa línea ya tenía picking la facturación fallaba («No puede cambiar el campo recoger si la línea tiene picking»). Desde hoy el criterio es otro: la factura se hace con todo el pedido, regalos incluidos, y lo que no se entrega en ese momento (también el regalo) se queda en carpeta y se entrega después con su nota de entrega. Así la factura siempre cuadra con lo que ha comprado el cliente y las entregas van por otro lado.'),
    ('Mejorado', N'Las notas de entrega del producto en carpeta se crean solas',
     N'Cuando se factura un pedido que deja producto en carpeta, Nesto crea automáticamente la nota de entrega de lo pendiente; ya no hace falta crearla a mano. Revisad los primeros días que salen bien y avisad si falta alguna.'),
    ('Nuevo', N'Copiar los datos para la transferencia de un pedido prepago',
     N'En el detalle de un pedido prepago por transferencia hay un botón para copiar de una vez los datos que necesita el cliente: IBAN, beneficiario, importe y el concepto «Cliente NNNNN - Pedido MMMMMM». Se pegan directamente en el correo o el WhatsApp. Gracias, Paloma, por la sugerencia.'),
    ('Mejorado', N'El prepago nuevo nace con el concepto y la cuenta puestos',
     N'Al crear un prepago en el detalle del pedido, el concepto ya viene como «por TRN pedido NNNNNN» y la cuenta es la de Caixabank (57200013), que ahora se elige con un desplegable en vez de escribirla.'),
    ('Nuevo', N'Botón de Novedades junto a la campana',
     N'Al lado de la campana hay un megáfono que abre esta ventana de Novedades y sugerencias desde cualquier pestaña. Y cuando llega el aviso de versión nueva, al pulsarlo se abre Novedades directamente en esa versión.'),
    ('Nuevo', N'Vídeos: borrar duplicados y dar de baja',
     N'En la ventana Vídeos se marcan los vídeos duplicados y hay dos botones nuevos: «Borrar vídeo», para quitar un duplicado con sus productos, y «Dar de baja», para retirar cualquier vídeo. No se puede borrar el vídeo activo si sus duplicados están dados de baja, para no quedarnos sin ninguno.'),
    ('Nuevo', N'Administración: ventana de facturas pendientes de Verifactu',
     N'En Contabilidad hay un botón nuevo, «Facturas Verifactu», con las facturas que todavía no se han podido registrar en Hacienda o que Hacienda ha dado por incorrectas: para cada una se ve el motivo y qué hay que hacer, y con «Reintentar el envío» se vuelve a mandar una vez corregido el dato (por ejemplo, el NIF). Los días laborables a las 8:30, si hay alguna pendiente, os llega un aviso a la campana que abre directamente esa ventana.'),
    ('Mejorado', N'Agencias: «En reparto» y «Entregado» se ponen al día cada media hora',
     N'En Tramitados, el estado de los envíos (en reparto, entregado, último evento) se consulta a las agencias cada media hora de lunes a sábado de 8:00 a 20:00, en vez de cada dos horas. Para verlo, recargad la pestaña.'),
    ('Mejorado', N'«Recoger producto» elige la agencia más económica',
     N'Al pedir «Recoger producto» desde el detalle del pedido o la plantilla, ya no sale siempre por GLS (o por la agencia del primer envío): Nesto elige la agencia más económica para la entrega y la recogida. Si sale por CTT, el cliente recibe la etiqueta en PDF en el correo de «pedido entregado a la agencia» para pegarla en el paquete.'),
    ('Corregido', N'Agencias > Pendientes ya no cambia el retorno ni el servicio de otro envío',
     N'Con la pantalla de Agencias en una agencia, al pinchar un pendiente de otra (por ejemplo, uno de GLS estando en CTT) el retorno aparecía como «NO» y, al guardar, el envío se quedaba con el servicio y el horario de la otra agencia. Ya se respetan los del envío.'),
    ('Corregido', N'La cuenta del pedido no sale repetida debajo del desplegable',
     N'En la pestaña de pago del pedido, el texto «Se cargará en…» repetía la misma cuenta que ya se veía en el desplegable. Ahora solo sale cuando la cuenta a cargar es otra distinta. El aviso en rojo sigue saliendo siempre que haga falta.'),
    ('Corregido', N'La lista de pedidos encuentra al cliente aunque se escriba un espacio de más',
     N'En la lista de pedidos, si al buscar por número de cliente o de vendedor se colaba un espacio al final, no salía nada. Ya lo encuentra. En las búsquedas por texto el espacio se sigue respetando (por ejemplo, «tinte lk »).'),
    ('Corregido', N'Conciliación: ya no dice «contabilizado en asiento -1»',
     N'Al contabilizar un apunte desde la conciliación, a veces salía «contabilizado en asiento -1» sin haberse contabilizado nada. Ahora Nesto explica qué ha pasado y el error queda registrado para revisarlo.'),
    ('Corregido', N'Guardar un pedido sin vendedor por grupo ya no falla',
     N'Algunos pedidos daban «no se puede insertar el valor NULL en la columna vendedor» al guardarlos. Ya se guardan bien.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 14 filas, sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
