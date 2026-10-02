/*
    NestoAPI#556 — Prueba REAL de «Terminar» una salida (picking o reposición) desde Ariadna: FOTO de antes.

    Antes de terminar de verdad (sin ?ensayo=true) un picking o un traspaso, se copian a tablas aparte todas las filas
    que puede tocar. Si sale mal, Issue556_Ensayo_2_Restaurar.sql las deja como estaban.

    Qué se fotografía:
      PICK: las líneas EN CURSO o PENDIENTES (-1..1) de los pedidos del picking, sus ubicaciones (reservas) y el
            estado de sus productos. Y hasta qué Nº Orden había en LinPedidoVta y Ubicaciones (lo nuevo se reconoce).
      REPO: las filas del traspaso en PreExtrProducto y ExtractoProducto, sus ubicaciones (registro -4 / reserva 4) y
            el estado de sus productos. Y hasta qué Nº Orden había en Ubicaciones y ExtractoProducto.

    Lanzar como sa, en NV, ANTES de terminar. Solo lee de las tablas de verdad; crea tablas dbo._Foto556_<tipo>_<nº>_*.
    Se puede repetir: borra la foto anterior del mismo picking o traspaso.
*/
USE NV;
GO
SET NOCOUNT ON;

DECLARE @Empresa char(3) = '1';
DECLARE @Tipo varchar(4) = 'PICK';   -- 'PICK' o 'REPO'
DECLARE @Numero int = 0;             -- nº de picking o de traspaso

IF @Numero <= 0 OR @Tipo NOT IN ('PICK', 'REPO')
    THROW 50000, 'Pon @Tipo (PICK o REPO) y @Numero.', 1;

DECLARE @Prefijo sysname = CONCAT('_Foto556_', @Tipo, '_', @Numero, '_');
DECLARE @sql nvarchar(max);

-- Borrar la foto anterior de este mismo picking o traspaso
SELECT @sql = STRING_AGG(CAST('DROP TABLE dbo.' + QUOTENAME(name) AS nvarchar(max)), '; ')
FROM sys.tables WHERE name LIKE REPLACE(@Prefijo, '_', '[_]') + '%';
IF @sql IS NOT NULL EXEC sp_executesql @sql;

-- El LEFT JOIN a una fila vacía evita que SELECT INTO copie la propiedad IDENTITY (así se pueden restaurar los Nº Orden)
IF @Tipo = 'PICK'
BEGIN
    SET @sql = N'
SELECT l.* INTO dbo.' + QUOTENAME(@Prefijo + 'LinPedidoVta') + N'
FROM LinPedidoVta l LEFT JOIN (SELECT 1 AS x) j ON 1 = 0
WHERE l.Empresa = @Empresa AND l.Estado BETWEEN -1 AND 1
  AND l.[Número] IN (SELECT DISTINCT p.[Número] FROM LinPedidoVta p WHERE p.Empresa = @Empresa AND p.Picking = @Numero);

SELECT u.* INTO dbo.' + QUOTENAME(@Prefijo + 'Ubicaciones') + N'
FROM Ubicaciones u LEFT JOIN (SELECT 1 AS x) j ON 1 = 0
WHERE u.[NºOrdenVta] IN (SELECT f.[Nº Orden] FROM dbo.' + QUOTENAME(@Prefijo + 'LinPedidoVta') + N' f);

SELECT p.Empresa, p.[Número], p.Estado INTO dbo.' + QUOTENAME(@Prefijo + 'Productos') + N'
FROM Productos p
WHERE p.Empresa = @Empresa AND p.[Número] IN (SELECT f.Producto FROM dbo.' + QUOTENAME(@Prefijo + 'LinPedidoVta') + N' f);

SELECT GETDATE() AS FechaFoto,
       (SELECT MAX([Nº Orden]) FROM LinPedidoVta) AS MaxLinPedidoVta,
       (SELECT MAX([NºOrden]) FROM Ubicaciones) AS MaxUbicaciones,
       CAST(NULL AS int) AS MaxExtractoProducto
INTO dbo.' + QUOTENAME(@Prefijo + 'Marcas') + N';';
END
ELSE
BEGIN
    SET @sql = N'
SELECT p.* INTO dbo.' + QUOTENAME(@Prefijo + 'PreExtrProducto') + N'
FROM PreExtrProducto p LEFT JOIN (SELECT 1 AS x) j ON 1 = 0
WHERE p.Empresa = @Empresa AND p.[NºTraspaso] = @Numero;

SELECT e.* INTO dbo.' + QUOTENAME(@Prefijo + 'ExtractoProducto') + N'
FROM ExtractoProducto e LEFT JOIN (SELECT 1 AS x) j ON 1 = 0
WHERE e.Empresa = @Empresa AND e.[NºTraspaso] = @Numero;

SELECT u.* INTO dbo.' + QUOTENAME(@Prefijo + 'Ubicaciones') + N'
FROM Ubicaciones u LEFT JOIN (SELECT 1 AS x) j ON 1 = 0
WHERE u.[NºTraspasoRepo] = @Numero
   OR u.[NºOrdenRepo] IN (SELECT f.[Nº Orden] FROM dbo.' + QUOTENAME(@Prefijo + 'PreExtrProducto') + N' f);

SELECT p.Empresa, p.[Número], p.Estado INTO dbo.' + QUOTENAME(@Prefijo + 'Productos') + N'
FROM Productos p
WHERE p.Empresa = @Empresa AND p.[Número] IN (SELECT f.[Número] FROM dbo.' + QUOTENAME(@Prefijo + 'PreExtrProducto') + N' f);

SELECT GETDATE() AS FechaFoto,
       CAST(NULL AS int) AS MaxLinPedidoVta,
       (SELECT MAX([NºOrden]) FROM Ubicaciones) AS MaxUbicaciones,
       (SELECT MAX([Nº Orden]) FROM ExtractoProducto) AS MaxExtractoProducto
INTO dbo.' + QUOTENAME(@Prefijo + 'Marcas') + N';';
END

EXEC sp_executesql @sql, N'@Empresa char(3), @Numero int', @Empresa = @Empresa, @Numero = @Numero;

-- Qué se ha guardado
SELECT t.name AS Tabla, SUM(p.rows) AS Filas
FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name LIKE REPLACE(@Prefijo, '_', '[_]') + '%'
GROUP BY t.name ORDER BY t.name;
