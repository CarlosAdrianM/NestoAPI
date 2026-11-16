-- 16/11/26: RV2600154 y RV2600155 (05/11/26, Copiar factura «invertir» sobre RV2600152 y RV2600153) salieron en serie RV
-- con cantidades POSITIVAS y sin vinculaciones: Verifactu no las declara («no encontramos la factura de la venta que se devuelve»).
-- Desde el 11/11/26 deshacer un abono ya sale en NV, pero estas dos están emitidas en RV y así se quedan.
-- Arreglo: se vinculan a la rectificativa que deshacen (RV2600154 -> RV2600152, RV2600155 -> RV2600153), línea a línea por
-- producto, cantidad y precio. Se declaran como R por diferencias (en positivo) de esa rectificativa en la siguiente pasada del job.
-- Lanzar como sa. Idempotente: si ya tienen vinculaciones no hace nada.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @pares TABLE (Nueva char(10), Deshecha char(10));
INSERT INTO @pares VALUES ('RV2600154', 'RV2600152'), ('RV2600155', 'RV2600153');

DECLARE @filas TABLE (NumeroFactura char(10), NumeroLinea int, FacturaOriginalNumero char(10), FacturaOriginalLinea int, CantidadRectificada decimal(18,4));
INSERT INTO @filas
SELECT p.Nueva, n.[Nº Orden], p.Deshecha, d.[Nº Orden], ABS(n.Cantidad)
FROM @pares p
INNER JOIN LinPedidoVta n ON n.Empresa = '1' AND n.[Nº Factura] = p.Nueva AND n.Cantidad > 0
CROSS APPLY (SELECT TOP 1 x.[Nº Orden] FROM LinPedidoVta x
             WHERE x.Empresa = '1' AND x.[Nº Factura] = p.Deshecha AND x.Producto = n.Producto
               AND x.Cantidad = -n.Cantidad AND x.Precio = n.Precio
             ORDER BY x.[Nº Orden]) d
WHERE NOT EXISTS (SELECT 1 FROM LinFacturaVtaRectificacion r WHERE r.Empresa = '1' AND r.NumeroFactura = p.Nueva);

-- Cada línea de las dos facturas tiene que encontrar su pareja (5 + 5); si no, no se toca nada
DECLARE @lineas int = (SELECT COUNT(*) FROM LinPedidoVta l INNER JOIN @pares p ON l.[Nº Factura] = p.Nueva WHERE l.Empresa = '1');
IF EXISTS (SELECT 1 FROM LinFacturaVtaRectificacion r INNER JOIN @pares p ON r.NumeroFactura = p.Nueva WHERE r.Empresa = '1')
BEGIN
    PRINT 'Ya estaban vinculadas: no se hace nada.';
    ROLLBACK;
    RETURN;
END
DECLARE @casadas int = (SELECT COUNT(*) FROM @filas);
IF @casadas <> @lineas OR @lineas <> 10
BEGIN
    PRINT CONCAT('No casan todas las líneas (', @casadas, ' de ', @lineas, '): no se hace nada.');
    ROLLBACK;
    RETURN;
END

INSERT INTO LinFacturaVtaRectificacion (Empresa, NumeroFactura, NumeroLinea, FacturaOriginalNumero, FacturaOriginalLinea, CantidadRectificada)
SELECT '1', NumeroFactura, NumeroLinea, FacturaOriginalNumero, FacturaOriginalLinea, CantidadRectificada FROM @filas;

SELECT RTRIM(NumeroFactura) Factura, NumeroLinea, RTRIM(FacturaOriginalNumero) Rectifica, FacturaOriginalLinea, CantidadRectificada
FROM LinFacturaVtaRectificacion WHERE Empresa = '1' AND NumeroFactura IN ('RV2600154', 'RV2600155') ORDER BY 1, 2;

COMMIT;
PRINT 'Vinculadas. El job de Verifactu las declarará en su siguiente pasada.';
