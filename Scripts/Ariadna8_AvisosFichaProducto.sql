-- Ariadna#8: el mozo informa de un dato mal en la ficha de un producto (la foto → Tienda online; precio, nombre,
-- familia, subgrupo, tamaño, código de barras u otro → Compras). Un aviso abierto como mucho por producto y equipo:
-- si otro mozo avisa de lo mismo, se suma. Se cierra con «Cambiado» / «Estaba bien» (botones del correo o
-- POST api/Almacen/AvisosFicha/{id}/Cerrar) o solo, como «Cambiado», cuando el dato ya no es el que había (job cada hora).
-- Lanzar como sa en NV ANTES de publicar la API que lo trae.

USE NV;
GO

IF OBJECT_ID('dbo.AvisosFichaProducto') IS NULL
BEGIN
    CREATE TABLE dbo.AvisosFichaProducto (
        Id int IDENTITY(1,1) NOT NULL,
        Clave uniqueidentifier NOT NULL CONSTRAINT DF_AvisosFichaProducto_Clave DEFAULT (NEWID()), -- La lleva el enlace del correo
        Empresa char(3) NOT NULL,
        Producto char(15) NOT NULL,
        Destino varchar(15) NOT NULL,              -- TiendaOnline / Compras
        Campos varchar(200) NOT NULL,              -- Foto,Precio,Nombre,Familia,Subgrupo,Tamano,CodigoBarras,Otro
        Comentarios nvarchar(max) NULL,            -- «Pedro: …», una línea por mozo
        CodigoLeido varchar(50) NULL,              -- El código que leyó el mozo si no casaba
        UrlFoto varchar(500) NULL,                 -- La foto que vio el mozo
        Huella nvarchar(2000) NULL,                -- JSON: el valor de cada campo al avisar (si cambia, se cierra solo)
        Informantes varchar(500) NOT NULL,         -- Los mozos que han avisado (les llega la respuesta en Ariadna)
        Dispositivo varchar(50) NULL,
        Veces int NOT NULL CONSTRAINT DF_AvisosFichaProducto_Veces DEFAULT (1),
        Estado varchar(12) NOT NULL CONSTRAINT DF_AvisosFichaProducto_Estado DEFAULT ('Abierto'),
        FechaCreacion datetime NOT NULL CONSTRAINT DF_AvisosFichaProducto_FechaCreacion DEFAULT (GETDATE()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_AvisosFichaProducto_FechaModificacion DEFAULT (GETDATE()),
        FechaCierre datetime NULL,
        CerradoPor varchar(50) NULL,
        CONSTRAINT PK_AvisosFichaProducto PRIMARY KEY (Id),
        CONSTRAINT UQ_AvisosFichaProducto_Clave UNIQUE (Clave),
        CONSTRAINT CK_AvisosFichaProducto_Destino CHECK (Destino IN ('TiendaOnline', 'Compras')),
        CONSTRAINT CK_AvisosFichaProducto_Estado CHECK (Estado IN ('Abierto', 'Cambiado', 'EstabaBien'))
    );

    -- Uno abierto como mucho por producto y equipo (los siguientes se suman a él)
    CREATE UNIQUE INDEX UX_AvisosFichaProducto_Abierto ON dbo.AvisosFichaProducto (Empresa, Producto, Destino) WHERE Estado = 'Abierto';
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.AvisosFichaProducto TO [NUEVAVISION\RDS2016$];
GO

SELECT name FROM sys.tables WHERE name = 'AvisosFichaProducto';
