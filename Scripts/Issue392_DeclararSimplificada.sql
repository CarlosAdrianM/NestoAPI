-- NestoAPI#392: salida legal para una factura COMPLETA emitida con un NIF que no se puede conseguir
-- (caso real 20/08/26, cliente 9093, NIF de relleno "1000000"). Con Verifactu en producción no se puede
-- dejar sin declarar (huecos en la serie y en el encadenamiento), así que se declara como SIMPLIFICADA.
--
--   VerifactuDeclararSimplificada = 1 → Administración o Dirección la han marcado desde la ventana de
--                                       facturas pendientes de Verifactu (con motivo obligatorio, que queda
--                                       en la tabla Modificaciones). Se declara como F2, SIN destinatario, y
--                                       sus rectificativas (LinFacturaVtaRectificacion) heredan la marca y
--                                       se declaran como R5. Solo se permite si el importe no supera el
--                                       límite legal de la simplificada (400 €, art. 4 RD 1619/2012, #325).
--   NULL / 0                          → lo de siempre (F1/R1 con el destinatario de la factura).
--
-- La columna es NULL a propósito: el ALTER es instantáneo (sin reescribir CabFacturaVta) y los escritores
-- que no la conocen (prdCrearFacturaVta, Nesto viejo) siguen exactamente igual. SIN índice (ni filtrado ni
-- columnas calculadas indexadas): en CabFacturaVta escriben módulos viejos con QUOTED_IDENTIFIER OFF (#542).
--
-- ⚠️ EJECUTAR COMO sa EN SSMS ANTES de publicar la API: el EDMX ya mapea la columna y sin ella cualquier
-- lectura de CabFacturaVta se cae (mismo aviso que Issue522_VerifactuEnviadaProvisional).

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CabFacturaVta' AND COLUMN_NAME = 'VerifactuDeclararSimplificada')
BEGIN
    ALTER TABLE dbo.CabFacturaVta ADD VerifactuDeclararSimplificada bit NULL;
    PRINT 'Campo VerifactuDeclararSimplificada añadido';
END
GO

-- Diagnóstico: facturas marcadas para declararse como simplificadas y su estado en Verifactu
-- SELECT Número, Fecha, [Nº Cliente], CifNif, VerifactuUUID, VerifactuEstado, VerifactuUltimoError
-- FROM dbo.CabFacturaVta WITH (NOLOCK)
-- WHERE VerifactuDeclararSimplificada = 1
-- ORDER BY Fecha DESC;
--
-- Quién y por qué (auditoría):
-- SELECT Fecha, Usuario, Anterior, Nuevo FROM dbo.Modificaciones WITH (NOLOCK)
-- WHERE Tabla = 'CabFacturaVta' AND Nuevo LIKE '%#392%' ORDER BY Fecha DESC;
