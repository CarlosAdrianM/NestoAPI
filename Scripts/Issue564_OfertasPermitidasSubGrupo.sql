-- NestoAPI#564 (29/09/26): las reglas de OfertasPermitidas pueden limitarse a un subgrupo de producto.
-- Ejecutar como sa ANTES de publicar la API (el EDMX ya mapea la columna). Idempotente. Sin índices.
-- Ningún SP escribe en OfertasPermitidas (comprobado en sys.sql_modules), así que no hay riesgo de QUOTED_IDENTIFIER.
USE NV;
GO
IF COL_LENGTH('dbo.OfertasPermitidas', 'SubGrupo') IS NULL
    ALTER TABLE dbo.OfertasPermitidas ADD SubGrupo char(3) NULL;
GO
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OfertasPermitidas' AND COLUMN_NAME = 'SubGrupo';
