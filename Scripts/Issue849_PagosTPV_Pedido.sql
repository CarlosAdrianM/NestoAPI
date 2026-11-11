-- NestoAPI#849 (corte 1): guardar el pedido en el enlace de pago (PagosTPV.EmpresaPedido / PagosTPV.Pedido).
-- Lanzar como sa en NV ANTES de publicar la API (la API funciona también sin las columnas: entonces saca el pedido del concepto).
-- Idempotente: se puede lanzar las veces que haga falta. Sin GRANT: PagosTPV ya los tiene (RDS2016$ SELECT/INSERT/UPDATE).

SET NOCOUNT ON;
GO

-- 1. Columnas (cada una en su lote)
IF COL_LENGTH(N'dbo.PagosTPV', N'EmpresaPedido') IS NULL
    ALTER TABLE dbo.PagosTPV ADD EmpresaPedido nvarchar(2) NULL;
GO
IF COL_LENGTH(N'dbo.PagosTPV', N'Pedido') IS NULL
    ALTER TABLE dbo.PagosTPV ADD Pedido int NULL;
GO

-- 2. Índice
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PagosTPV') AND name = N'IX_PagosTPV_EmpresaPedido_Pedido')
    CREATE NONCLUSTERED INDEX IX_PagosTPV_EmpresaPedido_Pedido ON dbo.PagosTPV (EmpresaPedido, Pedido) WHERE Pedido IS NOT NULL;
GO

-- 3. Relleno hacia atrás: enlaces TPVVirtual de los últimos 12 meses sin pedido guardado cuyo concepto identifica (con la MISMA
--    regla que EnlacesPagoPedido.PedidoDelConcepto, de forma conservadora) un pedido existente del MISMO cliente:
--    a) el concepto entero es un número de 6 a 9 cifras («929559»);
--    b) la primera «pedido» va seguida (con espacios y «nº», «n» o «º» opcionales) de 5 a 9 cifras que no siguen con letra o cifra
--       («Pago pedido 928521 de Nueva Visión», «Pedido 928521»).
--    Son unos 900 enlaces en total (11/11/26): un solo UPDATE, en una transacción.
BEGIN TRANSACTION;

WITH Base AS (
    SELECT p.Id, p.Empresa, p.Cliente, LTRIM(RTRIM(ISNULL(p.Descripcion, N''))) AS D
    FROM dbo.PagosTPV p
    WHERE p.Tipo = N'TPVVirtual' AND p.FechaCreacion >= DATEADD(month, -12, GETDATE()) AND p.Pedido IS NULL
), Paso1 AS (
    SELECT b.*, PATINDEX(N'%pedido%', b.D) AS Pos FROM Base b
), Paso2 AS (
    SELECT p1.*, CASE WHEN p1.Pos > 0 THEN LTRIM(SUBSTRING(p1.D, p1.Pos + 6, 1000)) ELSE N'' END AS R0 FROM Paso1 p1
), Paso3 AS (
    SELECT p2.*, LTRIM(CASE WHEN p2.R0 LIKE N'nº%' THEN SUBSTRING(p2.R0, 3, 1000)
                            WHEN p2.R0 LIKE N'n[0-9 ]%' OR p2.R0 LIKE N'º%' THEN SUBSTRING(p2.R0, 2, 1000)
                            ELSE p2.R0 END) AS R1 FROM Paso2 p2
), Paso4 AS (
    SELECT p3.*, LEFT(p3.R1, PATINDEX(N'%[^0-9]%', p3.R1 + N'x') - 1) AS Digitos FROM Paso3 p3
), Candidato AS (
    SELECT p4.Id, p4.Empresa, p4.Cliente,
        -- TRY_CAST y no CAST: con la intercalación de la BD, [0-9] casa también con cifras «raras» (el 11/11/26 falló con un
        -- concepto que llevaba un cero en superíndice, «⁰915489»). Lo que no sea un entero de verdad se queda sin rellenar.
        CASE WHEN LEN(p4.D) BETWEEN 6 AND 9 AND p4.D NOT LIKE N'%[^0-9]%' THEN TRY_CAST(p4.D AS int)
             WHEN LEN(p4.Digitos) BETWEEN 5 AND 9 AND SUBSTRING(p4.R1, LEN(p4.Digitos) + 1, 1) NOT LIKE N'[A-Za-z0-9_]' THEN TRY_CAST(p4.Digitos AS int)
        END AS Pedido
    FROM Paso4 p4
)
UPDATE p
SET p.EmpresaPedido = RTRIM(c.Empresa), p.Pedido = c.Pedido
FROM dbo.PagosTPV p
INNER JOIN Candidato c ON c.Id = p.Id
INNER JOIN dbo.CabPedidoVta cab ON cab.Empresa = c.Empresa AND cab.[Número] = c.Pedido AND cab.[Nº Cliente] = c.Cliente
WHERE c.Pedido IS NOT NULL AND p.Pedido IS NULL;

PRINT CONCAT(N'Enlaces con el pedido rellenado: ', @@ROWCOUNT);
COMMIT TRANSACTION;
GO

-- Comprobación
SELECT COUNT(*) AS EnlacesTPVVirtual, SUM(CASE WHEN Pedido IS NOT NULL THEN 1 ELSE 0 END) AS ConPedido
FROM dbo.PagosTPV WHERE Tipo = N'TPVVirtual';
GO
