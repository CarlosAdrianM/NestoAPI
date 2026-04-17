-- Issue #159: Añadir flag NoCobrarComisionReembolso a pedidos de venta
-- Permite al vendedor marcar un pedido concreto para no aplicarle la comisión
-- por contra reembolso. A partir de 2026-09-01 el backend ignora el flag y
-- siempre aplica la comisión cuando procede.
--
-- Ejecutar ANTES de desplegar el código.

IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'CabPedidoVta' AND COLUMN_NAME = 'NoCobrarComisionReembolso'
)
BEGIN
    ALTER TABLE CabPedidoVta
        ADD NoCobrarComisionReembolso bit NOT NULL CONSTRAINT DF_CabPedidoVta_NoCobrarComisionReembolso DEFAULT 0;
    PRINT 'Columna NoCobrarComisionReembolso añadida a CabPedidoVta';
END
ELSE
    PRINT 'Columna NoCobrarComisionReembolso ya existe en CabPedidoVta';
GO
