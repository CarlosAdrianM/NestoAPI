/*
    NestoAPI#861 (13/11/26): apuntar cada día la fecha ORIENTATIVA de lo no pedido (NestoAPI#853, ReglaFechaOrientativaSinPedir) con
    sus ingredientes, y medirla después contra el pedido de compra y el albarán reales. La regla NO cambia ni cambia ninguna fecha que vea
    el usuario.

    Crea:
      - dbo.FechasOrientativasSinPedir (fuera del EDMX): una fila por (DiaFoto, Empresa, Producto) de cada producto con venta pendiente
        que, según el motor de la fecha de entrega, se queda SIN fecha y con fecha orientativa de lo no pedido. La escribe el job de
        Hangfire 'fechas-orientativas-sin-pedir' (todos los días a las 3:50 de Madrid, después de los plazos de las 3:40) o, a mano,
        POST api/Compras/PlazosProveedores/Orientativas/Procesar (Dirección o Informática).
          · Foto: Proveedor (habitual), Unidades y PedidosVenta que lo esperan, UltimoPedido al proveedor, Cadencia
            (IntervaloPedidosMediano), DiasDesdeUltimo, DiasHastaPedido (max(1, cadencia − días desde el último)), FechaPedidoPrevista
            (DiaFoto + DiasHastaPedido), PlazoDias y PlazoPropio (P80 propio o genérico), FechaLlegadaOrientativa (lo que da la regla),
            FechaSalidaOrientativa (lo que ve el usuario: el laborable siguiente, nunca antes de que entre en el picking), YaTocabaPedir
            (DiasDesdeUltimo >= Cadencia) y PrimeraDelEpisodio (el producto no tenía foto el laborable anterior: una falta de 10 días
            cuenta UNA vez).
          · Resolver (cada noche, mientras siga abierta): el primer pedido de compra al MISMO proveedor (empresa 1 o espejo 3) que lleva
            el producto y se envió el día de la foto o después (día de envío: dbo.PedidosCompraEnvios.FechaEnvio o, si no está,
            CabPedidoCmp.[Fecha Modificación]); el primer albarán de ese producto en ese pedido; ErrorPedidoDias = real − previsto y
            ErrorLlegadaDias = albarán − FechaLlegadaOrientativa (positivo = la orientativa se quedó corta). Con albarán: ResueltaEl.
            Sin albarán a los 120 días de la foto (pedida o no): Caducada = 1.
      - dbo.vFechasOrientativasError: resumen por empresa y proveedor de las PRIMERAS fotos de cada episodio ya resueltas: casos, error
        medio y mediano de pedido y de llegada, % que se queda corta; Grupo = 'Todas', 'YaTocaba' o 'NoTocaba'. (El endpoint
        GET api/Compras/PlazosProveedores/Orientativas?meses=6 calcula lo mismo, filtrando por los meses, con los totales.)

    REQUIERE Scripts/Issue853_PlazosProveedoresYEnvios.sql (dbo.PedidosCompraEnvios): sin él este script no hace nada y lo dice.

    ORDEN: puede ir antes o después de publicar la API. Sin este script la API no apunta nada (el job lo avisa una vez en ELMAH) y los
    endpoints dan 503. Tras lanzarlo, para no esperar a la noche: POST api/Compras/PlazosProveedores/Orientativas/Procesar.

    Lanzar como sa en NV. Idempotente: se puede relanzar (la vista se rehace).
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.PedidosCompraEnvios', N'U') IS NULL
    RAISERROR(N'Falta dbo.PedidosCompraEnvios: lance antes Scripts/Issue853_PlazosProveedoresYEnvios.sql.', 16, 1);
GO
IF OBJECT_ID(N'dbo.PedidosCompraEnvios', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.FechasOrientativasSinPedir', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FechasOrientativasSinPedir (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FechasOrientativasSinPedir PRIMARY KEY CLUSTERED,
        DiaFoto date NOT NULL,
        Empresa char(3) NOT NULL,
        Producto varchar(15) NOT NULL,
        Proveedor varchar(10) NOT NULL,
        Unidades int NOT NULL,                         -- unidades sin fecha de todos los pedidos de venta que lo esperan
        PedidosVenta int NOT NULL,
        UltimoPedido date NULL,                        -- último pedido al proveedor (ProveedoresPlazosEntrega)
        Cadencia int NOT NULL,                         -- IntervaloPedidosMediano
        DiasDesdeUltimo int NOT NULL,
        DiasHastaPedido int NOT NULL,                  -- max(1, Cadencia - DiasDesdeUltimo)
        FechaPedidoPrevista date NOT NULL,             -- DiaFoto + DiasHastaPedido
        PlazoDias int NOT NULL,                        -- P80 propio o genérico
        PlazoPropio bit NOT NULL,
        FechaLlegadaOrientativa date NOT NULL,         -- lo que da ReglaFechaOrientativaSinPedir
        FechaSalidaOrientativa date NULL,              -- lo que ve el usuario (PartidaFechaEntregaAgenciaDTO.FechaOrientativa)
        YaTocabaPedir bit NOT NULL,
        PrimeraDelEpisodio bit NOT NULL,
        -- Lo real (lo rellena el job después)
        EmpresaPedidoReal char(3) NULL,
        PedidoReal int NULL,
        FechaPedidoReal date NULL,
        FechaPedidoRealDeEnvios bit NULL,              -- 1 = FechaEnvio de PedidosCompraEnvios; 0 = CabPedidoCmp.[Fecha Modificación]
        FechaAlbaranReal date NULL,
        ErrorPedidoDias int NULL,                      -- FechaPedidoReal - FechaPedidoPrevista
        ErrorLlegadaDias int NULL,                     -- FechaAlbaranReal - FechaLlegadaOrientativa (positivo = se quedó corta)
        ResueltaEl datetime NULL,
        Caducada bit NOT NULL CONSTRAINT DF_FechasOrientativasSinPedir_Caducada DEFAULT (0),
        FechaHora datetime NOT NULL CONSTRAINT DF_FechasOrientativasSinPedir_FechaHora DEFAULT (GETDATE())
    );
END
GO
IF OBJECT_ID(N'dbo.FechasOrientativasSinPedir', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_FechasOrientativasSinPedir_Foto' AND object_id = OBJECT_ID(N'dbo.FechasOrientativasSinPedir'))
    CREATE UNIQUE INDEX UX_FechasOrientativasSinPedir_Foto ON dbo.FechasOrientativasSinPedir (Empresa, Producto, DiaFoto);
GO
IF OBJECT_ID(N'dbo.FechasOrientativasSinPedir', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FechasOrientativasSinPedir_Abiertas' AND object_id = OBJECT_ID(N'dbo.FechasOrientativasSinPedir'))
    CREATE INDEX IX_FechasOrientativasSinPedir_Abiertas ON dbo.FechasOrientativasSinPedir (Empresa, Caducada, Id)
        INCLUDE (ResueltaEl, DiaFoto, Producto, Proveedor);
GO
IF OBJECT_ID(N'dbo.FechasOrientativasSinPedir', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE ON dbo.FechasOrientativasSinPedir TO [NUEVAVISION\RDS2016$];
GO
IF OBJECT_ID(N'dbo.vFechasOrientativasError', N'V') IS NOT NULL
    DROP VIEW dbo.vFechasOrientativasError;
GO
IF OBJECT_ID(N'dbo.FechasOrientativasSinPedir', N'U') IS NOT NULL
    EXEC (N'
CREATE VIEW dbo.vFechasOrientativasError
AS
WITH Casos AS (
    SELECT f.Empresa, f.Proveedor, g.Grupo, f.ErrorPedidoDias, f.ErrorLlegadaDias
    FROM dbo.FechasOrientativasSinPedir f
    CROSS APPLY (SELECT ''Todas'' AS Grupo
                 UNION ALL
                 SELECT CASE WHEN f.YaTocabaPedir = 1 THEN ''YaTocaba'' ELSE ''NoTocaba'' END) g
    WHERE f.PrimeraDelEpisodio = 1 AND f.ResueltaEl IS NOT NULL AND f.ErrorPedidoDias IS NOT NULL AND f.ErrorLlegadaDias IS NOT NULL
), Numerados AS (
    SELECT c.*,
        COUNT(*) OVER (PARTITION BY c.Empresa, c.Proveedor, c.Grupo) AS N,
        ROW_NUMBER() OVER (PARTITION BY c.Empresa, c.Proveedor, c.Grupo ORDER BY c.ErrorPedidoDias) AS RnPedido,
        ROW_NUMBER() OVER (PARTITION BY c.Empresa, c.Proveedor, c.Grupo ORDER BY c.ErrorLlegadaDias) AS RnLlegada
    FROM Casos c
)
SELECT n.Empresa, n.Proveedor, n.Grupo,
    COUNT(*) AS Casos,
    CAST(AVG(1.0 * n.ErrorPedidoDias) AS decimal(9,2)) AS ErrorPedidoMedio,
    MAX(CASE WHEN n.RnPedido = (n.N + 1) / 2 THEN n.ErrorPedidoDias END) AS ErrorPedidoMediano,   -- rango más cercano, como en la API
    CAST(AVG(1.0 * n.ErrorLlegadaDias) AS decimal(9,2)) AS ErrorLlegadaMedio,
    MAX(CASE WHEN n.RnLlegada = (n.N + 1) / 2 THEN n.ErrorLlegadaDias END) AS ErrorLlegadaMediano,
    CAST(100.0 * SUM(CASE WHEN n.ErrorLlegadaDias > 0 THEN 1 ELSE 0 END) / COUNT(*) AS decimal(9,2)) AS PorcentajeCorta
FROM Numerados n
GROUP BY n.Empresa, n.Proveedor, n.Grupo');
GO
IF OBJECT_ID(N'dbo.vFechasOrientativasError', N'V') IS NOT NULL
    GRANT SELECT ON dbo.vFechasOrientativasError TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación
SELECT CASE WHEN OBJECT_ID(N'dbo.FechasOrientativasSinPedir', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.vFechasOrientativasError', N'V') IS NOT NULL
             AND OBJECT_ID(N'dbo.PedidosCompraEnvios', N'U') IS NOT NULL
            THEN N'OK: NestoAPI#861 (FechasOrientativasSinPedir y vFechasOrientativasError)'
            ELSE N'FALTA algo (¿se lanzó antes Scripts/Issue853_PlazosProveedoresYEnvios.sql?)' END AS Resultado;
GO
