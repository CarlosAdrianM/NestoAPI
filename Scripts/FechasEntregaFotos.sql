/*
    Fechas de entrega fiables (corte 1, EN SOMBRA, 11/11/26): medir el error de la fecha que damos a lo que NO sale del stock de Algete.

    1. dbo.FechasEntregaFotos: por pedido de venta pendiente, producto y día en que la promesa CAMBIA (no una fila diaria idéntica), la
       fecha en que podía salir todo lo del producto (FechaSalida, NULL = sin fecha), de qué dependía (Origen: Tienda, EnCamino,
       Proveedor, SinStock; Caso: Interno, SinPedir, PedidoEnviado, EnTransito), el pedido a proveedor y su prevista, y la fecha de entrega
       a la agencia del pedido entero ese día. La escribe el job «fechas-entrega-fotos-sombra» (L-S 4:50); si se relanza el mismo día,
       sustituye la de ese día (idempotente por Empresa + Pedido + Producto + FechaFoto).
    2. dbo.vFechasEntregaError: cada producto de un pedido ya albaraneado ENTERO (ninguna línea suya pendiente) con su primera y su
       última foto y el error en días (fecha del último albarán de ese producto − FechaSalida). Positivo = salió tarde.
       La resume GET api/PedidosVenta/FechasEntrega/Error?meses=3 (Dirección e Informática).

    Lanzar como sa en NV. Puede ir ANTES o DESPUÉS de publicar la API (sin la tabla y la vista, el job no hace nada y el GET devuelve
    la lista vacía: «sin tabla = sin función»). Idempotente.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.FechasEntregaFotos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FechasEntregaFotos (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FechasEntregaFotos PRIMARY KEY CLUSTERED,
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Producto varchar(15) NOT NULL,
        FechaFoto date NOT NULL,
        FechaHora datetime NOT NULL CONSTRAINT DF_FechasEntregaFotos_FechaHora DEFAULT (GETDATE()),
        Unidades int NOT NULL,
        FechaSalida date NULL,                    -- puede salir todo lo del producto; NULL = alguna parte sin fecha
        Origen varchar(20) NULL,                  -- el de la partida que manda (PartidaFechaEntregaAgenciaDTO)
        Caso varchar(20) NULL,                    -- Interno, SinPedir, PedidoEnviado, EnTransito
        PedidoProveedor int NULL,
        FechaPrevistaProveedor date NULL,         -- LinPedidoCmp.FechaRecepción de ese día (luego se pisa: por eso se guarda)
        PrevistaVencida bit NOT NULL CONSTRAINT DF_FechasEntregaFotos_PrevistaVencida DEFAULT (0),
        FechaEntregaAgencia date NULL,            -- la del pedido entero ese día (la que ve el vendedor)
        FechaPrometidaAlCrear date NULL           -- CabPedidoVta.FechaEntregaAgenciaPrometida (NestoAPI#606)
    );
    CREATE UNIQUE INDEX UX_FechasEntregaFotos_PedidoProductoDia ON dbo.FechasEntregaFotos (Empresa, Pedido, Producto, FechaFoto);
END
GO
GRANT SELECT, INSERT, UPDATE ON dbo.FechasEntregaFotos TO [NUEVAVISION\RDS2016$];
GO
IF OBJECT_ID(N'dbo.vFechasEntregaError', N'V') IS NOT NULL
    DROP VIEW dbo.vFechasEntregaError;
GO
CREATE VIEW dbo.vFechasEntregaError
AS
WITH f AS (
    SELECT x.*,
        ROW_NUMBER() OVER (PARTITION BY x.Empresa, x.Pedido, x.Producto ORDER BY x.FechaFoto, x.Id) AS OrdenPrimera,
        ROW_NUMBER() OVER (PARTITION BY x.Empresa, x.Pedido, x.Producto ORDER BY x.FechaFoto DESC, x.Id DESC) AS OrdenUltima,
        COUNT(*) OVER (PARTITION BY x.Empresa, x.Pedido, x.Producto) AS Fotos
    FROM dbo.FechasEntregaFotos x
)
SELECT p.Empresa, p.Pedido, p.Producto,
    p.Origen AS OrigenPrimera, p.Caso AS CasoPrimera, cp.[NºProveedor] AS ProveedorPrimera, p.FechaSalida AS FechaSalidaPrimera,
    u.Origen AS OrigenUltima, u.Caso AS CasoUltima, cu.[NºProveedor] AS ProveedorUltima, u.FechaSalida AS FechaSalidaUltima,
    p.Fotos, a.FechaAlbaran,
    DATEDIFF(day, p.FechaSalida, a.FechaAlbaran) AS ErrorPrimeraDias,
    DATEDIFF(day, u.FechaSalida, a.FechaAlbaran) AS ErrorUltimaDias
FROM f p
INNER JOIN f u ON u.Empresa = p.Empresa AND u.Pedido = p.Pedido AND u.Producto = p.Producto AND u.OrdenUltima = 1
CROSS APPLY (SELECT CAST(MAX(l.[Fecha Albarán]) AS date) AS FechaAlbaran
             FROM dbo.LinPedidoVta l WITH (NOLOCK)
             WHERE l.Empresa = p.Empresa AND l.Número = p.Pedido AND l.Producto = p.Producto AND l.Estado >= 2) a
LEFT JOIN dbo.CabPedidoCmp cp WITH (NOLOCK) ON cp.Empresa = p.Empresa AND cp.Número = p.PedidoProveedor
LEFT JOIN dbo.CabPedidoCmp cu WITH (NOLOCK) ON cu.Empresa = u.Empresa AND cu.Número = u.PedidoProveedor
WHERE p.OrdenPrimera = 1
  AND a.FechaAlbaran IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK)
                  WHERE l.Empresa = p.Empresa AND l.Número = p.Pedido AND l.Producto = p.Producto AND l.Estado >= -1 AND l.Estado <= 1);
GO
GRANT SELECT ON dbo.vFechasEntregaError TO [NUEVAVISION\RDS2016$];
GO
