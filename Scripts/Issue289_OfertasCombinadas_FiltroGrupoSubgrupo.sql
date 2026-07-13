-- Issue #289: filas de filtro de ofertas combinadas tambien por Grupo y/o Subgrupo del producto
-- (ej. 2+1 de "Aceites, fluidos y geles profesionales" = Grupo COS + Subgrupo 107 + Familia CV).
-- Todos los criterios informados de la fila (Familia, FiltroProducto, Grupo, Subgrupo) se combinan
-- en AND. En blanco = se comporta exactamente igual que hasta ahora.
--
-- EJECUTAR EN PRODUCCION ANTES DE DESPLEGAR LA API (la API nueva lee estas columnas).
-- No necesita GRANT: las columnas nuevas heredan los permisos de tabla dados en la Issue #282.

ALTER TABLE OfertasCombinadasDetalle ADD
    Grupo char(3) NULL,
    Subgrupo char(3) NULL;
GO

-- El CHECK de la #282 exigia producto o filtro (familia/prefijo); ahora Grupo y Subgrupo
-- tambien identifican que casa la fila.
ALTER TABLE OfertasCombinadasDetalle DROP CONSTRAINT CK_OfertasCombinadasDetalle_ProductoOFiltro;
GO

ALTER TABLE OfertasCombinadasDetalle ADD CONSTRAINT CK_OfertasCombinadasDetalle_ProductoOFiltro
    CHECK (Producto IS NOT NULL OR Familia IS NOT NULL OR FiltroProducto IS NOT NULL
           OR Grupo IS NOT NULL OR Subgrupo IS NOT NULL);
GO

-- VERIFICACION (ejecutar y revisar antes de dar por bueno):
-- 1) Deben salir las dos columnas nuevas, NULLables, char(3):
SELECT name, TYPE_NAME(system_type_id) AS tipo, max_length, is_nullable
FROM sys.columns
WHERE object_id = OBJECT_ID('OfertasCombinadasDetalle') AND name IN ('Grupo', 'Subgrupo');

-- 2) El CHECK debe mencionar Grupo y Subgrupo:
SELECT definition FROM sys.check_constraints
WHERE name = 'CK_OfertasCombinadasDetalle_ProductoOFiltro';
