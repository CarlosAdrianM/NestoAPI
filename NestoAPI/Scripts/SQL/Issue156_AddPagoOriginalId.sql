-- Issue #156: Añadir PagoOriginalId a PagosTPV para vincular reintentos de pagos denegados
-- Ejecutar ANTES de desplegar

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'PagosTPV' AND COLUMN_NAME = 'PagoOriginalId'
)
BEGIN
    ALTER TABLE PagosTPV ADD PagoOriginalId INT NULL;

    ALTER TABLE PagosTPV ADD CONSTRAINT FK_PagosTPV_PagoOriginal
        FOREIGN KEY (PagoOriginalId) REFERENCES PagosTPV(Id);
END
