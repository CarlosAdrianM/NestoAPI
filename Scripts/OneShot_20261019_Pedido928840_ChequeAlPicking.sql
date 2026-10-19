-- 19/10/26: la línea del cheque regalo del pedido 928840 (CHEQUE50_OCT26, -1 ud., -50 €) volvió a pendiente: el picking de la API
-- la sacó bien (99875), pero Ariadna la enseñó en el recorrido como algo que recoger (no filtra ficticios), Andre la marcó «No está»
-- y al terminar el picking SacarDelPedido la devolvió a Estado -1 / Picking 0. Se vuelve a meter en el 99875 para que el albarán y la
-- factura lleven el descuento. LANZADO por Carlos el 19/10/26. Lanzar como sa ANTES de crear el albarán del 928840.
USE NV;
BEGIN TRAN;
UPDATE LinPedidoVta SET Picking = 99875, Estado = 1
 WHERE Empresa = '1' AND Número = 928840 AND Producto = 'CHEQUE50_OCT26' AND Cantidad = -1
   AND ISNULL(Picking, 0) = 0 AND Estado = -1 AND [Nº Albarán] IS NULL;
IF @@ROWCOUNT = 1 COMMIT ELSE BEGIN ROLLBACK; RAISERROR('No se ha tocado nada: la línea no estaba como se esperaba', 16, 1); END
SELECT RTRIM(Producto) Producto, Estado, Picking, Cantidad, Bruto FROM LinPedidoVta WHERE Número = 928840 ORDER BY [Nº Orden];   -- todas con Picking 99875 y Estado 1
