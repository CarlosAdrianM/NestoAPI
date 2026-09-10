-- NestoAPI#477: variantes de producto para la tienda (color, tapizado...). Una familia de
-- referencias de Nesto que en PrestaShop es UNA ficha (la principal) con combinaciones.
-- Cada fila es una referencia; la principal también es una fila (con su propio valor), porque
-- en PrestaShop un producto con combinaciones no se vende "sin combinación".
-- Mismo patrón que ProductosCategoriasSecundarias (#414).
--
-- ⚠️ EJECUTAR COMO sa EN SSMS ANTES de publicar la API que lo usa: ProductoDTO.ConstruirParaPublicar
-- consulta esta tabla en cada publicación y sin ella la sincronización de productos se cae.

CREATE TABLE dbo.ProductosVariantes (
    Empresa char(3) NOT NULL,
    Número char(15) NOT NULL,               -- la referencia (cada hermana, INCLUIDA la principal)
    NúmeroPrincipal char(15) NOT NULL,      -- la referencia cuya ficha (nombre, textos, fotos) es la de la tienda
    Atributo varchar(30) NOT NULL,          -- 'Color' de salida; es el nombre del atributo en PrestaShop
    Valor varchar(50) NOT NULL,             -- 'Negro', 'Gris', 'Blanco'... lo que ve el cliente
    Orden int NOT NULL,                     -- posición de la combinación en la ficha
    Usuario varchar(30) NULL,
    [Fecha Modificación] datetime NOT NULL CONSTRAINT DF_ProductosVariantes_Fecha DEFAULT (GETDATE()),
    -- Una referencia solo puede estar en una familia
    CONSTRAINT PK_ProductosVariantes PRIMARY KEY (Empresa, Número),
    -- Dentro de una familia no puede repetirse el mismo valor del mismo atributo
    CONSTRAINT UQ_ProductosVariantes_ValorUnico UNIQUE (Empresa, NúmeroPrincipal, Atributo, Valor),
    CONSTRAINT FK_ProductosVariantes_Productos
        FOREIGN KEY (Empresa, Número) REFERENCES dbo.Productos (Empresa, Número),
    CONSTRAINT FK_ProductosVariantes_Principal
        FOREIGN KEY (Empresa, NúmeroPrincipal) REFERENCES dbo.Productos (Empresa, Número)
);
GO

-- Las consultas de la familia entran siempre por la principal
CREATE INDEX IX_ProductosVariantes_Principal ON dbo.ProductosVariantes (Empresa, NúmeroPrincipal, Orden);
GO

-- GRANTs (BD NV / NestoConnection: el API entra por integrated security con la cuenta de máquina)
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ProductosVariantes TO [NUEVAVISION\RDS2016$];
GO
