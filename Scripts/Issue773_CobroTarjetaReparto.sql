/*
    NestoAPI#773 (31/10/26): reparto propio — cobrar con tarjeta desde la PDA (enlace Redsys por WhatsApp o correo) y autorización
    para entregar un reembolso sin cobrar.

    Ejecutar en SSMS contra NV como sa (CREATE, ALTER y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    Nada de esto está en el EDMX (la API lo lee y escribe con SQL a mano), así que se puede lanzar antes o después del deploy.
    Después de lanzarlo, reciclar el app pool (la API recuerda qué tablas y columnas existen).

    Sin el script, todo como antes: «Tarjeta» da 400 «sin datáfono», los endpoints nuevos dan 400 «falta el script» y entregar sin
    cobrar NO pide autorización. Con el script, entregar con reembolso sin cobrarlo exige autorización: las PDA con una Ariadna sin
    «Pedir autorización» reciben un 400 que dice que la actualicen (publicar Ariadna a la vez).

    1. CK_EnviosAgenciaPruebasEntrega_Cobro admite 'Tarjeta' (el enlace con tarjeta del reparto, ya pagado). Antes que las tablas: la
       API solo acepta «Tarjeta» si existe RepartoCobrosTarjeta (lección de #698: valor nuevo en una columna con CHECK).
    2. CK_EnviosAgenciaPruebasEntrega_Motivo admite 'ReembolsoNoCobrado' (la incidencia cuando Administración deniega no cobrar).
    3. RepartoCobrosTarjeta: cada enlace con tarjeta que genera un repartidor (el PagosTPV raíz; si Redsys lo deniega, ServicioPagos
       genera otro con PagoOriginalId y la cadena se sigue desde el raíz). Para reutilizar el vivo, avisar a la PDA al pagarse y que la
       notificación de Redsys no mande el «URGENTE» de #679. Tipo: Reembolso (EnvioId) o Deuda (Efectos = Nº Orden ordenados, «1,2»).
    4. RepartoAutorizacionesReembolso: las peticiones de autorización (Pendiente → Autorizada / Denegada) y las autorizaciones por
       teléfono (Telefonica: Respondio = lo que escribió el repartidor).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT a la cuenta máquina con la que corre la API (NUEVAVISION\RDS2016$).
*/

SET NOCOUNT ON;
USE NV;
GO

-- 1. Cobro nuevo: Tarjeta ---------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnviosAgenciaPruebasEntrega_Cobro' AND definition NOT LIKE '%Tarjeta%')
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega DROP CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Cobro;
    PRINT 'EnviosAgenciaPruebasEntrega: CHECK del cobro sin Tarjeta, quitada.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnviosAgenciaPruebasEntrega_Cobro')
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Cobro
        CHECK (CobroReembolso IS NULL OR CobroReembolso IN ('Efectivo', 'Tarjeta', 'Enlace', 'NoCobrado'));
    PRINT 'EnviosAgenciaPruebasEntrega: CHECK del cobro con Tarjeta.';
END
GO

-- 2. Motivo nuevo: ReembolsoNoCobrado ---------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnviosAgenciaPruebasEntrega_Motivo' AND definition NOT LIKE '%ReembolsoNoCobrado%')
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega DROP CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Motivo;
    PRINT 'EnviosAgenciaPruebasEntrega: CHECK del motivo sin ReembolsoNoCobrado, quitada.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnviosAgenciaPruebasEntrega_Motivo')
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Motivo
        CHECK (Motivo IS NULL OR Motivo IN ('Ausente', 'DireccionIncorrecta', 'Rechazado', 'FaltanBultos', 'NadaQueRecoger', 'ReembolsoNoCobrado', 'Otro'));
    PRINT 'EnviosAgenciaPruebasEntrega: CHECK del motivo con ReembolsoNoCobrado.';
END
GO

