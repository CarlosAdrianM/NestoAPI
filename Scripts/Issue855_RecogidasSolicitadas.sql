/*
    NestoAPI#855 (12/11/26): recogida solicitada por el vendedor. El vendedor la PIDE (NestoApp, NestoApp#259, o Nesto) y el almacén la
    CONFIRMA (nace la nota de recogida NR{pedido}) o la RECHAZA con motivo en la pantalla de Nesto (Nesto#635).

    RecogidasSolicitadas: una fila por solicitud.
      - Estado guardado: Solicitada, Confirmada, Rechazada, Anulada. La API calcula además EnReparto y Recogida al leer.
      - QuienRecoge: Reparto (la furgoneta; solo contactos de ruta propia) o Vendedor (la recoge él al pasar por el cliente; la nota no
        entra en el picking y no hay parada). En los dos casos se cierra al leer el NR en Algete (el abono de la recogida).
      - PedidoNota: la nota de recogida creada al confirmar.
      - AvisoSinRespuesta: cuándo avisó el job recogidas-solicitadas-sin-respuesta (un día laborable sin contestar; una sola vez).
      - UsuarioAnula, FechaAnulacion, MotivoAnulacion: quien la anuló (quien la pidió mientras no esté en reparto; después, Almacén o
        Administración).

    Fuera del EDMX: la API comprueba que existen la tabla y sus columnas (COL_LENGTH). Sin el script, los endpoints de
    api/NotasEntrega/Recogidas/Solicitudes dan 503 «falta lanzar el script» y el job no hace nada.

    Requiere Scripts/Issue757_NotasEntregaCabecera.sql. Lanzar en SSMS contra NV como sa ANTES de publicar la API. Idempotente: se puede
    lanzar dos veces (columnas nuevas, cada una en su propio lote, por si la tabla ya existía de una versión anterior del script).
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID(N'dbo.RecogidasSolicitadas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RecogidasSolicitadas (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RecogidasSolicitadas PRIMARY KEY,
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        Contacto char(3) NOT NULL,
        Texto nvarchar(500) NOT NULL,                       -- qué hay que recoger
        Bultos smallint NOT NULL CONSTRAINT DF_RecogidasSolicitadas_Bultos DEFAULT (1),
        FacturaOriginal varchar(10) NULL,
        Vendedor char(3) NULL,                              -- el del token de quien la pide
        UsuarioSolicita varchar(50) NOT NULL,
        FechaSolicitud datetime NOT NULL CONSTRAINT DF_RecogidasSolicitadas_Fecha DEFAULT (GETDATE()),
        Estado varchar(12) NOT NULL CONSTRAINT DF_RecogidasSolicitadas_Estado DEFAULT ('Solicitada'),
        PedidoNota int NULL,                                -- la nota de recogida creada al confirmar
        UsuarioResuelve varchar(50) NULL,
        FechaResolucion datetime NULL,
        MotivoRechazo nvarchar(300) NULL,
        CONSTRAINT CK_RecogidasSolicitadas_Estado CHECK (Estado IN ('Solicitada', 'Confirmada', 'Rechazada', 'Anulada')),
        CONSTRAINT CK_RecogidasSolicitadas_Bultos CHECK (Bultos BETWEEN 1 AND 999)
    );
    PRINT 'RecogidasSolicitadas: creada.';
END
ELSE
    PRINT 'RecogidasSolicitadas: ya existía.';
GO

-- Quién la recoge (matiz de Carlos 12/11/26): el reparto propio o el propio vendedor
IF COL_LENGTH(N'dbo.RecogidasSolicitadas', N'QuienRecoge') IS NULL
    ALTER TABLE dbo.RecogidasSolicitadas ADD QuienRecoge varchar(10) NOT NULL
        CONSTRAINT DF_RecogidasSolicitadas_QuienRecoge DEFAULT ('Reparto') WITH VALUES;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_RecogidasSolicitadas_QuienRecoge')
    ALTER TABLE dbo.RecogidasSolicitadas ADD CONSTRAINT CK_RecogidasSolicitadas_QuienRecoge CHECK (QuienRecoge IN ('Reparto', 'Vendedor'));
GO

-- El aviso de «un día laborable sin respuesta» (una sola vez por solicitud)
IF COL_LENGTH(N'dbo.RecogidasSolicitadas', N'AvisoSinRespuesta') IS NULL
    ALTER TABLE dbo.RecogidasSolicitadas ADD AvisoSinRespuesta datetime NULL;
GO

-- La anulación
IF COL_LENGTH(N'dbo.RecogidasSolicitadas', N'UsuarioAnula') IS NULL
    ALTER TABLE dbo.RecogidasSolicitadas ADD UsuarioAnula varchar(50) NULL;
GO
IF COL_LENGTH(N'dbo.RecogidasSolicitadas', N'FechaAnulacion') IS NULL
    ALTER TABLE dbo.RecogidasSolicitadas ADD FechaAnulacion datetime NULL;
GO
IF COL_LENGTH(N'dbo.RecogidasSolicitadas', N'MotivoAnulacion') IS NULL
    ALTER TABLE dbo.RecogidasSolicitadas ADD MotivoAnulacion nvarchar(300) NULL;
GO

-- Las del cliente (ficha de NestoApp, «ya tiene otra sin contestar») y las pendientes (pantalla de Nesto y job)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecogidasSolicitadas_Cliente' AND object_id = OBJECT_ID(N'dbo.RecogidasSolicitadas'))
    CREATE INDEX IX_RecogidasSolicitadas_Cliente ON dbo.RecogidasSolicitadas (Empresa, Cliente, Contacto, FechaSolicitud);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecogidasSolicitadas_Estado' AND object_id = OBJECT_ID(N'dbo.RecogidasSolicitadas'))
    CREATE INDEX IX_RecogidasSolicitadas_Estado ON dbo.RecogidasSolicitadas (Estado, FechaSolicitud);
GO
-- El abono de la recogida pregunta si la nota es de una que recoge el vendedor
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecogidasSolicitadas_PedidoNota' AND object_id = OBJECT_ID(N'dbo.RecogidasSolicitadas'))
    CREATE INDEX IX_RecogidasSolicitadas_PedidoNota ON dbo.RecogidasSolicitadas (PedidoNota);
GO

-- La API corre con la cuenta máquina de RDS2016 (integrated security). Lee, inserta y actualiza (no borra: es la historia).
GRANT SELECT, INSERT, UPDATE ON dbo.RecogidasSolicitadas TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COL_LENGTH(N'dbo.RecogidasSolicitadas', N'QuienRecoge') AS QuienRecoge,
       COL_LENGTH(N'dbo.RecogidasSolicitadas', N'AvisoSinRespuesta') AS AvisoSinRespuesta,
       COL_LENGTH(N'dbo.RecogidasSolicitadas', N'MotivoAnulacion') AS MotivoAnulacion;
SELECT TOP 20 Id, Empresa, Cliente, Contacto, Estado, QuienRecoge, PedidoNota, UsuarioSolicita, FechaSolicitud
FROM dbo.RecogidasSolicitadas ORDER BY Id DESC;
