/*
    NestoAPI#526: los usuarios sugieren características nuevas desde Novedades.

    Una sugerencia es una fila de Novedades SIN versión (Version NULL). Sale por delante de la
    versión actual, junto a las demás sin implementar, y se vota y comenta como cualquier novedad
    (#520). Al implementarla se le pone la versión y pasa a su sitio conservando votos y comentarios.

    Ejecutar en SSMS contra NV, como sa, ANTES de publicar la API que lo usa. Idempotente.
    Mientras no se ejecute, GET api/Novedades sigue igual y los endpoints de sugerencias dan error.
*/

USE NV;
GO

-- Sin versión = sugerencia sin implementar
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Novedades') AND name = 'Version' AND is_nullable = 0)
    ALTER TABLE dbo.Novedades ALTER COLUMN Version varchar(23) NULL;
GO

IF COL_LENGTH('dbo.Novedades', 'TextoOriginal') IS NULL
    ALTER TABLE dbo.Novedades ADD
        -- Lo que escribió el usuario, tal cual y para siempre. La Descripcion es la versión clara.
        TextoOriginal nvarchar(2000) NULL,
        -- La captura, con los mismos límites que en los comentarios (2 MB, PNG o JPEG)
        Imagen varbinary(max) NULL,
        ImagenTipo varchar(30) NULL,
        SugeridaPor nvarchar(128) NULL,
        SugeridaNombre nvarchar(100) NULL,
        SugeridaFecha datetime2 NULL,
        -- NULL en las novedades normales. En las sugerencias: Pendiente, Aceptada, Implementada o Descartada
        Estado varchar(20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Novedades_Estado')
    ALTER TABLE dbo.Novedades ADD CONSTRAINT CK_Novedades_Estado
        CHECK (Estado IS NULL OR Estado IN ('Pendiente', 'Aceptada', 'Implementada', 'Descartada'));
GO

-- Comprobación
SELECT name, is_nullable FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.Novedades') AND name IN ('Version', 'TextoOriginal', 'Imagen', 'Estado');
