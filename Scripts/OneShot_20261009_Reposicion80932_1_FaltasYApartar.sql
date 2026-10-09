/*
    09/10/26: la reposición 80932 (Algete → Reina, 46 productos, 96 uds) la rellenó la API y se prepara con el papel de
    Nesto viejo porque Ariadna aún no valida el código al preparar. Para contabilizarla desde Nesto viejo sin llevarse
    la 80938 (Algete → Alcobendas, 53 líneas, sin preparar), que comparte el diario «General», este script hace lo
    mismo que Ariadna al terminar la recogida (SalidasAlmacen / EscriturasSalida / ApartadoTraspasosSql):

      1) FALTAS: por cada producto de @Faltas, quita esas unidades de la salida de ALG (General), de la entrada de REI
         (PendRepo) y del hueco reservado (Ubicaciones estado -4 con NºTraspasoRepo = 80932), y las deja en ALG
         «pendiente de ubicar» (Ubicaciones estado 2), como QuitarDeLaReposicion.
      2) APARTA en el diario «RepoEscond» todas las líneas de «General» que no son de la 80932 (como hacíais a mano) y
         guarda sus números en dbo.Apartadas_Repo80932 para devolverlas después.

    DESPUÉS: (a) COMMIT de este script; (b) en Nesto viejo, contabilizar el diario «General» (solo queda la 80932);
             (c) lanzar OneShot_20261009_Reposicion80932_2_Devolver.sql. Mejor seguido, sin que nadie meta líneas en
             «General» entre medias (se contabilizarían con la 80932).
    Lanzar en SSMS contra NV como sa. Si los recuentos no cuadran: ROLLBACK.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

DECLARE @Traspaso int = 80932;

-- Rellenar con lo que ha faltado al prepararla (producto y unidades que NO salen). Vacía = salió todo.
DECLARE @Faltas TABLE (Producto varchar(15) PRIMARY KEY, Falta int NOT NULL CHECK (Falta > 0));
-- INSERT INTO @Faltas (Producto, Falta) VALUES ('12345', 1), ('67890', 2);

-- 0) Comprobaciones
IF OBJECT_ID('dbo.Apartadas_Repo80932') IS NOT NULL
BEGIN RAISERROR('Ya existe dbo.Apartadas_Repo80932: este script ya se lanzó. Lanza el 2 (Devolver) o revisa.', 16, 1); RETURN; END
IF EXISTS (SELECT 1 FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'RepoEscond')
BEGIN RAISERROR('El diario RepoEscond no está vacío: revisa antes de apartar nada.', 16, 1); RETURN; END

SELECT f.Producto, f.Falta, -s.Cantidad AS SeMandaban,
       CASE WHEN s.[Nº Orden] IS NULL THEN 'NO ESTÁ EN LA 80932'
            WHEN f.Falta > -s.Cantidad THEN 'FALTA MAYOR QUE LO QUE SE MANDABA' ELSE 'ok' END AS Revision
FROM @Faltas f
LEFT JOIN PreExtrProducto s ON s.Empresa = '1' AND s.NºTraspaso = @Traspaso AND s.Diario = 'General' AND s.Cantidad < 0
                           AND RTRIM(s.[Número]) = f.Producto;
IF EXISTS (SELECT 1 FROM @Faltas f LEFT JOIN PreExtrProducto s ON s.Empresa = '1' AND s.NºTraspaso = @Traspaso AND s.Diario = 'General'
                    AND s.Cantidad < 0 AND RTRIM(s.[Número]) = f.Producto WHERE s.[Nº Orden] IS NULL OR f.Falta > -s.Cantidad)
BEGIN RAISERROR('Alguna falta no cuadra (ver la tabla de arriba). No se ha hecho nada.', 16, 1); RETURN; END

BEGIN TRANSACTION;

-- 1) Faltas (la salida va en negativo y se acerca a cero; la entrada y el hueco bajan lo mismo)
INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Estado, Usuario)
SELECT u.Empresa, u.[Almacén], u.[Número], f.Falta, 2, LEFT(SYSTEM_USER, 30)
FROM Ubicaciones u INNER JOIN @Faltas f ON RTRIM(u.[Número]) = f.Producto
WHERE u.NºTraspasoRepo = @Traspaso AND u.[Almacén] = 'ALG' AND u.Estado = -4;

UPDATE u SET Cantidad = u.Cantidad - SIGN(u.Cantidad) * f.Falta
FROM Ubicaciones u INNER JOIN @Faltas f ON RTRIM(u.[Número]) = f.Producto
WHERE u.NºTraspasoRepo = @Traspaso AND u.[Almacén] = 'ALG' AND u.Estado = -4;
DELETE Ubicaciones WHERE NºTraspasoRepo = @Traspaso AND [Almacén] = 'ALG' AND Estado = -4 AND Cantidad = 0;

UPDATE s SET Cantidad = s.Cantidad + f.Falta
FROM PreExtrProducto s INNER JOIN @Faltas f ON RTRIM(s.[Número]) = f.Producto
WHERE s.Empresa = '1' AND s.NºTraspaso = @Traspaso AND s.Diario = 'General' AND s.Cantidad < 0;
DELETE PreExtrProducto WHERE Empresa = '1' AND NºTraspaso = @Traspaso AND Diario = 'General' AND Cantidad = 0;

UPDATE e SET Cantidad = e.Cantidad - f.Falta
FROM PreExtrProducto e INNER JOIN @Faltas f ON RTRIM(e.[Número]) = f.Producto
WHERE e.Empresa = '1' AND e.NºTraspaso = @Traspaso AND e.Diario = 'PendRepo' AND e.Cantidad > 0;
DELETE PreExtrProducto WHERE Empresa = '1' AND NºTraspaso = @Traspaso AND Diario = 'PendRepo' AND Cantidad = 0;

-- 2) Apartar lo demás de «General»
CREATE TABLE dbo.Apartadas_Repo80932 ([Nº Orden] int PRIMARY KEY);
UPDATE PreExtrProducto SET Diario = 'RepoEscond'
OUTPUT inserted.[Nº Orden] INTO dbo.Apartadas_Repo80932 ([Nº Orden])
WHERE Empresa = '1' AND Diario = 'General' AND ISNULL(NºTraspaso, 0) <> @Traspaso;

-- Comprobación: en General solo queda la 80932, y su salida cuadra con su entrada y con los huecos
SELECT 'General' AS Que, COUNT(*) Lineas, SUM(Cantidad) Uds, COUNT(DISTINCT ISNULL(NºTraspaso, 0)) Traspasos FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'General'
UNION ALL SELECT 'Entrada REI 80932', COUNT(*), SUM(Cantidad), 1 FROM PreExtrProducto WHERE Empresa = '1' AND NºTraspaso = @Traspaso AND Diario = 'PendRepo'
UNION ALL SELECT 'Huecos 80932', COUNT(*), SUM(Cantidad), 1 FROM Ubicaciones WHERE NºTraspasoRepo = @Traspaso AND Estado = -4
UNION ALL SELECT 'Apartadas en RepoEscond', COUNT(*), NULL, NULL FROM dbo.Apartadas_Repo80932;
-- Debe salir: General con 1 traspaso y Uds = -(Entrada REI); Huecos con la misma suma que General; Apartadas = 53 (las de la
-- 80938) más las que hubiera de otros movimientos de General.

-- Si cuadra: COMMIT. Si no: ROLLBACK.
-- COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;
