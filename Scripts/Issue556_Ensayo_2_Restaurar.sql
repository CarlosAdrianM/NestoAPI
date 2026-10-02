/*
    NestoAPI#556 — Prueba REAL de «Terminar» una salida desde Ariadna: RESTAURAR desde la foto.

    Deja las filas como estaban en Issue556_Ensayo_1_Foto.sql (mismo @Tipo y @Numero). Solo si la prueba real ha
    salido mal: lo que se haya hecho DESPUÉS en esos pedidos o en ese traspaso (otra persona trabajando con ellos)
    también se deshace.

    PICK: borra las líneas nuevas de esos pedidos (la parte pendiente que creó Terminar), deja cada línea de la foto
          como estaba (cantidad, estado, picking, importes…) y sus ubicaciones exactamente como estaban (reservas
          incluidas), borra las «pendiente de ubicar» que creó Terminar y devuelve el estado de los productos.
          trgLinPedidoVtaUpd no deja cambiar la cantidad con picking: se sueltan las reservas, se quita el picking, se
          restaura y se vuelve a poner, todo dentro de la misma transacción.
    REPO: borra lo que se contabilizó (ExtractoProducto nuevo del traspaso), vuelve a dejar el traspaso en
          PreExtrProducto como estaba, sus ubicaciones como estaban, borra las «pendiente de ubicar» nuevas y devuelve el
          estado de los productos.

    Lanzar como sa, en NV. Todo en UNA transacción: si algo falla, no cambia nada (y lo dice).
    Al final enseña las diferencias que queden con la foto (deberían salir vacías).
    Con @Ensayo = 1 (por defecto) lo hace todo, enseña la comprobación y lo DESHACE: lanzarlo así primero; si la
    comprobación sale vacía, otra vez con @Ensayo = 0.
*/
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Empresa char(3) = '1';
DECLARE @Tipo varchar(4) = 'PICK';   -- 'PICK' o 'REPO' (el mismo que en la foto)
DECLARE @Numero int = 0;             -- el mismo que en la foto
DECLARE @Ensayo bit = 1;             -- 1: lo hace, lo enseña y lo deshace. 0: de verdad.

DECLARE @Prefijo sysname = CONCAT('_Foto556_', @Tipo, '_', @Numero, '_');
IF OBJECT_ID('dbo.' + QUOTENAME(@Prefijo + 'Marcas')) IS NULL
    THROW 50000, 'No hay foto de ese picking o traspaso: primero Issue556_Ensayo_1_Foto.sql.', 1;

DECLARE @sql nvarchar(max);
DECLARE @columnas nvarchar(max);       -- todas las que se pueden escribir (con la identidad)
DECLARE @asignaciones nvarchar(max);   -- SET col = f.col, sin identidad ni Picking
DECLARE @Foto nvarchar(300);

BEGIN TRANSACTION;

IF @Tipo = 'PICK'
BEGIN
    SET @Foto = 'dbo.' + QUOTENAME(@Prefijo + 'LinPedidoVta');

    -- 1. Lo nuevo de esos pedidos (la parte pendiente que creó Terminar) sobra
    SET @sql = N'
DELETE l FROM LinPedidoVta l
WHERE l.Empresa = @Empresa AND l.Estado BETWEEN -1 AND 1
  AND l.[Número] IN (SELECT DISTINCT f.[Número] FROM ' + @Foto + N' f)
  AND l.[Nº Orden] > (SELECT MaxLinPedidoVta FROM dbo.' + QUOTENAME(@Prefijo + 'Marcas') + N');

-- 2. Las reservas de las líneas de la foto, fuera un momento; el picking a 0 (así se puede cambiar la cantidad)
UPDATE Ubicaciones SET [NºOrdenVta] = NULL
WHERE Estado = 3 AND [NºOrdenVta] IN (SELECT f.[Nº Orden] FROM ' + @Foto + N' f);
UPDATE l SET Picking = 0 FROM LinPedidoVta l INNER JOIN ' + @Foto + N' f ON f.[Nº Orden] = l.[Nº Orden];';
    EXEC sp_executesql @sql, N'@Empresa char(3)', @Empresa = @Empresa;

    -- 3. Cada línea, como en la foto (todas las columnas que se pueden escribir, salvo el picking)
    SELECT @asignaciones = STRING_AGG(CAST('l.' + QUOTENAME(c.name) + ' = f.' + QUOTENAME(c.name) AS nvarchar(max)), ', ')
    FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID('dbo.LinPedidoVta') AND c.is_identity = 0 AND c.is_computed = 0
      AND t.name NOT IN ('timestamp', 'rowversion') AND c.name <> 'Picking';
    SET @sql = N'UPDATE l SET ' + @asignaciones + N' FROM LinPedidoVta l INNER JOIN ' + @Foto + N' f ON f.[Nº Orden] = l.[Nº Orden];
UPDATE l SET Picking = f.Picking FROM LinPedidoVta l INNER JOIN ' + @Foto + N' f ON f.[Nº Orden] = l.[Nº Orden];';
    EXEC sp_executesql @sql;
END
ELSE
BEGIN
    SET @Foto = 'dbo.' + QUOTENAME(@Prefijo + 'PreExtrProducto');

    -- 1. Lo contabilizado del traspaso después de la foto sobra
    SET @sql = N'
DELETE ExtractoProducto
WHERE Empresa = @Empresa AND [NºTraspaso] = @Numero
  AND [Nº Orden] > (SELECT MaxExtractoProducto FROM dbo.' + QUOTENAME(@Prefijo + 'Marcas') + N');
