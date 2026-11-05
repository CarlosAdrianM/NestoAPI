-- OneShot 05/11/26: Paloma rellenó la tienda ENTERA de Alcobendas desde Nesto a través de la API (NUEVAVISION\Paloma, Aplicacion NestoAPI, 12:43; 3206 filas, 9335 unidades),
-- igual que Reina por la mañana: filas de Inventarios (ALC, Estado 1, StockReal 0) para cada producto con stock. Si alguien
-- contabiliza ALC con esas filas, el stock de la tienda se pone a cero. Se borran las filas pendientes a 0 de ese relleno.
-- NO toca filas con StockReal <> 0 (si Paloma llegó a contar alguna) ni las de otros almacenes. Ejecutar como sa en NV. Idempotente.
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRAN;

SELECT 'ANTES' Momento, Almacén, Usuario, Estado, COUNT(*) N, SUM(StockReal) Real, SUM(StockCalculado) Calc
FROM dbo.Inventarios WHERE Fecha >= '20261105' GROUP BY Almacén, Usuario, Estado;

DELETE dbo.Inventarios
WHERE Almacén = 'ALC'
  AND Estado = 1
  AND Fecha >= '20261105' AND Fecha < '20261106'
  AND Usuario = 'NUEVAVISION\Paloma'
  AND StockReal = 0;
PRINT CONCAT('Filas borradas: ', @@ROWCOUNT);

SELECT 'DESPUES' Momento, Almacén, Usuario, Estado, COUNT(*) N, SUM(StockReal) Real, SUM(StockCalculado) Calc
FROM dbo.Inventarios WHERE Fecha >= '20261105' GROUP BY Almacén, Usuario, Estado;

COMMIT;
GO
