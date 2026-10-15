/*
    OneShot 15/10/26 — NestoAPI#651: las reservas de las reposiciones de Algete que siguen «por salir» vuelven a estar vivas.
    Ejecutar en SSMS contra NV como sa. Idempotente. Por defecto NO escribe nada (@Aplicar = 0): solo enseña lo que haría.

    QUÉ PASA
    Al crear una reposición desde Algete (job ReposicionAutomatica, Ariadna F7/F8, POST api/Reposiciones), la API pasaba
    ya las reservas de los huecos a salida (Estado −4, cantidad negativa, NºTraspasoRepo). Pero la salida se queda en
    PreExtrProducto sin contabilizar hasta que Ariadna termina la recogida, así que el extracto aún cuenta esas unidades:
    mientras tanto las Ubicaciones vivas (estado ≥ 0) quedaban N por debajo del stock. 15/10/26: traspaso 81016,
    37800 con stock 32 y 31 vivas.
    Desde NestoAPI#651 la reserva se queda en 4 (positiva, enlazada al traspaso) y pasa a −4 al contabilizar la salida.

    QUÉ HACE
    Para cada traspaso de Algete cuya salida sigue en PreExtrProducto (Almacén = 'ALG', Cantidad < 0, NºTraspaso no nulo)
    y no está en ExtractoProducto: sus filas −4 de Ubicaciones en ALG vuelven a estado 4 con la cantidad en positivo,
    manteniendo NºTraspasoRepo y NºOrdenRepo. Así, al terminar la recogida en Ariadna (con la API ya publicada), pasan a
    −4 como las nuevas. Si Ariadna la termina con la API vieja, el −4 ya estaba hecho: no hay que lanzar esto.

    ORDEN: publicar antes la API con NestoAPI#651 (si no, al terminar en Ariadna la API vieja no pasaría la reserva a −4
    y quedaría viva de más) y lanzar esto después. Las que se creen con la API nueva ya nacen bien.

    SEGURIDAD
    Con @Aplicar = 1, todo en una transacción. Al final comprueba que para los productos tocados la suma de Ubicaciones
    vivas (estado ≥ 0, empresas 1 y 3, ALG) es igual al stock de ALG (ExtractoProducto, empresas 1 y 3), el mismo criterio
    que GET api/Ubicaciones/Cuadre. Si no, ROLLBACK y no se toca nada. Los productos en un inventario abierto los
    rechazaría el trigger de Ubicaciones (trdUpdUbicaciones): se dejan fuera y se avisan (volver a lanzar al cerrarlo).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Aplicar bit = 0;            -- 1 para escribir
DECLARE @usuario varchar(30) = 'OneShot_20261015_651';

IF OBJECT_ID('tempdb..#filas') IS NOT NULL DROP TABLE #filas;
SELECT u.[NºOrden] AS Id, u.Empresa, u.[Almacén] AS Almacen, u.[Número] AS Producto, u.Cantidad, u.Estado,
       u.[NºTraspasoRepo] AS Traspaso, u.[NºOrdenRepo] AS OrdenRepo, u.Pasillo, u.Fila, u.Columna
INTO #filas
FROM Ubicaciones u
WHERE u.[Almacén] = 'ALG' AND u.Estado = -4 AND u.Cantidad < 0
  AND u.[NºTraspasoRepo] IN (SELECT s.[NºTraspaso] FROM PreExtrProducto s
                             WHERE s.[Almacén] = 'ALG' AND s.Cantidad < 0 AND s.[NºTraspaso] IS NOT NULL)
  AND NOT EXISTS (SELECT 1 FROM ExtractoProducto e
                  WHERE e.[NºTraspaso] = u.[NºTraspasoRepo] AND e.[Almacén] = 'ALG' AND e.Cantidad < 0);

PRINT 'Se dejan para cuando se cierre su inventario:';
SELECT f.Id, f.Traspaso, RTRIM(f.Producto) AS Producto, f.Cantidad FROM #filas f
WHERE EXISTS (SELECT 1 FROM Inventarios i WHERE i.[Número] = f.Producto AND i.[Almacén] = f.Almacen AND i.Empresa = f.Empresa
              AND i.Estado = 1 AND i.[NºTraspaso] IS NULL AND i.Almacen <> 'REI');
DELETE f FROM #filas f
WHERE EXISTS (SELECT 1 FROM Inventarios i WHERE i.[Número] = f.Producto AND i.[Almacén] = f.Almacen AND i.Empresa = f.Empresa
              AND i.Estado = 1 AND i.[NºTraspaso] IS NULL AND i.Almacen <> 'REI');

PRINT 'Filas −4 que vuelven a reserva viva (estado 4, cantidad positiva):';
SELECT f.Id, f.Traspaso, RTRIM(f.Empresa) AS Empresa, RTRIM(f.Producto) AS Producto, f.Cantidad AS CantidadAntes, -f.Cantidad AS CantidadDespues,
       f.OrdenRepo, ISNULL(RTRIM(f.Pasillo) + '/' + RTRIM(f.Fila) + '/' + RTRIM(f.Columna), '-') AS Hueco
FROM #filas f ORDER BY f.Traspaso, f.Producto;

-- El cuadre de los productos tocados: antes, y cómo quedaría (las −4 no cuentan; como 4 cuentan en positivo)
IF OBJECT_ID('tempdb..#cuadre') IS NOT NULL DROP TABLE #cuadre;
SELECT p.Producto,
       ISNULL((SELECT SUM(CAST(e.Cantidad AS int)) FROM ExtractoProducto e
               WHERE e.Empresa IN ('1', '3') AND e.[Almacén] = 'ALG' AND e.[Número] = p.Producto), 0) AS Stock,
       ISNULL((SELECT SUM(u.Cantidad) FROM Ubicaciones u
               WHERE u.Empresa IN ('1', '3') AND u.[Almacén] = 'ALG' AND u.[Número] = p.Producto AND ISNULL(u.Estado, 0) >= 0), 0) AS VivasAntes,
       (SELECT -SUM(f.Cantidad) FROM #filas f WHERE f.Producto = p.Producto) AS Vuelven
INTO #cuadre
FROM (SELECT DISTINCT Producto FROM #filas) p;

PRINT 'Cuadre de los productos tocados (VivasDespues tiene que ser igual a Stock):';
SELECT RTRIM(c.Producto) AS Producto, c.Stock, c.VivasAntes, c.Vuelven, c.VivasAntes + c.Vuelven AS VivasDespues,
       CASE WHEN c.VivasAntes + c.Vuelven = c.Stock THEN 'Cuadra' ELSE 'NO CUADRA' END AS Situacion
FROM #cuadre c ORDER BY c.Producto;

IF @Aplicar = 0
BEGIN
    PRINT 'Ensayo (@Aplicar = 0): no se ha escrito nada.';
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM #filas)
BEGIN
    PRINT 'No hay nada que hacer.';
    RETURN;
END

BEGIN TRANSACTION;

UPDATE u SET Cantidad = -u.Cantidad, Estado = 4, Usuario = @usuario
FROM Ubicaciones u INNER JOIN #filas f ON f.Id = u.[NºOrden]
WHERE u.Estado = -4 AND u.Cantidad < 0;

IF @@ROWCOUNT <> (SELECT COUNT(*) FROM #filas)
BEGIN
    ROLLBACK;
    RAISERROR('Las filas han cambiado mientras tanto: no se ha tocado nada. Vuelve a lanzarlo.', 16, 1);
    RETURN;
END

IF EXISTS (
    SELECT 1 FROM #cuadre c
    WHERE ISNULL((SELECT SUM(u.Cantidad) FROM Ubicaciones u
                  WHERE u.Empresa IN ('1', '3') AND u.[Almacén] = 'ALG' AND u.[Número] = c.Producto AND ISNULL(u.Estado, 0) >= 0), 0)
       <> ISNULL((SELECT SUM(CAST(e.Cantidad AS int)) FROM ExtractoProducto e
                  WHERE e.Empresa IN ('1', '3') AND e.[Almacén] = 'ALG' AND e.[Número] = c.Producto), 0))
BEGIN
    ROLLBACK;
    RAISERROR('Algún producto tocado no cuadra (Ubicaciones vivas <> stock de ALG): ROLLBACK, no se ha tocado nada.', 16, 1);
    RETURN;
END

COMMIT;
PRINT 'Hecho: las reservas vuelven a estar vivas (estado 4) y cuadran con el stock.';
