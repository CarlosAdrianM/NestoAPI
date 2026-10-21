-- NestoAPI#684: impedir fechas de entrega anteriores al año 2000 en LinPedidoVta.
-- Pedido 928623 (19/10/26): 19/10/1926 en vez de 2026; con 1927 por 2027 se habría servido algo retenido.
-- WITH NOCHECK: las 17 líneas históricas (todas facturadas, 2002-2024) con 1899/1931/1958/1997 se quedan como están.
-- Lanzar como sa en NV. Idempotente.

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = N'CK_LinPedidoVta_FechaEntregaDesde2000' AND parent_object_id = OBJECT_ID(N'dbo.LinPedidoVta'))
BEGIN
    ALTER TABLE dbo.LinPedidoVta WITH NOCHECK
        ADD CONSTRAINT CK_LinPedidoVta_FechaEntregaDesde2000
        CHECK ([Fecha Entrega] IS NULL OR [Fecha Entrega] >= '20000101');
END
GO

-- Comprobación: debe devolver 1 fila (is_not_trusted = 1 por el NOCHECK, es lo esperado).
SELECT name, is_disabled, is_not_trusted, definition
FROM sys.check_constraints
WHERE name = N'CK_LinPedidoVta_FechaEntregaDesde2000';
GO
