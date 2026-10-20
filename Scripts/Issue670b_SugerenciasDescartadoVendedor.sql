/*
    NestoAPI#670 (Nesto#537, segunda vuelta): tercera respuesta «DescartadoVendedor» («🚫 Quitar» en la plantilla: el
    vendedor no ofrece el producto porque sabe que el cliente no lo quiere o no se lo puede vender; p. ej. LevelLash a un
    distribuidor de Córdoba). Excluye el producto 60 días para ese cliente, igual que «NoLeInteresa», pero se registra aparte:
    para el modelo de aceptación (NestoAPI#671, 2e) NO es un rechazo del cliente.

    Ejecutar en SSMS contra NV como sa (ALTER del CK: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    Requiere Issue670_SugerenciasProductos.sql (lanzado el 19/10/26). Se puede lanzar antes o después del deploy:
    - Sin este script, la API mira el CK (sys.check_constraints) y, si no admite «DescartadoVendedor», guarda «NoLeInteresa»
      con la marca «[DescartadoVendedor] » delante del Texto (excluye igual 60 días y nunca falla por el CK).
    - Este script amplía el CK y pasa esas filas provisionales a «DescartadoVendedor» (quitando la marca del Texto).
      Después, la API guarda «DescartadoVendedor» directamente.

    Respuesta es varchar(20): «DescartadoVendedor» tiene 18 caracteres, cabe sin tocar la columna.
*/

SET NOCOUNT ON;
USE NV;
GO

-- 1) El CK de Respuesta con «DescartadoVendedor» (se rehace solo si todavía no lo admite)
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_SugerenciasProductosMostradas_Respuesta'
                 AND parent_object_id = OBJECT_ID('dbo.SugerenciasProductosMostradas')
                 AND definition LIKE '%DescartadoVendedor%')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_SugerenciasProductosMostradas_Respuesta'
                 AND parent_object_id = OBJECT_ID('dbo.SugerenciasProductosMostradas'))
        ALTER TABLE dbo.SugerenciasProductosMostradas DROP CONSTRAINT CK_SugerenciasProductosMostradas_Respuesta;
    ALTER TABLE dbo.SugerenciasProductosMostradas ADD CONSTRAINT CK_SugerenciasProductosMostradas_Respuesta
        CHECK (Respuesta IS NULL OR Respuesta IN ('Anadido', 'NoLeInteresa', 'DescartadoVendedor'));
END
GO

-- 2) Las guardadas antes de este script como «NoLeInteresa» con la marca: a «DescartadoVendedor», sin la marca
--    («[DescartadoVendedor] » son 21 caracteres: el texto original empieza en el 22)
UPDATE dbo.SugerenciasProductosMostradas
SET Respuesta = 'DescartadoVendedor',
    Texto = NULLIF(SUBSTRING(Texto, 22, 200), N'')
WHERE Respuesta = 'NoLeInteresa'
  AND Texto LIKE N'[[]DescartadoVendedor] %';
PRINT CONCAT('Filas provisionales pasadas a DescartadoVendedor: ', @@ROWCOUNT);
GO

-- Comprobación:
-- SELECT definition FROM sys.check_constraints WHERE name = 'CK_SugerenciasProductosMostradas_Respuesta';
-- SELECT Respuesta, COUNT(*) FROM dbo.SugerenciasProductosMostradas GROUP BY Respuesta;
-- SELECT COUNT(*) FROM dbo.SugerenciasProductosMostradas WHERE Texto LIKE N'[[]DescartadoVendedor] %';   -- 0
