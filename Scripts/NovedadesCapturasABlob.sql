/*
    NestoAPI#754 (sugerencia 900): varias capturas por sugerencia / «Algo no funciona» y por comentario, guardadas
    en Azure Blob (contenedor privado «novedades-capturas»), no en la BD.

    Ejecutar en SSMS contra NV (NestoConnection), como sa. Idempotente: se puede volver a lanzar.
    Orden indiferente respecto al deploy: mientras la tabla no exista, la API funciona como siempre (una sola
    imagen en Novedades.Imagen / NovedadesComentarios.Imagen) y rechaza las peticiones con más de una captura.

    Cada fila es UNA captura: de la sugerencia (ComentarioId NULL) o de uno de sus comentarios (ComentarioId con
    valor; NovedadId es entonces la novedad del comentario, para comprobar los permisos sin más consultas).
    El contenido está en el blob «Ruta» del contenedor novedades-capturas; aquí solo lo justo para listar.

    La API (NovedadesController):
      POST api/Novedades/Sugerencias, POST api/Novedades/{id}/Comentarios: "ImagenesBase64": ["...", "..."]
      GET  api/Novedades/Capturas/{id}               → la imagen (mismos permisos que la novedad)
      POST api/Novedades/Capturas/Migrar?aplicar=false&maximo=25
           → pasa las imágenes de antes (varbinary) al blob y vacía la columna. Solo Dirección / Informática.
             Sin aplicar (o aplicar=false) solo mira: cuántas quedan y si existe el contenedor.

    Imágenes de antes en varbinary el 30/10/26: 127 sugerencias + 24 comentarios (~36 MB).
*/

USE NV;
GO

IF OBJECT_ID('dbo.NovedadesCapturas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NovedadesCapturas (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NovedadesCapturas PRIMARY KEY,
        NovedadId int NOT NULL CONSTRAINT FK_NovedadesCapturas_Novedades REFERENCES dbo.Novedades (Id),
        ComentarioId int NULL CONSTRAINT FK_NovedadesCapturas_NovedadesComentarios REFERENCES dbo.NovedadesComentarios (Id),
        Ruta varchar(400) NOT NULL,
        Tipo varchar(30) NOT NULL,
        Bytes int NOT NULL,
        Orden int NOT NULL,
        Usuario nvarchar(128) NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_NovedadesCapturas_Fecha DEFAULT GETDATE(),
        CONSTRAINT UQ_NovedadesCapturas_Ruta UNIQUE (Ruta)
    );
END
GO

-- Las capturas de las sugerencias se leen de una vez para toda la lista; las de los comentarios, por comentario
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NovedadesCapturas_Novedad' AND object_id = OBJECT_ID('dbo.NovedadesCapturas'))
    CREATE INDEX IX_NovedadesCapturas_Novedad ON dbo.NovedadesCapturas (NovedadId, ComentarioId, Orden) INCLUDE (Ruta, Tipo, Bytes);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NovedadesCapturas_Comentario' AND object_id = OBJECT_ID('dbo.NovedadesCapturas'))
    CREATE INDEX IX_NovedadesCapturas_Comentario ON dbo.NovedadesCapturas (ComentarioId, Orden) INCLUDE (NovedadId, Ruta, Tipo, Bytes);
GO

-- La API corre con la cuenta de máquina del servidor (como el resto de tablas de Novedades).
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.NovedadesCapturas TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT name, create_date FROM sys.tables WHERE name = 'NovedadesCapturas';
SELECT (SELECT COUNT(*) FROM dbo.Novedades WHERE Imagen IS NOT NULL) AS SugerenciasConImagenEnBD,
       (SELECT COUNT(*) FROM dbo.NovedadesComentarios WHERE Imagen IS NOT NULL) AS ComentariosConImagenEnBD,
       (SELECT COUNT(*) FROM dbo.NovedadesCapturas) AS CapturasEnBlob;
