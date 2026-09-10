-- NestoAPI#478: interruptor de familia para la tienda. Pausar la venta de una casa entera
-- (Mirplay, 10/09/26: tarifa del proveedor errónea) sin correos ni listas: se marca la familia
-- en Nesto, la API republica sus productos con VentaPausada = true y el módulo de PrestaShop
-- los desactiva con marca propia; al desmarcar, reactiva exactamente esos.
--
-- ⚠️ EJECUTAR COMO sa EN SSMS ANTES de publicar la API: el EDMX ya mapea la columna y sin ella
-- cualquier lectura de Familias (y la publicación de productos) se cae.

ALTER TABLE dbo.Familias
    ADD VentaPausadaEnTienda bit NOT NULL
        CONSTRAINT DF_Familias_VentaPausadaEnTienda DEFAULT (0);
GO

-- Comprobación
SELECT Número, Descripción, VentaPausadaEnTienda FROM dbo.Familias WHERE Empresa = '1' AND VentaPausadaEnTienda = 1;
GO
