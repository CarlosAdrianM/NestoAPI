-- Issue #120: Añadir ImporteMinimoPedido a Ganavisiones
-- El campo indica el importe mínimo de base imponible que debe tener el pedido
-- para que el producto aparezca como opción de regalo.
-- Valor por defecto 0 = sin restricción (retrocompatible).

ALTER TABLE [dbo].[Ganavisiones]
ADD [ImporteMinimoPedido] DECIMAL(18,2) NOT NULL
CONSTRAINT [DF_Ganavisiones_ImporteMinimoPedido] DEFAULT 0;
