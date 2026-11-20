/*
    Carlos 20/11/26: las lecturas del repartidor que la API rechaza.

    Hoy, cuando Ariadna lee un código y la API lo rechaza (404 «no existe el envío», 409 NO_CARGADA, EN_FURGONETA…), no queda nada en
    ningún sitio. Desde esta tabla, toda respuesta 4xx de GET Bulto, Cargar, Entregar, Incidencia, Recoger, Descargar y NoRecogida
    (RepartoPropioController, un filtro único) se apunta aquí en segundo plano, sin retrasar la respuesta.

    Se lee con GET api/RepartoPropio/LecturasRechazadas?dias=2&usuario= (Almacén, Dirección, Informática).

    Fuera del EDMX: la API comprueba que existe (OBJECT_ID). Sin el script, no se apunta nada (ELMAH una vez) y el GET da 503.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.RepartoLecturasRechazadas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoLecturasRechazadas (
        Id int IDENTITY(1,1) NOT NULL,
        FechaHora datetime NOT NULL,                        -- la del servidor al responder
        Usuario varchar(50) NOT NULL,
        Endpoint varchar(50) NOT NULL,                      -- GetBulto, Cargar, Entregar, Incidencia, Recoger, Descargar, NoRecogida
        Codigo nvarchar(100) NULL,                          -- lo leído (o el EnvioId de la ruta)
        EnvioId int NULL,
        Status smallint NOT NULL,                           -- 400, 403, 404, 409…
        CodigoError nvarchar(50) NULL,                      -- NO_CARGADA, EN_FURGONETA… (el Codigo del 409)
        Mensaje nvarchar(500) NULL,
        Dispositivo nvarchar(100) NULL,                     -- X-Dispositivo
        Version nvarchar(50) NULL,                          -- X-Ariadna-Version
        CONSTRAINT PK_RepartoLecturasRechazadas PRIMARY KEY CLUSTERED (Id)
    );
    PRINT 'RepartoLecturasRechazadas: creada.';
END
ELSE
    PRINT 'RepartoLecturasRechazadas: ya existía.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoLecturasRechazadas_FechaHora' AND object_id = OBJECT_ID('dbo.RepartoLecturasRechazadas'))
    CREATE INDEX IX_RepartoLecturasRechazadas_FechaHora ON dbo.RepartoLecturasRechazadas (FechaHora) INCLUDE (Usuario);
GO

GRANT SELECT, INSERT ON dbo.RepartoLecturasRechazadas TO [NUEVAVISION\RDS2016$];
GO
