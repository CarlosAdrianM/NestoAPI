/*
    09/10/26 (Carlos): las reposiciones 80932 (Algete → Reina, 46 líneas, 96 uds) y 80938 (Algete → Alcobendas, 53 líneas,
    99 uds) las rellenó la API y, para Algete, la API las CIERRA al crearlas (reserva los huecos para Ariadna): en «General»
    quedan juntas las dos salidas de ALG en negativo (el listado de Nesto viejo sale mezclado y en negativo) y las tiendas ya
    las ven para recibir. Este script las deja EXACTAMENTE como las deja Nesto viejo al rellenar (lo contrario de
    CerrarPreparacion de la API), para imprimir, modificar y contabilizar en Nesto viejo como siempre, una detrás de otra:

      - REINA (80932) en preparación en «General»: líneas con Almacén = REI, en positivo, SIN número de traspaso, Estado 3.
      - ALCOBENDAS (80938) igual de preparada pero APARTADA en «RepoEscond» (como hacíais a mano), para que no salga en el
        listado ni estorbe al contabilizar la de Reina. El script 2 la pasa a «General» cuando Reina esté contabilizada.

    Para cada traspaso: (1) los huecos pasan de salida (estado -4, negativo, NºTraspasoRepo) a reserva (estado 4, positivo,
    sin traspaso; NºOrdenRepo sigue apuntando a su línea); (2) se borran las salidas de ALG (negativas); (3) las líneas de la
    tienda vuelven al diario de preparación sin traspaso; (4) PedidosEspeciales sueltan el traspaso. Nesto viejo les pondrá
    un número nuevo al contabilizar (80932 y 80938 no se reutilizan).

    ANTES: OneShot_20261009_RepoAlgete_0_DesactivarAutomaticasDesdeAlgete.sql (que el job no cree otra mientras tanto).
    DESPUÉS: en Nesto viejo imprimir, preparar, corregir faltas y contabilizar REINA; luego el script 2; luego lo mismo con
    ALCOBENDAS. SSMS contra NV como sa. COMMIT a mano si los recuentos cuadran; si no, ROLLBACK.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

-- 0) Comprobaciones: nada contabilizado, General solo con estas dos, RepoEscond vacío
IF EXISTS (SELECT 1 FROM ExtractoProducto WITH (NOLOCK) WHERE Empresa = '1' AND NºTraspaso IN (80932, 80938))
BEGIN RAISERROR('La 80932 o la 80938 ya tienen algo contabilizado en ExtractoProducto: no se pueden reabrir. No se ha hecho nada.', 16, 1); RETURN; END
IF EXISTS (SELECT 1 FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General' AND ISNULL(NºTraspaso, 0) NOT IN (80932, 80938))
BEGIN RAISERROR('En General hay líneas que no son de la 80932 ni de la 80938: revisa antes. No se ha hecho nada.', 16, 1); RETURN; END
IF EXISTS (SELECT 1 FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'RepoEscond')
BEGIN RAISERROR('El diario RepoEscond no está vacío: revisa antes. No se ha hecho nada.', 16, 1); RETURN; END

SELECT 'Antes' AS Momento, NºTraspaso, RTRIM(Diario) Diario, RTRIM([Almacén]) Alm, COUNT(*) Lineas, SUM(Cantidad) Uds
FROM PreExtrProducto WHERE Empresa = '1' AND NºTraspaso IN (80932, 80938) GROUP BY NºTraspaso, Diario, [Almacén]
UNION ALL
SELECT 'Antes', NºTraspasoRepo, 'Huecos', RTRIM([Almacén]), COUNT(*), SUM(Cantidad) FROM Ubicaciones WHERE NºTraspasoRepo IN (80932, 80938) AND Estado = -4 GROUP BY NºTraspasoRepo, [Almacén]
ORDER BY 2, 3;
-- Esperado: 80932 General/ALG 46/-96, PendRepo/REI 46/96, Huecos 46/-96; 80938 General/ALG 53/-99, RepoAlgAlc/ALC 53/99, Huecos 53/-99

BEGIN TRANSACTION;

-- 1) Huecos: de salida a reserva
UPDATE Ubicaciones SET Cantidad = -Cantidad, Estado = 4, NºTraspasoRepo = NULL, Usuario = LEFT(SYSTEM_USER, 30)
WHERE NºTraspasoRepo IN (80932, 80938) AND Estado = -4 AND [Almacén] = 'ALG';
DECLARE @Huecos int = @@ROWCOUNT;

-- 2) Fuera las salidas de ALG (negativas)
DELETE PreExtrProducto WHERE Empresa = '1' AND NºTraspaso IN (80932, 80938) AND Diario = 'General' AND [Almacén] = 'ALG' AND Cantidad < 0;
DECLARE @Salidas int = @@ROWCOUNT;

-- 3a) Reina: a «General», en preparación
UPDATE PreExtrProducto SET Diario = 'General', NºTraspaso = NULL, Estado = 3
WHERE Empresa = '1' AND NºTraspaso = 80932 AND Diario = 'PendRepo' AND [Almacén] = 'REI' AND Cantidad > 0;
DECLARE @Reina int = @@ROWCOUNT;

-- 3b) Alcobendas: preparada igual, pero apartada en «RepoEscond»
UPDATE PreExtrProducto SET Diario = 'RepoEscond', NºTraspaso = NULL, Estado = 3
WHERE Empresa = '1' AND NºTraspaso = 80938 AND Diario = 'RepoAlgAlc' AND [Almacén] = 'ALC' AND Cantidad > 0;
DECLARE @Alcobendas int = @@ROWCOUNT;

-- 4) Pedidos especiales
UPDATE PedidosEspeciales SET NºTraspaso = NULL WHERE NºTraspaso IN (80932, 80938);

-- Comprobación
SELECT @Huecos HuecosAReserva, @Salidas SalidasBorradas, @Reina LineasReinaEnGeneral, @Alcobendas LineasAlcobendasEnRepoEscond,
       (SELECT COUNT(*) FROM PreExtrProducto WHERE Empresa = '1' AND NºTraspaso IN (80932, 80938)) QuedanConNumero,
       (SELECT SUM(Cantidad) FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General') UdsEnGeneral,
       (SELECT COUNT(DISTINCT [Almacén]) FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General') AlmacenesEnGeneral,
       (SELECT SUM(Cantidad) FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'RepoEscond') UdsEnRepoEscond;
-- Debe salir: 99 / 99 / 46 / 53 / 0 / 96 / 1 (solo REI) / 99.

-- Si cuadra: COMMIT. Si no: ROLLBACK.
-- COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;
