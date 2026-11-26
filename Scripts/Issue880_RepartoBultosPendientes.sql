/*
    NestoAPI#880, corte 2 (26/11/26): bultos pendientes de una entrega incompleta del reparto propio, con cierre a mano.

    Cuando el repartidor entrega un envío y falta algún bulto (corte 1: el envío queda Entregado con DetalleEstado
    «Entregado (reparto propio): 4 de 5 bultos. Falta RP…-5»), cada bulto que falta queda aquí como Pendiente.
      - GET  api/RepartoPropio/BultosPendientes?soloPendientes=true&empresa=&pedido=&envio=&dias=30  (Nesto: Agencias y pestaña Entrega)
      - POST api/RepartoPropio/BultosPendientes/{id}/Cerrar { Cierre: EntregadoEnMano | Perdido | Abonado, Motivo }  (Almacén, Dirección, Informática)
      - Job reparto-bultos-pendientes (L-V 9:00): campana a Almacén con lo que sigue pendiente.
      - Seguimiento público: «Entregado parcialmente» mientras quede alguno Pendiente.

    Fuera del EDMX: la API comprueba que existe (OBJECT_ID). Sin el script no se apunta nada (la entrega incompleta sigue avisando como
    en el corte 1) y los endpoints dan 503.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.RepartoBultosPendientes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoBultosPendientes (
        Id int IDENTITY(1,1) NOT NULL,
        Empresa char(3) NULL,
        EnvioId int NOT NULL,                               -- EnviosAgencia.Numero
        Pedido int NULL,
        Cliente varchar(10) NULL,
        Contacto varchar(3) NULL,
        Nombre nvarchar(100) NULL,
        Codigo varchar(30) NOT NULL,                        -- RP{envío}-{n}, como en RepartoBultosMovimientos
        Bulto int NOT NULL,
        Total int NOT NULL,
        SituacionAlEntregar varchar(20) NOT NULL,           -- por qué faltaba al entregar (EntregaIncompletaReparto)
        Repartidor varchar(50) NULL,                        -- quien hizo la entrega incompleta
        FechaEntrega datetime NOT NULL,
        Estado varchar(10) NOT NULL,                        -- Pendiente, Cerrado
        Cierre varchar(20) NULL,                            -- EntregadoEnMano, Perdido, Abonado
        MotivoCierre nvarchar(500) NULL,
        UsuarioCierre varchar(50) NULL,
        FechaCierre datetime NULL,
        FechaRegistro datetime NOT NULL CONSTRAINT DF_RepartoBultosPendientes_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_RepartoBultosPendientes PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RepartoBultosPendientes_Codigo UNIQUE (Codigo),
        CONSTRAINT CK_RepartoBultosPendientes_Estado CHECK (Estado IN ('Pendiente', 'Cerrado')),
        CONSTRAINT CK_RepartoBultosPendientes_Cierre CHECK (Cierre IS NULL OR Cierre IN ('EntregadoEnMano', 'Perdido', 'Abonado')),
        CONSTRAINT CK_RepartoBultosPendientes_Situacion CHECK (SituacionAlEntregar IN ('NoLeidoSeQueda', 'NoSeCargo', 'VolvioAlAlmacen', 'LoLlevaOtro')),
        CONSTRAINT CK_RepartoBultosPendientes_Coherencia CHECK (
            (Estado = 'Pendiente' AND Cierre IS NULL AND FechaCierre IS NULL)
            OR (Estado = 'Cerrado' AND Cierre IS NOT NULL AND MotivoCierre IS NOT NULL AND UsuarioCierre IS NOT NULL AND FechaCierre IS NOT NULL)),
        CONSTRAINT CK_RepartoBultosPendientes_Bulto CHECK (Bulto > 0 AND Total >= Bulto)
    );
    PRINT 'RepartoBultosPendientes: creada.';
END
ELSE
    PRINT 'RepartoBultosPendientes: ya existía.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoBultosPendientes_Estado' AND object_id = OBJECT_ID('dbo.RepartoBultosPendientes'))
    CREATE INDEX IX_RepartoBultosPendientes_Estado ON dbo.RepartoBultosPendientes (Estado, FechaEntrega);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoBultosPendientes_Envio' AND object_id = OBJECT_ID('dbo.RepartoBultosPendientes'))
    CREATE INDEX IX_RepartoBultosPendientes_Envio ON dbo.RepartoBultosPendientes (EnvioId, Estado);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoBultosPendientes_Pedido' AND object_id = OBJECT_ID('dbo.RepartoBultosPendientes'))
    CREATE INDEX IX_RepartoBultosPendientes_Pedido ON dbo.RepartoBultosPendientes (Pedido, Estado);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.RepartoBultosPendientes TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COUNT(*) AS Filas, SUM(CASE WHEN Estado = 'Pendiente' THEN 1 ELSE 0 END) AS Pendientes FROM dbo.RepartoBultosPendientes;
GO