DELETE PreExtrProducto WHERE Empresa = @Empresa AND [NºTraspaso] = @Numero;';
    EXEC sp_executesql @sql, N'@Empresa char(3), @Numero int', @Empresa = @Empresa, @Numero = @Numero;

    -- 2. El traspaso en PreExtrProducto, como en la foto (con sus Nº Orden: las ubicaciones apuntan a ellos)
    SELECT @columnas = STRING_AGG(CAST(QUOTENAME(c.name) AS nvarchar(max)), ', ')
    FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID('dbo.PreExtrProducto') AND c.is_computed = 0 AND t.name NOT IN ('timestamp', 'rowversion');
    SET @sql = N'SET IDENTITY_INSERT PreExtrProducto ON;
INSERT INTO PreExtrProducto (' + @columnas + N') SELECT ' + @columnas + N' FROM ' + @Foto + N';
SET IDENTITY_INSERT PreExtrProducto OFF;';
    EXEC sp_executesql @sql;
END

-- Ubicaciones (las dos): fuera las de la foto y las «pendiente de ubicar» nuevas de sus productos, y otra vez las de la foto
SET @sql = N'
DELETE Ubicaciones WHERE [NºOrden] IN (SELECT f.[NºOrden] FROM dbo.' + QUOTENAME(@Prefijo + 'Ubicaciones') + N' f);
DELETE Ubicaciones
WHERE Estado = 2 AND [NºOrdenVta] IS NULL
  AND [NºOrden] > (SELECT MaxUbicaciones FROM dbo.' + QUOTENAME(@Prefijo + 'Marcas') + N')
  AND [Número] IN (SELECT p.[Número] FROM dbo.' + QUOTENAME(@Prefijo + 'Productos') + N' p);';
EXEC sp_executesql @sql;

SELECT @columnas = STRING_AGG(CAST(QUOTENAME(c.name) AS nvarchar(max)), ', ')
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.Ubicaciones') AND c.is_computed = 0 AND t.name NOT IN ('timestamp', 'rowversion');
SET @sql = N'SET IDENTITY_INSERT Ubicaciones ON;
INSERT INTO Ubicaciones (' + @columnas + N') SELECT ' + @columnas + N' FROM dbo.' + QUOTENAME(@Prefijo + 'Ubicaciones') + N';
SET IDENTITY_INSERT Ubicaciones OFF;';
EXEC sp_executesql @sql;

-- Estado de los productos (prdExtrProducto lo puede cambiar al quedarse sin stock)
SET @sql = N'UPDATE p SET Estado = f.Estado FROM Productos p INNER JOIN dbo.' + QUOTENAME(@Prefijo + 'Productos') + N' f
ON f.Empresa = p.Empresa AND f.[Número] = p.[Número] WHERE p.Estado <> f.Estado;';
EXEC sp_executesql @sql;

-- Comprobación: lo que difiera todavía de la foto (debería salir vacío)
IF @Tipo = 'PICK'
    SET @sql = N'
SELECT ''LinPedidoVta'' AS Tabla, f.[Nº Orden] AS Clave, f.Cantidad AS CantidadFoto, l.Cantidad, f.Estado AS EstadoFoto, l.Estado, f.Picking AS PickingFoto, l.Picking
FROM ' + @Foto + N' f LEFT JOIN LinPedidoVta l ON l.[Nº Orden] = f.[Nº Orden]
WHERE l.[Nº Orden] IS NULL OR l.Cantidad <> f.Cantidad OR l.Estado <> f.Estado OR ISNULL(l.Picking, 0) <> ISNULL(f.Picking, 0)
UNION ALL
SELECT ''Ubicaciones'', f.[NºOrden], f.Cantidad, u.Cantidad, f.Estado, u.Estado, f.[NºOrdenVta], u.[NºOrdenVta]
FROM dbo.' + QUOTENAME(@Prefijo + 'Ubicaciones') + N' f LEFT JOIN Ubicaciones u ON u.[NºOrden] = f.[NºOrden]
WHERE u.[NºOrden] IS NULL OR u.Cantidad <> f.Cantidad OR u.Estado <> f.Estado OR ISNULL(u.[NºOrdenVta], 0) <> ISNULL(f.[NºOrdenVta], 0);';
ELSE
    SET @sql = N'
SELECT ''PreExtrProducto'' AS Tabla, f.[Nº Orden] AS Clave, f.Cantidad AS CantidadFoto, p.Cantidad
FROM ' + @Foto + N' f LEFT JOIN PreExtrProducto p ON p.[Nº Orden] = f.[Nº Orden]
WHERE p.[Nº Orden] IS NULL OR p.Cantidad <> f.Cantidad
UNION ALL
SELECT ''Ubicaciones'', f.[NºOrden], f.Cantidad, u.Cantidad
FROM dbo.' + QUOTENAME(@Prefijo + 'Ubicaciones') + N' f LEFT JOIN Ubicaciones u ON u.[NºOrden] = f.[NºOrden]
WHERE u.[NºOrden] IS NULL OR u.Cantidad <> f.Cantidad OR u.Estado <> f.Estado
UNION ALL
SELECT ''ExtractoProducto (nuevo)'', e.[Nº Orden], NULL, e.Cantidad
FROM ExtractoProducto e
WHERE e.Empresa = ''' + @Empresa + N''' AND e.[NºTraspaso] = ' + CAST(@Numero AS nvarchar(20)) + N'
  AND e.[Nº Orden] > (SELECT MaxExtractoProducto FROM dbo.' + QUOTENAME(@Prefijo + 'Marcas') + N');';
EXEC sp_executesql @sql;

IF @Ensayo = 1
BEGIN
    ROLLBACK;
    PRINT 'ENSAYO: se ha hecho todo, se ha enseñado la comprobación y se ha DESHECHO. Si sale vacía, lanza con @Ensayo = 0.';
END
ELSE
BEGIN
    COMMIT;
    PRINT 'Restaurado.';
END
