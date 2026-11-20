-- OneShot 20/11/26: limpiar las reservas huérfanas de Ubicaciones (ALG) ANTES del inventario del 21/11/26.
--
-- Por qué: el inventario solo ve y solo toca lo LIBRE (estado 0) de cada hueco: el teórico (StockCalculado) es lo
-- libre del hueco y prdContabilizarInventario pone el estado 0 al contado. Lo reservado (3 picking, 4 reposición)
-- ni se cuenta ni se toca. Una reserva cuyo picking o reposición ya no existe se queda fuera del inventario para
-- siempre: si la unidad está en el hueco se cuenta como sobrante (stock inflado: libre + reserva) y si no está,
-- sigue como stock fantasma. Hoy el stock (extracto 1+3) de estos productos CUADRA con Ubicaciones contando estas
-- reservas, así que no se borran: se pasan a LIBRE en su hueco y el recuento de mañana decide si están o no.
--
-- Qué hay (comprobado el 20/11/26 a las 07:30):
--   * 7 reservas de picking (estado 3) de líneas YA FACTURADAS (estado 4), 12 unidades:
--       23560 x1 (003-003-007), 27559 x1 (003-002-014) y 24801 x1 (007-003-001): pedidos 431356/431497 de 2010, empresa 3
--       42528 x1 (005-003-005): pedido 822330 de 2022, empresa 3 (la línea acabó siendo del 38539)
--       38507 x1 (002-002-011): pedido 915280, abril 2026 (la línea acabó siendo del 12524)
--       25964 x5 (013-000-003) y 24470 x2 (009-003-012): pedido 924473, albarán 727742 de agosto 2026
--   * 2 reservas de reposición (estado 4) de octubre/noviembre 2025 cuyo NºOrdenRepo ya no está en PreExtrProducto:
--       40212 x1 (001-006-015) y 40214 x1 (007-003-005)
--   * 41255: stock de ALG 0 (el traspaso 81493 del 19/11 a SDC sacó 2) y queda 1 libre en 001-004-009. Si mañana se
--     cuenta 0, el inventario restaría 1 y dejaría el stock en -1. Se borra la fila: si la unidad está, el recuento
--     la da de alta; si no está, no pasa nada. (Además hoy bloquea cualquier albarán del 41255 por descuadre.)
--   * TiCKET (203) y CHEQUE50_OCT26 (81) libres en 006-003-001: son productos FICTICIOS con Ubicar = 1. Como se venden
--     con cantidad negativa, cada albarán los deja «pendientes de ubicar» (prdCrearAlbaránVta, rama de las entradas,
--     que filtra p.ubicar = 1) y acaban en un hueco. No son mercancía: se borran sus filas vivas y se les quita
--     Ubicar (todas las empresas) para que no vuelvan a generar ubicaciones. Su «stock» del extracto no se toca
--     (los ficticios no entran en la comprobación de descuadre).
--
-- Ejecutar como sa, con Algete sin inventario pendiente. Todo o nada.
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Huerfanas TABLE (Orden int NOT NULL PRIMARY KEY, Producto char(15) NOT NULL, Cantidad int NOT NULL, Estado int NOT NULL);
INSERT INTO @Huerfanas (Orden, Producto, Cantidad, Estado) VALUES
    (101258901, '23560', 1, 3), (100583001, '27559', 1, 3), (100584201, '24801', 1, 3), (267081601, '42528', 1, 3),
    (316133101, '38507', 1, 3), (320522601, '25964', 5, 3), (320631201, '24470', 2, 3),
    (308791401, '40212', 1, 4), (309137001, '40214', 1, 4);

BEGIN TRAN;

-- 1) Las nueve filas siguen como se vieron
IF (SELECT COUNT(*) FROM Ubicaciones u INNER JOIN @Huerfanas h ON h.Orden = u.[NºOrden]
    WHERE u.[Almacén] = 'ALG' AND u.[Número] = h.Producto AND u.Cantidad = h.Cantidad AND u.Estado = h.Estado) <> 9
BEGIN RAISERROR('Alguna de las 9 reservas ya no está como se vio el 20/11 a las 07:30. No se ha tocado nada.', 16, 1); ROLLBACK; RETURN; END

-- 2) Siguen siendo huérfanas: ninguna línea de venta sin facturar ni movimiento de reposición vivo detrás
IF EXISTS (SELECT 1 FROM Ubicaciones u INNER JOIN @Huerfanas h ON h.Orden = u.[NºOrden]
           INNER JOIN LinPedidoVta l ON l.[Nº Orden] = u.[NºOrdenVta] WHERE h.Estado = 3 AND l.Estado < 4)
   OR EXISTS (SELECT 1 FROM Ubicaciones u INNER JOIN @Huerfanas h ON h.Orden = u.[NºOrden]
              INNER JOIN PreExtrProducto p ON p.[Nº Orden] = u.[NºOrdenRepo] WHERE h.Estado = 4)
BEGIN RAISERROR('Alguna reserva vuelve a tener una línea o una reposición viva detrás. No se ha tocado nada.', 16, 1); ROLLBACK; RETURN; END

SELECT 'ANTES' Momento, u.Empresa, u.[NºOrden], RTRIM(u.[Número]) Producto, u.Cantidad, u.Pasillo, u.Fila, u.Columna, u.Estado, u.PedidoVta
FROM Ubicaciones u WHERE u.[Almacén] = 'ALG' AND u.Estado >= 0
  AND u.[Número] IN (SELECT Producto FROM @Huerfanas UNION ALL SELECT '41255')
