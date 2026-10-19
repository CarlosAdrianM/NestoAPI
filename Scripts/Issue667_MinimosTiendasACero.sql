-- NestoAPI#667 (19/10/26): en las tiendas no hay stock mínimo, solo máximo. El 16/10 «Actualizar Stocks por Proveedor»
-- grabó mínimos en REI (780) y ALC (595). Se ponen a 0; los máximos que grabó Manuel se quedan como están.
-- Al poner a 0 los de REI deja de salir el correo «Productos con Stock Mínimo Elevado» (prdCorreoStocksMinimosAltosReina).
-- Lanzar como sa.
USE NV;
SELECT Almacén, COUNT(*) AS ConMinimo FROM ControlesStock WHERE Almacén IN ('REI', 'ALC') AND StockMínimo <> 0 GROUP BY Almacén;   -- antes: REI 780, ALC 595
UPDATE ControlesStock SET StockMínimo = 0
 WHERE Almacén IN ('REI', 'ALC') AND StockMínimo <> 0;
SELECT @@ROWCOUNT AS Actualizadas;   -- debe dar 1375 (si nadie ha tocado nada desde el 19/10)
SELECT COUNT(*) AS Quedan FROM ControlesStock WHERE Almacén IN ('REI', 'ALC') AND StockMínimo <> 0;   -- debe dar 0
