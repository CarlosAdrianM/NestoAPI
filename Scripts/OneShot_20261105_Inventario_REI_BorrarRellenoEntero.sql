-- OneShot 05/11/26: en la prueba del inventario de una referencia, Reina rellenó la tienda ENTERA desde Nesto
-- («Rellenar» sin evolutivo): 2.512 filas en Inventarios (REI, Estado 1, StockReal 0, StockCalculado 4.577 uds en total,
-- NºOrden 943032-945543, usuario NUEVAVISION\Reina, 05/11/26). Si alguien contabiliza REI con esas filas, el stock de
-- la tienda se pone a cero (es exactamente el borrado de 2025). Se borran las filas pendientes a 0 de ese relleno.
-- Ejecutar como sa en NV. Idempotente. NO toca las filas con StockReal <> 0 (si Reina llegó a contar algo) ni las de ALG.
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRAN;

SELECT 'ANTES' Momento, Almacén, Usuario, Estado, COUNT(*) N, SUM(StockReal) Real, SUM(StockCalculado) Calc
FROM dbo.Inventarios WHERE Fecha >= '20261105' GROUP BY Almacén, Usuario, Estado;

DELETE dbo.Inventarios
WHERE Almacén = 'REI'
  AND Estado = 1
  AND Fecha >= '20261105' AND Fecha < '20261106'
  AND Usuario = 'NUEVAVISION\Reina'
  AND StockReal = 0
  AND NºOrden BETWEEN 943032 AND 945543;
PRINT CONCAT('Filas borradas: ', @@ROWCOUNT, ' (se esperaban 2512)');

SELECT 'DESPUES' Momento, Almacén, Usuario, Estado, COUNT(*) N, SUM(StockReal) Real, SUM(StockCalculado) Calc
FROM dbo.Inventarios WHERE Fecha >= '20261105' GROUP BY Almacén, Usuario, Estado;

COMMIT;
GO
