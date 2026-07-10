-- Issue #282: Ofertas combinadas con detalle por FILTRO (Familia + prefijo de nombre)
-- además de por producto concreto (caso Lisap OPC 36+36 / agua ox. 3+3 → regalo 45473+45472).
--
-- Una fila de detalle pasa a ser:
--   - De PRODUCTO: Producto informado (comportamiento actual, sin cambios).
--   - De FILTRO:   Producto NULL + Familia y/o FiltroProducto informados. Casa las líneas del
--                  pedido cuyo producto es de esa Familia y cuyo Nombre empieza por FiltroProducto
--                  (mismo matching que OfertasPermitidas). La Cantidad se cuenta AGREGADA sobre
--                  todas las líneas que casan.
--
-- ⚠️ EJECUTAR EN PRODUCCIÓN ANTES DE DESPLEGAR LA API (el EDMX ya mapea las columnas nuevas).
-- La FK compuesta (Empresa, Producto) → Productos no se aplica en filas con Producto NULL
-- (comportamiento estándar de SQL Server con FK multicolumna y NULL), no hay que tocarla.

ALTER TABLE OfertasCombinadasDetalle ALTER COLUMN Producto char(15) NULL;
GO

ALTER TABLE OfertasCombinadasDetalle ADD
    Familia char(10) NULL,
    FiltroProducto nvarchar(50) NULL;
GO

-- Toda fila debe identificar QUÉ casa: un producto concreto o un filtro (familia y/o prefijo).
ALTER TABLE OfertasCombinadasDetalle ADD CONSTRAINT CK_OfertasCombinadasDetalle_ProductoOFiltro
    CHECK (Producto IS NOT NULL OR Familia IS NOT NULL OR FiltroProducto IS NOT NULL);
GO
