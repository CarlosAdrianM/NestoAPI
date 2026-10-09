/*
    09/10/26: DESPUÉS de contabilizar en Nesto viejo el diario «General» con solo la 80932 (ver
    OneShot_20261009_Reposicion80932_1_FaltasYApartar.sql): devuelve a «General» las líneas apartadas en «RepoEscond»
    (la 80938 de Algete → Alcobendas y lo que hubiera), apunta en ReposicionesTraspasos que la 80932 está preparada y
    borra la tabla auxiliar. Como DevolverApartadas de Ariadna. SSMS contra NV como sa; COMMIT a mano si cuadra.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

DECLARE @Traspaso int = 80932;

IF OBJECT_ID('dbo.Apartadas_Repo80932') IS NULL
BEGIN RAISERROR('No existe dbo.Apartadas_Repo80932: no hay nada apartado (¿no se lanzó el script 1?).', 16, 1); RETURN; END

-- La salida de la 80932 ya tiene que estar contabilizada (fuera de PreExtrProducto)
SELECT 'Salida 80932 aún en General (debe ser 0)' AS Que, COUNT(*) N FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General' AND NºTraspaso = @Traspaso
UNION ALL SELECT 'Salida 80932 ya en ExtractoProducto', COUNT(*) FROM ExtractoProducto WITH (NOLOCK) WHERE Empresa = '1' AND Diario = 'General' AND NºTraspaso = @Traspaso;
IF EXISTS (SELECT 1 FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General' AND NºTraspaso = @Traspaso)
BEGIN RAISERROR('La salida de la 80932 sigue en PreExtrProducto: contabiliza antes el diario General en Nesto viejo. No se ha hecho nada.', 16, 1); RETURN; END

DECLARE @Apartadas int = (SELECT COUNT(*) FROM dbo.Apartadas_Repo80932);

BEGIN TRANSACTION;

UPDATE p SET Diario = 'General'
FROM PreExtrProducto p INNER JOIN dbo.Apartadas_Repo80932 a ON a.[Nº Orden] = p.[Nº Orden]
WHERE p.Empresa = '1' AND p.Diario = 'RepoEscond';
DECLARE @Devueltas int = @@ROWCOUNT;

IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
    UPDATE dbo.ReposicionesTraspasos SET UsuarioPreparacion = LEFT(SYSTEM_USER, 30), FechaPreparada = GETDATE()
    WHERE Empresa = '1' AND NumTraspaso = @Traspaso AND FechaPreparada IS NULL;

SELECT @Apartadas Apartadas, @Devueltas Devueltas,
       (SELECT COUNT(*) FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'RepoEscond') QuedanEnRepoEscond,
       (SELECT COUNT(*) FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General' AND NºTraspaso = 80938) LineasDeLa80938EnGeneral;
-- Debe salir: Devueltas = Apartadas, QuedanEnRepoEscond = 0 y las 53 líneas de la 80938 otra vez en General.

DROP TABLE dbo.Apartadas_Repo80932;

-- Si cuadra: COMMIT. Si no: ROLLBACK (la tabla auxiliar vuelve).
-- COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;
