-- Issue #87: cuando el usuario crea la rectificativa SIN "Crear albarán y factura automáticamente"
-- (o la copia acaba en una rama de advertencia que no factura), la metadata de qué factura/línea
-- original rectifica cada línea se perdía, y al facturar el pedido a mano LinFacturaVtaRectificacion
-- quedaba vacía (y desde #36, la rectificativa tampoco se envía a Verifactu sin vinculaciones).
--
-- Esta tabla guarda esa metadata entre la copia y la facturación manual. ServicioFacturas.CrearFactura
-- la consume al facturar: puebla LinFacturaVtaRectificacion, borra las pendientes y envía a Verifactu.
-- Filas huérfanas (pedido borrado sin facturar) son inocuas; se pueden purgar por FechaCreacion.

USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.RectificativaPendiente') AND type = 'U')
BEGIN
    CREATE TABLE dbo.RectificativaPendiente (
        Empresa char(3) NOT NULL,
        NumeroPedido int NOT NULL,
        NumeroLinea int NOT NULL,                  -- Nº_Orden de la línea copiada en el pedido rectificativo
        FacturaOriginalNumero varchar(20) NOT NULL,
        FacturaOriginalLinea int NOT NULL,         -- Nº_Orden de la línea en la factura original
        CantidadRectificada decimal(18, 6) NOT NULL,
        FechaCreacion datetime NOT NULL CONSTRAINT DF_RectificativaPendiente_FechaCreacion DEFAULT (GETDATE()),
        CONSTRAINT PK_RectificativaPendiente PRIMARY KEY (Empresa, NumeroPedido, NumeroLinea)
    );
END
GO

-- BD de negocio (NestoConnection): el API accede con la cuenta de máquina
GRANT SELECT, INSERT, DELETE ON dbo.RectificativaPendiente TO [NUEVAVISION\RDS2016$];
GO
