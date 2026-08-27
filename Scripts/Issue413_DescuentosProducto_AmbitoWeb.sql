-- NestoAPI#413: ofertas de tarifa por audiencia — EJECUTADO EN PROD el 27/08/2026
-- (histórico: la columna nació como AmbitoWeb y se renombró a AudienciaOferta el mismo
--  día, antes de marcar ninguna fila; los descuentos son POR AUDIENCIA — profesional /
--  público — se apliquen donde se apliquen: web, mostrador o futuros clientes).

-- Estado final de las columnas:

-- AudienciaOferta (tinyint NOT NULL DEFAULT 0):
--   0 = no se publica como oferta (DEFAULT; la fila sigue aplicándose en Nesto al
--       calcular precios de profesionales, como siempre)
--   1 = oferta para profesionales
--   2 = oferta para profesionales y público
--   3 = oferta solo para público (excepcional)
--
-- DescuentoPublico (decimal(18,4) NULL): solo con audiencia 2-3.
--   NULL  = el público ve el MISMO % que el profesional
--   valor = el público ve ESE % (0.20 = 20 %)

/* Script aplicado (por pasos, el 27/08/2026):

ALTER TABLE dbo.DescuentosProducto
    ADD AmbitoWeb tinyint NOT NULL
        CONSTRAINT DF_DescuentosProducto_AmbitoWeb DEFAULT (0),
        CONSTRAINT CK_DescuentosProducto_AmbitoWeb CHECK (AmbitoWeb BETWEEN 0 AND 3);
ALTER TABLE dbo.DescuentosProducto
    ADD DescuentoPublico decimal(18,4) NULL;

-- Renombrado (las "dependencias forzadas" eran las dos constraints de arriba):
ALTER TABLE dbo.DescuentosProducto DROP CONSTRAINT CK_DescuentosProducto_AmbitoWeb;
ALTER TABLE dbo.DescuentosProducto DROP CONSTRAINT DF_DescuentosProducto_AmbitoWeb;
EXEC sp_rename 'dbo.DescuentosProducto.AmbitoWeb', 'AudienciaOferta', 'COLUMN';
ALTER TABLE dbo.DescuentosProducto ADD CONSTRAINT DF_DescuentosProducto_AudienciaOferta DEFAULT (0) FOR AudienciaOferta;
ALTER TABLE dbo.DescuentosProducto ADD CONSTRAINT CK_DescuentosProducto_AudienciaOferta CHECK (AudienciaOferta BETWEEN 0 AND 3);
*/

-- Ejemplo de uso manual mientras no exista la pestaña (Nesto#455) — ref 41269, oferta
-- del 20 % solo para profesionales:
-- UPDATE dbo.DescuentosProducto SET AudienciaOferta = 1
-- WHERE Empresa='1' AND [Nº Producto]='41269' AND [Nº Cliente] IS NULL AND NºProveedor IS NULL;
