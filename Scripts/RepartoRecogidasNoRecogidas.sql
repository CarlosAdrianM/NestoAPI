/*
    Carlos 20/11/26: «No se ha recogido» pasa a Almacén, que decide.

    Cuando el repartidor marca «No se ha recogido» (POST api/RepartoPropio/Envios/{envioId}/NoRecogida), la recogida ya NO se cierra:
    el envío de su parada queda Incidentado con FechaEntrega = 31/12/2099 (así no sale en ningún día del reparto) y aquí una fila
    Pendiente, con campana a Almacén (tipo RecogidaNoRecogida). Almacén decide:
      - POST api/RepartoPropio/Recogidas/{pedido}/Reprogramar { Fecha? }: la MISMA nota y la misma parada vuelven al reparto (Tramitado,
        ese día o el siguiente día de reparto propio del cliente), sin repartidor. Fila → Reprogramada.
      - POST api/RepartoPropio/Recogidas/{pedido}/Anular { Motivo }: se cierra del todo (Cerrado a mano). Fila → Anulada.
    Las pendientes: GET api/RepartoPropio/Recogidas/NoRecogidas.

    Fuera del EDMX: la API comprueba que existe (OBJECT_ID). Sin el script, NoRecogida cierra la parada como antes (Cerrado a mano) y
    los dos endpoints nuevos dan 503.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.RepartoRecogidasNoRecogidas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoRecogidasNoRecogidas (
        Id int IDENTITY(1,1) NOT NULL,
        Pedido int NOT NULL,                                -- la nota de recogida (empresa 1)
        EnvioId int NOT NULL,                               -- EnviosAgencia.Numero de su parada
        Motivo nvarchar(500) NOT NULL,
        Repartidor varchar(50) NOT NULL,
        FechaHora datetime NOT NULL,                        -- la del aparato
        FechaEntregaAnterior date NULL,                     -- el día de reparto que tenía la parada
        ClaveIdempotencia uniqueidentifier NOT NULL,        -- la de la petición NoRecogida
        Estado varchar(15) NOT NULL,                        -- Pendiente, Reprogramada, Anulada
        FechaDecision datetime NULL,
        UsuarioDecision varchar(50) NULL,
        FechaReprogramada date NULL,
        FechaRegistro datetime NOT NULL CONSTRAINT DF_RepartoRecogidasNoRecogidas_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_RepartoRecogidasNoRecogidas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RepartoRecogidasNoRecogidas_Clave UNIQUE (ClaveIdempotencia),
        CONSTRAINT CK_RepartoRecogidasNoRecogidas_Estado CHECK (Estado IN ('Pendiente', 'Reprogramada', 'Anulada'))
    );
    PRINT 'RepartoRecogidasNoRecogidas: creada.';
END
ELSE
    PRINT 'RepartoRecogidasNoRecogidas: ya existía.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoRecogidasNoRecogidas_Pedido' AND object_id = OBJECT_ID('dbo.RepartoRecogidasNoRecogidas'))
    CREATE INDEX IX_RepartoRecogidasNoRecogidas_Pedido ON dbo.RepartoRecogidasNoRecogidas (Pedido, Estado);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoRecogidasNoRecogidas_Estado' AND object_id = OBJECT_ID('dbo.RepartoRecogidasNoRecogidas'))
    CREATE INDEX IX_RepartoRecogidasNoRecogidas_Estado ON dbo.RepartoRecogidasNoRecogidas (Estado, EnvioId);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.RepartoRecogidasNoRecogidas TO [NUEVAVISION\RDS2016$];
GO
