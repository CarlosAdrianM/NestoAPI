/*
    NestoAPI#605: varios códigos de barras por producto (como SAP, Dynamics y Odoo).

    Ejecutar en SSMS contra NV como sa (crea una tabla y un trigger: el login nuevavision no tiene ALTER). Idempotente: se
    puede lanzar dos veces. Lanzarlo ANTES de publicar la API: la tabla está en el EDMX y sin ella fallan la preparación
    (Ariadna) y la búsqueda de productos por código.

    - Tabla ProductosCodigosBarras: Empresa, Producto, Codigo, Cantidad (1 = unidad; 100 = caja de 100), Proveedor (opcional),
      Principal, Origen ('Ficha' | 'Almacen' | 'Proveedor'), Usuario, Fecha y Activo. Una fila por código y producto
      (UNIQUE Empresa, Producto, Codigo); un mismo código puede estar en varios productos solo si se ha añadido a propósito
      (la API pide PermitirCompartido). No se borra nada: la baja es Activo = 0.
    - Productos.CodBarras SIGUE SIENDO el principal (Nesto viejo, PrestaShop, TNV, Odoo). La API lo actualiza al marcar otro
      principal, y el trigger trgProductosCodigosBarras hace lo contrario: si alguien cambia Productos.CodBarras por otro
      camino (la ficha de Nesto, la sincronización con Odoo, un alta), la tabla se pone al día sola:
        * el código nuevo pasa a ser la fila principal (se crea con Origen 'Ficha' si no existía, o se reactiva);
        * el principal anterior se da de baja (Activo = 0): cambiar el código en la ficha es «este ya no vale», como hasta
          ahora. Para conservarlo como alternativo, hay que marcar el principal desde la API (se queda activo).
    - Migración: una fila principal con Origen 'Ficha' por cada Productos.CodBarras no vacío (RTRIM).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Tabla
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.ProductosCodigosBarras') IS NULL
BEGIN
    CREATE TABLE dbo.ProductosCodigosBarras (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Producto char(15) NOT NULL,                      -- Productos.[Número]
        Codigo varchar(20) NOT NULL,                     -- sin espacios (la ficha solo admite 13)
        Cantidad int NOT NULL CONSTRAINT DF_ProductosCodigosBarras_Cantidad DEFAULT (1),   -- 1 = unidad; 100 = caja de 100
        Proveedor char(10) NULL,                         -- Proveedores.[Número], si el código es de un proveedor
        Principal bit NOT NULL CONSTRAINT DF_ProductosCodigosBarras_Principal DEFAULT (0),
        Origen varchar(10) NOT NULL CONSTRAINT DF_ProductosCodigosBarras_Origen DEFAULT ('Ficha'),
        Usuario varchar(30) NOT NULL CONSTRAINT DF_ProductosCodigosBarras_Usuario DEFAULT (SUSER_SNAME()),
        Fecha datetime NOT NULL CONSTRAINT DF_ProductosCodigosBarras_Fecha DEFAULT (GETDATE()),
        Activo bit NOT NULL CONSTRAINT DF_ProductosCodigosBarras_Activo DEFAULT (1),
        CONSTRAINT PK_ProductosCodigosBarras PRIMARY KEY (Id),
        CONSTRAINT UQ_ProductosCodigosBarras UNIQUE (Empresa, Producto, Codigo),
        CONSTRAINT CK_ProductosCodigosBarras_Cantidad CHECK (Cantidad > 0),
        CONSTRAINT CK_ProductosCodigosBarras_Origen CHECK (Origen IN ('Ficha', 'Almacen', 'Proveedor'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ProductosCodigosBarras') AND name = 'IX_ProductosCodigosBarras_Codigo')
    CREATE INDEX IX_ProductosCodigosBarras_Codigo ON dbo.ProductosCodigosBarras (Empresa, Codigo) INCLUDE (Producto, Activo, Principal, Cantidad);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.ProductosCodigosBarras TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- 2. Migración: el código de la ficha de cada producto, como principal
------------------------------------------------------------------------------------------------
INSERT INTO dbo.ProductosCodigosBarras (Empresa, Producto, Codigo, Cantidad, Proveedor, Principal, Origen, Usuario, Fecha, Activo)
SELECT p.Empresa, p.[Número], RTRIM(p.CodBarras), 1, NULL, 1, 'Ficha', 'NestoAPI#605', GETDATE(), 1
FROM dbo.Productos p
WHERE RTRIM(ISNULL(p.CodBarras, '')) <> ''
  AND NOT EXISTS (SELECT 1 FROM dbo.ProductosCodigosBarras c
                  WHERE c.Empresa = p.Empresa AND c.Producto = p.[Número] AND c.Codigo = RTRIM(p.CodBarras));
PRINT CONCAT('Códigos de la ficha migrados: ', @@ROWCOUNT);
GO

------------------------------------------------------------------------------------------------
-- 3. Trigger: si cambia Productos.CodBarras por cualquier camino, la tabla se pone al día
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.trgProductosCodigosBarras') IS NOT NULL
    DROP TRIGGER dbo.trgProductosCodigosBarras;
GO

CREATE TRIGGER dbo.trgProductosCodigosBarras ON dbo.Productos
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(CodBarras)
        RETURN;

    DECLARE @cambios TABLE (Empresa char(3) NOT NULL, Producto char(15) NOT NULL, Codigo varchar(20) NOT NULL, Usuario varchar(30) NULL);
    INSERT INTO @cambios (Empresa, Producto, Codigo, Usuario)
    SELECT i.Empresa, i.[Número], RTRIM(ISNULL(i.CodBarras, '')), LEFT(ISNULL(i.Usuario, SUSER_SNAME()), 30)
    FROM inserted i
         LEFT JOIN deleted d ON d.Empresa = i.Empresa AND d.[Número] = i.[Número]
    WHERE d.[Número] IS NULL OR RTRIM(ISNULL(d.CodBarras, '')) <> RTRIM(ISNULL(i.CodBarras, ''));

    IF NOT EXISTS (SELECT 1 FROM @cambios)
        RETURN;

    -- El principal anterior (si sigue marcado: cuando el cambio viene de la API, ya lo ha desmarcado ella) deja de valer
    UPDATE c SET Principal = 0, Activo = 0
    FROM dbo.ProductosCodigosBarras c
         JOIN @cambios x ON x.Empresa = c.Empresa AND x.Producto = c.Producto
    WHERE c.Principal = 1 AND c.Codigo <> x.Codigo;

    -- El código nuevo, principal y activo
    UPDATE c SET Principal = 1, Activo = 1
    FROM dbo.ProductosCodigosBarras c
         JOIN @cambios x ON x.Empresa = c.Empresa AND x.Producto = c.Producto AND x.Codigo = c.Codigo
    WHERE x.Codigo <> '' AND (c.Principal = 0 OR c.Activo = 0);

    INSERT INTO dbo.ProductosCodigosBarras (Empresa, Producto, Codigo, Cantidad, Proveedor, Principal, Origen, Usuario, Fecha, Activo)
    SELECT x.Empresa, x.Producto, x.Codigo, 1, NULL, 1, 'Ficha', ISNULL(x.Usuario, SUSER_SNAME()), GETDATE(), 1
    FROM @cambios x
    WHERE x.Codigo <> ''
      AND NOT EXISTS (SELECT 1 FROM dbo.ProductosCodigosBarras c
                      WHERE c.Empresa = x.Empresa AND c.Producto = x.Producto AND c.Codigo = x.Codigo);
END
GO

------------------------------------------------------------------------------------------------
-- 4. Comprobación
------------------------------------------------------------------------------------------------
SELECT COUNT(*) AS Filas, SUM(CAST(Principal AS int)) AS Principales, SUM(CAST(Activo AS int)) AS Activas
FROM dbo.ProductosCodigosBarras;

-- Productos con código en la ficha y sin su fila principal activa (debe salir 0)
SELECT COUNT(*) AS FichasSinFila
FROM dbo.Productos p
WHERE RTRIM(ISNULL(p.CodBarras, '')) <> ''
  AND NOT EXISTS (SELECT 1 FROM dbo.ProductosCodigosBarras c
                  WHERE c.Empresa = p.Empresa AND c.Producto = p.[Número] AND c.Codigo = RTRIM(p.CodBarras) AND c.Principal = 1 AND c.Activo = 1);
GO
