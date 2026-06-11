-- Script para crear las tablas de Ofertas Escalonadas (Issue #226)
-- Escalado de descuento por cantidad sobre una lista de referencias combinables entre si
-- (ej.: 2 und -> 5%, 3 -> 10%, 4 -> 15%, 5 -> 20%, 6 o mas -> 25%).
-- Sustituye al patron de duplicar ofertas combinadas, una por tramo (Allure 247-250).
-- Ejecutar en SQL Server Management Studio ANTES de actualizar el EDMX.

CREATE TABLE dbo.OfertasEscalonadas (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    Empresa           CHAR(3) NOT NULL,
    Nombre            NVARCHAR(100) NULL,
    FechaDesde        DATE NULL,
    FechaHasta        DATE NULL,
    Usuario           NVARCHAR(30) NOT NULL,
    FechaModificacion DATETIME NOT NULL
);

CREATE TABLE dbo.OfertasEscalonadasProductos (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    Empresa           CHAR(3) NOT NULL,
    OfertaId          INT NOT NULL,
    Producto          CHAR(15) NOT NULL,
    -- Precio de partida sobre el que se aplica el descuento del tramo (PVP de
    -- ficha en el momento del alta; editable si la oferta parte de otro precio).
    PrecioBase        MONEY NOT NULL,
    Usuario           NVARCHAR(30) NOT NULL,
    FechaModificacion DATETIME NOT NULL,

    CONSTRAINT FK_OfertasEscalonadasProductos_Oferta
        FOREIGN KEY (OfertaId) REFERENCES dbo.OfertasEscalonadas(Id),
    CONSTRAINT FK_OfertasEscalonadasProductos_Producto
        FOREIGN KEY (Empresa, Producto) REFERENCES dbo.Productos(Empresa, Número),
    CONSTRAINT UQ_OfertasEscalonadasProductos_OfertaProducto
        UNIQUE (OfertaId, Producto),
    CONSTRAINT CK_OfertasEscalonadasProductos_PrecioBase
        CHECK (PrecioBase >= 0)
);

CREATE TABLE dbo.OfertasEscalonadasTramos (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    OfertaId          INT NOT NULL,
    -- Cantidad minima total (sumando todas las referencias de la oferta presentes
    -- en el pedido) para alcanzar el tramo. Es "o mas": el tramo superior no tiene tope.
    CantidadMinima    SMALLINT NOT NULL,
    -- Descuento en tanto por uno (0.25 = 25 %).
    Descuento         DECIMAL(9,6) NOT NULL,
    Usuario           NVARCHAR(30) NOT NULL,
    FechaModificacion DATETIME NOT NULL,

    CONSTRAINT FK_OfertasEscalonadasTramos_Oferta
        FOREIGN KEY (OfertaId) REFERENCES dbo.OfertasEscalonadas(Id),
    CONSTRAINT UQ_OfertasEscalonadasTramos_OfertaCantidad
        UNIQUE (OfertaId, CantidadMinima),
    CONSTRAINT CK_OfertasEscalonadasTramos_CantidadMinima
        CHECK (CantidadMinima > 0),
    CONSTRAINT CK_OfertasEscalonadasTramos_Descuento
        CHECK (Descuento > 0 AND Descuento <= 1)
);

-- El validador busca las ofertas por referencia en cada pedido.
CREATE INDEX IX_OfertasEscalonadasProductos_Producto
    ON dbo.OfertasEscalonadasProductos(Producto);

CREATE INDEX IX_OfertasEscalonadasProductos_OfertaId
    ON dbo.OfertasEscalonadasProductos(OfertaId);

CREATE INDEX IX_OfertasEscalonadasTramos_OfertaId
    ON dbo.OfertasEscalonadasTramos(OfertaId);

-- Dar permisos a la cuenta de maquina con la que corre el API (NestoConnection usa integrated security)
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.OfertasEscalonadas TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.OfertasEscalonadasProductos TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.OfertasEscalonadasTramos TO [NUEVAVISION\RDS2016$];
