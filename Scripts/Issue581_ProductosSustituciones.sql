/*
    NestoAPI#581: sustitución temporal de referencias. Compras dice «mientras tanto, servid la 45685 en lugar
    de la 25539» y quien mete el pedido (Nesto: plantilla y detalle; NestoApp, issue gemela) lo ve al meter
    el producto, con la opción de cambiarlo con un clic. El caso: pedido 927489 (30/09/26), pantalones de
    presoterapia 25539 → 45685; el aviso vivía en un correo de Santi.

    Cuándo deja de avisar (como SAP VB11 / Dynamics Item substitutions):
      MientrasNoHayaStock = 1 y FechaHasta NULL  → mientras no haya disponible suficiente del original
                                                  (se apaga sola al llegar la mercancía). Por defecto.
      MientrasNoHayaStock = 0 y FechaHasta       → hasta esa fecha (incluida), haya stock o no.
      MientrasNoHayaStock = 1 y FechaHasta       → lo que ocurra antes.
    Una sola activa por producto: dar de alta otra anula la anterior. Anular no borra (FechaAnulacion).

    Ejecutar en SSMS contra NV (NestoConnection), como sa. Idempotente: se puede volver a lanzar.
    Orden indiferente respecto al deploy: mientras la tabla no exista, la API dice que no hay sustitución
    (no avisa a nadie) y solo fallan el alta y la anulación.

    La API (ProductosSustitucionesController, [Authorize]):
      GET    api/Productos/{producto}/Sustitucion?empresa=1&cantidad=100 → la vigente para esa cantidad, o null
      GET    api/Productos/{producto}/Sustituciones?empresa=1           → todas (historial), la activa primero
      POST   api/Productos/{producto}/Sustituciones                     → alta (anula la anterior)
      DELETE api/Productos/{producto}/Sustituciones/{id}?empresa=1      → anular
*/

USE NV;
GO

IF OBJECT_ID('dbo.ProductosSustituciones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductosSustituciones (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductosSustituciones PRIMARY KEY,
        Empresa char(3) NOT NULL,
        Producto char(15) NOT NULL,
        ProductoSustituto char(15) NOT NULL,
        Motivo nvarchar(250) NULL,
        MientrasNoHayaStock bit NOT NULL CONSTRAINT DF_ProductosSustituciones_MientrasNoHayaStock DEFAULT 1,
        FechaHasta date NULL,
        Usuario nvarchar(50) NULL,
        FechaCreacion datetime NOT NULL CONSTRAINT DF_ProductosSustituciones_FechaCreacion DEFAULT GETDATE(),
        FechaAnulacion datetime NULL,
        UsuarioAnulacion nvarchar(50) NULL,
        -- Sin stock ni fecha no se apagaría nunca: alguien tendría que acordarse de quitarla (el problema de Odoo)
        CONSTRAINT CK_ProductosSustituciones_CuandoTermina CHECK (MientrasNoHayaStock = 1 OR FechaHasta IS NOT NULL),
        CONSTRAINT CK_ProductosSustituciones_DistintoProducto CHECK (Producto <> ProductoSustituto)
    );
END
GO

-- Una sola activa por producto (la API anula la anterior en la misma transacción)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ProductosSustituciones_Activa' AND object_id = OBJECT_ID('dbo.ProductosSustituciones'))
    CREATE UNIQUE INDEX UX_ProductosSustituciones_Activa ON dbo.ProductosSustituciones (Empresa, Producto) WHERE FechaAnulacion IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductosSustituciones_Producto' AND object_id = OBJECT_ID('dbo.ProductosSustituciones'))
    CREATE INDEX IX_ProductosSustituciones_Producto ON dbo.ProductosSustituciones (Empresa, Producto, FechaCreacion);
GO

-- La API corre con la cuenta de máquina del servidor.
GRANT SELECT, INSERT, UPDATE ON dbo.ProductosSustituciones TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT name, create_date FROM sys.tables WHERE name = 'ProductosSustituciones';
