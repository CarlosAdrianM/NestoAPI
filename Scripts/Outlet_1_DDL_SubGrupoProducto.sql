-- =====================================================================================
-- Outlet por familia + categoría (paso 1 de 3): columna nueva en DescuentosProducto
-- 09/09/2026 — Ejecutar en SSMS como sa contra NV. ANTES de desplegar la NestoAPI que
-- trae el nivel nuevo del motor de precios (EF ignora una columna que no está en el modelo,
-- pero revienta si el modelo la espera y no existe).
--
-- Qué añade: SubGrupoProducto char(3) NULL. Una fila con Familia + GrupoProducto +
-- SubGrupoProducto es "esta marca, en esta categoría (Grupo/SubGrupo)", donde la categoría
-- puede ser la principal de la ficha O una de sus categorías secundarias
-- (ProductosCategoriasSecundarias, #414). Es lo que permite decir "Maystar en Outlet
-- Estética al 15 %" con UNA fila, y que un producto de Maystar que entre mañana en Outlet
-- se lleve el descuento sin tocar nada más.
--
-- La restricción deja el nivel nuevo SOLO en tarifa (sin cliente, sin proveedor, sin
-- producto): los niveles de cliente y de compras del motor no lo miran.
-- =====================================================================================
SET NOCOUNT ON;

IF COL_LENGTH('dbo.DescuentosProducto', 'SubGrupoProducto') IS NULL
BEGIN
    ALTER TABLE dbo.DescuentosProducto ADD SubGrupoProducto char(3) NULL;
    PRINT 'Columna SubGrupoProducto añadida';
END
ELSE
    PRINT 'La columna SubGrupoProducto ya existía';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DescuentosProducto_SubGrupo')
BEGIN
    ALTER TABLE dbo.DescuentosProducto WITH CHECK
        ADD CONSTRAINT CK_DescuentosProducto_SubGrupo CHECK (
            SubGrupoProducto IS NULL
            OR (Familia IS NOT NULL AND GrupoProducto IS NOT NULL
                AND [Nº Producto] IS NULL AND [Nº Cliente] IS NULL AND NºProveedor IS NULL
                AND FiltroProducto IS NULL));
    PRINT 'Restricción CK_DescuentosProducto_SubGrupo creada';
END
ELSE
    PRINT 'La restricción CK_DescuentosProducto_SubGrupo ya existía';

-- Comprobación
SELECT c.name, t.name AS tipo, c.max_length, c.is_nullable
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.DescuentosProducto') AND c.name = 'SubGrupoProducto';
