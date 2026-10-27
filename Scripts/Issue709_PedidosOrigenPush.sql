/*
    NestoAPI#709 (gemela de TiendasNuevaVision#95): tabla PedidosOrigenPush.

    ¿Venden las push? La app NestoTiendas recuerda la última push con la que se abrió (tipo, campaña y fecha de apertura) durante
    7 días o hasta el siguiente pedido, y la manda en el POST de crear pedido (api/Pedidos/Cliente: OrigenPushTipo,
    OrigenPushCampana, OrigenPushFecha). La API guarda aquí una fila por pedido creado con esos datos, best-effort: sin la tabla
    o con un fallo al insertar, un aviso a ELMAH y el pedido se crea igual.

    Tipo:           el «tipo» de los datos de la push: reposicion, chequeregalo, AnunciosEventos, NuevoProtocolo, AvisosEstadoPedido…
    Campana:        la campaña, si la push la trae (cheques regalo). NULL si no.
    FechaApertura:  cuándo se abrió la app desde la push (la dice el móvil; NULL si no vino o era una fecha futura).
    FechaPedido:    cuándo se creó el pedido.

    El informe (GET api/PedidosCliente/OrigenPush/Informe) cruza esta tabla con los pedidos de la app (usuario APP\<cliente> o
    líneas en forma de venta APP) para comparar, por semana, pedidos y base imponible con push frente a sin push.

    No está en el EDMX (la API la lee y escribe con SQL a mano, como CorreosPostCompraRecomendaciones #707): se puede lanzar antes
    o después del deploy. Sin ella la API no guarda nada (y en el informe todos los pedidos de la app salen sin push).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT e INSERT. Sin UPDATE ni DELETE: las filas no se tocan.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PedidosOrigenPush', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidosOrigenPush (
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Tipo varchar(40) NOT NULL,
        Campana varchar(60) NULL,
        FechaApertura datetime NULL,
        FechaPedido datetime NOT NULL CONSTRAINT DF_PedidosOrigenPush_FechaPedido DEFAULT (GETDATE()),

        CONSTRAINT PK_PedidosOrigenPush PRIMARY KEY CLUSTERED (Empresa, Pedido)
    );
END
GO

-- Para leer por fechas sin pasar por CabPedidoVta. Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PedidosOrigenPush_FechaPedido' AND object_id = OBJECT_ID('dbo.PedidosOrigenPush'))
    CREATE INDEX IX_PedidosOrigenPush_FechaPedido ON dbo.PedidosOrigenPush (FechaPedido) INCLUDE (Tipo, Campana);
GO

GRANT SELECT, INSERT ON dbo.PedidosOrigenPush TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.PedidosOrigenPush', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.PedidosOrigenPush) AS Filas;
GO

-- Lo de la última semana:
-- SELECT o.FechaPedido, o.Pedido, o.Tipo, o.Campana, o.FechaApertura
-- FROM dbo.PedidosOrigenPush o WHERE o.FechaPedido >= DATEADD(day, -7, GETDATE()) ORDER BY o.FechaPedido;
