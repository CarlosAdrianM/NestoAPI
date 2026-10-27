/*
    Novedad 791 (Andre, 27/10/26 15:14): la reposición 81187 (Alcobendas → Algete, creada por Paloma el 27/10 a las 09:37)
    no se puede terminar en Ariadna: «Se ha realizado un inventario posterior a esta fecha. No se pueden meter movimientos
    anteriores a ese inventario.» (NestoAPI#695, tercer caso en una semana: 81028, 81177 y esta).

    Causa: las entradas en Algete van en PreExtrProducto (diario PendRepo2) con la fecha del ENVÍO (09:37), y a las 10:13
    Alfredo regularizó por inventario el 40275 en Algete (−3, diario _RegInvent). El 40275 viene en la 81187 (2 unidades).
    Es el único producto de la 81187 con un inventario posterior al envío.

    Arreglo: la entrada pendiente del 40275 pasa a la fecha de ahora (posterior al inventario). A las 10:13 la caja seguía en
    Alcobendas (el repartidor la trae después), así que esas 2 unidades no estaban en Algete cuando Alfredo contó: entran como
    dos más. La salida de Alcobendas (26/10) no se toca. Después Andre pulsa «Terminar» en Ariadna.

    Ejecutar en SSMS contra NV como sa. Si no cuadra la fila (1), deshace todo solo.
*/
SET NOCOUNT ON;
USE NV;

SELECT 'ANTES' AS Momento, LTRIM(RTRIM([Número])) AS Producto, CONVERT(varchar(16), Fecha, 120) AS Fecha, Cantidad, [Almacén], Diario, Estado
FROM dbo.PreExtrProducto
WHERE Empresa = '1' AND [NºTraspaso] = 81187 AND Diario = 'PendRepo2' AND [Almacén] = 'ALG' AND [Número] = '40275';

BEGIN TRANSACTION;

UPDATE dbo.PreExtrProducto
SET Fecha = GETDATE()
WHERE Empresa = '1' AND [NºTraspaso] = 81187 AND Diario = 'PendRepo2' AND [Almacén] = 'ALG' AND [Número] = '40275';
DECLARE @cambiadas int = @@ROWCOUNT;

SELECT 'DESPUÉS' AS Momento, LTRIM(RTRIM([Número])) AS Producto, CONVERT(varchar(16), Fecha, 120) AS Fecha, Cantidad, [Almacén], Diario, Estado
FROM dbo.PreExtrProducto
WHERE Empresa = '1' AND [NºTraspaso] = 81187 AND Diario = 'PendRepo2' AND [Almacén] = 'ALG' AND [Número] = '40275';

IF @cambiadas = 1
BEGIN
    COMMIT TRANSACTION;
    SELECT 'HECHO: la entrada del 40275 va con fecha de hoy; la 81187 ya se puede terminar.' AS Resultado;
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    SELECT 'DESHECHO: no se ha encontrado la línea del 40275 (se esperaba 1). No se ha cambiado nada.' AS Resultado;
END
