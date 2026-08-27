-- NestoAPI#406: la regla "esta familia se vende al público al MISMO precio que al profesional"
-- deja de vivir en un script de un día concreto y pasa a ser un DATO en la ficha de la familia.
--
-- POR QUÉ AQUÍ Y NO EN UNA TABLA NUEVA: es una propiedad de la familia, no una relación. Son 293
-- familias, así que una tabla aparte solo para un booleano sería dar un rodeo. Y estando en
-- Familias, el día que se sume una marca nueva se marca su familia y ya está: nadie tiene que
-- acordarse de tocar un script.
--
-- Lo consume el job nocturno 'sentinel-precio-publico' (SentinelPrecioPublicoJobsService), que
-- pone PVP_IVA_Incluido = -1 a los productos vivos de estas familias que aún no lo tengan.
--
-- Ejecutar en SSMS (sa) contra NV. Idempotente.

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.Familias', 'PublicoIgualQueProfesional') IS NULL
BEGIN
    ALTER TABLE dbo.Familias
        ADD PublicoIgualQueProfesional bit NOT NULL
            CONSTRAINT DF_Familias_PublicoIgualQueProfesional DEFAULT (0);
END
GO

-- Las 5 familias que hoy están en el script Nesto340_MarcarMismoPrecioPublicoProfesional.sql.
-- Correspondencia con los fabricantes de PrestaShop, para poder rastrearlo:
--   Silverfox = Weelko (268), UniónLáser = 72, Fama = Fama Fabre (1), Staleks = 357,
--   DDUUEETT = 348.
--
-- CERAS DEPILATORIAS SIGUE FUERA a propósito: su descuento es del 25 %, no del 30 %, así que el
-- público NO queda igual al profesional (queda un 7,14 % por encima). Marcarla aquí le BAJARÍA el
-- precio a 52 productos. Si algún día se quiere igualar de verdad, es una decisión de negocio.
UPDATE dbo.Familias
SET PublicoIgualQueProfesional = 1,
    Usuario = 'Issue406',
    [Fecha Modificación] = GETDATE()
WHERE Empresa = '1'
  AND RTRIM([Número]) IN ('Silverfox', 'UniónLáser', 'Fama', 'Staleks', 'DDUUEETT')
  AND PublicoIgualQueProfesional = 0;
GO

-- Comprobación: deben salir las 5.
SELECT RTRIM([Número]) AS Familia, RTRIM([Descripción]) AS Descripcion, PublicoIgualQueProfesional
FROM dbo.Familias
WHERE Empresa = '1' AND PublicoIgualQueProfesional = 1
ORDER BY [Número];
GO
