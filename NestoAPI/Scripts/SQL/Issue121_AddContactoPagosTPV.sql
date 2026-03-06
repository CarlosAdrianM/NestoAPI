-- Issue #121: Añadir columna Contacto a PagosTPV para contabilizar cobros correctamente
-- Ejecutar ANTES de desplegar esta versión

ALTER TABLE dbo.PagosTPV
ADD Contacto NVARCHAR(3) NULL;
