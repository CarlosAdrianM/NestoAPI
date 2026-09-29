-- NestoAPI#522 (parte 1): la factura definitiva se reenvía sola al cliente que recibió el justificante provisional.
--
-- Con el interruptor Verifactu:JustificanteProvisionalDesde encendido (01/12/26), una factura que todavía
-- no está registrada en Verifactu se envía por correo como «DOCUMENTO PROVISIONAL: NO ES UNA FACTURA», y el
-- texto promete «La recibirá por correo electrónico en cuanto se emita».
--
--   VerifactuEnviadaProvisional = 1  → el envío diario de facturas le mandó al cliente el justificante
--                                      provisional (el correo salió bien). Cuando la factura se registre,
--                                      el job de Verifactu le manda la factura definitiva (con QR) y la
--                                      marca vuelve a 0.
--   0                                → ya se le mandó la definitiva (o nunca hizo falta).
--   NULL                             → nunca se envió como provisional (lo de siempre).
--
-- La columna es NULL a propósito: el ALTER es instantáneo (sin reescribir CabFacturaVta) y los escritores
-- que no la conocen (prdCrearFacturaVta, Nesto viejo) siguen exactamente igual. SIN índice: la consulta del
-- job es por VerifactuEnviadaProvisional = 1, que casi nunca tendrá filas, y en CabFacturaVta no se crean
-- índices filtrados (QUOTED_IDENTIFIER OFF de los módulos viejos, #542).
--
-- ⚠️ EJECUTAR COMO sa EN SSMS ANTES de publicar la API: el EDMX ya mapea la columna y sin ella cualquier
-- lectura de CabFacturaVta se cae (mismo aviso que Issue522_VerifactuIncidencia).

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'VerifactuEnviadaProvisional')
BEGIN
    ALTER TABLE dbo.CabFacturaVta ADD VerifactuEnviadaProvisional bit NULL;
    PRINT 'Campo VerifactuEnviadaProvisional añadido';
END
GO

-- Diagnóstico: justificantes provisionales enviados cuya factura definitiva está pendiente de mandar
-- SELECT Número, Fecha, [Nº Cliente], VerifactuUUID, VerifactuEstado
-- FROM dbo.CabFacturaVta WITH (NOLOCK)
-- WHERE VerifactuEnviadaProvisional = 1
-- ORDER BY Fecha DESC;
