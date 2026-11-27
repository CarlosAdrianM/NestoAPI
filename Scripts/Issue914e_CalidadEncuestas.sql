/*
    NestoAPI#914 (encuestas a clientes, corte E1 EN SOMBRA, Carlos 27/11/26): las tablas de las encuestas.

    En E1 NO se envía nada a ningún cliente. El job diario «calidad-encuestas-sombra» (4:45, cola «indicadores») decide a quién se le
    habría preguntado qué AYER y lo guarda aquí con Sombra = 1; GET api/Calidad/Encuestas/Sombra lo resume. La simulación hacia atrás
    (GET api/Calidad/Encuestas/Simulacion) NO escribe nada y no necesita estas tablas.

      dbo.CalidadEncuestas            una fila por encuesta decidida (como mucho una por cliente y día; el gobernador, una cada 60 días).
                                      Cliente, contacto, tema, por qué (Motivo), a qué se enlaza (pedido, factura, envío, evento, producto,
                                      vendedor por el que se pregunta y su nombre, profesora), canal previsto y el intervalo aplicado.
                                      Columnas de E2 YA PREVISTAS Y VACÍAS: EnviadaEl, CanalEnvio, RespondidaEl, Nota (1 mal, 2 regular,
                                      3 bien), Texto. Sombra = 1 en todo lo de E1.
      dbo.CalidadEncuestasDescartes   las candidatas que el gobernador no cogió y por qué (Baja, Impago, TopeEntreEncuestas, TemaRepetido,
                                      SinCanal, OtraCandidataElegida).
      dbo.CalidadEncuestasDias        un día ya procesado (lo que hace el job idempotente) con sus cifras.

    Fuera del EDMX. Sin el script la API funciona igual: el job no hace nada (ELMAH una sola vez) y GET …/Encuestas/Sombra da 503.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces (si una tabla ya existe, no se toca).
    ANTES de lanzarlo: SELECT name, create_date FROM sys.tables WHERE name LIKE 'CalidadEncuestas%';  -- deben no existir.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y (solo la de encuestas) UPDATE, que usará E2 para apuntar el envío y la respuesta. Sin DELETE.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.CalidadEncuestas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CalidadEncuestas (
        Id int IDENTITY(1,1) NOT NULL,
        Empresa char(3) NOT NULL,
        Fecha date NOT NULL,                        -- el día en que se decidió (el de la candidatura)
        Cliente varchar(10) NOT NULL,
        Contacto varchar(3) NOT NULL,
        Tema varchar(30) NOT NULL,                  -- PreparacionEntrega | Vendedor | ProblemaResuelto | AdministracionCobro | InformacionTecnica
        ClaveTema varchar(60) NOT NULL,             -- el tema y, en el de Vendedor, «Vendedor|<código>» (no repetir se mira por vendedor)
        Origen varchar(50) NOT NULL,                -- la fuente (EntregaPedido, VendedorPrimerPedido, ProblemaRectificativa, …)
        Clase varchar(20) NOT NULL,                 -- ProblemaResuelto | Cambio | PrimeraVez | Otra (la prioridad)
        Motivo nvarchar(400) NULL,                  -- de usted: por qué se le pregunta
        FechaEvento datetime NOT NULL,
        Pedido int NULL,
        Factura varchar(20) NULL,
        Envio int NULL,
        EventoId int NULL,
        Producto varchar(20) NULL,
        NombreProducto nvarchar(100) NULL,
        Vendedor varchar(10) NULL,                  -- el vendedor POR EL QUE SE PREGUNTA (al que se avisará de una nota mala en E3)
        NombreVendedor nvarchar(100) NULL,          -- Vendedores.Descripción
        AmbitoVendedor varchar(20) NULL,            -- Estetica | Peluqueria
        Profesora nvarchar(100) NULL,               -- Eventos.Ponente, si consta
        CanalPrevisto varchar(10) NOT NULL,         -- Push | Correo
        Correo varchar(255) NULL,
        IntervaloDias int NOT NULL,                 -- el intervalo que aplicó el gobernador (con la duplicación por ignoradas)
        Sombra bit NOT NULL CONSTRAINT DF_CalidadEncuestas_Sombra DEFAULT (1),
        -- E2 (vacías en E1)
        EnviadaEl datetime NULL,
        CanalEnvio varchar(10) NULL,
        RespondidaEl datetime NULL,
        Nota tinyint NULL,
        Texto nvarchar(2000) NULL,
        Creado datetime NOT NULL CONSTRAINT DF_CalidadEncuestas_Creado DEFAULT (GETDATE()),
        CONSTRAINT PK_CalidadEncuestas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_CalidadEncuestas_ClienteDia UNIQUE (Empresa, Cliente, Fecha),
        CONSTRAINT CK_CalidadEncuestas_Nota CHECK (Nota IS NULL OR Nota IN (1, 2, 3)),
        CONSTRAINT CK_CalidadEncuestas_Canal CHECK (CanalPrevisto IN ('Push', 'Correo'))
    );
    PRINT 'CalidadEncuestas: creada.';
END
ELSE
    PRINT 'CalidadEncuestas: ya existía (no se toca).';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CalidadEncuestas_Fecha' AND object_id = OBJECT_ID('dbo.CalidadEncuestas'))
    CREATE INDEX IX_CalidadEncuestas_Fecha ON dbo.CalidadEncuestas (Empresa, Fecha);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CalidadEncuestas_Pedido' AND object_id = OBJECT_ID('dbo.CalidadEncuestas'))
    CREATE INDEX IX_CalidadEncuestas_Pedido ON dbo.CalidadEncuestas (Empresa, Pedido);
GO

IF OBJECT_ID('dbo.CalidadEncuestasDescartes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CalidadEncuestasDescartes (
        Id int IDENTITY(1,1) NOT NULL,
        Empresa char(3) NOT NULL,
        Fecha date NOT NULL,
        Cliente varchar(10) NOT NULL,
        Contacto varchar(3) NOT NULL,
        Tema varchar(30) NOT NULL,
        ClaveTema varchar(60) NOT NULL,
        Origen varchar(50) NOT NULL,
        Motivo varchar(30) NOT NULL,                -- Baja | Impago | TopeEntreEncuestas | TemaRepetido | SinCanal | OtraCandidataElegida
        FechaEvento datetime NOT NULL,
        Pedido int NULL,
        Vendedor varchar(10) NULL,
        CanalPrevisto varchar(10) NULL,
        Creado datetime NOT NULL CONSTRAINT DF_CalidadEncuestasDescartes_Creado DEFAULT (GETDATE()),
        CONSTRAINT PK_CalidadEncuestasDescartes PRIMARY KEY CLUSTERED (Id)
    );
    PRINT 'CalidadEncuestasDescartes: creada.';
END
ELSE
    PRINT 'CalidadEncuestasDescartes: ya existía (no se toca).';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CalidadEncuestasDescartes_Fecha' AND object_id = OBJECT_ID('dbo.CalidadEncuestasDescartes'))
    CREATE INDEX IX_CalidadEncuestasDescartes_Fecha ON dbo.CalidadEncuestasDescartes (Empresa, Fecha);
GO

IF OBJECT_ID('dbo.CalidadEncuestasDias', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CalidadEncuestasDias (
        Empresa char(3) NOT NULL,
        Fecha date NOT NULL,
        Candidatas int NOT NULL,
        Encuestas int NOT NULL,
        Descartes int NOT NULL,
        IntervaloDias int NOT NULL,
        Procesado datetime NOT NULL CONSTRAINT DF_CalidadEncuestasDias_Procesado DEFAULT (GETDATE()),
        CONSTRAINT PK_CalidadEncuestasDias PRIMARY KEY CLUSTERED (Empresa, Fecha)
    );
    PRINT 'CalidadEncuestasDias: creada.';
END
ELSE
    PRINT 'CalidadEncuestasDias: ya existía (no se toca).';
GO

GRANT SELECT, INSERT, UPDATE ON dbo.CalidadEncuestas TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT ON dbo.CalidadEncuestasDescartes TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT ON dbo.CalidadEncuestasDias TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT t.name AS Tabla, t.create_date AS Creada, (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS Columnas
FROM sys.tables t WHERE t.name IN ('CalidadEncuestas', 'CalidadEncuestasDescartes', 'CalidadEncuestasDias') ORDER BY t.name;
GO
