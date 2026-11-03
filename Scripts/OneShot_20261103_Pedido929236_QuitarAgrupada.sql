/*
    03/11/26: el pedido 929236 (cliente 12786) se facturó en el fin de mes del 30/10 (NV2617663) y le quedan 2 líneas
    pendientes (estado -1), pero la cabecera sigue con Agrupada = 1, que el fin de mes no quitó (ver NestoAPI#727).
    Se quita la marca para que las 2 líneas sigan su curso normal. Lanzar como sa en NV.
*/
USE NV;
SELECT Empresa, Número, RTRIM([Nº Cliente]) Cliente, Agrupada, MantenerJunto FROM dbo.CabPedidoVta WHERE Empresa = '1' AND Número = 929236;
UPDATE dbo.CabPedidoVta SET Agrupada = 0 WHERE Empresa = '1' AND Número = 929236 AND Agrupada = 1;
PRINT CONCAT('Filas: ', @@ROWCOUNT);
