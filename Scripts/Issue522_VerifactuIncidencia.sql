-- NestoAPI#522 (parte 1): marca de «pendiente por incidencia» de Verifactu en la factura.
--
-- Cuando el envío a Verifacti falla por un FALLO TÉCNICO (sin conexión, timeout, HTTP 5xx) y no por
-- un rechazo de los datos (4xx de validación, NIF...), la factura queda «pendiente por incidencia»:
-- la normativa exige remitirla al recuperarse marcando la incidencia (Verifacti: incidencia = "S").
--
--   VerifactuIncidencia = 1  → hubo una caída técnica al enviarla. Si además VerifactuUUID es NULL,
--                              sigue PENDIENTE de registrar (se reenvía con incidencia = S el mismo
--                              día; las de días anteriores esperan la respuesta de Verifacti sobre
--                              PUT modify). Cuando se registre, la marca se conserva como rastro de
--                              que se declaró con incidencia (el payload queda en VerifactuRegistros).
--   NULL / 0                 → sin incidencia (comportamiento de siempre).
--
-- La columna es NULL a propósito: el ALTER es instantáneo (sin reescribir CabFacturaVta) y los
-- escritores que no la conocen (prdCrearFacturaVta, Nesto viejo) siguen exactamente igual.
--
-- ⚠️ EJECUTAR COMO sa EN SSMS ANTES de publicar la API: el EDMX ya mapea la columna y sin ella
-- cualquier lectura de CabFacturaVta se cae (mismo aviso que Issue346_347 y Issue542).

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'VerifactuIncidencia')
BEGIN
    ALTER TABLE dbo.CabFacturaVta ADD VerifactuIncidencia bit NULL;
    PRINT 'Campo VerifactuIncidencia añadido';
END
GO

-- Diagnóstico: facturas pendientes por incidencia (sin registrar) y las que ya se registraron con ella
-- SELECT Número, Fecha, VerifactuUUID, VerifactuUltimoIntento, VerifactuUltimoError
-- FROM dbo.CabFacturaVta WITH (NOLOCK)
-- WHERE VerifactuIncidencia = 1
-- ORDER BY Fecha DESC;
