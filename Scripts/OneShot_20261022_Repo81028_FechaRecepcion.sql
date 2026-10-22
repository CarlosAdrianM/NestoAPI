﻿/*
    Novedad 751 (Alfredo, 22/10/26): la reposición 81028 (Reina → Algete, enviada el 16/10) no se puede terminar:
    «Se ha realizado un inventario posterior a esta fecha. No se pueden meter movimientos anteriores a ese inventario.»

    Causa: las entradas en Algete van en PreExtrProducto (diario PendRepo2) con la fecha del ENVÍO (16/10), y el 19/10 a las
    17:03 Santiago regularizó por inventario el 43992 en Algete (+2). La comprobación de inventario bloquea esa entrada.

    Estado a las 16:50 del 22/10: 10 de las 12 líneas YA se han dado de alta hoy (16:46-16:49). Quedan pendientes:
      - 43763: no vino (Alfredo). Al «Terminar» queda como diferencia, como siempre.
      - 43992: la que bloquea. Esa unidad ya la contó Santiago en el inventario del 19/10 (y el 20/10 se vendieron 2): si se
        recibiera ahora, Algete quedaría con una de más.

    Arreglo (decisión de Carlos, 22/10/26): se quita de la 81028 la entrada pendiente del 43992 en Algete. La salida de Reina
    (−1, 16/10) se queda como está. Después Alfredo pulsa «Terminar» en Recibir.

    Ejecutar en SSMS contra NV como sa. Si no cuadra la fila (1), deshace todo solo.
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

SELECT 'DESPUÉS' AS Momento, LTRIM(RTRIM([Número])) AS Producto, CONVERT(varchar(10), Fecha, 120) AS Fecha, Cantidad, [Almacén], Diario, Estado
FROM dbo.PreExtrProducto
WHERE Empresa = '1' AND [NºTraspaso] = 81028 AND Diario = 'PendRepo2' AND [Almacén] = 'ALG';

IF @quitadas = 1
BEGIN
    COMMIT TRANSACTION;
    SELECT 'HECHO: quitada la entrada del 43992; la 81028 ya se puede «Terminar».' AS Resultado;
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    SELECT 'DESHECHO: no se ha encontrado la línea del 43992 (se esperaba 1). No se ha cambiado nada.' AS Resultado;
END
