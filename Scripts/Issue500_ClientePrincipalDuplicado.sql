-- NestoAPI#500: 79 clientes de la empresa 1 tienen DOS fichas con ClientePrincipal = 1.
-- Eso revienta todos los Single/SingleOrDefault(ClientePrincipal) del servidor con un 500
-- "La secuencia contiene mas de un elemento": plantilla de venta, razon social de la factura,
-- formas de pago, plazos de pago, extracto de cliente y pedidos del cliente.
-- Caso real: 19/09/2026, cliente 15296, GET api/PlantillaVentas.
--
-- CRITERIO (decidido con los datos, ver la issue):
--   1. Si solo UNA de las dos fichas principales esta activa (Estado >= 0), esa se queda de
--      principal y la otra pasa a ClientePrincipal = 0. Son 75 de los 79.
--   2. Si NINGUNA esta activa, se queda la del contacto mas bajo (que es la que cogeria el
--      servidor si solo hubiera una). Son 2: los clientes 38239 y 38492.
--   3. Si las DOS estan activas, el script NO LAS TOCA: son dos entidades fiscales distintas
--      metidas bajo el mismo numero de cliente por la carrera de altas del #263 (41094 =
--      SENDING TRANSPORTE / CENTRO DE ESTETICA PILAR, 41639 = MARY'S LIVING BEAUTY /
--      ACHOTEGUI NANCY IVONNE). Hay que SEPARARLAS moviendo una a un numero nuevo, y eso
--      es una decision de negocio, no un UPDATE.
--
-- OJO: trgClientesUpd mete una fila en Nesto_sync por cada cliente cuyo ClientePrincipal
-- cambia, asi que los 77 se vuelven a publicar a Odoo/Prestashop en la siguiente pasada de
-- sincronizar-clientes. Son pocos, pero mejor ejecutarlo fuera de horario punta.
--
-- El script es idempotente: si se vuelve a lanzar, no encuentra nada que cambiar.

SET NOCOUNT ON;
USE NV;

-- ---------------------------------------------------------------------------------------
-- 1. Fotografia: las fichas principales de los clientes que tienen mas de una
-- ---------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#Principales') IS NOT NULL DROP TABLE #Principales;

SELECT c.Empresa,
       c.[Nº Cliente] AS Cliente,
       c.Contacto,
       c.Estado,
       c.Nombre,
       Activos = SUM(CASE WHEN c.Estado >= 0 THEN 1 ELSE 0 END)
                 OVER (PARTITION BY c.Empresa, c.[Nº Cliente]),
       Orden   = ROW_NUMBER()
                 OVER (PARTITION BY c.Empresa, c.[Nº Cliente]
                       ORDER BY CASE WHEN c.Estado >= 0 THEN 0 ELSE 1 END, c.Contacto)
INTO #Principales
FROM Clientes c
INNER JOIN (
    SELECT Empresa, [Nº Cliente] AS Cliente
    FROM Clientes
    WHERE ClientePrincipal = 1
    GROUP BY Empresa, [Nº Cliente]
    HAVING COUNT(*) > 1
) d ON d.Empresa = c.Empresa AND d.Cliente = c.[Nº Cliente]
WHERE c.ClientePrincipal = 1;

PRINT '--- Clientes con mas de una ficha principal (antes) ---';
SELECT Clientes = COUNT(DISTINCT Cliente) FROM #Principales;

PRINT '--- Los que NO se tocan (dos fichas activas = dos entidades distintas) ---';
SELECT Cliente, Contacto, Estado, Nombre
FROM #Principales
WHERE Activos > 1
ORDER BY Cliente, Contacto;

PRINT '--- Lo que se va a cambiar a ClientePrincipal = 0 ---';
SELECT Cliente, Contacto, Estado, Nombre
FROM #Principales
WHERE Activos <= 1 AND Orden > 1
ORDER BY Cliente, Contacto;

PRINT '--- Lo que se queda de principal ---';
SELECT Cliente, Contacto, Estado, Nombre
FROM #Principales
WHERE Activos <= 1 AND Orden = 1
ORDER BY Cliente, Contacto;

-- ---------------------------------------------------------------------------------------
-- 2. La correccion
-- ---------------------------------------------------------------------------------------
DECLARE @Filas int;

BEGIN TRANSACTION;

UPDATE c
SET c.ClientePrincipal = 0
FROM Clientes c
INNER JOIN #Principales p
        ON p.Empresa  = c.Empresa
       AND p.Cliente  = c.[Nº Cliente]
       AND p.Contacto = c.Contacto
WHERE p.Activos <= 1
  AND p.Orden > 1
  AND c.ClientePrincipal = 1;

SET @Filas = @@ROWCOUNT;

-- Red de seguridad: ningun cliente de los tocados puede quedarse SIN principal ni con dos.
IF EXISTS (
    SELECT 1
    FROM Clientes c
    INNER JOIN (SELECT DISTINCT Empresa, Cliente FROM #Principales WHERE Activos <= 1) t
            ON t.Empresa = c.Empresa AND t.Cliente = c.[Nº Cliente]
    GROUP BY c.Empresa, c.[Nº Cliente]
    HAVING SUM(CASE WHEN c.ClientePrincipal = 1 THEN 1 ELSE 0 END) <> 1
)
BEGIN
    PRINT 'ERROR: algun cliente se quedaria sin ficha principal (o con dos). No se cambia nada.';
    ROLLBACK TRANSACTION;
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT '--- Fichas degradadas a contacto normal ---';
    SELECT Filas = @Filas;
    PRINT 'OK: correccion aplicada.';
END

-- ---------------------------------------------------------------------------------------
-- 3. Comprobacion (debe quedar SOLO los 2 casos a separar a mano: 41094 y 41639)
-- ---------------------------------------------------------------------------------------
/*
SELECT c.[Nº Cliente] AS Cliente, c.Contacto, c.Estado, c.Nombre
FROM Clientes c
INNER JOIN (
    SELECT Empresa, [Nº Cliente] AS Cliente
    FROM Clientes
    WHERE ClientePrincipal = 1
    GROUP BY Empresa, [Nº Cliente]
    HAVING COUNT(*) > 1
) d ON d.Empresa = c.Empresa AND d.Cliente = c.[Nº Cliente]
WHERE c.ClientePrincipal = 1
ORDER BY Cliente, Contacto;

-- Y que la plantilla del 15296 vuelve a cargar:
--   GET api/PlantillaVentas?empresa=1&cliente=15296
*/
