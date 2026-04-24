-- NestoAPI#188: Infraestructura para OAuth2 refresh_token flow (solo NestoApp)
-- Ejecutar ANTES de desplegar.
--
-- IMPORTANTE: esta tabla vive en la base de datos de Identity (connection string
-- "NVIdentity"), NO en la base de datos principal de negocio ("NestoConnection").
-- Identity es Code First, así que NO hay que actualizar ningún EDMX. La entidad
-- se mapea en ApplicationDbContext.OnModelCreating.
--
-- Scope: solo el flow de /oauth/token (NestoApp, grant password). Los flows de
-- Nesto (/api/auth/windows-token) y TiendasNuevaVision (/api/auth/token) no
-- usan esta tabla.

CREATE TABLE dbo.AspNetRefreshTokens (
    Id                NVARCHAR(64)   NOT NULL,   -- SHA-256 hex del secret; PK server-side
    UserName          NVARCHAR(256)  NOT NULL,   -- AspNetUsers.UserName
    ClientId          NVARCHAR(50)   NOT NULL,   -- 'NestoApp' por ahora; reservado multi-cliente
    IssuedUtc         DATETIME       NOT NULL,
    ExpiresUtc        DATETIME       NOT NULL,
    RevokedUtc        DATETIME       NULL,       -- se rellena al rotar (emitir uno nuevo)
    ProtectedTicket   NVARCHAR(MAX)  NOT NULL,   -- ticket OWIN serializado y protegido
    CONSTRAINT PK_AspNetRefreshTokens PRIMARY KEY CLUSTERED (Id ASC)
);

-- Para barridos administrativos / revocación en masa por usuario
CREATE NONCLUSTERED INDEX IX_AspNetRefreshTokens_UserName
    ON dbo.AspNetRefreshTokens (UserName, ClientId)
    INCLUDE (ExpiresUtc, RevokedUtc);

-- Permisos (mismo principal que el resto de scripts del proyecto)
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.AspNetRefreshTokens TO [ABORRAR\sqlServerSvc];
