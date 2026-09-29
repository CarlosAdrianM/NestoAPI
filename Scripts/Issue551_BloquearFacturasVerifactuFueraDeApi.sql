-- NestoAPI#551 (29/09/26): las facturas de las series que se declaran a Verifactu (NV, CV, EV, UL, RV, RC)
-- solo se pueden crear desde NestoAPI, que es la que genera y envía el registro de facturación.
--
-- Por qué en la BD: el camino viejo de #348 sigue abierto. Los grupos Administración, Cursos, Facturación y
-- Tiendas tienen EXECUTE sobre prdCrearFacturaVta y se usa a diario para la serie GB (interna, no se declara).
-- Por ahí podría salir una NV/CV sin registro (CV2600484/485, 20/07), y pasado un día Verifacti ya no la admite.
-- No se quitan los permisos porque GB dejaría de funcionar; el trigger solo frena las series declarables.
--
-- NestoAPI y sus jobs de Hangfire conectan con seguridad integrada como NUEVAVISION\RDS2016$ (verificado:
-- todas las facturas de esas series desde el 21/07 tienen UUID, o sea, salieron por la API).
-- ORIGINAL_LOGIN() y no SUSER_SNAME(): no cambia aunque algún módulo use EXECUTE AS.
--
-- Ejecutar como sa. Idempotente. Vuelta atrás al final (comentada).
USE NV;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
CREATE OR ALTER TRIGGER dbo.trgCabFacturaVtaSoloDesdeNestoAPI
ON dbo.CabFacturaVta
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF ORIGINAL_LOGIN() <> N'NUEVAVISION\RDS2016$'
        AND EXISTS (SELECT 1 FROM inserted WHERE RTRIM(Serie) IN ('NV', 'CV', 'EV', 'UL', 'RV', 'RC'))
    BEGIN
        DECLARE @serie varchar(10) = (SELECT TOP 1 RTRIM(Serie) FROM inserted WHERE RTRIM(Serie) IN ('NV', 'CV', 'EV', 'UL', 'RV', 'RC'));
        RAISERROR(N'Las facturas de la serie %s solo se pueden crear desde Nesto (NestoAPI), que es la que las declara a Verifactu. La factura NO se ha creado (NestoAPI#551).', 16, 1, @serie);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END
GO

-- CV2600484/485: facturadas el 20/07 por el camino viejo, antes de que Verifactu sea obligatorio (01/12/26).
-- Se descartan para que salgan de la ventana de pendientes y del job (NestoAPI#551).
UPDATE dbo.CabFacturaVta
SET VerifactuEstado = 'DescartadaPreObligatoria'
WHERE Empresa = '1' AND Número IN ('CV2600484', 'CV2600485')
    AND (VerifactuUUID IS NULL OR VerifactuUUID = '');
GO

-- Comprobaciones: el trigger existe y las dos facturas están descartadas
SELECT name, is_disabled FROM sys.triggers WHERE name = 'trgCabFacturaVtaSoloDesdeNestoAPI';
SELECT Número, VerifactuEstado FROM dbo.CabFacturaVta WHERE Empresa = '1' AND Número IN ('CV2600484', 'CV2600485');

-- VUELTA ATRÁS (solo si el trigger bloquease algo legítimo):
-- DROP TRIGGER dbo.trgCabFacturaVtaSoloDesdeNestoAPI;
