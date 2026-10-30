/*
    NestoAPI#759 (corte 3 de la épica #755): paradas del reparto propio.

    Ejecutar en SSMS contra NV como sa (CREATE, ALTER y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar
    dos veces. Nada de esto está en el EDMX (la API lo lee y escribe con SQL a mano), así que se puede lanzar antes o después
    del deploy. Sin el script:
      - PUT api/RepartoPropio/Orden responde 400 «falta el script» y la lista sale sin Orden (como hasta ahora);
      - cargar un bulto de reposición (R{traspaso}-{N}) responde 400 «falta el script»;
      - los bultos recogidos de una nota de recogida solo quedan en la historia del envío;
      - el motivo NadaQueRecoger responde 400 «falta el script» (la CHECK de Motivo no lo admitiría: lección de #698).
    Después de lanzarlo, reciclar el app pool (la API recuerda qué tablas y columnas existen).

    1. RepartoPropioOrdenParadas: el orden de cada parada que le da SU repartidor desde la PDA (último gana por FechaHora).
       ParadaId = el EnvioId de la parada en la API: positivo = EnviosAgencia.Numero; negativo = −RepartoParadasReposicion.Id.
       Por eso no lleva FK (es de dos tablas).
    2. RepartoParadasReposicion + RepartoParadasReposicionBultos: la parada «Reposición {tienda}» (Reina o Alcobendas) que se crea
       al cargar el primer bulto R{traspaso}-{N} de ese día para esa tienda (UNA por tienda y día aunque lleve varios traspasos),
       y cada bulto cargado. La recepción en la tienda NO cambia (sigue leyendo ReposicionesBultos como siempre).
    3. EnviosAgenciaPruebasEntrega.BultosRecogidos (los bultos que se trae el repartidor de una nota de recogida).
    4. CK_EnviosAgenciaPruebasEntrega_Motivo admite 'NadaQueRecoger'.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT a la cuenta máquina con la que corre la API (NUEVAVISION\RDS2016$).
*/

SET NOCOUNT ON;
USE NV;
GO

-- 1. Orden de las paradas ---------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.RepartoPropioOrdenParadas') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoPropioOrdenParadas (
        ParadaId int NOT NULL,                          -- EnviosAgencia.Numero, o −RepartoParadasReposicion.Id
        Orden int NOT NULL,                             -- posición 1..n en la lista del repartidor
        Usuario varchar(50) NOT NULL,                   -- quién la ordenó (su repartidor)
        FechaHora datetime NOT NULL,                    -- la hora de la PDA (último gana)
        FechaRegistro datetime NOT NULL CONSTRAINT DF_RepartoPropioOrdenParadas_FechaRegistro DEFAULT (GETDATE()),

        CONSTRAINT PK_RepartoPropioOrdenParadas PRIMARY KEY CLUSTERED (ParadaId),
        CONSTRAINT CK_RepartoPropioOrdenParadas_Orden CHECK (Orden > 0)
    );
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RepartoPropioOrdenParadas TO [NUEVAVISION\RDS2016$];
GO

-- 2. Paradas de reposición --------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.RepartoParadasReposicion') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoParadasReposicion (
        Id int IDENTITY(1, 1) NOT NULL,
        Fecha date NOT NULL,                            -- el día de la carga
        Destino char(3) NOT NULL,                       -- REI o ALC (el destino del traspaso)
        Estado varchar(12) NOT NULL CONSTRAINT DF_RepartoParadasReposicion_Estado DEFAULT ('EnReparto'),
        Usuario varchar(50) NOT NULL,                   -- quien la creó (el primero que cargó un bulto)
        FechaRegistro datetime NOT NULL CONSTRAINT DF_RepartoParadasReposicion_FechaRegistro DEFAULT (GETDATE()),
        FechaEntrega datetime NULL,
        UsuarioEntrega varchar(50) NULL,
        ClaveEntrega uniqueidentifier NULL,             -- idempotencia de «Entregar» (sin UNIQUE: admite varios NULL)
        Receptor nvarchar(100) NULL,
        Observaciones nvarchar(1000) NULL,
        Latitud decimal(9, 6) NULL,
        Longitud decimal(9, 6) NULL,

        CONSTRAINT PK_RepartoParadasReposicion PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_RepartoParadasReposicion_Estado CHECK (Estado IN ('EnReparto', 'Entregada'))
    );
