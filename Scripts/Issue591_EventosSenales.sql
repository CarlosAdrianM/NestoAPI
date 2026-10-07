/*
    NestoAPI#591 (MVP): eventos (cursos, masterclass…) y señales reembolsables.

    Ejecutar en SSMS contra NV como sa (crea dos tablas: el login nuevavision no tiene ALTER). Idempotente: se puede
    lanzar dos veces. Lanzarlo ANTES de publicar la API: las dos tablas están en el EDMX y, sin ellas, api/Eventos falla.

    - Eventos: lo que mantiene Tienda online (y Dirección/Informática): título, fecha, importe de la señal y si está activo.
    - EventosSenales: qué apunte a favor del extracto del cliente (ExtractoCliente.[Nº Orden]) es la señal de qué evento.
      Lo marca Administración (y Dirección/Informática) desde el extracto del cliente en Nesto. Un apunte solo puede ser
      señal de UN evento (UQ_EventosSenales_Apunte). El estado (Pendiente / Liberada / Sin compra / Consumida) no se
      guarda: lo calcula la API con la fecha del evento y el ImportePdte del apunte.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Eventos
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.Eventos') IS NULL
BEGIN
    CREATE TABLE dbo.Eventos (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Titulo nvarchar(100) NOT NULL,
        Fecha date NOT NULL,                            -- desde ese día la señal está liberada
        ImporteSenal decimal(18, 2) NOT NULL,           -- lo que se cobra para reservar la plaza (orientativo)
        Activo bit NOT NULL CONSTRAINT DF_Eventos_Activo DEFAULT (1),
        Usuario varchar(30) NOT NULL CONSTRAINT DF_Eventos_Usuario DEFAULT (SUSER_SNAME()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_Eventos_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_Eventos PRIMARY KEY (Id),
        CONSTRAINT CK_Eventos_ImporteSenal CHECK (ImporteSenal >= 0)
    );
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Eventos TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- 2. EventosSenales
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.EventosSenales') IS NULL
BEGIN
    CREATE TABLE dbo.EventosSenales (
        Id int IDENTITY(1, 1) NOT NULL,
        EventoId int NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,                      -- ExtractoCliente.Número
        Contacto char(3) NOT NULL,                      -- ExtractoCliente.Contacto
        NumOrdenExtracto int NOT NULL,                  -- ExtractoCliente.[Nº Orden] del apunte a favor que es la señal
        Importe decimal(18, 2) NOT NULL,                -- importe original del apunte a favor (positivo); lo pendiente se lee de ExtractoCliente
        Usuario varchar(30) NOT NULL CONSTRAINT DF_EventosSenales_Usuario DEFAULT (SUSER_SNAME()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_EventosSenales_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_EventosSenales PRIMARY KEY (Id),
        CONSTRAINT FK_EventosSenales_Eventos FOREIGN KEY (EventoId) REFERENCES dbo.Eventos (Id),
        CONSTRAINT UQ_EventosSenales_Apunte UNIQUE (Empresa, NumOrdenExtracto),
        CONSTRAINT CK_EventosSenales_Importe CHECK (Importe > 0)
    );
    CREATE INDEX IX_EventosSenales_Cliente ON dbo.EventosSenales (Empresa, Cliente, Contacto);
    CREATE INDEX IX_EventosSenales_Evento ON dbo.EventosSenales (EventoId);
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosSenales TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: las dos tablas, vacías la primera vez
------------------------------------------------------------------------------------------------
SELECT 'Eventos' AS Tabla, COUNT(*) AS Filas FROM dbo.Eventos
UNION ALL
SELECT 'EventosSenales', COUNT(*) FROM dbo.EventosSenales;
GO
