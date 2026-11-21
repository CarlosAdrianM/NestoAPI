/*
    Inventario 21/11/26. Las dos correcciones de todos los años ANTES de contabilizar, para ALG, REI y ALC a la vez. Lanzar como sa,
    cuando ya se haya terminado de corregir lo tecleado (las referencias insertadas por error salen aquí como anuladas con stock).

      1. Productos ANULADOS (Estado -1) con stock contado  → Estado 4.
      2. Productos SIN PRECIO MEDIO (0 o NULL) con stock contado, no ficticios → PrecioMedio 0,001.

    Solo toca Productos (la empresa de la fila del inventario) de los que tengan fila PENDIENTE con contado <> 0. No toca Inventarios.
    Enseña antes y después. Se puede relanzar: lo ya hecho no vuelve a salir.
    Poner @SoloVer = 0 para escribir.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;

DECLARE @SoloVer bit = 1;   -- 1 = solo enseña lo que haría; 0 = lo hace

DECLARE @anulados TABLE (Empresa char(3) NOT NULL, Numero char(15) NOT NULL, PRIMARY KEY (Empresa, Numero));
DECLARE @sinPrecio TABLE (Empresa char(3) NOT NULL, Numero char(15) NOT NULL, PRIMARY KEY (Empresa, Numero));

INSERT INTO @anulados (Empresa, Numero)
SELECT DISTINCT i.Empresa, i.[Número]
FROM Inventarios i WITH (NOLOCK)
JOIN Productos p WITH (NOLOCK) ON p.Empresa = i.Empresa AND p.[Número] = i.[Número]
WHERE i.Estado = 1 AND i.NºTraspaso IS NULL AND i.[Almacén] IN ('ALG', 'REI', 'ALC') AND p.Estado = -1 AND i.StockReal <> 0;

INSERT INTO @sinPrecio (Empresa, Numero)
SELECT DISTINCT i.Empresa, i.[Número]
FROM Inventarios i WITH (NOLOCK)
JOIN Productos p WITH (NOLOCK) ON p.Empresa = i.Empresa AND p.[Número] = i.[Número]
WHERE i.Estado = 1 AND i.NºTraspaso IS NULL AND i.[Almacén] IN ('ALG', 'REI', 'ALC')
  AND (p.PrecioMedio = 0 OR p.PrecioMedio IS NULL) AND i.StockReal <> 0 AND ISNULL(p.Ficticio, 0) = 0;

PRINT '1. ANULADOS CON STOCK CONTADO (pasan a estado 4)';
SELECT RTRIM(i.[Almacén]) AS Almacen, RTRIM(i.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, p.Estado,
       ISNULL(i.Pasillo, '') + '-' + ISNULL(i.Fila, '') + '-' + ISNULL(i.Columna, '') AS Hueco, i.StockCalculado, i.StockReal
FROM Inventarios i WITH (NOLOCK)
JOIN @anulados x ON x.Empresa = i.Empresa AND x.Numero = i.[Número]
JOIN Productos p WITH (NOLOCK) ON p.Empresa = i.Empresa AND p.[Número] = i.[Número]
WHERE i.Estado = 1 AND i.NºTraspaso IS NULL AND i.StockReal <> 0
ORDER BY 1, 2;

PRINT '2. SIN PRECIO MEDIO CON STOCK CONTADO (pasan a 0,001): cuántos por almacén';
SELECT RTRIM(i.[Almacén]) AS Almacen, COUNT(DISTINCT i.[Número]) AS Productos
FROM Inventarios i WITH (NOLOCK)
JOIN @sinPrecio x ON x.Empresa = i.Empresa AND x.Numero = i.[Número]
WHERE i.Estado = 1 AND i.NºTraspaso IS NULL AND i.StockReal <> 0
GROUP BY i.[Almacén];

DECLARE @nAnulados int = (SELECT COUNT(*) FROM @anulados), @nSinPrecio int = (SELECT COUNT(*) FROM @sinPrecio);
PRINT CONCAT('Anulados: ', @nAnulados, ' · sin precio medio: ', @nSinPrecio);

IF @SoloVer = 1
BEGIN
    PRINT 'SOLO VER: no se ha escrito nada. Ponga @SoloVer = 0 para hacerlo.';
    RETURN;
END
IF @nAnulados > 60 OR @nSinPrecio > 900
BEGIN
    RAISERROR('Salen demasiados (%d anulados, %d sin precio medio): no se toca nada; revíselo.', 16, 1, @nAnulados, @nSinPrecio);
    RETURN;
END

BEGIN TRANSACTION;
UPDATE p SET Estado = 4
FROM Productos p JOIN @anulados x ON x.Empresa = p.Empresa AND x.Numero = p.[Número]
WHERE p.Estado = -1;
DECLARE @hechosAnulados int = @@ROWCOUNT;

UPDATE p SET PrecioMedio = 0.001
FROM Productos p JOIN @sinPrecio x ON x.Empresa = p.Empresa AND x.Numero = p.[Número]
WHERE (p.PrecioMedio = 0 OR p.PrecioMedio IS NULL) AND ISNULL(p.Ficticio, 0) = 0;
DECLARE @hechosPrecio int = @@ROWCOUNT;

IF @hechosAnulados <> @nAnulados OR @hechosPrecio <> @nSinPrecio
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('No cuadra lo esperado con lo cambiado (anulados %d de %d, precio medio %d de %d): deshecho, no se ha tocado nada.', 16, 1,
        @hechosAnulados, @nAnulados, @hechosPrecio, @nSinPrecio);
    RETURN;
END
COMMIT TRANSACTION;
PRINT CONCAT('HECHO. A estado 4: ', @hechosAnulados, ' · precio medio a 0,001: ', @hechosPrecio);

PRINT 'DESPUÉS: no debe quedar ninguno';
SELECT 'Anulados con stock' AS Queda, COUNT(*) AS Filas
FROM Inventarios i WITH (NOLOCK) JOIN Productos p WITH (NOLOCK) ON p.Empresa = i.Empresa AND p.[Número] = i.[Número]
WHERE i.Estado = 1 AND i.NºTraspaso IS NULL AND i.[Almacén] IN ('ALG', 'REI', 'ALC') AND p.Estado = -1 AND i.StockReal <> 0
UNION ALL
SELECT 'Sin precio medio con stock', COUNT(*)
FROM Inventarios i WITH (NOLOCK) JOIN Productos p WITH (NOLOCK) ON p.Empresa = i.Empresa AND p.[Número] = i.[Número]
WHERE i.Estado = 1 AND i.NºTraspaso IS NULL AND i.[Almacén] IN ('ALG', 'REI', 'ALC')
  AND (p.PrecioMedio = 0 OR p.PrecioMedio IS NULL) AND i.StockReal <> 0 AND ISNULL(p.Ficticio, 0) = 0;
