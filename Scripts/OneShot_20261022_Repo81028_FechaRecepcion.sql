/*
    Novedad 751 (Alfredo, 22/10/26): la reposición 81028 (Reina → Algete, enviada el 16/10) no se puede terminar:
    «Se ha realizado un inventario posterior a esta fecha. No se pueden meter movimientos anteriores a ese inventario.»

    Causa: las entradas en Algete van en PreExtrProducto (diario PendRepo2) con la fecha del ENVÍO (16/10), y el 19/10 a las
    17:03 Santiago regularizó por inventario el 43992 en Algete (+2). La comprobación de inventario bloquea toda la recepción.

    Arreglo (decisión de Carlos, 22/10/26):
      1) La fecha de las entradas pendientes de la 81028 pasa a hoy (la fecha en que de verdad entran).
      2) Se quita de la 81028 la entrada del 43992: esa unidad ya la contó Santiago en el inventario del 19/10 (y el 20/10 se
         vendieron 2). Si se recibiera ahora, Algete quedaría con una de más. La salida de Reina (−1, 16/10) se queda como está.
    Después Alfredo pulsa «Terminar» en Recibir: la 43763 (no vino) queda como diferencia, como siempre.

    Ejecutar en SSMS contra NV como sa. Si no cuadran las filas (1 y 11), deshace todo solo.
*/
SET NOCOUNT ON;
USE NV;

SELECT 'ANTES' AS Momento, LTRIM(RTRIM([Número])) AS Producto, CONVERT(varchar(10), Fecha, 120) AS Fecha, Cantidad, [Almacén], Diario, Estado
FROM dbo.PreExtrProducto
WHERE Empresa = '1' AND [NºTraspaso] = 81028 AND Diario = 'PendRepo2' AND [Almacén] = 'ALG';

BEGIN TRANSACTION;

DELETE FROM dbo.PreExtrProducto
WHERE Empresa = '1' AND [NºTraspaso] = 81028 AND Diario = 'PendRepo2' AND [Almacén] = 'ALG' AND [Número] = '43992';
DECLARE @quitadas int = @@ROWCOUNT;
SELECT @quitadas AS LineasQuitadas43992;   -- debe ser 1

UPDATE dbo.PreExtrProducto
SET Fecha = CAST(GETDATE() AS date)
WHERE Empresa = '1' AND [NºTraspaso] = 81028 AND Diario = 'PendRepo2' AND [Almacén] = 'ALG';
DECLARE @fechadas int = @@ROWCOUNT;
SELECT @fechadas AS LineasConFechaDeHoy;   -- debe ser 11

SELECT 'DESPUÉS' AS Momento, LTRIM(RTRIM([Número])) AS Producto, CONVERT(varchar(10), Fecha, 120) AS Fecha, Cantidad, [Almacén], Diario, Estado
FROM dbo.PreExtrProducto
WHERE Empresa = '1' AND [NºTraspaso] = 81028 AND Diario = 'PendRepo2' AND [Almacén] = 'ALG';

IF @quitadas = 1 AND @fechadas = 11
BEGIN
    COMMIT TRANSACTION;
    SELECT 'HECHO: 81028 lista para «Terminar».' AS Resultado;
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    SELECT 'DESHECHO: no cuadran las filas (se esperaban 1 y 11). No se ha cambiado nada.' AS Resultado;
END
