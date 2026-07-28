-- =============================================================================
-- NestoAPI#366: Subida de facturas de venta a Amazon (feed UPLOAD_VAT_INVOICE)
-- Registro de las facturas subidas: idempotencia (saber qué pedidos tienen ya
-- factura subida para el grid de CanalesExternos) y auditoría del resultado del
-- feed (el job amazon-facturas-resultados actualiza Estado/Resultado).
--
-- BD: NestoConnection (NV).  GRANT a [NUEVAVISION\RDS2016$] (cuenta de máquina del servidor).
-- Única vía de acceso: AlmacenFacturasAmazon (SQL crudo; la tabla NO está en el EDMX).
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AmazonFacturasSubidas')
BEGIN
    CREATE TABLE dbo.AmazonFacturasSubidas (
        Id             INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AmazonFacturasSubidas PRIMARY KEY,
        Empresa        VARCHAR(3)     NOT NULL,
        Pedido         INT            NOT NULL,
        NumeroFactura  VARCHAR(20)    NOT NULL,
        AmazonOrderId  VARCHAR(20)    NOT NULL,
        MarketplaceId  VARCHAR(20)    NOT NULL,
        FeedId         VARCHAR(30)    NOT NULL,
        -- ENVIADA (feed aceptado, pendiente de procesar) o el processingStatus
        -- final de Amazon: DONE / FATAL / CANCELLED
        Estado         VARCHAR(10)    NOT NULL,
        -- Informe de proceso del feed (recortado a 4000 chars), solo si hay
        Resultado      NVARCHAR(4000) NULL,
        FechaEnvio     DATETIME       NOT NULL
            CONSTRAINT DF_AmazonFacturasSubidas_FechaEnvio DEFAULT (GETDATE()),
        FechaResultado DATETIME       NULL,
        Usuario        NVARCHAR(100)  NULL,
        -- Una fila por pedido: resubir actualiza la existente (reemplaza en Amazon)
        CONSTRAINT UQ_AmazonFacturasSubidas_Pedido UNIQUE (Empresa, Pedido)
    );
END
GO

GRANT SELECT, INSERT, UPDATE ON dbo.AmazonFacturasSubidas TO [NUEVAVISION\RDS2016$];
GO
