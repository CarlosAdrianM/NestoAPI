/*
    NestoAPI#774 (Carlos, 31/10/26): GLOBOS («consejos», coach marks) para enseñar lo nuevo dentro de las apps.
    Gemelas: Nesto#592 y Ariadna#75. TNV y NestoApp, más adelante, con el mismo contrato.

    Globos: el contenido, definido UNA vez. Cada app solo lo pinta:
      - Aplicacion: Nesto, Ariadna, TNV o NestoApp.
      - Pantalla: la clave de la pantalla. Nesto: el nombre de la vista sin «View» (EnviarReposicionView → EnviarReposicion).
        La ventana principal (la cinta y la campana) es «Principal».
        Ariadna: el de la página sin «Page» (RepartoPage → Reparto, PackingPage → Packing).
      - Ancla: la clave COMPLETA del control («Reparto.EnLaFurgoneta»), la misma que lleva el control en el XAML
        (Nesto: globos:Globo.Ancla="…"; Ariadna: globos:Globo.Ancla="…"). NULL = tarjeta centrada. Si el control no existe
        o no está a la vista cuando se enseña, también sale como tarjeta centrada.
      - VersionDesde: la versión de la app a partir de la que sale (la que trae el control nuevo). NULL = todas.
        Nesto 1.10.53.1; Ariadna 2.27. La app manda la suya (?version=); si no la manda, sale.
      - Grupos: a quién (grupos de dominio o roles de Identity sin dominio, o nombres de usuario), separados por comas.
        NULL o vacío = a todos. P. ej. 'Almacén' o 'Almacén, Tiendas' o 'carlos, alfredo'. Sin distinguir mayúsculas ni tildes.
      - FechaDesde / FechaHasta: los días en que sale (los dos incluidos). FechaHasta NULL = sin fin.
      - Orden: en qué orden salen en la misma pantalla (cada app enseña como mucho 2 seguidos, de uno en uno).
      - NovedadId: la novedad que explica (opcional).
      - Activo: 0 para retirarlo sin borrarlo.

    GlobosVistos: «Entendido» (o «Siguiente») por USUARIO (sin dominio), no por aparato. Solo inserciones, idempotente
    (PK Usuario + GloboId). Para que un usuario lo vuelva a ver: DELETE de su fila.

    Endpoints (con token): GET api/Globos?aplicacion=Ariadna&pantalla=Reparto&version=2.27 (los pendientes, por Orden) y
    POST api/Globos/{id}/Visto. Fuera del EDMX: sin este script, GET devuelve [] y las apps siguen como siempre.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.Globos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Globos (
        Id int IDENTITY(1,1) NOT NULL,
        Aplicacion varchar(10) NOT NULL,                    -- Nesto, Ariadna, TNV, NestoApp
        VersionDesde varchar(23) NULL,                      -- 1.10.53.1 / 2.27; NULL = todas
        Pantalla nvarchar(60) NOT NULL,                     -- EnviarReposicion, Reparto, Packing…
        Ancla nvarchar(120) NULL,                           -- Reparto.EnLaFurgoneta; NULL = tarjeta centrada
        Titulo nvarchar(100) NULL,
        Texto nvarchar(500) NOT NULL,                       -- de usted, con tildes, corto (cabe en 320 px)
        Grupos nvarchar(500) NULL,                          -- 'Almacén, Tiendas'; NULL = todos
        FechaDesde date NOT NULL CONSTRAINT DF_Globos_FechaDesde DEFAULT (CAST(GETDATE() AS date)),
        FechaHasta date NULL,
        Orden smallint NOT NULL CONSTRAINT DF_Globos_Orden DEFAULT (0),
        NovedadId int NULL,
        Activo bit NOT NULL CONSTRAINT DF_Globos_Activo DEFAULT (1),
        Usuario nvarchar(50) NULL CONSTRAINT DF_Globos_Usuario DEFAULT (SUSER_SNAME()),
        FechaRegistro datetime NOT NULL CONSTRAINT DF_Globos_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_Globos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_Globos_Aplicacion CHECK (Aplicacion IN ('Nesto', 'Ariadna', 'TNV', 'NestoApp')),
        CONSTRAINT CK_Globos_Fechas CHECK (FechaHasta IS NULL OR FechaHasta >= FechaDesde),
        CONSTRAINT FK_Globos_Novedades FOREIGN KEY (NovedadId) REFERENCES dbo.Novedades (Id)
    );
    PRINT 'Globos: creada.';
END
ELSE
    PRINT 'Globos: ya existía.';
GO

-- Lo que pide cada app al entrar en una pantalla
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Globos_Aplicacion_Pantalla' AND object_id = OBJECT_ID('dbo.Globos'))
    CREATE INDEX IX_Globos_Aplicacion_Pantalla ON dbo.Globos (Aplicacion, Pantalla) INCLUDE (Activo, FechaDesde, FechaHasta);
GO

IF OBJECT_ID('dbo.GlobosVistos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GlobosVistos (
        Usuario nvarchar(128) NOT NULL,                     -- sin dominio (carlos, no NUEVAVISION\carlos)
        GloboId int NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_GlobosVistos_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_GlobosVistos PRIMARY KEY CLUSTERED (Usuario, GloboId),
        CONSTRAINT FK_GlobosVistos_Globos FOREIGN KEY (GloboId) REFERENCES dbo.Globos (Id)
    );
    PRINT 'GlobosVistos: creada.';
END
ELSE
    PRINT 'GlobosVistos: ya existía.';
GO

-- BD de negocio (NestoConnection): el API accede con la cuenta de máquina. Los globos se leen; los vistos se leen y se insertan.
GRANT SELECT ON dbo.Globos TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT ON dbo.GlobosVistos TO [NUEVAVISION\RDS2016$];
GO

SELECT name FROM sys.tables WHERE name IN ('Globos', 'GlobosVistos');
GO

-- =====================================================================================================================
-- ALTA DE UN GLOBO (plantilla):
--   INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden, NovedadId)
--   VALUES ('Ariadna', '2.27', 'Reparto', 'Reparto.EnLaFurgoneta', N'Título opcional', N'Texto de usted.', NULL,
--           CAST(GETDATE() AS date), DATEADD(day, 30, CAST(GETDATE() AS date)), 10, NULL);
--
-- Retirarlo: UPDATE dbo.Globos SET Activo = 0 WHERE Id = …
-- Que alguien lo vuelva a ver: DELETE FROM dbo.GlobosVistos WHERE GloboId = … AND Usuario = N'alfredo'
-- Cuántos lo han visto: SELECT g.Id, g.Ancla, COUNT(v.Usuario) FROM dbo.Globos g LEFT JOIN dbo.GlobosVistos v ON v.GloboId = g.Id GROUP BY g.Id, g.Ancla
-- =====================================================================================================================

-- GLOBOS DE ESTA VERSIÓN (los activa Carlos: quitar el comentario y AJUSTAR las versiones a las que se publiquen).
/*
DECLARE @ariadna varchar(23) = '2.27';      -- <-- AJUSTAR: la versión de Ariadna que trae furgoneta, tarjeta y fotos del packing
DECLARE @nesto varchar(23) = '1.10.53.1';   -- <-- AJUSTAR: la versión de Nesto que trae los globos
DECLARE @hoy date = CAST(GETDATE() AS date);
DECLARE @fin date = DATEADD(day, 45, @hoy);

INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden) VALUES
-- Ariadna: reparto (NestoAPI#772 furgoneta, NestoAPI#773 tarjeta y autorización)
('Ariadna', @ariadna, 'Reparto', 'Reparto.EnLaFurgoneta', N'En la furgoneta',
 N'Escanee todos los bultos al cargar, al entregar, al recoger en la tienda y al descargar en Algete.', NULL, @hoy, @fin, 10),
('Ariadna', @ariadna, 'Reparto', 'Reparto.CobrarConTarjeta', N'Cobrar con tarjeta (F6)',
 N'Si el cliente quiere pagar con tarjeta, pulse F6: le llega el enlace de pago por WhatsApp.', NULL, @hoy, @fin, 20),
('Ariadna', @ariadna, 'Reparto', 'Reparto.PedirAutorizacion', N'Pedir autorización (F7)',
 N'Si no puede cobrar un reembolso, pulse F7 y escriba el motivo: Administración le contesta en la PDA.', NULL, @hoy, @fin, 30),
-- Ariadna: packing (NestoAPI#771)
('Ariadna', @ariadna, 'Packing', 'Packing.AnadirFoto', N'Foto del packing',
 N'Basta con una foto por packing; puede hacer las que quiera.', NULL, @hoy, @fin, 10),
-- Nesto: Enviar reposición y la campana de autorizaciones
('Nesto', @nesto, 'EnviarReposicion', 'EnviarReposicion.EstadoRecogida', N'Estado de la recogida',
 N'Aquí ve si el repartidor ya ha recogido la reposición en la tienda y dónde está cada bulto.', 'Tiendas, Almacén', @hoy, @fin, 10),
('Nesto', @nesto, 'Principal', 'Principal.CampanaAutorizaciones', N'Autorizaciones',
 N'Cuando un repartidor pida permiso para no cobrar un reembolso, le llegará aquí. Ábralo y conteste en el momento.', 'Administración', @hoy, @fin, 10);
*/
GO
