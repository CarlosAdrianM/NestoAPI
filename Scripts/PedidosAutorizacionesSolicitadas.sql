/*
    Novedades 1258 (Carlos 20/11/26): pedir autorización de un pedido que no pasa la validación de ofertas y descuentos.

    El vendedor, desde el aviso «no pasa la validación», pide la autorización (POST api/PedidosVenta/Autorizaciones); a quien puede
    autorizarla (el mismo que puede «crearlo de todos modos»: Dirección, Almacén, Tiendas con todas las líneas de su almacén o el
    parámetro PermitirCrearPedidoConErroresValidacion; y el permiso propio de la denegación si lo pide) le llega a la campana de Nesto.
    Al autorizarla, la API crea el pedido por el POST de siempre a nombre del solicitante.

    PedidosAutorizacionesSolicitadas: una fila por solicitud.
      - PedidoJson: el PedidoVentaDTO tal como se iba a guardar. Motivos: JSON { Motivos: [..], Errores: [{Producto, Motivo}] } de la
        validación que hizo la API al pedirla.
      - Estado: Pendiente, Autorizada (PedidoCreado = el número), Denegada (MotivoResolucion obligatorio) o Anulada (por quien la pidió).
      - Además de las del contrato: Huella (SHA-256 de cliente, contacto y líneas: una Pendiente idéntica del mismo solicitante en 24 h da
        409), PermisoNecesario (el permiso propio de la denegación, NestoAPI#501), Importe (base imponible del borrador), NumeroLineas,
        Vendedor (para el push de NestoApp al resolverla) y Almacenes (de las líneas, para la regla de Tiendas).

    Fuera del EDMX. Sin el script, los seis endpoints de api/PedidosVenta/Autorizaciones dan 503 y nada más cambia.

    Lanzar en SSMS contra NV como sa (antes o después de publicar la API). Idempotente.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID(N'dbo.PedidosAutorizacionesSolicitadas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidosAutorizacionesSolicitadas (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PedidosAutorizacionesSolicitadas PRIMARY KEY,
        Empresa char(3) NOT NULL,
        Cliente varchar(10) NOT NULL,
        Contacto varchar(3) NULL,
        Solicitante varchar(50) NOT NULL,
        FechaSolicitud datetime NOT NULL CONSTRAINT DF_PedidosAutorizacionesSolicitadas_Fecha DEFAULT (GETDATE()),
        PedidoJson nvarchar(max) NOT NULL,
        Motivos nvarchar(max) NULL,
        Comentario nvarchar(500) NULL,
        Estado varchar(12) NOT NULL CONSTRAINT DF_PedidosAutorizacionesSolicitadas_Estado DEFAULT ('Pendiente'),
        UsuarioResolucion varchar(50) NULL,
        FechaResolucion datetime NULL,
        MotivoResolucion nvarchar(500) NULL,
        PedidoCreado int NULL,
        Huella varchar(64) NULL,
        PermisoNecesario varchar(100) NULL,
        Importe decimal(18,2) NOT NULL CONSTRAINT DF_PedidosAutorizacionesSolicitadas_Importe DEFAULT (0),
        NumeroLineas int NOT NULL CONSTRAINT DF_PedidosAutorizacionesSolicitadas_Lineas DEFAULT (0),
        Vendedor varchar(3) NULL,
        Almacenes varchar(100) NULL,
        CONSTRAINT CK_PedidosAutorizacionesSolicitadas_Estado CHECK (Estado IN ('Pendiente', 'Autorizada', 'Denegada', 'Anulada'))
    );
    PRINT 'PedidosAutorizacionesSolicitadas: creada.';
END
ELSE
    PRINT 'PedidosAutorizacionesSolicitadas: ya existía.';
GO

-- El listado (últimos 30 días, pendientes primero) y la comprobación de «ya la ha pedido» (solicitante + huella)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PedidosAutorizacionesSolicitadas_Fecha' AND object_id = OBJECT_ID(N'dbo.PedidosAutorizacionesSolicitadas'))
    CREATE INDEX IX_PedidosAutorizacionesSolicitadas_Fecha ON dbo.PedidosAutorizacionesSolicitadas (FechaSolicitud, Estado);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PedidosAutorizacionesSolicitadas_Solicitante' AND object_id = OBJECT_ID(N'dbo.PedidosAutorizacionesSolicitadas'))
    CREATE INDEX IX_PedidosAutorizacionesSolicitadas_Solicitante ON dbo.PedidosAutorizacionesSolicitadas (Solicitante, Estado, Huella);
GO

-- La API corre con la cuenta máquina de RDS2016 (integrated security). Lee, inserta y actualiza (no borra: es la historia).
GRANT SELECT, INSERT, UPDATE ON dbo.PedidosAutorizacionesSolicitadas TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID(N'dbo.PedidosAutorizacionesSolicitadas', N'U') AS Tabla;
SELECT TOP 20 Id, Empresa, Cliente, Contacto, Solicitante, FechaSolicitud, Estado, Importe, NumeroLineas, UsuarioResolucion, FechaResolucion, PedidoCreado
FROM dbo.PedidosAutorizacionesSolicitadas ORDER BY Id DESC;
