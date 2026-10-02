-- NestoAPI#559: la evidencia de las recepciones (lo contado al recibir) se guarda en PreparacionEscaneos,
-- con TipoOrigen = 'COMP' (pedido de compra) y Fase = 'RECE' (recepción). Las restricciones de la tabla solo
-- admitían PICK/REPO y PICK/PACK: se amplían. Nada más cambia.
--
-- Además, esa evidencia es lo que impide recibir dos veces la misma recepción si se reenvía (IdRecepcion).
--
-- ORDEN: lanzar ANTES de terminar la primera recepción desde Ariadna o Nesto. Sin el script, «Terminar
-- recepción» falla entero (no se recibe nada) con un mensaje que lo dice.
-- Se puede lanzar dos veces sin problema.
--
-- Es DDL: desde SSMS con `sa` (el login `nuevavision` no tiene ALTER).

USE NV;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreparacionEscaneos_TipoOrigen')
    ALTER TABLE dbo.PreparacionEscaneos DROP CONSTRAINT CK_PreparacionEscaneos_TipoOrigen;
GO
ALTER TABLE dbo.PreparacionEscaneos
    ADD CONSTRAINT CK_PreparacionEscaneos_TipoOrigen CHECK (TipoOrigen IN ('PICK', 'REPO', 'COMP'));
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreparacionEscaneos_Fase')
    ALTER TABLE dbo.PreparacionEscaneos DROP CONSTRAINT CK_PreparacionEscaneos_Fase;
GO
ALTER TABLE dbo.PreparacionEscaneos
    ADD CONSTRAINT CK_PreparacionEscaneos_Fase CHECK (Fase IN ('PICK', 'PACK', 'RECE'));
GO

-- Comprobación: tienen que salir las dos restricciones con COMP y RECE
SELECT name, definition FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID('dbo.PreparacionEscaneos') AND name IN ('CK_PreparacionEscaneos_TipoOrigen', 'CK_PreparacionEscaneos_Fase');
GO
