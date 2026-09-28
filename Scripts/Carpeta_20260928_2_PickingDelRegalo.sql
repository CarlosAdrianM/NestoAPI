-- 28/09/26: el regalo en carpeta de 926373 y 927146 necesita el mismo picking que el resto del pedido para poder albaranarlo
-- (prdCrearAlbaránVta: «No se puede crear albarán de líneas sin picking en un pedido que tiene líneas con picking»).
-- Ejecutar como sa en NV.
USE NV;
GO
UPDATE LinPedidoVta SET Picking = 99680 WHERE Empresa = '1' AND [Nº Orden] = 328091200 AND Estado = 1 AND Recoger = Cantidad AND ISNULL(Picking, 0) = 0;
UPDATE LinPedidoVta SET Picking = 99678 WHERE Empresa = '1' AND [Nº Orden] = 328462700 AND Estado = 1 AND Recoger = Cantidad AND ISNULL(Picking, 0) = 0;
SELECT [Nº Orden], Número, Estado, Recoger, Picking FROM LinPedidoVta WHERE [Nº Orden] IN (328091200, 328462700); -- 2 filas con picking 99680 y 99678
GO
