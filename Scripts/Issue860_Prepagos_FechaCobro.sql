/*
    NestoAPI#860 (corte 1, 13/11/26): FECHA REAL DEL COBRO DEL PREPAGO.

    dbo.Prepagos.FechaCobro (date NULL): el día en que el cliente pagó de verdad (transferencia, tarjeta, enlace de pago).
    NULL = como hasta ahora (al facturar, el cobro se contabiliza con la fecha de la factura). En este corte solo se guarda y se
    devuelve (PrepagoDTO.FechaCobro de PedidoVentaDTO); la contabilización con esta fecha, en un asiento aparte, es el corte 2
    (prdCrearFacturaVta y prdDeshacerFacturaVta).

    La columna NO está en el EDMX: la API la lee y la escribe con SQL aparte y, sin ella, todo funciona como hoy (los prepagos
    se guardan sin fecha). Tras lanzar el script, la API la ve sola en la siguiente comprobación (no hace falta publicar).

    Lanzar como sa en NV. Idempotente. La columna va en su propio lote y con EXEC (el lote entero se compila antes de
    ejecutarse). Sin GRANTs nuevos: la API ya lee y escribe dbo.Prepagos (EF hace INSERT/UPDATE de la tabla).
*/
SET NOCOUNT ON;
USE NV;
GO
-- Columna FechaCobro (en su propio lote)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Prepagos') AND name = N'FechaCobro')
BEGIN
    EXEC(N'ALTER TABLE dbo.Prepagos ADD FechaCobro date NULL');
    PRINT 'Prepagos: añadida FechaCobro (NULL en las filas que ya había = fecha de la factura, como siempre).';
END
ELSE
    PRINT 'Prepagos: FechaCobro ya existía; no se toca.';
GO
-- Comprobación
SELECT c.name AS Columna, t.name AS Tipo, c.is_nullable AS AdmiteNulos
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.Prepagos') AND c.name = N'FechaCobro';
GO
