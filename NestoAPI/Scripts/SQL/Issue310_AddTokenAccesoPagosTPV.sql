-- Issue Nesto#310: Añadir TokenAcceso a PagosTPV para URLs de pago seguras
-- Ejecutar ANTES de desplegar y ANTES de actualizar el EDMX

ALTER TABLE PagosTPV ADD TokenAcceso UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();

CREATE UNIQUE INDEX UQ_PagosTPV_TokenAcceso ON PagosTPV (TokenAcceso);

-- Tras ejecutar este script, actualizar el EDMX en Visual Studio:
-- 1. Abrir NestoEntities.edmx
-- 2. Click derecho > "Update Model from Database"
-- 3. Seleccionar la tabla PagosTPV (actualizar, no añadir)
-- 4. Guardar y compilar
