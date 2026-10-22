/*
    NestoAPI#692 (Nesto#545, NestoApp#247): anunciar un evento completo en las apps (push a NestoApp y a TNV) y
    «Notificaciones que recibe» en TNV (un interruptor por tipo de push, como «Correos que recibe» de #532).

    Ejecutar en SSMS contra NV como sa (ALTER y CREATE: el login nuevavision no puede). Idempotente: se puede lanzar
    dos veces.

    1. Eventos.AnunciadoFecha / AnunciadoUsuario: cuándo y quién lo anunció la última vez (NULL = nunca). Con fecha, un
       segundo POST api/Eventos/{id}/Anunciar sin ?otraVez=true da 409.
    2. EventosAnuncios: cada anuncio (también los repetidos con reclamo y los pedidos fuera de 9:00-21:00, que salen a las
       9:00 por Hangfire): reclamo, para cuándo quedó programado, cuándo salió y a cuántos dispositivos (NestoApp y TNV) y
       clientes de TNV llegó.
    3. NotificacionesClientesBajas: las push que cada cliente de TNV ha apagado en «Notificaciones que recibe». Sin fila =
       la recibe (por defecto, todas encendidas). Tipos (los valida la API, TiposNotificacionCliente; sin CHECK para no tocar
       la BD con cada tipo nuevo): 'EventosYCursos' (este anuncio) y 'AvisosReposicion' (la push de reposición, #662).

    TODO va fuera del EDMX (SQL a mano en la API, como EventosImagenes y CorreosPostCompraBajas). ORDEN: este script y luego
    el API. Si se publica el API sin él: los eventos salen como no anunciados (ELMAH una vez), Anunciar da error, la push
    de reposición sale como hasta ahora (sin mirar el interruptor, lo loguea) y las preferencias de la app dan error.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Eventos: el último anuncio
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.Eventos', 'AnunciadoFecha') IS NULL
    ALTER TABLE dbo.Eventos ADD AnunciadoFecha datetime NULL;
GO
IF COL_LENGTH('dbo.Eventos', 'AnunciadoUsuario') IS NULL
    ALTER TABLE dbo.Eventos ADD AnunciadoUsuario nvarchar(50) NULL;
GO

------------------------------------------------------------------------------------------------
-- 2. EventosAnuncios: el histórico de anuncios
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.EventosAnuncios', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventosAnuncios (
        Id int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_EventosAnuncios PRIMARY KEY,
        EventoId int NOT NULL CONSTRAINT FK_EventosAnuncios_Eventos REFERENCES dbo.Eventos (Id),
        Fecha datetime NOT NULL,                     -- cuándo se pidió
        Usuario nvarchar(50) NULL,
        Reclamo nvarchar(60) NULL,                   -- «¡Últimas plazas disponibles!»; NULL en el primer anuncio
        ProgramadoPara datetime NULL,                -- pedido fuera de 9:00-21:00: sale a esta hora (Hangfire)
        EnviadoFecha datetime NULL,                  -- cuándo salió (NULL = programado y aún no ha salido)
        DispositivosNestoApp int NULL,
        DispositivosTiendas int NULL,
        ClientesTiendas int NULL
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventosAnuncios_EventoId' AND object_id = OBJECT_ID('dbo.EventosAnuncios'))
    CREATE INDEX IX_EventosAnuncios_EventoId ON dbo.EventosAnuncios (EventoId);
GO

------------------------------------------------------------------------------------------------
-- 3. NotificacionesClientesBajas: «Notificaciones que recibe» de TNV
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.NotificacionesClientesBajas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotificacionesClientesBajas (
        Empresa varchar(3) NOT NULL,
        Cliente varchar(10) NOT NULL,
        Tipo varchar(40) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_NotificacionesClientesBajas_Fecha DEFAULT GETDATE(),
        Usuario nvarchar(50) NULL,
        CONSTRAINT PK_NotificacionesClientesBajas PRIMARY KEY (Empresa, Cliente, Tipo)
    );
END
GO

-- Eventos ya tenía SELECT/INSERT/UPDATE/DELETE (#591, #611): las columnas nuevas no necesitan GRANT.
GRANT SELECT, INSERT, UPDATE ON dbo.EventosAnuncios TO [NUEVAVISION\RDS2016$];
GO
GRANT SELECT, INSERT, DELETE ON dbo.NotificacionesClientesBajas TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: 2 columnas en Eventos y las 2 tablas nuevas
------------------------------------------------------------------------------------------------
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE (TABLE_NAME = 'Eventos' AND COLUMN_NAME IN ('AnunciadoFecha', 'AnunciadoUsuario'))
   OR TABLE_NAME IN ('EventosAnuncios', 'NotificacionesClientesBajas')
ORDER BY TABLE_NAME, ORDINAL_POSITION;
GO

-- CONSULTAS ÚTILES
-- Los anuncios y a cuántos llegaron:
-- SELECT a.*, e.Titulo FROM dbo.EventosAnuncios a JOIN dbo.Eventos e ON e.Id = a.EventoId ORDER BY a.Id DESC;
-- Quién ha apagado qué:
-- SELECT Tipo, COUNT(*) AS Clientes FROM dbo.NotificacionesClientesBajas GROUP BY Tipo;
