/*
    Novedades de la revisión 1.10.61.1 (12/11/2026, noche): API + Nesto. Revisión menor a propósito (Carlos): van en la misma
    página que las de la 1.10.61.0, que los usuarios aún no han visto.

    ORDEN:
      1) Scripts de tablas (sa): Issue857c_ComprasCorreosEntregaEstimada e Issue857d_ComprasCorreosAplicarFecha YA lanzados
         el 12/11; falta Issue853_PlazosProveedoresYEnvios.sql.
      2) Publicar la API (master). 3) Publicar Nesto (1.10.61.1). 4) ESTE script.

    SOLO SI SE PUBLICA NESTO: la primera (la ventana). Las otras dos valen aunque Nesto no salga.

    Se omiten a propósito (nada perceptible todavía):
      - Plazo real por proveedor al enviar el pedido de compra (NestoAPI#853): interruptor apagado.
      - Modelo ML.NET de plazos en sombra y registro de envíos de pedidos de compra: solo recogen datos.
      - La asignación por número de pedido de los correos, endurecida; los envíos nuestros de salida en copia.

    Ejecutar contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.61.1';
DECLARE @fecha DATE = '2026-11-12';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Compras: envíos de proveedor por asignar',
     N'Ventana nueva (Productos › Compras › «Envíos por asignar»). Cuando llega al buzón de Compras el aviso de una agencia de que un proveedor ha enviado mercancía, la fecha de recepción del pedido de compra se actualiza sola si solo puede ser de un pedido. Si el proveedor tiene varios pedidos pendientes, el envío aparece en esta ventana con un pedido sugerido: se elige el pedido y se pulsa «Asignar», o «No es una compra» si el aviso no corresponde a ninguna compra. Los avisos del mismo envío (por ejemplo, uno por bulto) van agrupados y se resuelven de una vez. Cuando hay algo por asignar llega un aviso a la campana.',
     NULL),
    ('Mejorado', N'La fecha de entrega dice ahora lo segura que es',
     N'En el correo del pedido y en la explicación de la fecha de entrega, lo que no sale del stock de Algete indica su grado de certeza: «Prácticamente segura» si está en otra tienda o si el proveedor ya lo ha enviado y la agencia ha dado fecha; «Aproximada» si hay pedido al proveedor pero aún no hay seguimiento de la agencia; y «Orientativa» cuando todavía no hay pedido al proveedor: el pedido sigue sin fecha, pero se indica sobre qué día estimamos que podría salir. La fecha orientativa no es un compromiso con el cliente.',
     NULL),
    ('Mejorado', N'La fecha de recepción de las compras se actualiza con el aviso de la agencia',
     N'Cuando la agencia avisa por correo de que un pedido de un proveedor está en camino, la fecha de recepción de ese pedido de compra pasa a ser la que indica el aviso (si da un tramo horario, se toma el final). Con ello se recalcula la fecha de entrega de los pedidos de venta que lo esperan y, si se retrasa, se avisa como siempre.',
     NULL);

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
