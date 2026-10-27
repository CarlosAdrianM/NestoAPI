/*
    Novedad 788 (Paloma, 27/10/26): la reposición 81177 (Algete → Alcobendas, enviada el 26/10 a las 13:00) no se puede
    dar de alta: «Se ha realizado un inventario posterior a esta fecha. No se pueden meter movimientos anteriores a ese
    inventario.» (NestoAPI#695).

    Causa: las entradas en Alcobendas van en PreExtrProducto (diario RepoAlgAlc) con la fecha del ENVÍO (26/10 13:00), y ese
    mismo día a las 14:19 Paloma regularizó por inventario el 42267 en Alcobendas (+1). Es el único producto de la 81177
    con un inventario posterior al envío.

    Arreglo: la entrada pendiente del 42267 pasa a la fecha de ahora (posterior al inventario). La caja salió de Algete a
    las 13:00 y no pudo estar en la tienda a las 14:19, así que esa unidad no se contó en el inventario: entra como una más.
    Si Paloma confirma lo contrario (la caja ya estaba y la contó), NO ejecutar este script: habría que quitar la entrada,
    como con la 81028 (OneShot_20261022_Repo81028_FechaRecepcion.sql).
    La salida de Algete (−1, 26/10) no se toca. Después Paloma pulsa «Dar de alta solo lo leído» o «Terminar».

    Ejecutar en SSMS contra NV como sa. Si no cuadra la fila (1), deshace todo solo.
*/
SET NOCOUNT ON;
USE NV;

SELECT 'ANTES' AS Momento, LTRIM(RTRIM([Número])) AS Producto, CONVERT(varchar(16), Fecha, 120) AS Fecha, Cantidad, [Almacén], Diario, Estado
FROM dbo.PreExtrProducto
WHERE Empresa = '1' AND [NºTraspaso] = 81177 AND Diario = 'RepoAlgAlc' AND [Almacén] = 'ALC' AND [Número] = '42267';

BEGIN TRANSACTION;

UPDATE dbo.PreExtrProducto
SET Fecha = GETDATE()
WHERE Empresa = '1' AND [NºTraspaso] = 81177 AND Diario = 'RepoAlgAlc' AND [Almacén] = 'ALC' AND [Número] = '42267';
DECLARE @cambiadas int = @@ROWCOUNT;

SELECT 'DESPUÉS' AS Momento, LTRIM(RTRIM([Número])) AS Producto, CONVERT(varchar(16), Fecha, 120) AS Fecha, Cantidad, [Almacén], Diario, Estado
FROM dbo.PreExtrProducto
WHERE Empresa = '1' AND [NºTraspaso] = 81177 AND Diario = 'RepoAlgAlc' AND [Almacén] = 'ALC' AND [Número] = '42267';

IF @cambiadas = 1
BEGIN
    COMMIT TRANSACTION;
    SELECT 'HECHO: la entrada del 42267 va con fecha de hoy; la 81177 ya se puede dar de alta.' AS Resultado;
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    SELECT 'DESHECHO: no se ha encontrado la línea del 42267 (se esperaba 1). No se ha cambiado nada.' AS Resultado;
END
