-- NestoAPI#331: saneo de ClientePrincipal (18/08/26)
-- Estado medido en producción: 74 clientes activos sin ningún principal (62 con un solo
-- contacto activo + 12 multi-contacto donde TODOS los contactos comparten NIF) y 3 con
-- varios principales (40293 duplicado inocente; 41094 y 41639 = entidades distintas o
-- dudosas, se dejan para decisión manual de administración).
--
-- Regla del saneo automático (exacta, no toca nada dudoso):
--   * Cliente sin principal: se marca como principal el contacto ACTIVO de MENOR número,
--     SOLO si todos sus contactos activos comparten el mismo NIF (o solo hay uno).
--   * 40293: mismo nombre y NIF (NULL) en ambos contactos -> se deja el 0 como único principal.
--   * 41094 y 41639: NO se tocan; salen en el listado final para administración.

SET NOCOUNT ON;
BEGIN TRAN;

------------------------------------------------------------------------------------------
-- 1. Sin principal: marcar el contacto activo de menor número (solo si NIF único)
------------------------------------------------------------------------------------------
WITH sinPrincipal AS (
    SELECT [Nº Cliente] AS Cliente
    FROM Clientes
    WHERE Empresa = '1' AND Estado >= 0
    GROUP BY [Nº Cliente]
    HAVING SUM(CASE WHEN ClientePrincipal = 1 THEN 1 ELSE 0 END) = 0
       AND COUNT(DISTINCT LTRIM(RTRIM(ISNULL([CIF/NIF], '')))) = 1  -- mismo NIF en todos (o único contacto)
),
elegido AS (
    SELECT c.Empresa, c.[Nº Cliente] AS Cliente, MIN(c.Contacto) AS Contacto
    FROM Clientes c
    INNER JOIN sinPrincipal s ON s.Cliente = c.[Nº Cliente]
    WHERE c.Empresa = '1' AND c.Estado >= 0
    GROUP BY c.Empresa, c.[Nº Cliente]
)
UPDATE c
SET c.ClientePrincipal = 1
FROM Clientes c
INNER JOIN elegido e
    ON c.Empresa = e.Empresa AND c.[Nº Cliente] = e.Cliente AND c.Contacto = e.Contacto;

PRINT CONCAT('Clientes sin principal corregidos (principal = contacto activo menor): ', @@ROWCOUNT);

------------------------------------------------------------------------------------------
-- 2. 40293: dos principales con el mismo nombre y sin NIF -> se queda el contacto 0
------------------------------------------------------------------------------------------
UPDATE Clientes
SET ClientePrincipal = 0
WHERE Empresa = '1' AND [Nº Cliente] = '40293' AND Contacto <> '0'
  AND ClientePrincipal = 1
  AND EXISTS (SELECT 1 FROM Clientes p WHERE p.Empresa = '1' AND p.[Nº Cliente] = '40293'
              AND p.Contacto = '0' AND p.ClientePrincipal = 1);

PRINT CONCAT('40293 - principales de sobra desmarcados: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------
-- 3. Verificación: lo que quede aquí es tarea MANUAL de administración (esperado: 41094 y 41639)
------------------------------------------------------------------------------------------
SELECT 'VARIOS PRINCIPALES (manual)' AS Caso, RTRIM([Nº Cliente]) AS Cliente, RTRIM(Contacto) AS Contacto,
       RTRIM([CIF/NIF]) AS NIF, LEFT(RTRIM(Nombre), 40) AS Nombre
FROM Clientes c
WHERE Empresa = '1' AND Estado >= 0 AND ClientePrincipal = 1
  AND [Nº Cliente] IN (SELECT [Nº Cliente] FROM Clientes WHERE Empresa = '1' AND Estado >= 0 AND ClientePrincipal = 1
                       GROUP BY [Nº Cliente] HAVING COUNT(*) > 1)
ORDER BY [Nº Cliente], Contacto;

SELECT 'SIN PRINCIPAL (manual, NIF distinto entre contactos)' AS Caso, RTRIM(c.[Nº Cliente]) AS Cliente,
       RTRIM(c.Contacto) AS Contacto, RTRIM(c.[CIF/NIF]) AS NIF, LEFT(RTRIM(c.Nombre), 40) AS Nombre
FROM Clientes c
WHERE c.Empresa = '1' AND c.Estado >= 0
  AND c.[Nº Cliente] IN (SELECT [Nº Cliente] FROM Clientes WHERE Empresa = '1' AND Estado >= 0
                         GROUP BY [Nº Cliente]
                         HAVING SUM(CASE WHEN ClientePrincipal = 1 THEN 1 ELSE 0 END) = 0)
ORDER BY c.[Nº Cliente], c.Contacto;

COMMIT;
PRINT 'Saneo #331 aplicado.';
