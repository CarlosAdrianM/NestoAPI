-- 22/10/26: cuadra Ubicaciones con el stock de Algete en los dos descuadres del 21/10/26.
--   41823: la nota de entrega 928959 de Reina restó 1 de la fila 322773401 (estado 2 de Algete, desmontaje del kit
--          45383). Vuelve a 3, que son los que siguen pendientes de ubicar. (Arreglo del SP: prdExtrProducto_EntregFacEstado2PorAlmacen.sql)
--   45621: el albarán de compra 125686 (+3 y −3) dejó la fila 322774101 en estado 2 con 3 que no han llegado.
--          Pasa a estado −1 (traza, no cuenta). (Arreglo del SP: prdCrearAlbaranCmp_EntradaNetaEstado2.sql)
-- Lanzar como sa. Cada UPDATE comprueba el valor actual: si alguien ya lo ha tocado, no hace nada (0 filas).
USE NV;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

UPDATE dbo.Ubicaciones SET Cantidad = 3
WHERE NºOrden = 322773401 AND Número = '41823' AND Almacén = 'ALG' AND Estado = 2 AND Cantidad = 2;
PRINT CONCAT('41823: ', @@ROWCOUNT, ' fila(s)');

UPDATE dbo.Ubicaciones SET Estado = -1
WHERE NºOrden = 322774101 AND Número = '45621' AND Almacén = 'ALG' AND Estado = 2 AND Cantidad = 3;
PRINT CONCAT('45621: ', @@ROWCOUNT, ' fila(s)');

COMMIT TRANSACTION;
GO

-- Comprobación: Stock (ExtractoProducto) frente a Ubicaciones vivas (estados 0, 2 y 3... los que no son traza) en ALG
SELECT p.Número AS Producto,
       (SELECT SUM(e.Cantidad) FROM dbo.ExtractoProducto e WITH (NOLOCK)
         WHERE e.Empresa = '1' AND e.Número = p.Número AND e.Almacén = 'ALG') AS Stock,
       (SELECT SUM(u.Cantidad) FROM dbo.Ubicaciones u WITH (NOLOCK)
         WHERE u.Número = p.Número AND u.Almacén = 'ALG' AND u.Estado >= 0) AS Ubicacion
FROM (VALUES ('41823'), ('45621')) AS p(Número);
GO
