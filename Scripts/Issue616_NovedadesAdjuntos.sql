/*
    NestoAPI#616: adjuntos (PDF e imágenes) en las Novedades, p. ej. las normas de los cupones (#610).

    Ejecutar en SSMS contra NV (NestoConnection), como sa. Idempotente: se puede volver a lanzar.
    Orden indiferente respecto al deploy: mientras la tabla no exista, las Novedades salen igual
    (con "Adjuntos": []) y solo fallan los endpoints de subir adjuntos.

    La API (NovedadesController):
      GET    api/Novedades/{id}/Adjuntos   → [{ Id, Nombre, Tipo, Tamano, Orden }]
      GET    api/Novedades/Adjuntos/{id}   → el fichero (Content-Disposition: attachment)
      POST   api/Novedades/{id}/Adjuntos   → multipart/form-data, campo «fichero» (uno o varios)
      DELETE api/Novedades/Adjuntos/{id}
    Subir y borrar: solo Dirección / Informática. Máximo 10 MB por fichero; PDF, PNG, JPEG, GIF y WebP.
*/

USE NV;
GO

IF OBJECT_ID('dbo.NovedadesAdjuntos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NovedadesAdjuntos (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_NovedadesAdjuntos PRIMARY KEY,
        NovedadId int NOT NULL CONSTRAINT FK_NovedadesAdjuntos_Novedades REFERENCES dbo.Novedades (Id),
        Nombre nvarchar(200) NOT NULL,
        Tipo varchar(100) NOT NULL,
        Tamano int NOT NULL,
        Contenido varbinary(max) NOT NULL,
        Orden int NOT NULL,
        Usuario nvarchar(50) NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_NovedadesAdjuntos_Fecha DEFAULT GETDATE()
    );
END
GO

-- La lista de adjuntos de las novedades se lee de una vez y sin el contenido
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NovedadesAdjuntos_Novedad' AND object_id = OBJECT_ID('dbo.NovedadesAdjuntos'))
    CREATE INDEX IX_NovedadesAdjuntos_Novedad ON dbo.NovedadesAdjuntos (NovedadId, Orden) INCLUDE (Nombre, Tipo, Tamano);
GO

-- La API corre con la cuenta de máquina del servidor.
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.NovedadesAdjuntos TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT name, create_date FROM sys.tables WHERE name = 'NovedadesAdjuntos';
