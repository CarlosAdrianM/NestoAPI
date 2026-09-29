/*
    NestoAPI#558: «🐞 Algo no funciona» en Novedades, junto a «💡 Sugerir una mejora».

    Una incidencia es una sugerencia (fila de Novedades sin versión, #526) con Categoria = 'Incidencia'.
    Al corregirla se le pone la versión y pasa a Corregido (lo hace la API), y sale en el changelog.
    Contexto: lo que la API añade sola al crearla (versión del programa, pantalla abierta y los errores
    de ELMAH del usuario de la última hora). Solo lo ven Dirección / Informática.

    Ejecutar en SSMS contra NV, como sa, ANTES de publicar la API que lo usa. Idempotente.
    Mientras no se ejecute, los endpoints de sugerencias dan error (la API ya lee y graba Contexto).
    Sin GRANTs nuevos: NUEVAVISION\RDS2016$ ya tiene SELECT/INSERT/UPDATE en Novedades y SELECT en ELMAH_Error.
*/

USE NV;
GO

IF COL_LENGTH('dbo.Novedades', 'Contexto') IS NULL
    ALTER TABLE dbo.Novedades ADD Contexto nvarchar(max) NULL;
GO

-- La categoría admite Incidencia (antes solo Nuevo, Mejorado y Corregido)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Novedades_Categoria'
           AND definition NOT LIKE '%Incidencia%')
    ALTER TABLE dbo.Novedades DROP CONSTRAINT CK_Novedades_Categoria;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Novedades_Categoria')
    ALTER TABLE dbo.Novedades ADD CONSTRAINT CK_Novedades_Categoria
        CHECK (Categoria IN (N'Nuevo', N'Mejorado', N'Corregido', N'Incidencia'));
GO

-- Comprobación
SELECT name, definition FROM sys.check_constraints WHERE name = 'CK_Novedades_Categoria';
SELECT name, is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Novedades') AND name = 'Contexto';
