-- 24/11/26. La RV2600189 (cliente 10714, pedido 931334) deshace el abono RV2600186: mismas líneas en positivo,
-- creadas al copiar la rectificativa invertida DENTRO del mismo pedido RV. Se quedó sin vinculaciones
-- (LinFacturaVtaRectificacion) y por eso no se declara («no encontramos la factura de la venta que se devuelve»).
-- Decisión de Carlos (24/11/26): rectificar una rectificativa se permite y se declara bien -> la RV2600189
-- rectifica a la RV2600186. Con estas filas, el job de Verifactu la envía sola en su siguiente pasada.
-- OJO: la factura es del 24/11; el alta solo se admite hasta el 25/11. Lanzar como sa.
SET NOCOUNT ON;
SET XACT_ABORT ON;

SELECT 'ANTES' Momento, * FROM dbo.LinFacturaVtaRectificacion WHERE Empresa = '1' AND NumeroFactura IN ('RV2600186', 'RV2600189');

BEGIN TRAN;

IF EXISTS (SELECT 1 FROM dbo.LinFacturaVtaRectificacion WHERE Empresa = '1' AND NumeroFactura = 'RV2600189')
BEGIN
    PRINT 'La RV2600189 ya tiene vinculaciones: no se hace nada.';
    ROLLBACK;
    RETURN;
END

-- Cada línea de la RV2600189 (positiva) con la línea de la RV2600186 (negativa) del mismo producto y la cantidad contraria
INSERT INTO dbo.LinFacturaVtaRectificacion (Empresa, NumeroFactura, NumeroLinea, FacturaOriginalNumero, FacturaOriginalLinea, CantidadRectificada)
SELECT '1', 'RV2600189', n.[Nº Orden], 'RV2600186', o.[Nº Orden], ABS(n.Cantidad)
FROM dbo.LinPedidoVta n
JOIN dbo.LinPedidoVta o ON o.Empresa = n.Empresa AND o.Número = n.Número AND o.Producto = n.Producto AND o.Cantidad = -n.Cantidad
    AND RTRIM(o.[Nº Factura]) = 'RV2600186'
WHERE n.Empresa = '1' AND n.Número = 931334 AND RTRIM(n.[Nº Factura]) = 'RV2600189' AND n.Cantidad > 0;

IF @@ROWCOUNT <> 3
BEGIN
    PRINT 'Se esperaban 3 líneas vinculadas y han salido otras: se deshace.';
    ROLLBACK;
    RETURN;
END

COMMIT;

SELECT 'DESPUES' Momento, * FROM dbo.LinFacturaVtaRectificacion WHERE Empresa = '1' AND NumeroFactura IN ('RV2600186', 'RV2600189');
