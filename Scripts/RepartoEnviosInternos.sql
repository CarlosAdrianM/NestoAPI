/*
    Sugerencia 1277 (Alfredo, Carlos 20/11/26): envíos internos entre sedes.

    Lo que va de una sede a otra en la furgoneta y NO es stock (papel higiénico, café, folios, aparatos de demostración o de clientes,
    inmovilizado…). No toca almacenes ni ExtractoProducto y no crea clientes ni pedidos ficticios.

    RepartoEnviosInternos: uno por envío (origen y destino ALG, REI o ALC, distintos; 1..50 bultos). Cada bulto lleva la etiqueta
    EI{Id}-{N} y su historia en RepartoBultosMovimientos como los demás (TipoCodigo 'EI', EnvioId = −(1000000000 + Id*2), el de su
    parada de entrega). Estado guardado: Pendiente (EnCamino se deduce de sus bultos), Entregado o Anulado.

    En el reparto (GET api/RepartoPropio/Envios): una parada de RECOGIDA en el origen (si no es Algete) y una de ENTREGA en el destino.

    Este script AMPLÍA la CHECK CK_RepartoBultosMovimientos_TipoCodigo con 'EI' (requiere Scripts/Issue772_FurgonetaBultos.sql).

    Fuera del EDMX: la API comprueba que existe (OBJECT_ID). Sin el script, los endpoints de envíos internos dan 503 y el reparto,
    como antes.

    Ejecutar en SSMS contra NV como sa (CREATE, ALTER y GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

-- 1) Los movimientos de bultos admiten el tipo de código EI (antes que la tabla: sin esto el primer movimiento EI fallaría)
IF OBJECT_ID('dbo.RepartoBultosMovimientos', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name = 'CK_RepartoBultosMovimientos_TipoCodigo' AND definition LIKE '%''EI''%')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_RepartoBultosMovimientos_TipoCodigo')
        ALTER TABLE dbo.RepartoBultosMovimientos DROP CONSTRAINT CK_RepartoBultosMovimientos_TipoCodigo;
    ALTER TABLE dbo.RepartoBultosMovimientos ADD CONSTRAINT CK_RepartoBultosMovimientos_TipoCodigo
        CHECK (TipoCodigo IN ('RP', 'NR', 'R', 'EI'));
    PRINT 'CK_RepartoBultosMovimientos_TipoCodigo: ampliada con EI.';
END
ELSE
    PRINT 'CK_RepartoBultosMovimientos_TipoCodigo: ya admitía EI (o falta la tabla de la furgoneta).';
GO

-- 2) Los envíos internos
IF OBJECT_ID('dbo.RepartoEnviosInternos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoEnviosInternos (
        Id int IDENTITY(1,1) NOT NULL,
        Origen char(3) NOT NULL,
        Destino char(3) NOT NULL,
        Descripcion nvarchar(200) NOT NULL,
        Bultos smallint NOT NULL,
        Estado varchar(12) NOT NULL,                         -- Pendiente, EnCamino, Entregado, Anulado
        Usuario varchar(50) NOT NULL,                        -- quien lo da de alta
        FechaAlta datetime NOT NULL,
        UsuarioRecepcion varchar(50) NULL,                   -- el repartidor que lo entrega
        FechaRecepcion datetime NULL,
        NombreReceptor nvarchar(100) NULL,                   -- quien lo recibe en el destino
        ClaveEntrega uniqueidentifier NULL,
        DetalleEntrega nvarchar(200) NULL,                   -- con una entrega incompleta, qué faltó
        MotivoAnulacion nvarchar(200) NULL,
        UsuarioAnulacion varchar(50) NULL,
        FechaAnulacion datetime NULL,
        CONSTRAINT PK_RepartoEnviosInternos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_RepartoEnviosInternos_Sedes CHECK (Origen IN ('ALG', 'REI', 'ALC') AND Destino IN ('ALG', 'REI', 'ALC') AND Origen <> Destino),
        CONSTRAINT CK_RepartoEnviosInternos_Bultos CHECK (Bultos BETWEEN 1 AND 50),
        CONSTRAINT CK_RepartoEnviosInternos_Estado CHECK (Estado IN ('Pendiente', 'EnCamino', 'Entregado', 'Anulado'))
    );
    PRINT 'RepartoEnviosInternos: creada.';
END
ELSE
    PRINT 'RepartoEnviosInternos: ya existía.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoEnviosInternos_Estado' AND object_id = OBJECT_ID('dbo.RepartoEnviosInternos'))
    CREATE INDEX IX_RepartoEnviosInternos_Estado ON dbo.RepartoEnviosInternos (Estado, FechaAlta);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.RepartoEnviosInternos TO [NUEVAVISION\RDS2016$];
GO
