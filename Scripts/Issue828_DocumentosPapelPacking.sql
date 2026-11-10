/*
    NestoAPI#828 (sugerencia 973 de Alfredo, decisiones de Carlos del 09/11/26): la factura física sale en el packing, al hacer la
    etiqueta de cada envío (F8 en Ariadna; la etiqueta en Agencias de Nesto con «Facturar al imprimir etiqueta»), y no toda la tanda
    al final de Facturar rutas. Esta tabla es la marca de cada documento en papel que sale (o debería salir) en el packing:
      - Estado 'Impreso': ha salido por la impresora (la de documentos del usuario o la de «(defecto)»; en Nesto, la suya).
        Facturar rutas lo cuenta: «N facturas en papel ya impresas en el packing».
      - Estado 'Pendiente': no ha salido (impresora caída, sin impresora…). Repetir F8 o «Reintentar impresión» en Ariadna (o
        repetir la etiqueta en Nesto) lo vuelve a imprimir SIN volver a facturar. Facturar rutas lo lista con su envío.
    Una fila por documento (Empresa, Pedido, Tipo, Documento). Tipo: 'Factura', 'Albaran', 'NotaEntrega' o 'CartaCheque' (Documento =
    el número de la factura, del albarán o, en la nota, el del pedido; en la carta del cheque regalo, el de la factura que lo generó).
    Aplicacion: 'Ariadna' (imprime el servidor) o 'Nesto' (imprime Nesto).
    'CartaCheque' se añadió el 10/11/26, con la tabla ya creada en producción: allí lo amplía
    Scripts/Issue828b_DocumentosPapelPacking_CartaCheque.sql. Este script ya la crea con los cuatro tipos.

    Fuera del EDMX: la API la lee y escribe por SQL (RepositorioDocumentosPapelPacking) y, si la tabla no existe, todo sigue como
    antes del #828 salvo la marca: F8 factura todos los pedidos de la entrega e imprime, pero no se sabe qué quedó pendiente (no
    se puede reimprimir sin facturar) y Facturar rutas no cuenta lo del packing. Sin errores.

    Lanzar como sa en NV ANTES de publicar la API (también vale después: hasta entonces no hay marcas). Idempotente.
    Deshacer: DROP TABLE dbo.DocumentosPapelPacking; (la API vuelve a funcionar sin marcas).
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.DocumentosPapelPacking', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentosPapelPacking (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Envio int NULL,                          -- el envío de la etiqueta (para localizar la caja)
        Tipo varchar(12) NOT NULL,               -- Factura, Albaran, NotaEntrega, CartaCheque
        Documento varchar(20) NOT NULL,          -- NV2617986, el nº de albarán o el pedido de la nota
        Estado varchar(10) NOT NULL,             -- Impreso, Pendiente
        Impresora nvarchar(128) NULL,
        Mensaje nvarchar(400) NULL,              -- por qué no ha salido
        Aplicacion varchar(10) NOT NULL,         -- Ariadna, Nesto
        Usuario varchar(50) NOT NULL,
        Intentos int NOT NULL CONSTRAINT DF_DocumentosPapelPacking_Intentos DEFAULT (1),
        Fecha datetime NOT NULL CONSTRAINT DF_DocumentosPapelPacking_Fecha DEFAULT (GETDATE()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_DocumentosPapelPacking_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_DocumentosPapelPacking PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_DocumentosPapelPacking_Tipo CHECK (Tipo IN ('Factura', 'Albaran', 'NotaEntrega', 'CartaCheque')),
        CONSTRAINT CK_DocumentosPapelPacking_Estado CHECK (Estado IN ('Impreso', 'Pendiente')),
        CONSTRAINT CK_DocumentosPapelPacking_Aplicacion CHECK (Aplicacion IN ('Ariadna', 'Nesto'))
    );
    PRINT N'Creada dbo.DocumentosPapelPacking.';
END
ELSE
    PRINT N'dbo.DocumentosPapelPacking ya existía: nada que crear.';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DocumentosPapelPacking') AND name = N'UX_DocumentosPapelPacking_Documento')
    CREATE UNIQUE NONCLUSTERED INDEX UX_DocumentosPapelPacking_Documento ON dbo.DocumentosPapelPacking (Empresa, Pedido, Tipo, Documento);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DocumentosPapelPacking') AND name = N'IX_DocumentosPapelPacking_FechaModificacion')
    CREATE NONCLUSTERED INDEX IX_DocumentosPapelPacking_FechaModificacion ON dbo.DocumentosPapelPacking (FechaModificacion) INCLUDE (Estado);
GO
GRANT SELECT, INSERT, UPDATE ON dbo.DocumentosPapelPacking TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación: lo del packing de hoy
SELECT Empresa, Pedido, Envio, Tipo, Documento, Estado, Impresora, Mensaje, Aplicacion, Usuario, Intentos, Fecha, FechaModificacion
FROM dbo.DocumentosPapelPacking WHERE FechaModificacion >= CAST(GETDATE() AS date) ORDER BY Id;
