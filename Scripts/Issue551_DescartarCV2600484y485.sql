-- NestoAPI#551 (29/09/26): CV2600484/485 se facturaron el 20/07 por el camino viejo (Nesto viejo), cuando
-- prdCrearFacturaVta aún dejaba pasar la serie CV. Hoy el SP solo admite RDS2016$ (NestoAPI), Carlos y la
-- serie GB, así que no hace falta ningún control más. Las dos son anteriores a la obligación (01/12/26):
-- se descartan para que salgan de la ventana de pendientes y del job.
--
-- Ejecutar como sa DESPUÉS de publicar la versión que conoce el estado DescartadaPreObligatoria (con la
-- versión anterior, el job las volvería a intentar y las dejaría otra vez en SinDatosFiscales). Idempotente.
USE NV;
GO
UPDATE dbo.CabFacturaVta
SET VerifactuEstado = 'DescartadaPreObligatoria'
WHERE Empresa = '1' AND Número IN ('CV2600484', 'CV2600485')
    AND (VerifactuUUID IS NULL OR VerifactuUUID = '');
GO

-- Comprobación: las dos con el estado nuevo
SELECT Número, VerifactuEstado FROM dbo.CabFacturaVta WHERE Empresa = '1' AND Número IN ('CV2600484', 'CV2600485');
