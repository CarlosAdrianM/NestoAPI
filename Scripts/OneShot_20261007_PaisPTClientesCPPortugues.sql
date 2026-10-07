/*
    OneShot 07/10/2026 (tras Issue596_LimpiezaCodigosPostalesPortugal.sql en real): clientes con código postal portugués
    canónico (dddd-ddd) que siguen con Pais = ES. Les afecta a Verifactu (#599: el NIF-IVA se declara con el país de la
    ficha) y a las agencias (país destino). Se pone Pais = 'PT'.
    Ejecutar como sa. @SoloSimular = 1 enseña y deshace; 0 aplica.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @SoloSimular bit = 1;

BEGIN TRAN;

SELECT 'Antes' Momento, Empresa, RTRIM([Nº Cliente]) Cliente, RTRIM(Contacto) Contacto, RTRIM(CodPostal) CP, RTRIM(Población) Poblacion, RTRIM(Pais) Pais, Estado
FROM Clientes
WHERE CodPostal LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9]%' AND RTRIM(ISNULL(Pais, '')) IN ('ES', '')
ORDER BY Empresa, [Nº Cliente], Contacto;

UPDATE Clientes SET Pais = 'PT'
WHERE CodPostal LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9][0-9]%' AND RTRIM(ISNULL(Pais, '')) IN ('ES', '');

SELECT @@ROWCOUNT AS ClientesConPaisPT;

IF @SoloSimular = 1
BEGIN
    ROLLBACK TRAN;
    PRINT 'SIMULACIÓN: deshecho (ROLLBACK). Pon @SoloSimular = 0 para aplicarlo.';
END
ELSE
BEGIN
    COMMIT TRAN;
    PRINT 'Aplicado.';
END
