-- NestoAPI#542 — VUELTA ATRÁS URGENTE del índice filtrado IX_CabPedidoVta_PedidoOrigen (29/09/26).
-- El trabajo del Agente lo creó a las 02:30. Desde entonces TODO UPDATE/INSERT sobre CabPedidoVta falla con el
-- error 1934 (QUOTED_IDENTIFIER): los triggers trgCabPedidoVtaIns, trgCabPedidoVtaUpd y trgLinPedidoVtaUpd (y
-- prdCrearAlbaránVta, prdCrearFacturaVta, prdDeshacerFacturaVta, prdAgruparAlbaranesVta...) están creados con
-- QUOTED_IDENTIFIER OFF, y SQL Server exige ON para hacer DML en una tabla con índice filtrado. Mismo tropiezo que #294.
-- Sin el índice, la búsqueda de la nota de entrega por PedidoOrigen solo es más lenta; nada deja de funcionar.
-- Ejecutar como sa. Idempotente.
USE NV;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.CabPedidoVta') AND name = 'IX_CabPedidoVta_PedidoOrigen')
    DROP INDEX IX_CabPedidoVta_PedidoOrigen ON dbo.CabPedidoVta;
GO
-- Comprobación: debe devolver 0 filas
SELECT name, filter_definition FROM sys.indexes
WHERE object_id IN (OBJECT_ID('dbo.CabPedidoVta'), OBJECT_ID('dbo.LinPedidoVta')) AND has_filter = 1;
