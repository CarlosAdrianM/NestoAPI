-- OneShot 05/11/26: el CP 48430 no existe (Paloma lo creó desde Nesto viejo el 05/11/26 al cambiar la dirección
-- del 27593/91; el de Ortuella es el 48530, que ya está en la tabla de la empresa 1). Se corrige la ficha y se
-- borra la fila inventada. Ejecutar como sa en NV, DESPUÉS de OneShot_20261105_CodigosPostales_SoloPorLaApi.sql
-- o antes, da igual. Idempotente.
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRAN;

-- Debe devolver solo 27593/91 (comprobado 05/11/26 09:50)
SELECT Empresa, [Nº Cliente], Contacto, Nombre, CodPostal, Población FROM dbo.Clientes WHERE CodPostal = '48430';

UPDATE dbo.Clientes
SET CodPostal = '48530'
WHERE Empresa = '1' AND [Nº Cliente] = '27593' AND Contacto = '91' AND CodPostal = '48430';
PRINT CONCAT('Clientes corregidos: ', @@ROWCOUNT);

IF NOT EXISTS (SELECT 1 FROM dbo.Clientes WHERE CodPostal = '48430')
   AND NOT EXISTS (SELECT 1 FROM dbo.ExtractoRuta WHERE CodPostal = '48430')
BEGIN
    DELETE dbo.[CódigosPostales] WHERE Número = '48430';
    PRINT CONCAT('CPs 48430 borrados: ', @@ROWCOUNT);
END
ELSE
    PRINT 'El 48430 sigue en uso: no se borra.';

COMMIT;
GO
