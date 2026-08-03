-- Issue #378: país en CódigosPostales
--   1. Columna Pais varchar(2) NULL (retrocompatible: Nesto viejo sigue insertando sin país)
--      + FK a Paises(Codigo) (UQ_Paises_Codigo).
--   2. Backfill:
--      a) PT en lote: Provincia = PORTUGAL o formato portugués dddd-ddd / dddd ddd.
--      b) Resto de países obvios: Provincia coincide con el nombre de un país de la tabla Paises.
--      c) ES: formato español (5 dígitos 01000-52999, o 4 dígitos por el cero inicial perdido
--         del sistema viejo) que no haya caído en a) ni b).
--   Los CPs que no encajen en nada quedan con Pais NULL (desconocido) y se corrigen desde la
--   ventana de mantenimiento de códigos postales de Nesto.
--
-- EJECUTAR EN: NV (DC2016\SQL2017). Revisar los SELECT de verificación del final.

-- =====================================================================
-- 1. Columna + FK
-- =====================================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CódigosPostales') AND name = 'Pais')
BEGIN
    ALTER TABLE dbo.CódigosPostales ADD Pais varchar(2) NULL;
END
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_CódigosPostales_Paises')
BEGIN
    ALTER TABLE dbo.CódigosPostales WITH CHECK
        ADD CONSTRAINT FK_CódigosPostales_Paises FOREIGN KEY (Pais)
        REFERENCES dbo.Paises (Codigo);
END
GO

-- =====================================================================
-- 2.a) Portugal en lote
-- =====================================================================
-- Revisión previa (debería devolver solo CPs portugueses):
SELECT RTRIM(Empresa) AS Empresa, RTRIM(Número) AS Numero, RTRIM(Descripción) AS Poblacion, RTRIM(Provincia) AS Provincia
FROM dbo.CódigosPostales
WHERE Pais IS NULL
  AND (RTRIM(Provincia) COLLATE Modern_Spanish_CI_AI LIKE 'PORTUGAL%'
       OR RTRIM(Número) LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9]'
       OR RTRIM(Número) LIKE '[0-9][0-9][0-9][0-9] [0-9][0-9][0-9]')
ORDER BY Número;

UPDATE dbo.CódigosPostales
SET Pais = 'PT'
WHERE Pais IS NULL
  AND (RTRIM(Provincia) COLLATE Modern_Spanish_CI_AI LIKE 'PORTUGAL%'
       OR RTRIM(Número) LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9]'
       OR RTRIM(Número) LIKE '[0-9][0-9][0-9][0-9] [0-9][0-9][0-9]');
GO

-- =====================================================================
-- 2.b) Resto de países obvios: la Provincia es el nombre de un país
--      (así se grababan los extranjeros: Provincia = MEXICO, EL SALVADOR...)
-- =====================================================================
-- Revisión previa:
SELECT RTRIM(cp.Número) AS Numero, RTRIM(cp.Provincia) AS Provincia, p.Codigo
FROM dbo.CódigosPostales cp
    INNER JOIN dbo.Paises p
        ON RTRIM(cp.Provincia) COLLATE Modern_Spanish_CI_AI = RTRIM(p.Nombre) COLLATE Modern_Spanish_CI_AI
WHERE cp.Pais IS NULL AND p.Codigo <> 'ES'
ORDER BY p.Codigo, cp.Número;

UPDATE cp
SET cp.Pais = p.Codigo
FROM dbo.CódigosPostales cp
    INNER JOIN dbo.Paises p
        ON RTRIM(cp.Provincia) COLLATE Modern_Spanish_CI_AI = RTRIM(p.Nombre) COLLATE Modern_Spanish_CI_AI
WHERE cp.Pais IS NULL AND p.Codigo <> 'ES';
GO

-- =====================================================================
-- 2.c) España: formato español que no haya caído antes.
--      5 dígitos con provincia 01-52, o 4 dígitos (cero inicial perdido → provincias 01-09).
--      OJO: un CP de 5 dígitos francés/alemán/italiano colisiona con este formato; si alguno
--      está mal, se corrige desde la ventana de mantenimiento (limitación asumida: la PK de la
--      tabla es Empresa+Número, sin país).
-- =====================================================================
UPDATE dbo.CódigosPostales
SET Pais = 'ES'
WHERE Pais IS NULL
  AND ((RTRIM(Número) LIKE '[0-9][0-9][0-9][0-9][0-9]' AND LEFT(RTRIM(Número), 2) BETWEEN '01' AND '52')
       OR RTRIM(Número) LIKE '[1-9][0-9][0-9][0-9]');
GO

-- =====================================================================
-- 3. Verificación
-- =====================================================================
SELECT ISNULL(Pais, '(null)') AS Pais, COUNT(*) AS N
FROM dbo.CódigosPostales
GROUP BY ISNULL(Pais, '(null)')
ORDER BY N DESC;

-- Los que quedan sin país (a corregir a mano desde la ventana de mantenimiento):
SELECT RTRIM(Empresa) AS Empresa, RTRIM(Número) AS Numero, RTRIM(Descripción) AS Poblacion, RTRIM(Provincia) AS Provincia
FROM dbo.CódigosPostales
WHERE Pais IS NULL
ORDER BY Número;
