-- =============================================================================
-- Issue #390 — Slice 1: repuntar los Prepagos pendientes de facturar a las 440
-- =============================================================================
-- Ejecutar EN EL MOMENTO de desplegar el Nesto con el cambio de DatosMarkets
-- (cuentas de pago 555 -> 440). Los pedidos de Amazon ya creados guardan la
-- cuenta vieja en Prepagos.CuentaContable; si no se repuntan, su apunte de
-- prepago (_FRAVTA) caería en la 555 al facturarse. Solo toca filas SIN
-- facturar (Factura vacía): las históricas ya contabilizadas no se tocan.
-- Es idempotente: re-ejecutarlo no encuentra nada que cambiar.
-- Los prepagos que se facturen entre el despliegue y esta ejecución caerán en
-- la 555 vieja: los recogerá el barrido de partidas abiertas (no es incidente).
-- =============================================================================

SET NOCOUNT ON;

SELECT 'Antes' AS Momento, RTRIM(CuentaContable) AS Cuenta, COUNT(*) AS Filas, SUM(Importe) AS Importe
FROM Prepagos
WHERE (Factura IS NULL OR RTRIM(Factura) = '')
  AND CuentaContable IN ('55500047','55500045','55500048','55500046','55500049','55500050',
                         '55500072','55500080','55500075','55500039','55500082','55500084','55500087')
GROUP BY CuentaContable;

UPDATE Prepagos
SET CuentaContable = '440000' + RIGHT(RTRIM(CuentaContable), 2),
    FechaModificacion = GETDATE()
WHERE (Factura IS NULL OR RTRIM(Factura) = '')
  AND CuentaContable IN ('55500047','55500045','55500048','55500046','55500049','55500050',
                         '55500072','55500080','55500075','55500039','55500082','55500084','55500087');

PRINT 'Prepagos repuntados: ' + CAST(@@ROWCOUNT AS varchar(10));

SELECT 'Después' AS Momento, RTRIM(CuentaContable) AS Cuenta, COUNT(*) AS Filas, SUM(Importe) AS Importe
FROM Prepagos
WHERE (Factura IS NULL OR RTRIM(Factura) = '')
  AND (CuentaContable LIKE '555000%' OR CuentaContable LIKE '440000%')
GROUP BY CuentaContable;
