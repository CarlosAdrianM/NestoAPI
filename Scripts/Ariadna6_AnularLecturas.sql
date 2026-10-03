-- Ariadna#6: descartar lo que un mozo ha leído en una salida (una prueba olvidada en la cola de la PDA).
-- Lanzar como sa en NV ANTES de publicar la API que lo trae (si no, «Descartar» da error; terminar una salida
-- sigue funcionando igual: la marca de terminada solo se apunta si la tabla existe).
--
-- 1. PreparacionSalidasTerminadas: la API apunta aquí cada salida (PICK/REPO) que se termina desde Ariadna. Después
--    ya no se puede anular lo leído en ella: lo que faltaba ya se ha quitado del pedido.
-- 2. PreparacionAnulacionesLecturas: quién anuló las lecturas de qué mozo, en qué salida y cuándo. Las lecturas no se
--    borran: se añade la misma lectura en negativo (como «Deshacer»), así que la evidencia sigue en PreparacionEscaneos.

USE NV;
GO

IF OBJECT_ID('dbo.PreparacionSalidasTerminadas') IS NULL
BEGIN
    CREATE TABLE dbo.PreparacionSalidasTerminadas (
        Id int IDENTITY(1,1) NOT NULL,
        Empresa char(3) NOT NULL,
        TipoOrigen char(4) NOT NULL,               -- PICK / REPO
        NumeroOrigen int NOT NULL,                 -- Picking o traspaso
        Usuario varchar(50) NOT NULL,              -- Quién la terminó
        Fecha datetime NOT NULL CONSTRAINT DF_PreparacionSalidasTerminadas_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_PreparacionSalidasTerminadas PRIMARY KEY (Id),
        CONSTRAINT UQ_PreparacionSalidasTerminadas UNIQUE (Empresa, TipoOrigen, NumeroOrigen)
    );
END
GO

IF OBJECT_ID('dbo.PreparacionAnulacionesLecturas') IS NULL
BEGIN
    CREATE TABLE dbo.PreparacionAnulacionesLecturas (
        Id int IDENTITY(1,1) NOT NULL,
        Empresa char(3) NOT NULL,
        TipoOrigen char(4) NOT NULL,
        NumeroOrigen int NOT NULL,
        UsuarioLecturas varchar(50) NOT NULL,      -- El mozo cuyas lecturas se anulan
        AnuladoPor varchar(50) NOT NULL,           -- Admin o Dirección
        Filas int NOT NULL,                        -- Lecturas en negativo añadidas (0 si no tenía nada subido)
        Fecha datetime NOT NULL CONSTRAINT DF_PreparacionAnulacionesLecturas_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_PreparacionAnulacionesLecturas PRIMARY KEY (Id)
    );
    CREATE INDEX IX_PreparacionAnulacionesLecturas_Origen ON dbo.PreparacionAnulacionesLecturas (Empresa, TipoOrigen, NumeroOrigen);
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PreparacionSalidasTerminadas TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PreparacionAnulacionesLecturas TO [NUEVAVISION\RDS2016$];
GO

SELECT name FROM sys.tables WHERE name IN ('PreparacionSalidasTerminadas', 'PreparacionAnulacionesLecturas');
