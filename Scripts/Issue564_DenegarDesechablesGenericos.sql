-- NestoAPI#564 (29/09/26): el 6+1 de la familia Genéricos (regla 48, autorización) NO vale en los desechables
-- (subgrupo DES). Ejecutar como sa DESPUÉS de publicar la API (la versión anterior no sabe de subgrupos ni de
-- denegaciones y trataría esta fila como una autorización más de toda la familia). Idempotente.
USE NV;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.OfertasPermitidas WHERE Empresa = '1' AND Familia = 'Genéricos' AND SubGrupo = 'DES'
               AND CantidadConPrecio = 6 AND CantidadRegalo = 1 AND Denegar = 1 AND Número IS NULL AND Cliente IS NULL)
    INSERT INTO dbo.OfertasPermitidas (Empresa, Número, Familia, CantidadConPrecio, CantidadRegalo, Denegar, Cliente, Contacto,
                                       FiltroProducto, Usuario, FechaModificación, SubGrupo)
    VALUES ('1', NULL, 'Genéricos', 6, 1, 1, NULL, NULL, NULL, 'NUEVAVISION\Carlos', GETDATE(), 'DES');
GO
SELECT [NºOrden], Número, Familia, SubGrupo, CantidadConPrecio, CantidadRegalo, Denegar
FROM dbo.OfertasPermitidas WHERE Empresa = '1' AND Familia = 'Genéricos';
