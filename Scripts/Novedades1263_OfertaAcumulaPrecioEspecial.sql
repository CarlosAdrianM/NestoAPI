/*
    Novedades 1263 (Sancho, 13/11/26) / continuación de NestoAPI#589: oferta N+M que ADEMÁS admite el precio especial.

    La regla general no cambia: una oferta N+M exige cobrar las unidades de pago a precio de ficha y sin descuento
    (o precio especial o la oferta, nunca las dos). Excepción autorizada por Manuel para la feria online: los packs
    45929, 45930 y 45931 llevan su precio especial de profesional Y el 6+1, del 12 al 16/11/2026.

    Cómo: columna nueva OfertasPermitidas.AcumulaPrecioEspecial (bit, por defecto 0 = como siempre). Con 1, la oferta
    de ESA fila se acepta también con las unidades de pago al precio especial vigente del producto (nunca por debajo,
    ni con otro descuento encima). Se pone en una oferta propia de cada pack, con las fechas de la feria: el día 17
    dejan de valer solas. La oferta 6+1 de la familia CV (fila 41) NO se toca.

    Lanzar como sa. Idempotente. La columna va en su propio lote. La API sin esta columna funciona como siempre.
*/

SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.OfertasPermitidas', 'AcumulaPrecioEspecial') IS NULL
    ALTER TABLE dbo.OfertasPermitidas ADD AcumulaPrecioEspecial bit NOT NULL
        CONSTRAINT DF_OfertasPermitidas_AcumulaPrecioEspecial DEFAULT (0);
GO

-- Una oferta propia 6+1 por pack, solo durante la feria, que admite además el precio especial
DECLARE @packs TABLE (Producto char(15) NOT NULL);
INSERT INTO @packs (Producto) VALUES ('45929'), ('45930'), ('45931');

INSERT INTO dbo.OfertasPermitidas (Empresa, Número, CantidadConPrecio, CantidadRegalo, Denegar, FechaDesde, FechaHasta, AcumulaPrecioEspecial)
SELECT '1', p.Producto, 6, 1, 0, '20261112', '20261116', 1
FROM @packs p
WHERE NOT EXISTS (SELECT 1 FROM dbo.OfertasPermitidas o
                  WHERE o.Empresa = '1' AND o.Número = p.Producto AND o.CantidadConPrecio = 6 AND o.CantidadRegalo = 1
                    AND o.Denegar = 0 AND o.Cliente IS NULL AND o.FechaDesde = '20261112' AND o.FechaHasta = '20261116');
GO

SELECT o.NºOrden, RTRIM(o.Número) AS Producto, o.CantidadConPrecio, o.CantidadRegalo, o.FechaDesde, o.FechaHasta, o.AcumulaPrecioEspecial,
       RTRIM(o.Usuario) AS Usuario
FROM dbo.OfertasPermitidas o
WHERE o.Empresa = '1' AND o.AcumulaPrecioEspecial = 1
ORDER BY o.NºOrden;
GO
