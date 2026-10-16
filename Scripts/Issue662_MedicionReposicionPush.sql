-- NestoAPI#662: ¿sirve la push de reposición? SOLO LECTURA.
-- Por cada push de reposición enviada (sale del buzón: NotificacionesBuzon, Datos con "tipo":"reposicion"), y por cada
-- producto que llevaba, si hubo albarán de ese producto para ese cliente (por cualquier canal) en los 7 días siguientes.
-- No hay grupo de control (son pocas decenas de clientes): se mira a mano un par de meses.
--
-- Acotado por fechas (@desde/@hasta, por defecto los últimos 3 meses) y con NOLOCK. El cruce con LinPedidoVta va por
-- cliente y producto concretos (índices que empiezan por Nº Cliente o Producto): se puede lanzar en horario.
-- La BD está en compatibilidad 100: nada de JSON_VALUE ni STRING_SPLIT; Datos se trocea a mano.

USE NV;
GO

SET NOCOUNT ON;

DECLARE @desde datetime = DATEADD(month, -3, CAST(GETDATE() AS date));
DECLARE @hasta datetime = GETDATE();

IF OBJECT_ID('tempdb..#PushReposicion') IS NOT NULL DROP TABLE #PushReposicion;

-- Una push por cliente y día (el buzón guarda una fila por usuario: un cliente con dos usuarios tiene dos filas)
;WITH Push AS (
    SELECT RTRIM(b.Cliente) AS Cliente,
           CAST(CAST(b.FechaCreacion AS date) AS datetime) AS Dia,
           MIN(b.FechaCreacion) AS Fecha,
           MAX(SUBSTRING(b.Datos,
                         CHARINDEX('"productos":"', b.Datos) + 13,
                         CHARINDEX('"', b.Datos + '"', CHARINDEX('"productos":"', b.Datos) + 13)
                             - CHARINDEX('"productos":"', b.Datos) - 13)) AS Productos
    FROM dbo.NotificacionesBuzon b WITH (NOLOCK)
    WHERE b.Aplicacion = 'NestoTiendas'
      AND b.FechaCreacion >= @desde AND b.FechaCreacion < @hasta
      AND b.Cliente IS NOT NULL
      AND b.Datos LIKE '%"tipo":"reposicion"%'
      AND CHARINDEX('"productos":"', b.Datos) > 0
    GROUP BY RTRIM(b.Cliente), CAST(b.FechaCreacion AS date)
),
-- "ref1:cant1;ref2:cant2" -> una fila por producto
Partes AS (
    SELECT Cliente, Dia, Fecha, CAST(NULL AS varchar(200)) AS Item, CAST(Productos + ';' AS varchar(8000)) AS Resto
    FROM Push
    UNION ALL
    SELECT Cliente, Dia, Fecha,
           CAST(LEFT(Resto, CHARINDEX(';', Resto) - 1) AS varchar(200)),
           CAST(SUBSTRING(Resto, CHARINDEX(';', Resto) + 1, 8000) AS varchar(8000))
    FROM Partes
    WHERE CHARINDEX(';', Resto) > 0
)
SELECT Cliente, Dia, Fecha,
       LEFT(Item, CHARINDEX(':', Item + ':') - 1) AS Producto,
       CASE WHEN CHARINDEX(':', Item) > 0 THEN SUBSTRING(Item, CHARINDEX(':', Item) + 1, 10) END AS CantidadHabitual
INTO #PushReposicion
FROM Partes
WHERE Item IS NOT NULL AND Item <> ''
OPTION (MAXRECURSION 10);

-- Detalle: cada producto de cada push
SELECT p.Cliente, p.Fecha AS FechaPush, p.Producto, pr.Nombre, p.CantidadHabitual,
       a.PrimerAlbaran, a.Unidades,
       CASE WHEN a.PrimerAlbaran IS NOT NULL THEN 'Sí'
            WHEN DATEADD(day, 7, p.Dia) > CAST(GETDATE() AS date) THEN '(todavía no han pasado 7 días)'
            ELSE 'No' END AS AlbaranEn7Dias
FROM #PushReposicion p
LEFT JOIN dbo.Productos pr WITH (NOLOCK) ON pr.Empresa = '1' AND pr.Número = p.Producto
OUTER APPLY (
    SELECT MIN(l.[Fecha Albarán]) AS PrimerAlbaran, SUM(l.Cantidad) AS Unidades
    FROM dbo.LinPedidoVta l WITH (NOLOCK)
    WHERE l.Empresa = '1' AND l.[Nº Cliente] = p.Cliente AND l.Producto = p.Producto
      AND l.Estado >= 2 AND l.Cantidad > 0
      AND l.[Fecha Albarán] >= p.Dia AND l.[Fecha Albarán] < DATEADD(day, 8, p.Dia)
) a
ORDER BY p.Fecha, p.Cliente, p.Producto;

-- Resumen (solo las push de hace 7 días o más, que ya se pueden juzgar)
SELECT COUNT(DISTINCT p.Cliente + CONVERT(char(8), p.Dia, 112)) AS Push,
       COUNT(*) AS Productos,
       SUM(CASE WHEN a.Hay = 1 THEN 1 ELSE 0 END) AS ProductosConAlbaranEn7Dias,
       CAST(100.0 * SUM(CASE WHEN a.Hay = 1 THEN 1 ELSE 0 END) / NULLIF(COUNT(*), 0) AS decimal(5, 1)) AS PorcentajeRepuesto
FROM #PushReposicion p
OUTER APPLY (
    SELECT TOP 1 1 AS Hay
    FROM dbo.LinPedidoVta l WITH (NOLOCK)
    WHERE l.Empresa = '1' AND l.[Nº Cliente] = p.Cliente AND l.Producto = p.Producto
      AND l.Estado >= 2 AND l.Cantidad > 0
      AND l.[Fecha Albarán] >= p.Dia AND l.[Fecha Albarán] < DATEADD(day, 8, p.Dia)
) a
WHERE DATEADD(day, 7, p.Dia) <= CAST(GETDATE() AS date);

DROP TABLE #PushReposicion;
GO
