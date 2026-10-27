/*
    NestoAPI#711 (Carlos, 27/10/26): un vídeo corto en las Novedades. Como mucho UNO por versión (y ámbito),
    para la novedad que más cambia el trabajo diario. Unos 20 s, sin audio.

    Ejecutar en SSMS contra NV (NestoConnection), como sa. Idempotente: se puede volver a lanzar.
    Orden indiferente respecto al deploy: mientras las columnas no existan, la API lee y escribe con SQL dinámico
    (COL_LENGTH), las Novedades salen igual con "TieneVideo": false, y subir un vídeo contesta 400 «Todavía no se
    pueden poner vídeos…». Mientras la tabla de reproducciones no exista, simplemente no se cuentan.

    QUÉ HACE
      - Novedades.Video nvarchar(400) NULL: la ruta del blob (contenedor «bultos», el de las fotos de los bultos,
        con el prefijo novedades-videos/{IdNovedad}/{guid}.mp4). NULL = sin vídeo.
      - Novedades.VideoTamano int NULL: los bytes del MP4 (para enseñar «2,1 MB» sin pedirlo).
      - Tabla NovedadesVideoReproducciones: quién ve cada vídeo y cuándo (Cliente: Nesto / NestoApp / Ariadna).

    La API (NovedadesController):
      POST   api/Novedades/{id}/Video               → MP4 (multipart, campo «fichero», o el cuerpo tal cual con
                                                      Content-Type video/mp4). Máx. 10 MB. Dirección / Informática.
                                                      409 si otra novedad de la misma versión y ámbito ya tiene vídeo.
      DELETE api/Novedades/{id}/Video               → 204. Dirección / Informática.
      GET    api/Novedades/{id}/Video               → el MP4, con Range (206) para saltar adelante y atrás.
      POST   api/Novedades/{id}/Video/Reproduccion  → 204. Cuenta una reproducción (quién y cuándo).
*/

USE NV;
GO

IF COL_LENGTH('dbo.Novedades', 'Video') IS NULL
    ALTER TABLE dbo.Novedades ADD Video nvarchar(400) NULL;
GO

IF COL_LENGTH('dbo.Novedades', 'VideoTamano') IS NULL
    ALTER TABLE dbo.Novedades ADD VideoTamano int NULL;
GO

-- Sin índice en Video a propósito: la tabla tiene unos cientos de filas (ver Issue650_SugerenciasPrivadas.sql).

IF OBJECT_ID('dbo.NovedadesVideoReproducciones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NovedadesVideoReproducciones (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NovedadesVideoReproducciones PRIMARY KEY,
        NovedadId int NOT NULL CONSTRAINT FK_NovedadesVideoReproducciones_Novedades REFERENCES dbo.Novedades (Id),
        Usuario nvarchar(128) NOT NULL,
        Cliente nvarchar(20) NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_NovedadesVideoReproducciones_Fecha DEFAULT GETDATE()
    );
END
GO

-- Cuántas veces y quién ha visto el vídeo de cada novedad
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NovedadesVideoReproducciones_Novedad' AND object_id = OBJECT_ID('dbo.NovedadesVideoReproducciones'))
    CREATE INDEX IX_NovedadesVideoReproducciones_Novedad ON dbo.NovedadesVideoReproducciones (NovedadId, Fecha) INCLUDE (Usuario, Cliente);
GO

-- La API corre con la cuenta de máquina del servidor. Las columnas nuevas de Novedades heredan los permisos que ya
-- tiene sobre la tabla; la tabla nueva necesita los suyos.
GRANT SELECT, INSERT ON dbo.NovedadesVideoReproducciones TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COL_LENGTH('dbo.Novedades', 'Video') AS LongitudColumnaVideo,
       COL_LENGTH('dbo.Novedades', 'VideoTamano') AS LongitudColumnaVideoTamano,
       (SELECT COUNT(*) FROM dbo.Novedades WHERE Video IS NOT NULL) AS NovedadesConVideo,
       (SELECT create_date FROM sys.tables WHERE name = 'NovedadesVideoReproducciones') AS TablaReproducciones;

-- Para medir si se ven (Carlos):
-- SELECT n.Id, n.Version, n.Titulo, COUNT(r.Id) AS Reproducciones, COUNT(DISTINCT r.Usuario) AS Personas
-- FROM dbo.Novedades n LEFT JOIN dbo.NovedadesVideoReproducciones r ON r.NovedadId = n.Id
-- WHERE n.Video IS NOT NULL GROUP BY n.Id, n.Version, n.Titulo ORDER BY n.Id DESC;
