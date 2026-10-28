-- NestoAPI#698 (28/10/26): la restricción CK_CabPedidoVta_ModoServicio (Issue482) solo admitía 1..4 y «Retener» ponía el 5:
-- «Instrucción UPDATE en conflicto con la restricción CHECK» (MariaJose, 929113, 28/10 09:07). Faltó en Issue698_PedidosRetenidos.sql.
-- Lanzar como sa en NV. Idempotente.
SET NOCOUNT ON;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CabPedidoVta_ModoServicio' AND definition NOT LIKE '%(5)%')
BEGIN
    ALTER TABLE dbo.CabPedidoVta DROP CONSTRAINT CK_CabPedidoVta_ModoServicio;
    ALTER TABLE dbo.CabPedidoVta WITH CHECK
        ADD CONSTRAINT CK_CabPedidoVta_ModoServicio CHECK (ModoServicio IS NULL OR ModoServicio BETWEEN 1 AND 5);
    PRINT 'CK_CabPedidoVta_ModoServicio: ahora admite el 5 (Retenido).';
END
ELSE
    PRINT 'CK_CabPedidoVta_ModoServicio ya admite el 5.';

-- El 929113: la fila de PedidosRetenidos (Id 1, MariaJose, «Tiene que ampliar.», modo anterior 3) se insertó pero el modo no cambió.
-- Se completa la retención tal como la pidió (si prefieres liberarla, pon Liberado = GETDATE() en la fila y no toques el pedido).
IF EXISTS (SELECT 1 FROM dbo.PedidosRetenidos WHERE Id = 1 AND Pedido = 929113 AND Liberado IS NULL)
   AND EXISTS (SELECT 1 FROM dbo.CabPedidoVta WHERE Empresa = '1' AND Número = 929113 AND ISNULL(ModoServicio, 0) <> 5)
BEGIN
    INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario)
    SELECT 'CabPedidoVta', 'Pedido 929113 ModoServicio=' + ISNULL(CAST(ModoServicio AS varchar), 'NULL'),
           'ModoServicio=5 (Retenido #698, completado por script tras el fallo de la restricción: Tiene que ampliar.)', 'NUEVAVISION\MariaJose'
    FROM dbo.CabPedidoVta WHERE Empresa = '1' AND Número = 929113;
    UPDATE dbo.CabPedidoVta SET ModoServicio = 5 WHERE Empresa = '1' AND Número = 929113;
    PRINT '929113: retención completada (modo 5).';
END
