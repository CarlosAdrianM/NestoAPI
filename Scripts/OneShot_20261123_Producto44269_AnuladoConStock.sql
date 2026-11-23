/*
    23/11/26. CAMILLA ELÉCTRICA VITAL (44269): ficha ANULADA (Estado -1) con 1 unidad de stock en Alcobendas tras el recuento de Paloma
    (traspaso 81577). Decisión de Carlos: como los anulados con stock del día del inventario → Estado 4. Lanzar como sa.
    Solo toca Productos (empresa 1) si sigue en -1 y de verdad tiene stock. Se puede relanzar: si ya no está en -1, no hace nada.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;

PRINT 'ANTES';
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, p.Estado,
       (SELECT SUM(e.Cantidad) FROM ExtractoProducto e WITH (NOLOCK) WHERE e.[Número] = p.[Número] AND e.Empresa IN ('1', '3')) AS Stock
FROM Productos p WITH (NOLOCK) WHERE p.Empresa = '1' AND p.[Número] = '44269';

IF NOT EXISTS (SELECT 1 FROM Productos WHERE Empresa = '1' AND [Número] = '44269' AND Estado = -1)
BEGIN
    PRINT 'El 44269 ya no está anulado (Estado -1): nada que hacer.';
    RETURN;
END
IF ISNULL((SELECT SUM(e.Cantidad) FROM ExtractoProducto e WHERE e.[Número] = '44269' AND e.Empresa IN ('1', '3')), 0) <= 0
BEGIN
    PRINT 'El 44269 no tiene stock: no se toca.';
    RETURN;
END

BEGIN TRANSACTION;
UPDATE Productos SET Estado = 4 WHERE Empresa = '1' AND [Número] = '44269' AND Estado = -1;
IF @@ROWCOUNT <> 1
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('No se ha cambiado exactamente una ficha: deshecho.', 16, 1);
    RETURN;
END
COMMIT TRANSACTION;
PRINT 'Pasado a estado 4.';

PRINT 'DESPUÉS';
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, p.Estado FROM Productos p WITH (NOLOCK) WHERE p.Empresa = '1' AND p.[Número] = '44269';
