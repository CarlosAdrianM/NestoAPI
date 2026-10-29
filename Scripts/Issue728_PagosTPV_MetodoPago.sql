-- NestoAPI#728: Apple Pay y Google Pay en los enlaces de pago (NestoPago).
-- PagosTPV.MetodoPago era NVARCHAR(1) (Issue165) y solo cabían 'C' y 'z'. Se amplía a
-- NVARCHAR(10) para que quepan 'xpay' (Apple Pay) y 'google' (Google Pay), que son los
-- valores de DS_MERCHANT_PAYMETHODS de Redsys.
--
-- Ejecutar ANTES de desplegar la versión de la API con #728 (el EDMX ya espera 10).
-- Es solo ampliar: no toca los datos existentes, no necesita GRANTs nuevos.
-- BD: NV (la de NestoConnection).

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.PagosTPV')
      AND name = N'MetodoPago'
      AND max_length < 20 -- NVARCHAR(10) = 20 bytes
)
BEGIN
    ALTER TABLE dbo.PagosTPV ALTER COLUMN MetodoPago NVARCHAR(10) NULL;
END
GO

-- Valores esperados:
--   'C'      -> solo tarjeta
--   'z'      -> solo Bizum
--   'xpay'   -> solo Apple Pay (la página de pago lo ofrece solo en iPhone/iPad/iPod/Mac)
--   'google' -> solo Google Pay
--   NULL     -> sin selección (la página ofrece todos los métodos)

-- Comprobación:
SELECT c.name, t.name AS tipo, c.max_length / 2 AS longitud, c.is_nullable
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.PagosTPV') AND c.name = N'MetodoPago';