ORDER BY u.[Número], u.Estado;

-- 3) Donde el producto ya tiene fila libre en ese mismo hueco, se le suma la reserva y la reserva se borra
DECLARE @Sumadas TABLE (Orden int NOT NULL PRIMARY KEY);

UPDATE libre SET Cantidad = libre.Cantidad + r.Cantidad
OUTPUT r.[NºOrden] INTO @Sumadas (Orden)
FROM Ubicaciones libre
INNER JOIN Ubicaciones r ON r.[Almacén] = libre.[Almacén] AND r.[Número] = libre.[Número]
    AND r.Pasillo = libre.Pasillo AND r.Fila = libre.Fila AND r.Columna = libre.Columna
INNER JOIN @Huerfanas h ON h.Orden = r.[NºOrden]
WHERE libre.Estado = 0 AND libre.[Almacén] = 'ALG';

DELETE u FROM Ubicaciones u INNER JOIN @Sumadas s ON s.Orden = u.[NºOrden];

-- 4) Las demás pasan a libres en su hueco (empresa 1, sin pedido ni reposición)
UPDATE u SET Estado = 0, Empresa = '1', PedidoVta = NULL, [NºOrdenVta] = NULL, [NºOrdenRepo] = NULL, Usuario = SUSER_SNAME()
FROM Ubicaciones u INNER JOIN @Huerfanas h ON h.Orden = u.[NºOrden]
WHERE u.[NºOrden] NOT IN (SELECT Orden FROM @Sumadas);

-- 5) 41255: stock 0 en ALG y una fila libre que sobra
IF ISNULL((SELECT SUM(e.Cantidad) FROM ExtractoProducto e WHERE e.Empresa IN ('1', '3') AND e.[Almacén] = 'ALG' AND e.[Número] = '41255'), 0) = 0
BEGIN
    DELETE Ubicaciones WHERE [NºOrden] = 323648201 AND [Número] = '41255' AND [Almacén] = 'ALG' AND Estado = 0 AND Cantidad = 1;
    IF @@ROWCOUNT <> 1 PRINT '41255: la fila 323648201 ya no está como se vio (estado 0, cantidad 1); no se ha borrado. Revisar aparte.';
END
ELSE PRINT '41255: el stock de ALG ya no es 0; no se ha borrado su fila. Revisar aparte.';

-- 5b) Ficticios con ubicación: fuera sus filas vivas y fuera Ubicar
IF (SELECT COUNT(*) FROM Productos WHERE Empresa = '1' AND [Número] IN ('TiCKET', 'CHEQUE50_OCT26') AND Ficticio = 1) <> 2
BEGIN RAISERROR('TiCKET o CHEQUE50_OCT26 ya no son ficticios en la empresa 1. No se ha tocado nada.', 16, 1); ROLLBACK; RETURN; END

SELECT 'FICTICIOS ANTES' Momento, [NºOrden], RTRIM([Número]) Producto, Cantidad, Pasillo, Fila, Columna, Estado
FROM Ubicaciones WHERE Estado >= 0 AND [Número] IN ('TiCKET', 'CHEQUE50_OCT26');

DELETE Ubicaciones WHERE Estado >= 0 AND [Número] IN ('TiCKET', 'CHEQUE50_OCT26');
PRINT 'Ficticios: ' + CAST(@@ROWCOUNT AS varchar(10)) + ' filas vivas borradas.';

UPDATE Productos SET Ubicar = 0 WHERE [Número] IN ('TiCKET', 'CHEQUE50_OCT26') AND Ubicar = 1;
PRINT 'Ficticios: Ubicar quitado en ' + CAST(@@ROWCOUNT AS varchar(10)) + ' fichas.';

SELECT 'DESPUES' Momento, u.Empresa, u.[NºOrden], RTRIM(u.[Número]) Producto, u.Cantidad, u.Pasillo, u.Fila, u.Columna, u.Estado, u.PedidoVta
FROM Ubicaciones u WHERE u.[Almacén] = 'ALG' AND u.Estado >= 0
  AND u.[Número] IN (SELECT Producto FROM @Huerfanas UNION ALL SELECT '41255')
ORDER BY u.[Número], u.Estado;

-- 6) Tiene que seguir cuadrando: stock (empresas 1 + 3) = ubicado (estado >= 0). Si sale alguna fila, se deshace todo.
IF EXISTS (
    SELECT 1 FROM (SELECT Producto FROM @Huerfanas UNION SELECT '41255') pr
    WHERE ISNULL((SELECT SUM(e.Cantidad) FROM ExtractoProducto e WHERE e.Empresa IN ('1', '3') AND e.[Almacén] = 'ALG' AND e.[Número] = pr.Producto), 0)
       <> ISNULL((SELECT SUM(u.Cantidad) FROM Ubicaciones u WHERE u.[Almacén] = 'ALG' AND u.Estado >= 0 AND u.[Número] = pr.Producto), 0))
BEGIN RAISERROR('Algún producto queda descuadrado (stock distinto de lo ubicado). Se deshace todo.', 16, 1); ROLLBACK; RETURN; END

COMMIT;
PRINT 'Hecho: 9 reservas huérfanas pasadas a libres y 41255 cuadrado.';
