-- OneShot 05/11/26: la prueba del inventario de Alfredo en Algete (8 filas pendientes, Estado 1, ubicación 002/003/010,
-- NºOrden 943024-943031, todas con StockReal = StockCalculado) BLOQUEA los movimientos de esos 8 productos: el trigger
-- trgExtractoProductoUpd corta con «Se esta realizando el inventario del producto…» (ELMAH 12:06, Santiago, etiqueta del 929866,
-- producto 37547; el 929928 lleva el 37126 y fallará igual). La prueba ya está hecha y validada (todo cuadra), así que
-- contabilizarla no cambiaría ningún stock: se borran las filas para desbloquear. Alternativa: contabilizar ALG desde Nesto
-- (pero las 14 filas sueltas de Andre de otras fechas salen como bloqueante). Ejecutar como sa en NV. Idempotente.
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRAN;

SELECT 'ANTES' Momento, NºOrden, Número, StockCalculado, StockReal, Estado FROM dbo.Inventarios
WHERE Almacén = 'ALG' AND Estado = 1 AND Fecha >= '20261105' AND Fecha < '20261106' AND Usuario = 'Alfredo' ORDER BY NºOrden;

DELETE dbo.Inventarios
WHERE Almacén = 'ALG'
  AND Estado = 1
  AND Fecha >= '20261105' AND Fecha < '20261106'
  AND Usuario = 'Alfredo'
  AND StockReal = StockCalculado
  AND NºOrden BETWEEN 943024 AND 943031;
PRINT CONCAT('Filas borradas: ', @@ROWCOUNT, ' (se esperaban 8)');

SELECT 'DESPUES' Momento, Almacén, Usuario, Estado, COUNT(*) N FROM dbo.Inventarios WHERE Fecha >= '20261105' GROUP BY Almacén, Usuario, Estado;

COMMIT;
GO
