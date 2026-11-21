/*
    Inventario 21/11/26. Productos ANULADOS (Estado -1) con stock contado en Reina y Alcobendas → Estado 4,
    como todos los años antes de contabilizar. Lanzar como sa.

    A las 12:40 eran 5: ALC 37540 (3 uds); REI 27007 (41), 39573 (1), 39936 (10), 40705 (1).
    Solo toca Productos (empresa de la fila del inventario) de los que SIGUEN en -1 con fila pendiente y contado <> 0.
    No toca Inventarios. Se puede relanzar: si no queda ninguno, no hace nada.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;

PRINT 'ANTES';
SELECT RTRIM(i.[Almacén]) AS Almacen, RTRIM(i.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, p.Estado, i.StockCalculado, i.StockReal
FROM Inventarios i WITH (NOLOCK)
JOIN Productos p WITH (NOLOCK) ON p.Empresa = i.Empresa AND p.[Número] = i.[Número]
WHERE i.Estado = 1 AND i.NºTraspaso IS NULL AND i.[Almacén] IN ('REI', 'ALC') AND p.Estado = -1 AND i.StockReal <> 0
ORDER BY 1, 2;

DECLARE @productos TABLE (Empresa char(3) NOT NULL, Numero char(15) NOT NULL, PRIMARY KEY (Empresa, Numero));
INSERT INTO @productos (Empresa, Numero)
SELECT DISTINCT i.Empresa, i.[Número]
FROM Inventarios i WITH (NOLOCK)
JOIN Productos p WITH (NOLOCK) ON p.Empresa = i.Empresa AND p.[Número] = i.[Número]
WHERE i.Estado = 1 AND i.NºTraspaso IS NULL AND i.[Almacén] IN ('REI', 'ALC') AND p.Estado = -1 AND i.StockReal <> 0;

DECLARE @esperados int = (SELECT COUNT(*) FROM @productos);
IF @esperados = 0
BEGIN
    PRINT 'No queda ningún producto anulado con stock contado en REI/ALC: nada que hacer.';
    RETURN;
END
IF @esperados > 30
BEGIN
    RAISERROR('Salen %d productos: son demasiados para lo esperado (5). No se toca nada; revíselo.', 16, 1, @esperados);
    RETURN;
END

BEGIN TRANSACTION;
UPDATE p SET Estado = 4
FROM Productos p
JOIN @productos x ON x.Empresa = p.Empresa AND x.Numero = p.[Número]
WHERE p.Estado = -1;
DECLARE @hechos int = @@ROWCOUNT;
IF @hechos <> @esperados
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Se esperaban %d productos y se han cambiado %d: deshecho, no se ha tocado nada.', 16, 1, @esperados, @hechos);
    RETURN;
END
COMMIT TRANSACTION;
PRINT CONCAT('Pasados a estado 4: ', @hechos, ' productos.');

PRINT 'DESPUÉS';
SELECT RTRIM(p.Empresa) AS Empresa, RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, p.Estado
FROM Productos p WITH (NOLOCK)
JOIN @productos x ON x.Empresa = p.Empresa AND x.Numero = p.[Número]
ORDER BY 2;
