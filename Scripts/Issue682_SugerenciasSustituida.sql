/*
    NestoAPI#682: cuarta respuesta «Sustituida». Al cambiar el carrito, si sale una sugerencia claramente mejor que alguna de
    las visibles SIN contestar, la API la pone en su hueco (como mucho una por llamada) y la que sale se marca «Sustituida».
    No es una respuesta del vendedor ni del cliente: no cuenta como ofrecida, ni como positivo ni como negativo para ningún
    modelo, ni excluye 60 días. Solo sirve para no volver a enseñarla en esa sesión y para saber cuántas se cambiaron.

    OPCIONAL. Ejecutar en SSMS contra NV como sa (ALTER del CK: el login nuevavision no puede). Idempotente.
    Requiere Issue670_SugerenciasProductos.sql. Se puede lanzar antes o después del deploy (o no lanzarlo):
    - Sin este script, la API mira el CK (sys.check_constraints) y, si no admite «Sustituida», deja la Respuesta en NULL (como
      una no tocada, que tampoco cuenta) y pone la marca «[Sustituida] » delante del Texto para no volver a enseñarla.
    - Este script amplía el CK (manteniendo «DescartadoVendedor», del script Issue670b) y pasa esas filas provisionales a
      «Sustituida» (quitando la marca del Texto). Después, la API guarda «Sustituida» directamente.

    Respuesta es varchar(20): «Sustituida» tiene 10 caracteres.
*/

SET NOCOUNT ON;
USE NV;
GO

-- 1) El CK de Respuesta con «Sustituida» (se rehace solo si todavía no lo admite)
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_SugerenciasProductosMostradas_Respuesta'
                 AND parent_object_id = OBJECT_ID('dbo.SugerenciasProductosMostradas')
                 AND definition LIKE '%Sustituida%')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_SugerenciasProductosMostradas_Respuesta'
                 AND parent_object_id = OBJECT_ID('dbo.SugerenciasProductosMostradas'))
        ALTER TABLE dbo.SugerenciasProductosMostradas DROP CONSTRAINT CK_SugerenciasProductosMostradas_Respuesta;
    ALTER TABLE dbo.SugerenciasProductosMostradas ADD CONSTRAINT CK_SugerenciasProductosMostradas_Respuesta
        CHECK (Respuesta IS NULL OR Respuesta IN ('Anadido', 'NoLeInteresa', 'DescartadoVendedor', 'Sustituida'));
END
GO

-- 2) Las sustituidas antes de este script (Respuesta NULL con la marca): a «Sustituida», sin la marca
--    («[Sustituida] » son 13 caracteres: el texto original empieza en el 14). FechaRespuesta: no se sabe; la de mostrada.
UPDATE dbo.SugerenciasProductosMostradas
SET Respuesta = 'Sustituida',
    FechaRespuesta = FechaMostrada,
    Texto = NULLIF(SUBSTRING(Texto, 14, 200), N'')
WHERE Respuesta IS NULL
  AND Texto LIKE N'[[]Sustituida] %';
PRINT CONCAT('Filas provisionales pasadas a Sustituida: ', @@ROWCOUNT);
GO

-- Comprobación:
-- SELECT definition FROM sys.check_constraints WHERE name = 'CK_SugerenciasProductosMostradas_Respuesta';
-- SELECT Respuesta, COUNT(*) FROM dbo.SugerenciasProductosMostradas GROUP BY Respuesta;
-- SELECT COUNT(*) FROM dbo.SugerenciasProductosMostradas WHERE Texto LIKE N'[[]Sustituida] %';   -- 0
