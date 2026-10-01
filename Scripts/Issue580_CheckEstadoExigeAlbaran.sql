-- NestoAPI#580: una línea de pedido de venta NO puede estar en albarán (estado 2) ni en factura
-- (estado 4) sin número de albarán. Pasó con el pedido 926291: el PUT de NestoAPI creó una línea
-- de portes directamente en estado 2, la agrupación de fin de mes (que une por Nº Albarán) la dejó
-- sola y se facturó sin albarán (NV2616067).
--
-- Esta restricción lo impide para cualquiera que escriba en LinPedidoVta: NestoAPI, Nesto, los
-- procedimientos o una consulta a mano. Comprobado el 01/10/26:
--   - de 2,8 millones de líneas solo la incumplen 3 (las de #580: 918776, 921070, 926291, ya
--     facturadas). Se añade WITH NOCHECK para no tocarlas; las demás quedan protegidas.
--   - prdCrearAlbaránVta, prdDeshacerAlbaránVta y prdDeshacerFacturaVta cambian el estado y el
--     número de albarán en la MISMA sentencia, así que no les afecta.
--
-- Es DDL: desde SSMS con `sa` (el login `nuevavision` no tiene ALTER).
-- Pide un bloqueo de esquema sobre LinPedidoVta durante un instante: si hay consultas largas en
-- curso espera; con el LOCK_TIMEOUT de abajo se rinde a los 10 s en vez de dejar colgados a los
-- usuarios. Si pasa, volver a lanzarlo en un rato. Se puede lanzar dos veces sin problema.
--
-- Para quitarla (no debería hacer falta):
--   ALTER TABLE dbo.LinPedidoVta DROP CONSTRAINT CK_LinPedidoVta_AlbaranYFacturaExigenNumeroAlbaran;

USE NV;
GO

SET LOCK_TIMEOUT 10000;
GO

-- 1. Las que ya la incumplen: deben salir las 3 de #580. Si sale alguna más, avisar antes de seguir.
SELECT Número, [Nº Orden], Producto, Texto, Estado, [Nº Factura], Usuario, [Fecha Modificación]
FROM dbo.LinPedidoVta WITH (NOLOCK)
WHERE Estado >= 2 AND [Nº Albarán] IS NULL;
GO

-- 2. La restricción
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_LinPedidoVta_AlbaranYFacturaExigenNumeroAlbaran')
BEGIN
    ALTER TABLE dbo.LinPedidoVta WITH NOCHECK
        ADD CONSTRAINT CK_LinPedidoVta_AlbaranYFacturaExigenNumeroAlbaran
        CHECK (Estado < 2 OR [Nº Albarán] IS NOT NULL);
    PRINT N'Restricción creada.';
END
ELSE
    PRINT N'La restricción ya existía.';
GO

-- 3. Comprobación: intentar pasar a albarán una línea sin número de albarán TIENE que fallar.
--    Va dentro de una transacción que se deshace siempre: no cambia nada.
DECLARE @orden int = (SELECT TOP 1 [Nº Orden] FROM dbo.LinPedidoVta WITH (NOLOCK) WHERE Estado = 1 AND [Nº Albarán] IS NULL);
IF @orden IS NULL
    PRINT N'No hay ninguna línea en curso para probar; la restricción está creada igualmente.';
ELSE
BEGIN
    BEGIN TRAN;
    BEGIN TRY
        UPDATE dbo.LinPedidoVta SET Estado = 2 WHERE [Nº Orden] = @orden;
        PRINT N'ERROR: se ha podido pasar a albarán una línea sin albarán. La restricción NO funciona.';
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 547
            PRINT N'OK: la restricción impide pasar a albarán una línea sin número de albarán.';
        ELSE
            PRINT N'Error inesperado en la comprobación: ' + ERROR_MESSAGE();
    END CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
END
GO
