/*
    NestoAPI#788 (épica #784, 03/11/26): Inventarios.PorCodigoBarras — si la lectura casó por un código de barras (1) o por la
    referencia tecleada a mano (0). NULL en las filas de antes y en las que no grabe la API (Ariadna vieja, scripts).

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.

    OJO: la columna está en el EDMX. Lanzar ANTES de publicar la API: sin ella, todo lo que lea Inventarios por EF
    (api/Inventarios en Nesto) falla con «Invalid column name 'PorCodigoBarras'».
    Comprobado en NV (03/11/26): ningún SP ni trigger hace INSERT INTO Inventarios sin lista de columnas, así que la columna nueva
    (que admite NULL y no tiene DEFAULT) no rompe prdContabilizarInventario, prdDeshacerInventario ni los demás.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.Inventarios', 'PorCodigoBarras') IS NULL
BEGIN
    ALTER TABLE dbo.Inventarios ADD PorCodigoBarras bit NULL;
    PRINT 'Inventarios: columna PorCodigoBarras añadida.';
END
ELSE
    PRINT 'Inventarios: la columna PorCodigoBarras ya existía.';
GO
