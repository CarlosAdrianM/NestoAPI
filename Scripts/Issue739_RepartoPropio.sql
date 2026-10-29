/*
    NestoAPI#739 (29/10/26): el reparto propio (rutas AT y 16) como una agencia más.

    1. AgenciasTransporte: fila 14 «Reparto propio» (empresa 1). Sin Ruta a propósito: Nesto elige la agencia por defecto de
       una ruta con esa columna, y quien decide si un pedido de AT/16 va por el reparto propio es la API (interruptor
       Agencias:RepartoPropio del Web.config). Sin Identificador ni PrefijoCodigoBarras: el «albarán» es RP{envío} y cada bulto
       RP{envío}-{N}, los numera la API.
       CuentaReembolsos = NULL: es la CUENTA DE LA CAJA DEL REPARTIDOR y LA PONE CARLOS. El reembolso del reparto propio se
       contabiliza AL ENTREGAR (lo cobrado en efectivo o tarjeta, contra esta cuenta); sin cuenta, entregar con reembolso cobrado
       da 400 «la agencia no tiene cuenta de caja». Luego contabilidad hace el arqueo en Cajas de Nesto como cualquier caja.
    2. EnviosAgenciaPruebasEntrega: las entregas e incidencias que registra el repartidor en Ariadna (api/RepartoPropio).
       Firma y DNI son DATOS PERSONALES: solo viven aquí (nunca en ELMAH, Modificaciones ni EnviosHistoria) y solo los leen
       Almacén, Administración y Dirección. Idempotente por ClaveIdempotencia (Ariadna reintenta desde su cola sin cobertura).

    Fuera del EDMX: la API lee y escribe la tabla por SQL y comprueba que existe (OBJECT_ID). Sin el script, los endpoints de
    api/RepartoPropio dan 400 «falta el script» y la agencia 14 no se propone (no hay fila).

    Ejecutar en SSMS contra NV como sa (CREATE). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. La agencia 14
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.AgenciasTransporte WHERE Numero = 14)
BEGIN
    SET IDENTITY_INSERT dbo.AgenciasTransporte ON;
    INSERT INTO dbo.AgenciasTransporte (Numero, Empresa, Nombre, Ruta, Identificador, PrefijoCodigoBarras, CuentaReembolsos, RecargoCombustible, EsSombra)
    VALUES (14, '1', N'Reparto propio', NULL, NULL, NULL,
            NULL,   -- CuentaReembolsos: la cuenta de la caja del repartidor. LA PONE CARLOS (UPDATE ... SET CuentaReembolsos = '57xxxxxx').
            0, 0);
    SET IDENTITY_INSERT dbo.AgenciasTransporte OFF;
    PRINT 'AgenciasTransporte: alta de la 14 «Reparto propio».';
END
ELSE
    PRINT 'AgenciasTransporte: la 14 ya existía.';
GO

------------------------------------------------------------------------------------------------
-- 2. EnviosAgenciaPruebasEntrega
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.EnviosAgenciaPruebasEntrega', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EnviosAgenciaPruebasEntrega (
        Id int IDENTITY(1,1) NOT NULL,
        EnvioId int NOT NULL,                               -- EnviosAgencia.Numero
        ClaveIdempotencia uniqueidentifier NOT NULL,        -- la pone Ariadna; misma clave = misma respuesta
        Tipo varchar(15) NOT NULL,                          -- Entrega / Incidencia
        FechaHora datetime NOT NULL,                        -- la de la PDA (hora local)
        Receptor nvarchar(100) NULL,                        -- quien recoge
        DniReceptor nvarchar(20) NULL,                      -- DATO PERSONAL
        Firma varbinary(max) NULL,                          -- PNG. DATO PERSONAL
        CobroReembolso varchar(10) NULL,                    -- Efectivo / Tarjeta / NoCobrado
        ImporteCobrado decimal(18,2) NULL,
        Motivo varchar(30) NULL,                            -- de la incidencia
        Texto nvarchar(1000) NULL,                          -- texto de la incidencia u observaciones de la entrega
        Latitud decimal(9,6) NULL,
        Longitud decimal(9,6) NULL,
        Asiento int NULL,                                   -- el del reembolso cobrado, si se contabilizó al entregar
        Usuario varchar(50) NOT NULL,
        FechaRegistro datetime NOT NULL CONSTRAINT DF_EnviosAgenciaPruebasEntrega_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_EnviosAgenciaPruebasEntrega PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_EnviosAgenciaPruebasEntrega_Clave UNIQUE (ClaveIdempotencia),
        CONSTRAINT FK_EnviosAgenciaPruebasEntrega_EnviosAgencia FOREIGN KEY (EnvioId) REFERENCES dbo.EnviosAgencia (Numero),
        CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Tipo CHECK (Tipo IN ('Entrega', 'Incidencia')),
        CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Cobro CHECK (CobroReembolso IS NULL OR CobroReembolso IN ('Efectivo', 'Tarjeta', 'NoCobrado')),
        CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Motivo CHECK (Motivo IS NULL OR Motivo IN ('Ausente', 'DireccionIncorrecta', 'Rechazado', 'FaltanBultos', 'Otro'))
    );
    CREATE INDEX IX_EnviosAgenciaPruebasEntrega_EnvioId ON dbo.EnviosAgenciaPruebasEntrega (EnvioId, Tipo);
    PRINT 'EnviosAgenciaPruebasEntrega: creada.';
END
ELSE
    PRINT 'EnviosAgenciaPruebasEntrega: ya existía.';
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EnviosAgenciaPruebasEntrega TO [NUEVAVISION\RDS2016$];
GO

SELECT Numero, Empresa, Nombre, Ruta, CuentaReembolsos, EsSombra FROM dbo.AgenciasTransporte WHERE Numero = 14;
