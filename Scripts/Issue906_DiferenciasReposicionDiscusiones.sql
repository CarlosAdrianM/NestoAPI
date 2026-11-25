/*
    NestoAPI#906 (sugerencia 1433, Santiago; Carlos 25/11/26): «En discusión» en Diferencias de reposición.

    Ejecutar en SSMS contra NV como sa (CREATE, ALTER y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. No está en el EDMX (la API lo lee y escribe con SQL a mano), así que se puede lanzar antes o después del deploy: sin él,
    los endpoints nuevos dan 503, los jobs no hacen nada y Diferencias de reposición sigue exactamente como hoy.
    Requiere Scripts/Issue634_DiferenciasReposiciones.sql.

    1) CK_DiferenciasReposiciones_Resolucion admite 'Desacuerdo' (lo que decide la regla cuando origen y destino no se ponen de
       acuerdo). Lección #698: valor nuevo en columna con CHECK → rehacer la restricción.
    2) DiferenciasReposicionDiscusiones: una por diferencia (FALTA) en discusión. Estado Abierta / Acordada (el destino lo encontró, o
       el origen resolvió como siempre) / Decidida (lo decidió la regla).
    3) DiferenciasReposicionImputaciones: una fila por UNIDAD discutida con los rasgos del momento (para medir la regla y entrenar algún
       día un modelo), la decisión (AlmacenImputado, Paso Prueba/Indicador/Alternancia, Motivo) y la VERDAD que llega después
       (ErrorDemostradoDe, ComoSeDemostro Reconocido/Prueba/Inventario, FechaDemostracion, InventarioDemostracion = Inventarios.NºOrden
       que la demostró, ReglaAcerto).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE. Sin DELETE: las filas no se borran (son la constancia).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.DiferenciasReposiciones') IS NULL
BEGIN
    RAISERROR('Falta dbo.DiferenciasReposiciones (Scripts/Issue634_DiferenciasReposiciones.sql). No se ha cambiado nada.', 16, 1);
    SET NOEXEC ON;
END
GO

-- 1) La resolución «Desacuerdo»
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DiferenciasReposiciones_Resolucion'
           AND parent_object_id = OBJECT_ID('dbo.DiferenciasReposiciones')
           AND definition NOT LIKE '%''Desacuerdo''%')
    ALTER TABLE dbo.DiferenciasReposiciones DROP CONSTRAINT CK_DiferenciasReposiciones_Resolucion;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DiferenciasReposiciones_Resolucion'
               AND parent_object_id = OBJECT_ID('dbo.DiferenciasReposiciones'))
    ALTER TABLE dbo.DiferenciasReposiciones ADD CONSTRAINT CK_DiferenciasReposiciones_Resolucion
        CHECK (Resolucion IS NULL OR Resolucion IN ('Confirmada', 'NoEstaba', 'Sobraba', 'ErrorRecepcion', 'Desacuerdo'));
GO

-- 2) La discusión
IF OBJECT_ID('dbo.DiferenciasReposicionDiscusiones') IS NULL
BEGIN
    CREATE TABLE dbo.DiferenciasReposicionDiscusiones (
        Id int IDENTITY(1, 1) NOT NULL,
        DiferenciaId int NOT NULL,                      -- DiferenciasReposiciones.Id
        Empresa char(3) NOT NULL,
        NumTraspaso int NOT NULL,
        Origen char(3) NOT NULL,
        Destino char(3) NOT NULL,
        Producto varchar(15) NOT NULL,
        Unidades int NOT NULL,                          -- las que faltan (−Diferencia)
        Estado varchar(10) NOT NULL CONSTRAINT DF_DiferenciasReposicionDiscusiones_Estado DEFAULT ('Abierta'),
        AbiertaPor varchar(50) NOT NULL,
        FechaApertura datetime NOT NULL CONSTRAINT DF_DiferenciasReposicionDiscusiones_FechaApertura DEFAULT (GETDATE()),
        ComentarioOrigen nvarchar(500) NOT NULL,        -- lo que comprobó el origen
        Plazo datetime NOT NULL,                        -- hasta cuándo puede contestar el destino (Reposiciones.DiscusionPlazoHoras)
        RecordatorioEnviado datetime NULL,
        Respuesta varchar(20) NULL,                     -- LoHeEncontrado / NoEsta
        RespondidaPor varchar(50) NULL,
        FechaRespuesta datetime NULL,
        ComentarioDestino nvarchar(500) NULL,
        FechaCierre datetime NULL,
        Resumen nvarchar(1000) NULL,                    -- qué decidió la regla o cómo se pusieron de acuerdo

        CONSTRAINT PK_DiferenciasReposicionDiscusiones PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_DiferenciasReposicionDiscusiones_Diferencia UNIQUE (DiferenciaId),
        CONSTRAINT FK_DiferenciasReposicionDiscusiones_Diferencia FOREIGN KEY (DiferenciaId) REFERENCES dbo.DiferenciasReposiciones (Id),
        CONSTRAINT CK_DiferenciasReposicionDiscusiones_Estado CHECK (Estado IN ('Abierta', 'Acordada', 'Decidida')),
        CONSTRAINT CK_DiferenciasReposicionDiscusiones_Respuesta CHECK (Respuesta IS NULL OR Respuesta IN ('LoHeEncontrado', 'NoEsta')),
        CONSTRAINT CK_DiferenciasReposicionDiscusiones_Unidades CHECK (Unidades > 0)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DiferenciasReposicionDiscusiones_Estado' AND object_id = OBJECT_ID('dbo.DiferenciasReposicionDiscusiones'))
    CREATE INDEX IX_DiferenciasReposicionDiscusiones_Estado ON dbo.DiferenciasReposicionDiscusiones (Empresa, Estado, Plazo);
GO

-- 3) Una fila por unidad: rasgos, decisión y verdad
IF OBJECT_ID('dbo.DiferenciasReposicionImputaciones') IS NULL
BEGIN
    CREATE TABLE dbo.DiferenciasReposicionImputaciones (
        Id int IDENTITY(1, 1) NOT NULL,
        DiscusionId int NOT NULL,
        Unidad smallint NOT NULL,                       -- 1..Unidades: una unidad nunca se parte
        -- Rasgos del momento (al abrir la discusión)
        Pvp decimal(10, 2) NULL,
        Preparador varchar(50) NULL,
        UsuarioRecepcion varchar(50) NULL,
        Repartidor varchar(50) NULL,                    -- quien la recogió o cargó (RepartoBultosMovimientos)
        LecturaOrigen bit NOT NULL,                     -- esta unidad la leyó el origen por escáner al preparar
        EscaneadasOrigen int NOT NULL,                  -- PreparacionEscaneos REPO, Metodo SCAN (lo tecleado no cuenta)
        Enviado int NOT NULL,
        LecturaDestino bit NOT NULL,                    -- el destino leyó algún bulto al recibir (ReposicionesBultos.FechaLectura)
        Bultos smallint NOT NULL,
        BultosEnDestino smallint NOT NULL,              -- leídos al recibir o «Entregado» por el repartidor
        FechaRecepcion datetime NULL,
        IndicadorOrigen decimal(10, 4) NULL,            -- errores demostrados / discrepancias, 12 meses, al abrir
        IndicadorDestino decimal(10, 4) NULL,
        -- La decisión de la regla
        AlmacenImputado char(3) NULL,
        Paso varchar(12) NULL,
        Motivo nvarchar(500) NULL,
        FechaDecision datetime NULL,
        IndicadorOrigenDecision decimal(10, 4) NULL,    -- los que usó la regla al decidir
        IndicadorDestinoDecision decimal(10, 4) NULL,
        -- La verdad, cuando se sabe (no se pisa)
        ErrorDemostradoDe char(3) NULL,
        ComoSeDemostro varchar(12) NULL,
        FechaDemostracion datetime NULL,
        InventarioDemostracion int NULL,                -- Inventarios.[NºOrden] con el sobrante (cada uno gasta una vez)
        ReglaAcerto bit NULL,
        FechaRegistro datetime NOT NULL CONSTRAINT DF_DiferenciasReposicionImputaciones_FechaRegistro DEFAULT (GETDATE()),

        CONSTRAINT PK_DiferenciasReposicionImputaciones PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_DiferenciasReposicionImputaciones_Unidad UNIQUE (DiscusionId, Unidad),
        CONSTRAINT FK_DiferenciasReposicionImputaciones_Discusion FOREIGN KEY (DiscusionId) REFERENCES dbo.DiferenciasReposicionDiscusiones (Id),
        CONSTRAINT CK_DiferenciasReposicionImputaciones_Paso CHECK (Paso IS NULL OR Paso IN ('Prueba', 'Indicador', 'Alternancia')),
        CONSTRAINT CK_DiferenciasReposicionImputaciones_Como CHECK (ComoSeDemostro IS NULL OR ComoSeDemostro IN ('Reconocido', 'Prueba', 'Inventario'))
    );
END
GO

-- El turno de la alternancia por pareja y la búsqueda de la verdad
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DiferenciasReposicionImputaciones_Paso' AND object_id = OBJECT_ID('dbo.DiferenciasReposicionImputaciones'))
    CREATE INDEX IX_DiferenciasReposicionImputaciones_Paso ON dbo.DiferenciasReposicionImputaciones (Paso, FechaDecision);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.DiferenciasReposicionDiscusiones TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE ON dbo.DiferenciasReposicionImputaciones TO [NUEVAVISION\RDS2016$];
GO

SET NOEXEC OFF;
GO
