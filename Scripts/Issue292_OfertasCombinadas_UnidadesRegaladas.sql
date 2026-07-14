-- Issue #292: cuántas unidades se regalan por instancia en ofertas combinadas con
-- RegalarMenorImporte (2+2, 3+2, 36+36...). Hasta ahora la regla era fija: UNA unidad gratis
-- por instancia. Default 1 = comportamiento actual (migración inocua para las existentes).
--
-- ⚠️ EJECUTAR EN PRODUCCIÓN ANTES DE DESPLEGAR LA API (el EDMX ya mapea la columna).
-- BD: NV (NestoConnection). Columna nueva: hereda permisos de tabla (sin GRANTs).

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OfertasCombinadas') AND name = 'UnidadesRegaladas')
BEGIN
    ALTER TABLE OfertasCombinadas ADD UnidadesRegaladas smallint NOT NULL
        CONSTRAINT DF_OfertasCombinadas_UnidadesRegaladas DEFAULT 1;
END
GO

-- VERIFICACIÓN (debe devolver la columna smallint NOT NULL y todas las ofertas con valor 1):
SELECT name, TYPE_NAME(system_type_id) AS tipo, is_nullable
FROM sys.columns WHERE object_id = OBJECT_ID('OfertasCombinadas') AND name = 'UnidadesRegaladas';
SELECT UnidadesRegaladas, COUNT(*) AS ofertas FROM OfertasCombinadas GROUP BY UnidadesRegaladas;
GO
