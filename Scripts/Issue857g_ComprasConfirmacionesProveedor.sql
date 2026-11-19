/*
    NestoAPI#857g (19/11/26): las CONFIRMACIONES de pedido de los proveedores (Weelko: «Pedido SO253360 … Hemos recibido su pedido…»).

    Una confirmación no es un envío: no se guarda en dbo.ComprasCorreosSeguimiento, no pone fecha de recepción, no se reenvía ni avisa. Sirve
    para ENLAZAR el pedido del proveedor (SO253360) con nuestro pedido de compra: cuando llegue el aviso del envío con su referencia («Ref.:
    SO253360…», el resumen de MBE), ese correo se asigna a los candidatos de la confirmación (motivo «PedidoProveedor»).

    Crea dbo.ComprasConfirmacionesProveedor (fuera del EDMX):
      - MensajeId (único, el Message-ID del correo), Recibido, RemitenteDireccion, Formato (la clase que la ha leído: «Weelko»)
      - PedidoProveedor nvarchar(40): el número del pedido del proveedor
      - ReferenciaCliente nvarchar(40) NULL: la referencia que le dimos, si la da
      - Agencia nvarchar(50) NULL: la que anuncia («El pedido será enviado por: MAILBOXES» → MBE)
      - ProveedorPropuesto, PedidosCandidatos (JSON, como en los correos): nuestros pedidos en el aire que pueden ser
      - FechaCaptura, FechaModificacion

    ORDEN: después de Scripts/Issue857_ComprasCorreosSeguimiento.sql. Puede ir ANTES o DESPUÉS de publicar la API: sin la tabla, la API no
    guarda confirmaciones ni enlaza (como antes); lo vuelve a comprobar cada 10 minutos, sin reiniciar. Tras lanzarlo,
    POST api/Compras/AgenciasEntrada/Correos/Reanalizar?dias=30 recoge las confirmaciones de estos días y rehace los enlaces.

    Lanzar como sa en NV. Idempotente: se puede relanzar.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasConfirmacionesProveedor', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComprasConfirmacionesProveedor (
        Id int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ComprasConfirmacionesProveedor PRIMARY KEY,
        MensajeId nvarchar(450) NOT NULL,
        Recibido datetime NOT NULL,
        RemitenteDireccion nvarchar(255) NULL,
        Formato nvarchar(50) NULL,
        PedidoProveedor nvarchar(40) NOT NULL,
        ReferenciaCliente nvarchar(40) NULL,
        Agencia nvarchar(50) NULL,
        ProveedorPropuesto varchar(10) NULL,
        PedidosCandidatos nvarchar(2000) NULL,
        FechaCaptura datetime NOT NULL CONSTRAINT DF_ComprasConfirmacionesProveedor_FechaCaptura DEFAULT (GETDATE()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ComprasConfirmacionesProveedor_FechaModificacion DEFAULT (GETDATE())
    );
END
GO
IF OBJECT_ID(N'dbo.ComprasConfirmacionesProveedor', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ComprasConfirmacionesProveedor_MensajeId' AND object_id = OBJECT_ID(N'dbo.ComprasConfirmacionesProveedor'))
    CREATE UNIQUE INDEX UX_ComprasConfirmacionesProveedor_MensajeId ON dbo.ComprasConfirmacionesProveedor (MensajeId);
GO
IF OBJECT_ID(N'dbo.ComprasConfirmacionesProveedor', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ComprasConfirmacionesProveedor_Recibido' AND object_id = OBJECT_ID(N'dbo.ComprasConfirmacionesProveedor'))
    CREATE INDEX IX_ComprasConfirmacionesProveedor_Recibido ON dbo.ComprasConfirmacionesProveedor (Recibido) INCLUDE (PedidoProveedor);
GO
GRANT SELECT, INSERT, UPDATE ON dbo.ComprasConfirmacionesProveedor TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación (la misma que hace la API)
SELECT CASE WHEN OBJECT_ID(N'dbo.ComprasConfirmacionesProveedor', N'U') IS NOT NULL
            THEN N'OK: existe dbo.ComprasConfirmacionesProveedor'
            ELSE N'FALTA: no se ha creado dbo.ComprasConfirmacionesProveedor' END AS Resultado;
GO
