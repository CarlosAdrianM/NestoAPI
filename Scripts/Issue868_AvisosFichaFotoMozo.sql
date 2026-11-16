-- NestoAPI#868: en los avisos de ficha desde Ariadna, el mozo puede mandar una foto hecha por él del producto que tiene
-- en la mano. La foto va dentro del correo y se guarda en el blob de fotos del almacén (contenedor «bultos», carpeta
-- avisos-ficha/). Aquí se guarda su ruta:
--   FotoMozo (nvarchar(400) NULL)  ruta en el blob de la ÚLTIMA foto recibida (si se suman varios mozos, la última)
--
-- ORDEN: se puede lanzar antes o después de publicar el API. Sin la columna, el aviso funciona como hoy y la foto va
-- igual en el correo, pero no se guarda la ruta. El API comprueba la columna una vez por proceso: si se lanza con el API
-- ya publicado, reciclar el app pool.
-- Se puede lanzar dos veces sin problema.
--
-- Es DDL: desde SSMS con `sa` (el login `nuevavision` no tiene ALTER).

USE NV;
GO

IF OBJECT_ID('dbo.AvisosFichaProducto', 'U') IS NULL
BEGIN
    RAISERROR('Falta la tabla dbo.AvisosFichaProducto (Scripts/Ariadna8_AvisosFichaProducto.sql).', 16, 1);
END
GO

IF OBJECT_ID('dbo.AvisosFichaProducto', 'U') IS NOT NULL AND COL_LENGTH('dbo.AvisosFichaProducto', 'FotoMozo') IS NULL
BEGIN
    ALTER TABLE dbo.AvisosFichaProducto ADD FotoMozo nvarchar(400) NULL;
END
GO

-- Comprobación: 800 (bytes de nvarchar(400)) si está; NULL si no
SELECT COL_LENGTH('dbo.AvisosFichaProducto', 'FotoMozo') AS FotoMozo;
