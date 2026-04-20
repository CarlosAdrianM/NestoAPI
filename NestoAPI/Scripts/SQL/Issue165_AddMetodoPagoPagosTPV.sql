-- NestoAPI#165: persistir el método de pago elegido por el cliente en PagosTPV
-- para que el flujo de reintento tras denegado preserve la elección inicial
-- (tarjeta / bizum / sin selección).
-- Ejecutar ANTES de desplegar esta versión.

ALTER TABLE dbo.PagosTPV ADD MetodoPago NVARCHAR(1) NULL;

-- Valores esperados:
--   'C' → solo tarjeta
--   'z' → solo Bizum
--   NULL → sin selección (muestra todos los métodos habilitados en Redsys)
