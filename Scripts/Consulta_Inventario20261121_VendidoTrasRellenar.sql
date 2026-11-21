/*
    Inventario de las tiendas (rellenado el 20/11/26 a las 14:27 ALC y 14:28 REI; se cuenta con FECHA 20/11/2026).
    SOLO LECTURA. Lanzar el sábado 21/11 ANTES de que cada tienda contabilice.

    Saca los productos con fila de inventario pendiente que han tenido movimientos de stock DESPUÉS de rellenar.
    El teórico se fijó al rellenar: si un producto se vendió después de rellenar y ANTES de contarlo, lo contado va
    una unidad corto y el ajuste lo restaría otra vez. Con esta lista la tienda repasa solo esos productos:
      - «¿Antes de contar?» = Sí  → al contado hay que SUMARLE «MovidoTrasRellenar» cambiado de signo (lo vendido).
      - «¿Antes de contar?» = No  → está bien, no se toca.
      - Sin contar todavía        → contarlo sumando lo vendido, o contarlo tal cual y corregir después.
*/
SET NOCOUNT ON;
USE NV;

;WITH relleno AS (
    SELECT [Almacén] AS Almacen, MIN([Fecha Modificación]) AS Rellenado
    FROM Inventarios
    WHERE Estado = 1 AND NºTraspaso IS NULL AND [Almacén] IN ('ALC', 'REI')
    GROUP BY [Almacén]
)
SELECT RTRIM(i.[Almacén]) AS Tienda,
       RTRIM(i.[Número]) AS Producto,
       LEFT(RTRIM(p.Nombre), 45) AS Nombre,
       i.StockCalculado AS TeoricoAlRellenar,
       i.StockReal AS Contado,
       SUM(e.Cantidad) AS MovidoTrasRellenar,
       CONVERT(varchar(16), MIN(e.[Fecha Modificación]), 120) AS PrimerMovimiento,
       CONVERT(varchar(16), i.[Fecha Modificación], 120) AS UltimaLecturaDelProducto,
       CASE WHEN i.[Fecha Modificación] <= r.Rellenado THEN 'Sin contar todavía'
            WHEN MIN(e.[Fecha Modificación]) < i.[Fecha Modificación] THEN 'Sí: revisar'
            ELSE 'No' END AS [¿Antes de contar?]
FROM Inventarios i
JOIN relleno r ON r.Almacen = i.[Almacén]
JOIN ExtractoProducto e ON e.[Almacén] = i.[Almacén] AND e.[Número] = i.[Número] AND e.Empresa IN ('1', '3')
     AND e.[Fecha Modificación] > r.Rellenado AND e.Fecha >= '20261120'
LEFT JOIN Productos p ON p.Empresa = '1' AND p.[Número] = i.[Número]
WHERE i.Estado = 1 AND i.NºTraspaso IS NULL
GROUP BY i.[Almacén], i.[Número], p.Nombre, i.StockCalculado, i.StockReal, i.[Fecha Modificación], r.Rellenado
ORDER BY 1, 9 DESC, 2;
