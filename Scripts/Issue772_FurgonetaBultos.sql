/*
    NestoAPI#772 (Carlos, 31/10/26): lo que lleva cada furgoneta, bulto a bulto.

    RepartoBultosMovimientos: la historia de cada bulto del reparto propio (RP{envío}-{N}, NR{pedido}-{N} y R{traspaso}-{N}).
      - Tipo: Cargado (Cargar furgoneta), Recogido (NR del cliente o R de la tienda que vuelve a Algete), Entregado (al entregar
        la parada) y Descargado (Descargar furgoneta por la tarde; o el almacén al leer el bulto: recepción de la REPO o abono de
        la NR, Origen = 'Almacen').
      - «Lo que lleva encima» un repartidor = los bultos cuyo ÚLTIMO movimiento (por FechaHora e Id) es Cargado o Recogido suyo.
      - Lo que ya existía sigue igual: EnviosAgenciaCargaBultos y RepartoParadasReposicionBultos. «Cargar» escribe además aquí
        su movimiento Cargado con la MISMA ClaveIdempotencia.
      - Solo inserciones: es evidencia. Idempotente por ClaveIdempotencia (la cola de Ariadna reintenta sin cobertura).

    RepartoJornadas: «Terminar jornada» de cada repartidor (y cuántos bultos le quedaban en la furgoneta). El job de las 22:00
    (L-V) avisa de los repartidores con bultos encima y sin jornada terminada ese día.

    Fuera del EDMX: la API comprueba que existen (OBJECT_ID). Sin el script, Furgoneta, Recoger, Descargar y Jornada/Terminar dan
    400 «falta el script» y todo lo demás (Cargar, Entregar…) sigue como antes.

    Requiere Scripts/Issue739_CargaFurgoneta.sql y Scripts/Issue759_ParadasReparto.sql. Ejecutar en SSMS contra NV como sa
    (CREATE y GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.RepartoBultosMovimientos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoBultosMovimientos (
        Id int IDENTITY(1,1) NOT NULL,
        Codigo varchar(30) NOT NULL,                        -- la etiqueta normalizada: RP251300-1, NR929554-2, R81010-3
        TipoCodigo varchar(2) NOT NULL,                     -- RP, NR o R
        EnvioId int NULL,                                   -- RP/NR: EnviosAgencia.Numero; R cargado: −Id de la parada de reposición
        NumTraspaso int NULL,                               -- R
        Pedido int NULL,                                    -- NR (y el pedido del envío si se sabe)
        Bulto smallint NOT NULL,
        Total smallint NULL,
        Tipo varchar(12) NOT NULL,                          -- Cargado, Recogido, Entregado, Descargado
        Usuario varchar(50) NOT NULL,                       -- quien lo hace (el repartidor, o quien lo lee en el almacén)
        FechaHora datetime NOT NULL,                        -- la del aparato (hora local)
        FechaRegistro datetime NOT NULL CONSTRAINT DF_RepartoBultosMovimientos_FechaRegistro DEFAULT (GETDATE()),
        Latitud decimal(9, 6) NULL,
        Longitud decimal(9, 6) NULL,
        Origen varchar(10) NOT NULL,                        -- Repartidor o Almacen
        Descripcion nvarchar(150) NULL,                     -- «Peluquería Ana (Madrid)», «Reposición 81010 Reina → Algete»
        ClaveIdempotencia uniqueidentifier NOT NULL,
        CONSTRAINT PK_RepartoBultosMovimientos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RepartoBultosMovimientos_Clave UNIQUE (ClaveIdempotencia),
        CONSTRAINT CK_RepartoBultosMovimientos_Tipo CHECK (Tipo IN ('Cargado', 'Recogido', 'Entregado', 'Descargado')),
        CONSTRAINT CK_RepartoBultosMovimientos_TipoCodigo CHECK (TipoCodigo IN ('RP', 'NR', 'R')),
        CONSTRAINT CK_RepartoBultosMovimientos_Origen CHECK (Origen IN ('Repartidor', 'Almacen')),
        CONSTRAINT CK_RepartoBultosMovimientos_Bulto CHECK (Bulto > 0)
    );
    PRINT 'RepartoBultosMovimientos: creada.';
END
ELSE
    PRINT 'RepartoBultosMovimientos: ya existía.';
GO

-- La historia de un bulto y «el último de cada bulto» (lo que lleva cada uno)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoBultosMovimientos_Codigo' AND object_id = OBJECT_ID('dbo.RepartoBultosMovimientos'))
    CREATE INDEX IX_RepartoBultosMovimientos_Codigo ON dbo.RepartoBultosMovimientos (Codigo, FechaHora, Id) INCLUDE (Tipo, Usuario);
GO
-- La ventana de la furgoneta (los últimos 30 días)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoBultosMovimientos_FechaRegistro' AND object_id = OBJECT_ID('dbo.RepartoBultosMovimientos'))
    CREATE INDEX IX_RepartoBultosMovimientos_FechaRegistro ON dbo.RepartoBultosMovimientos (FechaRegistro) INCLUDE (Codigo);
GO
-- Los bultos de una parada (entregar) y de un traspaso (recoger, seguimiento)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoBultosMovimientos_EnvioId' AND object_id = OBJECT_ID('dbo.RepartoBultosMovimientos'))
    CREATE INDEX IX_RepartoBultosMovimientos_EnvioId ON dbo.RepartoBultosMovimientos (EnvioId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoBultosMovimientos_Traspaso' AND object_id = OBJECT_ID('dbo.RepartoBultosMovimientos'))
    CREATE INDEX IX_RepartoBultosMovimientos_Traspaso ON dbo.RepartoBultosMovimientos (NumTraspaso);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoBultosMovimientos_Pedido' AND object_id = OBJECT_ID('dbo.RepartoBultosMovimientos'))
    CREATE INDEX IX_RepartoBultosMovimientos_Pedido ON dbo.RepartoBultosMovimientos (Pedido);
GO

IF OBJECT_ID('dbo.RepartoJornadas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoJornadas (
        Id int IDENTITY(1,1) NOT NULL,
        Usuario varchar(50) NOT NULL,
        Fecha date NOT NULL,
        FechaFin datetime NOT NULL,                         -- la del aparato
        BultosPendientes int NOT NULL,                      -- los que quedaban en la furgoneta al terminar
        Pendientes nvarchar(max) NULL,                      -- sus códigos, separados por comas
        ClaveIdempotencia uniqueidentifier NOT NULL,
        FechaRegistro datetime NOT NULL CONSTRAINT DF_RepartoJornadas_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_RepartoJornadas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RepartoJornadas_Clave UNIQUE (ClaveIdempotencia),
        CONSTRAINT CK_RepartoJornadas_Pendientes CHECK (BultosPendientes >= 0)
    );
    CREATE INDEX IX_RepartoJornadas_Fecha ON dbo.RepartoJornadas (Fecha, Usuario);
    PRINT 'RepartoJornadas: creada.';
END
ELSE
    PRINT 'RepartoJornadas: ya existía.';
GO

-- BD de negocio (NestoConnection): el API accede con la cuenta de máquina. Solo se lee y se inserta (evidencia).
GRANT SELECT, INSERT ON dbo.RepartoBultosMovimientos TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT ON dbo.RepartoJornadas TO [NUEVAVISION\RDS2016$];
GO

-- Interruptor (apagado): escanear los bultos al entregar será obligatorio con RepartoEscaneoEntregaObligatorio = '1' bajo «(defecto)».
-- Se crea apagado ('0') para que se vea en la ventana de parámetros; para encenderlo, Valor = '1'.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RepartoEscaneoEntregaObligatorio')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'RepartoEscaneoEntregaObligatorio', '(defecto)', '0', 'Issue772', GETDATE());
GO

SELECT name FROM sys.tables WHERE name IN ('RepartoBultosMovimientos', 'RepartoJornadas');
