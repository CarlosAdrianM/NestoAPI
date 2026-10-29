/*
    NestoAPI#739 (29/10/26): el reparto propio (rutas AT y 16) como una agencia más.

    1. AgenciasTransporte: fila 14 «Reparto propio» (empresa 1). Sin Ruta a propósito: Nesto elige la agencia por defecto de
       una ruta con esa columna, y quien decide si un pedido de AT/16 va por el reparto propio es la API (interruptor
       Agencias:RepartoPropio del Web.config). Sin Identificador ni PrefijoCodigoBarras: el «albarán» es RP{envío} y cada bulto
       RP{envío}-{N}, los numera la API.
       CuentaReembolsos = NULL y NO HACE FALTA PONERLA (Carlos 29/10/26): el reembolso cobrado EN EFECTIVO se contabiliza AL
       ENTREGAR contra la CAJA DEL REPARTIDOR que entrega = su parámetro de usuario CajaDefecto (empresa 1, su propia fila, no la
       de «(defecto)»), el mismo que usa la ventana de Cajas de Nesto. Con dos repartidores, cada uno cobra en su caja. Sin
       CajaDefecto, entregar con efectivo da 400 «No tiene caja asignada». Luego contabilidad hace el arqueo en Cajas de Nesto.
       El repartidor no lleva datáfono: «Tarjeta» da 400; «Enlace» manda un enlace de pago al correo que diga el repartidor y
       no contabiliza nada al entregar (entra por la notificación de Redsys, como cualquier enlace).
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
            NULL,   -- CuentaReembolsos: NO se usa. La caja es la del repartidor (su parámetro CajaDefecto).
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
        CobroReembolso varchar(10) NULL,                    -- Efectivo / Enlace / NoCobrado (Tarjeta ya no: sin datáfono)
        ImporteCobrado decimal(18,2) NULL,                  -- lo cobrado en efectivo, o el importe del enlace
        Motivo varchar(30) NULL,                            -- de la incidencia
        Texto nvarchar(1000) NULL,                          -- texto de la incidencia u observaciones de la entrega
        Latitud decimal(9,6) NULL,
        Longitud decimal(9,6) NULL,
        Asiento int NULL,                                   -- el del reembolso cobrado, si se contabilizó al entregar
        CuentaCaja varchar(15) NULL,                        -- la caja (CajaDefecto del repartidor) del asiento
        PagoTPVId int NULL,                                 -- cobro por enlace: el PagosTPV del enlace
        UrlPago varchar(200) NULL,                          -- cobro por enlace: su página (la misma respuesta si Ariadna reintenta)
        Usuario varchar(50) NOT NULL,
        FechaRegistro datetime NOT NULL CONSTRAINT DF_EnviosAgenciaPruebasEntrega_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_EnviosAgenciaPruebasEntrega PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_EnviosAgenciaPruebasEntrega_Clave UNIQUE (ClaveIdempotencia),
        CONSTRAINT FK_EnviosAgenciaPruebasEntrega_EnviosAgencia FOREIGN KEY (EnvioId) REFERENCES dbo.EnviosAgencia (Numero),
        CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Tipo CHECK (Tipo IN ('Entrega', 'Incidencia')),
        CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Cobro CHECK (CobroReembolso IS NULL OR CobroReembolso IN ('Efectivo', 'Enlace', 'NoCobrado')),
        CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Motivo CHECK (Motivo IS NULL OR Motivo IN ('Ausente', 'DireccionIncorrecta', 'Rechazado', 'FaltanBultos', 'Otro'))
    );
    CREATE INDEX IX_EnviosAgenciaPruebasEntrega_EnvioId ON dbo.EnviosAgenciaPruebasEntrega (EnvioId, Tipo);
    PRINT 'EnviosAgenciaPruebasEntrega: creada.';
END
ELSE
    PRINT 'EnviosAgenciaPruebasEntrega: ya existía.';
GO

-- Ampliación del 29/10/26 (caja del repartidor y cobro por enlace), por si la tabla ya se creó con la versión anterior.
IF COL_LENGTH('dbo.EnviosAgenciaPruebasEntrega', 'CuentaCaja') IS NULL
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD CuentaCaja varchar(15) NULL;
    PRINT 'EnviosAgenciaPruebasEntrega: columna CuentaCaja.';
END
IF COL_LENGTH('dbo.EnviosAgenciaPruebasEntrega', 'PagoTPVId') IS NULL
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD PagoTPVId int NULL;
    PRINT 'EnviosAgenciaPruebasEntrega: columna PagoTPVId.';
END
IF COL_LENGTH('dbo.EnviosAgenciaPruebasEntrega', 'UrlPago') IS NULL
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD UrlPago varchar(200) NULL;
    PRINT 'EnviosAgenciaPruebasEntrega: columna UrlPago.';
END
-- El valor nuevo «Enlace» en la CHECK del cobro (si la tabla venía con la de Efectivo/Tarjeta/NoCobrado).
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EnviosAgenciaPruebasEntrega_Cobro' AND definition NOT LIKE '%Enlace%')
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega DROP CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Cobro;
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD CONSTRAINT CK_EnviosAgenciaPruebasEntrega_Cobro
        CHECK (CobroReembolso IS NULL OR CobroReembolso IN ('Efectivo', 'Tarjeta', 'Enlace', 'NoCobrado'));
    PRINT 'EnviosAgenciaPruebasEntrega: CHECK del cobro con «Enlace».';
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EnviosAgenciaPruebasEntrega TO [NUEVAVISION\RDS2016$];
GO

SELECT Numero, Empresa, Nombre, Ruta, CuentaReembolsos, EsSombra FROM dbo.AgenciasTransporte WHERE Numero = 14;