-- 3. Los enlaces con tarjeta del reparto ------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.RepartoCobrosTarjeta', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoCobrosTarjeta (
        Id int IDENTITY(1,1) NOT NULL,
        PagoTPVId int NOT NULL,                     -- PagosTPV.Id del enlace (el raíz)
        Tipo varchar(10) NOT NULL,                  -- Reembolso / Deuda
        EnvioId int NULL,                           -- EnviosAgencia.Numero (Reembolso)
        Empresa char(3) NULL,
        Cliente varchar(10) NULL,
        Contacto varchar(3) NULL,
        Importe decimal(18,2) NOT NULL,
        Efectos varchar(1000) NULL,                 -- Deuda: los Nº Orden de ExtractoCliente, ordenados («123,456»)
        Usuario varchar(50) NOT NULL,               -- el repartidor que lo genera
        Dispositivo nvarchar(100) NULL,             -- la PDA (X-Dispositivo)
        FechaCreacion datetime NOT NULL CONSTRAINT DF_RepartoCobrosTarjeta_FechaCreacion DEFAULT (GETDATE()),
        CONSTRAINT PK_RepartoCobrosTarjeta PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RepartoCobrosTarjeta_PagoTPVId UNIQUE (PagoTPVId),
        CONSTRAINT FK_RepartoCobrosTarjeta_PagosTPV FOREIGN KEY (PagoTPVId) REFERENCES dbo.PagosTPV (Id),
        CONSTRAINT CK_RepartoCobrosTarjeta_Tipo CHECK (Tipo IN ('Reembolso', 'Deuda')),
        CONSTRAINT CK_RepartoCobrosTarjeta_Importe CHECK (Importe > 0)
    );
    CREATE INDEX IX_RepartoCobrosTarjeta_EnvioId ON dbo.RepartoCobrosTarjeta (EnvioId, Tipo);
    CREATE INDEX IX_RepartoCobrosTarjeta_Cliente ON dbo.RepartoCobrosTarjeta (Cliente, Tipo);
    PRINT 'RepartoCobrosTarjeta: creada.';
END
ELSE
    PRINT 'RepartoCobrosTarjeta: ya existía.';
GO

GRANT SELECT, INSERT ON dbo.RepartoCobrosTarjeta TO [NUEVAVISION\RDS2016$];
GO

-- 4. Las autorizaciones para entregar sin cobrar ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.RepartoAutorizacionesReembolso', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoAutorizacionesReembolso (
        Id int IDENTITY(1,1) NOT NULL,
        EnvioId int NOT NULL,                       -- EnviosAgencia.Numero
        Importe decimal(18,2) NOT NULL,             -- el reembolso que no se cobra
        Repartidor varchar(50) NOT NULL,            -- quien la pide, sin dominio
        FechaSolicitud datetime NOT NULL,
        Motivo nvarchar(500) NULL,                  -- por qué no se cobra (en las telefónicas sin pedir antes, NULL)
        Estado varchar(12) NOT NULL,                -- Pendiente / Autorizada / Denegada / Telefonica
        Respondio nvarchar(100) NULL,               -- quien contestó (sin dominio) o, en las telefónicas, lo que escribió el repartidor
        FechaRespuesta datetime NULL,
        Comentario nvarchar(500) NULL,
        CONSTRAINT PK_RepartoAutorizacionesReembolso PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_RepartoAutorizacionesReembolso_EnviosAgencia FOREIGN KEY (EnvioId) REFERENCES dbo.EnviosAgencia (Numero),
        CONSTRAINT CK_RepartoAutorizacionesReembolso_Estado CHECK (Estado IN ('Pendiente', 'Autorizada', 'Denegada', 'Telefonica'))
    );
    CREATE INDEX IX_RepartoAutorizacionesReembolso_EnvioId ON dbo.RepartoAutorizacionesReembolso (EnvioId);
    PRINT 'RepartoAutorizacionesReembolso: creada.';
END
ELSE
    PRINT 'RepartoAutorizacionesReembolso: ya existía.';
GO

GRANT SELECT, INSERT, UPDATE ON dbo.RepartoAutorizacionesReembolso TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación: las dos tablas y las CHECK con Tarjeta y ReembolsoNoCobrado.
SELECT name FROM sys.tables WHERE name IN ('RepartoCobrosTarjeta', 'RepartoAutorizacionesReembolso');
SELECT name, definition FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.EnviosAgenciaPruebasEntrega');
GO
