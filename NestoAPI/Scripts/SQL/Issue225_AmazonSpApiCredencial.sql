-- =============================================================================
-- NestoAPI#225: Rotación automática de credenciales LWA de Amazon SP-API
-- Almacenamiento centralizado del client_secret (y refresh token) para que la
-- rotación recurrente (job Hangfire) lo lea y lo actualice, y para que los
-- clientes (Nesto) lo consuman vía API en vez de tenerlo en clavesSecretas.config.
--
-- BD: NestoConnection (NV).  GRANT a [NUEVAVISION\RDS2016$] (cuenta de máquina del servidor).
-- NO exponer esta tabla por endpoints genéricos: solo la lee ServicioRotacionCredencialesAmazon.
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AmazonSpApiCredencial')
BEGIN
    CREATE TABLE dbo.AmazonSpApiCredencial (
        Id                INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AmazonSpApiCredencial PRIMARY KEY,
        ClientId          NVARCHAR(200)  NOT NULL,
        ClientSecret      NVARCHAR(200)  NOT NULL,
        RefreshToken      NVARCHAR(MAX)  NULL,
        -- newClientSecretExpiryTime de la notificación APPLICATION_OAUTH_CLIENT_NEW_SECRET
        SecretExpiry      DATETIME       NULL,
        -- oldClientSecretExpiryTime: ventana de gracia de 7 días del secreto anterior
        OldSecretExpiry   DATETIME       NULL,
        FechaModificacion DATETIME       NOT NULL
            CONSTRAINT DF_AmazonSpApiCredencial_Fecha DEFAULT (GETDATE()),
        Usuario           NVARCHAR(100)  NULL,
        CONSTRAINT UQ_AmazonSpApiCredencial_ClientId UNIQUE (ClientId)
    );
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.AmazonSpApiCredencial TO [NUEVAVISION\RDS2016$];
GO

-- -----------------------------------------------------------------------------
-- Seed inicial (rellenar manualmente con las credenciales ACTUALES una sola vez).
-- Se deja comentado para NO commitear secretos. Ejecutar a mano sustituyendo los
-- valores (el ClientSecret es el NUEVO capturado el 10/06/2026; caduca 2026-12-07):
-- -----------------------------------------------------------------------------
-- IF NOT EXISTS (SELECT 1 FROM dbo.AmazonSpApiCredencial WHERE ClientId = N'<CLIENT_ID>')
-- INSERT INTO dbo.AmazonSpApiCredencial (ClientId, ClientSecret, RefreshToken, SecretExpiry, OldSecretExpiry, Usuario)
-- VALUES (N'<CLIENT_ID>', N'<CLIENT_SECRET_NUEVO>', N'<REFRESH_TOKEN>', '2026-12-07T10:02:48', '2026-06-17T10:02:48', N'rotacion-inicial');
-- GO
