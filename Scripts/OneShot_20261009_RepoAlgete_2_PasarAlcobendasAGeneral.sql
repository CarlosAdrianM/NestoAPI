/*
    09/10/26: DESPUÉS de contabilizar en Nesto viejo la reposición de Algete a Reina (reabierta con
    OneShot_20261009_RepoAlgete_1_ReabrirParaNestoViejo.sql): pasa la de Alcobendas, apartada en «RepoEscond» y ya en
    preparación, al diario «General» para imprimirla, prepararla y contabilizarla en Nesto viejo como siempre.
    Solo si «General» está vacío (Reina ya contabilizada). SSMS contra NV como sa; COMMIT a mano si cuadra.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

SELECT 'Líneas en General (debe ser 0)' AS Que, COUNT(*) N FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General'
UNION ALL SELECT 'Líneas de Alcobendas en RepoEscond (53, menos lo que se haya tocado)', COUNT(*) FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'RepoEscond' AND [Almacén] = 'ALC'
UNION ALL SELECT 'Otras líneas en RepoEscond (debe ser 0)', COUNT(*) FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'RepoEscond' AND [Almacén] <> 'ALC';
IF EXISTS (SELECT 1 FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General')
BEGIN RAISERROR('Todavía hay líneas en General: termina de contabilizar la de Reina en Nesto viejo. No se ha hecho nada.', 16, 1); RETURN; END
IF EXISTS (SELECT 1 FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'RepoEscond' AND [Almacén] <> 'ALC')
BEGIN RAISERROR('En RepoEscond hay líneas que no son de Alcobendas: revisa antes. No se ha hecho nada.', 16, 1); RETURN; END

-- El traspaso que ha puesto Nesto viejo a la de Reina (para apuntarlo)
SELECT TOP 1 NºTraspaso AS TraspasoDeReinaEnNestoViejo FROM PreExtrProducto WITH (NOLOCK)
WHERE Empresa = '1' AND Diario = 'PendRepo' AND [Almacén] = 'REI' AND NºTraspaso > 80938 ORDER BY NºTraspaso DESC;

BEGIN TRANSACTION;

UPDATE PreExtrProducto SET Diario = 'General'
WHERE Empresa = '1' AND Diario = 'RepoEscond' AND [Almacén] = 'ALC' AND ISNULL(NºTraspaso, 0) = 0;
SELECT @@ROWCOUNT LineasAlcobendasEnGeneral,
       (SELECT SUM(Cantidad) FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General') Uds,
       (SELECT COUNT(*) FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'RepoEscond') QuedanEnRepoEscond;
-- Debe salir: 53 / 99 / 0.

-- Si cuadra: COMMIT. Si no: ROLLBACK.
-- COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;
