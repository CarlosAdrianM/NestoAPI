-- NestoAPI#414: categorías secundarias de producto (N:M con orden explícito).
-- Grupo/SubGrupo de la ficha siguen siendo LOS PRINCIPALES; esta tabla añade la ristra de
-- secundarios por producto, sin límite, al estilo PrestaShop/Odoo.
-- EJECUTADO EN PROD el 27/08/2026 (verificado: la tabla existe y tiene datos). Se conserva
-- como registro del DDL que hay en producción, no como pendiente.

CREATE TABLE dbo.ProductosCategoriasSecundarias (
    Empresa char(3) NOT NULL,
    Número char(15) NOT NULL,               -- producto (convención de la casa, como en Kits)
    Orden int NOT NULL,                     -- posición en la ristra; reordenar = UPDATE de Orden
    Grupo char(3) NOT NULL,
    SubGrupo char(3) NOT NULL,
    Usuario varchar(30) NULL,
    [Fecha Modificación] datetime NOT NULL CONSTRAINT DF_ProductosCategoriasSecundarias_Fecha DEFAULT (GETDATE()),
    CONSTRAINT PK_ProductosCategoriasSecundarias PRIMARY KEY (Empresa, Número, Orden),
    -- La misma categoría no puede estar dos veces en el mismo producto
    CONSTRAINT UQ_ProductosCategoriasSecundarias_SinDuplicados UNIQUE (Empresa, Número, Grupo, SubGrupo),
    CONSTRAINT FK_ProductosCategoriasSecundarias_Productos
        FOREIGN KEY (Empresa, Número) REFERENCES dbo.Productos (Empresa, Número),
    CONSTRAINT FK_ProductosCategoriasSecundarias_SubGrupos
        FOREIGN KEY (Empresa, Grupo, SubGrupo) REFERENCES dbo.SubGruposProducto (Empresa, Grupo, Número)
);
GO

-- GRANTs (BD NV / NestoConnection: el API entra por integrated security con la cuenta de máquina)
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ProductosCategoriasSecundarias TO [NUEVAVISION\RDS2016$];
GO
