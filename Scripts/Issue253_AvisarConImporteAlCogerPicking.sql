-- NestoAPI#253: aviso automático con importe cuando el pedido coge picking.
-- Columna nueva en CabPedidoVta para la casilla "Avisar con importe cuando coja picking".
-- ⚠️ EJECUTAR EN PROD ANTES de desplegar la API (el EDMX ya mapea la columna).
-- BD: NV (NestoConnection). No necesita GRANT nuevo (la tabla ya los tiene).

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.CabPedidoVta') AND name = 'AvisarConImporteAlCogerPicking'
)
BEGIN
    ALTER TABLE dbo.CabPedidoVta
        ADD AvisarConImporteAlCogerPicking bit NOT NULL
            CONSTRAINT DF_CabPedidoVta_AvisarConImporteAlCogerPicking DEFAULT (0);
END
GO
