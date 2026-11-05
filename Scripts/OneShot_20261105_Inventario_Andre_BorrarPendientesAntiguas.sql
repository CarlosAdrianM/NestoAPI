-- OneShot 05/11/26 (Carlos): Andre tiene 44 filas de Inventarios pendientes (Estado 1) de días pasados que no se contabilizaron
-- (ALC 15, REI 15, ALG 14 con 2.576 unidades contadas): pruebas o recuentos a medias. Hoy no bloquean (el disparador solo mira el
-- día), pero antes del inventario del 21/11 hay que dejarlas a cero. Se borran SOLO las pendientes de NUEVAVISION\Andre con
-- Fecha anterior a hoy. No toca nada de hoy ni de otros usuarios. Ejecutar como sa en NV. Idempotente.
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRAN;

SELECT 'ANTES' Momento, Almacén, CONVERT(varchar(10), Fecha, 120) Fecha, COUNT(*) N, SUM(StockReal) Real, SUM(StockCalculado) Calc
FROM dbo.Inventarios WHERE Usuario = 'NUEVAVISION\Andre' AND Estado = 1 AND Fecha < '20261105'
GROUP BY Almacén, Fecha ORDER BY Almacén, Fecha;

DELETE dbo.Inventarios
WHERE Usuario = 'NUEVAVISION\Andre'
  AND Estado = 1
  AND Fecha < '20261105';
PRINT CONCAT('Filas borradas: ', @@ROWCOUNT, ' (se esperan 44)');

SELECT 'DESPUES' Momento, Almacén, Usuario, Estado, COUNT(*) N FROM dbo.Inventarios WHERE Estado = 1 GROUP BY Almacén, Usuario, Estado;

COMMIT;
GO
