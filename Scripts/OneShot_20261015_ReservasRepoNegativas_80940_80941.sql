/*
    OneShot 15/10/26 — Ubicaciones de las reposiciones ALG→ALC 80940 y 80941 (09/10/26, Andre: empezadas en Ariadna y
    terminadas en Nesto viejo). Ejecutar en SSMS contra NV como sa.

    QUÉ PASA
    Quedaron 99 filas de Ubicaciones en Estado 4 (reserva de reposición) con la cantidad en NEGATIVO (−195 uds en total,
    justo lo que salió con los dos traspasos). Las unidades salieron del almacén, pero no se descontaron de su hueco: la
    fila libre (estado 0/2) sigue contándolas y la fila negativa las compensa. Por eso el total cuadra con el stock y,
    en cambio, el hueco enseña unidades que ya no están (sugerencia 615 de Alfredo: 37804 con «Cantidad:-1 (Reposición)»).
      - 77 filas (173 uds): las unidades libres están en el mismo hueco → se descuentan de ahí.
      - 22 filas (1 ud cada una): eran «pendiente de ubicar» y esa tarde se ubicaron en un hueco contando también las
        que ya habían salido → se descuentan de las filas libres del producto (la más reciente primero).

    QUÉ HACE
    Para cada fila negativa: resta sus unidades de las filas libres (mismo hueco primero; si no, las del producto),
    borra las filas libres que se quedan a 0 (en Ubicaciones no hay filas libres a 0) y deja la fila negativa como lo
    que debió ser: salida de reposición (Estado −4) con su NºTraspasoRepo. La suma por producto no cambia.

    SEGURIDAD
    Todo en una transacción. Al final comprueba que: no quedan filas negativas en estado 4; para los 85 productos la
    suma de ubicaciones vivas sigue siendo igual al stock de ALG; y ninguna fila libre ha quedado en negativo. Si algo
    falla, ROLLBACK y no se toca nada. Si un producto está en inventario, el trigger de Ubicaciones lo rechaza → ROLLBACK.
    Fuera de horario si es posible (toca 99 productos de Algete).

    15/10 09:46, 1.er intento: el trigger de Ubicaciones lo paró porque el 37804 está en un inventario abierto (Alfredo).
    Ahora se SALTA los productos que estén en un inventario abierto (los avisa) y arregla el resto. Se puede volver a
    lanzar cuando se cierre el inventario: solo coge las filas negativas que queden.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @usuario varchar(30) = 'OneShot_20261015';

IF OBJECT_ID('tempdb..#neg') IS NOT NULL DROP TABLE #neg;
SELECT u.[NºOrden] AS Id, u.Empresa, u.[Almacén] AS Almacen, u.[Número] AS Producto, -u.Cantidad AS Falta,
       u.Pasillo, u.Fila, u.Columna, CAST(NULL AS int) AS Traspaso
INTO #neg
FROM Ubicaciones u
WHERE u.Estado = 4 AND u.Cantidad < 0 AND u.[Almacén] = 'ALG'
  AND u.[FechaCreación] = '20261009 13:16:14.017';

-- Los productos en un inventario abierto los rechaza el trigger de Ubicaciones (trdUpdUbicaciones): se dejan para después
PRINT 'Se dejan para cuando se cierre su inventario:';
SELECT g.Id, RTRIM(g.Producto) AS Producto, g.Falta FROM #neg g
WHERE EXISTS (SELECT 1 FROM Inventarios i WHERE i.[Número] = g.Producto AND i.[Almacén] = g.Almacen AND i.Empresa = g.Empresa
              AND i.Estado = 1 AND i.[NºTraspaso] IS NULL AND i.Almacen <> 'REI');
DELETE g FROM #neg g
WHERE EXISTS (SELECT 1 FROM Inventarios i WHERE i.[Número] = g.Producto AND i.[Almacén] = g.Almacen AND i.Empresa = g.Empresa
              AND i.Estado = 1 AND i.[NºTraspaso] IS NULL AND i.Almacen <> 'REI');

IF NOT EXISTS (SELECT 1 FROM #neg)
BEGIN
    PRINT 'No queda ninguna fila que arreglar.';
    RETURN;
END

-- Traspaso de cada fila: emparejar por producto, en orden, con las líneas de salida de 80940/80941 en ALG
;WITH n AS (SELECT Id, RTRIM(Producto) AS P, ROW_NUMBER() OVER (PARTITION BY RTRIM(Producto) ORDER BY Id) AS k FROM #neg),
      e AS (SELECT [NºTraspaso] AS T, RTRIM([Número]) AS P, ROW_NUMBER() OVER (PARTITION BY RTRIM([Número]) ORDER BY [Nº Orden]) AS k
            FROM ExtractoProducto WHERE [NºTraspaso] IN (80940, 80941) AND [Almacén] = 'ALG')
UPDATE g SET Traspaso = e.T
FROM #neg g JOIN n ON n.Id = g.Id JOIN e ON e.P = n.P AND e.k = n.k;
-- Si un producto tiene más filas que líneas (no debería), se queda con su primer traspaso
UPDATE g SET Traspaso = (SELECT MIN([NºTraspaso]) FROM ExtractoProducto
                         WHERE [NºTraspaso] IN (80940, 80941) AND [Almacén] = 'ALG' AND RTRIM([Número]) = RTRIM(g.Producto))
FROM #neg g WHERE g.Traspaso IS NULL;

-- Productos afectados y su stock antes (para la comprobación final)
IF OBJECT_ID('tempdb..#prod') IS NOT NULL DROP TABLE #prod;
SELECT DISTINCT Producto INTO #prod FROM #neg;

PRINT 'Antes:';
SELECT COUNT(*) AS FilasNegativas, SUM(Falta) AS Unidades, COUNT(DISTINCT Producto) AS Productos,
       SUM(CASE WHEN Traspaso IS NULL THEN 1 ELSE 0 END) AS SinTraspaso FROM #neg;

BEGIN TRANSACTION;

DECLARE @id int, @emp char(3), @alm char(3), @prod char(15), @falta int, @pas char(3), @fil char(3), @col char(3), @tr int;
DECLARE @libre int, @cant int, @quito int;

DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT Id, Empresa, Almacen, Producto, Falta, Pasillo, Fila, Columna, Traspaso FROM #neg ORDER BY Id;
OPEN c;
FETCH NEXT FROM c INTO @id, @emp, @alm, @prod, @falta, @pas, @fil, @col, @tr;
WHILE @@FETCH_STATUS = 0
BEGIN
    WHILE @falta > 0
    BEGIN
        SET @libre = NULL;
        -- 1.º del mismo hueco (la más antigua primero); 2.º cualquier libre del producto (la más reciente primero)
        SELECT TOP (1) @libre = [NºOrden], @cant = Cantidad FROM Ubicaciones
        WHERE [Almacén] = @alm AND [Número] = @prod AND Estado IN (0, 2) AND Cantidad > 0
          AND ISNULL(Pasillo, '') = ISNULL(@pas, '') AND ISNULL(Fila, '') = ISNULL(@fil, '') AND ISNULL(Columna, '') = ISNULL(@col, '')
        ORDER BY [FechaCreación], [NºOrden];
        IF @libre IS NULL
            SELECT TOP (1) @libre = [NºOrden], @cant = Cantidad FROM Ubicaciones
            WHERE [Almacén] = @alm AND [Número] = @prod AND Estado IN (0, 2) AND Cantidad > 0
            ORDER BY [FechaCreación] DESC, [NºOrden] DESC;
        IF @libre IS NULL
        BEGIN
            RAISERROR('Producto %s: no quedan unidades libres de las que descontar. ROLLBACK.', 16, 1, @prod);
            ROLLBACK TRANSACTION; RETURN;
        END

        SET @quito = CASE WHEN @cant <= @falta THEN @cant ELSE @falta END;
        IF @quito = @cant
            DELETE FROM Ubicaciones WHERE [NºOrden] = @libre;
        ELSE
            UPDATE Ubicaciones SET Cantidad = Cantidad - @quito, Usuario = @usuario WHERE [NºOrden] = @libre;
        SET @falta = @falta - @quito;
    END

    UPDATE Ubicaciones SET Estado = -4, [NºTraspasoRepo] = @tr, Usuario = @usuario WHERE [NºOrden] = @id AND Estado = 4;

    FETCH NEXT FROM c INTO @id, @emp, @alm, @prod, @falta, @pas, @fil, @col, @tr;
END
CLOSE c; DEALLOCATE c;

-- Comprobaciones
DECLARE @quedanNeg int = (SELECT COUNT(*) FROM Ubicaciones u JOIN #neg g ON g.Id = u.[NºOrden] WHERE u.Estado = 4);
DECLARE @libresNeg int = (SELECT COUNT(*) FROM Ubicaciones u JOIN #prod p ON p.Producto = u.[Número]
                          WHERE u.[Almacén] = 'ALG' AND u.Estado IN (0, 2) AND u.Cantidad <= 0);
DECLARE @descuadres int = (
    SELECT COUNT(*) FROM #prod p
    WHERE ISNULL((SELECT SUM(Cantidad) FROM ExtractoProducto WHERE [Almacén] = 'ALG' AND [Número] = p.Producto), 0)
       <> ISNULL((SELECT SUM(Cantidad) FROM Ubicaciones WHERE [Almacén] = 'ALG' AND [Número] = p.Producto AND Estado IN (0, 2, 3, 4)), 0));

SELECT @quedanNeg AS QuedanNegativasEstado4, @libresNeg AS LibresACeroONegativas, @descuadres AS ProductosDescuadrados;

IF @quedanNeg = 0 AND @libresNeg = 0 AND @descuadres = 0
BEGIN
    COMMIT TRANSACTION;
    DECLARE @hechas int = (SELECT COUNT(*) FROM #neg);
    PRINT 'OK: ' + CAST(@hechas AS varchar) + ' filas pasadas a salida de reposición (estado -4) y descontadas de los huecos. COMMIT.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'Algo no cuadra: ROLLBACK, no se ha tocado nada.';
END