END
GO

-- La parada abierta de una tienda en un día (al cargar) y las de la lista (por fecha)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoParadasReposicion_Fecha' AND object_id = OBJECT_ID('dbo.RepartoParadasReposicion'))
    CREATE INDEX IX_RepartoParadasReposicion_Fecha ON dbo.RepartoParadasReposicion (Fecha, Destino, Estado);
GO

IF OBJECT_ID('dbo.RepartoParadasReposicionBultos') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoParadasReposicionBultos (
        Id int IDENTITY(1, 1) NOT NULL,
        ParadaId int NOT NULL,
        Empresa char(3) NOT NULL,
        NumTraspaso int NOT NULL,
        Bulto smallint NOT NULL,                        -- el N de R{traspaso}-{N}
        Total smallint NOT NULL,                        -- ReposicionesBultos.Total al cargarlo
        Usuario varchar(50) NOT NULL,                   -- quien lo cargó (el de la última carga es el dueño de la parada)
        FechaHora datetime NOT NULL,                    -- la hora de la PDA
        ClaveIdempotencia uniqueidentifier NOT NULL,
        FechaRegistro datetime NOT NULL CONSTRAINT DF_RepartoParadasReposicionBultos_FechaRegistro DEFAULT (GETDATE()),

        CONSTRAINT PK_RepartoParadasReposicionBultos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RepartoParadasReposicionBultos_Clave UNIQUE (ClaveIdempotencia),
        CONSTRAINT FK_RepartoParadasReposicionBultos_Parada FOREIGN KEY (ParadaId) REFERENCES dbo.RepartoParadasReposicion (Id),
        CONSTRAINT CK_RepartoParadasReposicionBultos_Bulto CHECK (Bulto > 0 AND Bulto <= Total)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoParadasReposicionBultos_Parada' AND object_id = OBJECT_ID('dbo.RepartoParadasReposicionBultos'))
    CREATE INDEX IX_RepartoParadasReposicionBultos_Parada ON dbo.RepartoParadasReposicionBultos (ParadaId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoParadasReposicionBultos_Traspaso' AND object_id = OBJECT_ID('dbo.RepartoParadasReposicionBultos'))
    CREATE INDEX IX_RepartoParadasReposicionBultos_Traspaso ON dbo.RepartoParadasReposicionBultos (Empresa, NumTraspaso, Bulto);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.RepartoParadasReposicion TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT ON dbo.RepartoParadasReposicionBultos TO [NUEVAVISION\RDS2016$];
GO

-- 3. Bultos recogidos -------------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.EnviosAgenciaPruebasEntrega', 'BultosRecogidos') IS NULL
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD BultosRecogidos smallint NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnviosAgenciaPruebasEntrega_BultosRecogidos')
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD CONSTRAINT CK_EnviosAgenciaPruebasEntrega_BultosRecogidos
        CHECK (BultosRecogidos IS NULL OR (BultosRecogidos >= 0 AND BultosRecogidos <= 999));
GO

-- 4. Motivo nuevo: NadaQueRecoger (feedback_check_constraint_valor_nuevo, #698) --------------------------------------------
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnviosAgenciaPruebasEntrega_Motivo' AND definition NOT LIKE '%NadaQueRecoger%')
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega DROP CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Motivo;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnviosAgenciaPruebasEntrega_Motivo')
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Motivo
        CHECK (Motivo IS NULL OR Motivo IN ('Ausente', 'DireccionIncorrecta', 'Rechazado', 'FaltanBultos', 'NadaQueRecoger', 'Otro'));
GO

-- Comprobación: tienen que salir las tres tablas, la columna y la CHECK con NadaQueRecoger.
SELECT name FROM sys.tables WHERE name IN ('RepartoPropioOrdenParadas', 'RepartoParadasReposicion', 'RepartoParadasReposicionBultos');
SELECT COL_LENGTH('dbo.EnviosAgenciaPruebasEntrega', 'BultosRecogidos') AS BultosRecogidos;
SELECT name, definition FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.EnviosAgenciaPruebasEntrega');
GO
