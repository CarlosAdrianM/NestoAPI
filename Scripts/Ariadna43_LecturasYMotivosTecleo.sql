/*
    Ariadna#43: que en Algete se escanee todo.

    Ejecutar en SSMS contra NV como sa (ALTER, CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar
    dos veces. ORDEN: este script, luego la API y luego Ariadna (la versión con Ariadna#43 manda los métodos ETIQ y TECLA:
    sin este script la base de datos los rechaza; sin la API nueva, la API también).

    1. PreparacionEscaneos.Metodo (char(6)) admite dos valores más. Lo que ya estaba sigue valiendo:
         SCAN   el código del proveedor (EAN del envase), con el lector o con la cámara (y lo tecleado después de leerlo)
         ETIQ   nuestra etiqueta de producto (la de la referencia, NestoAPI#654), con el lector o con la cámara      (NUEVO)
         TECLA  tecleado a mano (la referencia o el código), y lo tecleado después                                 (NUEVO)
         MANUAL lo de antes de Ariadna#43 (etiqueta o tecleado, sin distinguir); lo siguen mandando las Ariadna sin #43
         FALTA  no estaba en el hueco

    2. ProductosMotivoTecleo: por qué se teclea un producto que tiene un código válido. Se pregunta una sola vez por producto
       (una fila por producto: el último que contesta manda). NOSELEE = «código que se lee mal»: sale en el informe semanal a
       Compras (NestoAPI#674). GRANT: SELECT, INSERT y UPDATE (no se borra desde la API).

    3. LecturasNoCasadasAlmacen: las lecturas que no casan (un código desconocido o de otro producto), con el código leído.
       Ariadna las apunta sin molestar al mozo. GRANT: SELECT e INSERT.

    Las tablas no están en el EDMX (la API las lee y escribe con SQL a mano).
*/

SET NOCOUNT ON;
USE NV;
GO

-- 1. Métodos nuevos en PreparacionEscaneos
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreparacionEscaneos_Metodo'
           AND parent_object_id = OBJECT_ID('dbo.PreparacionEscaneos')
           AND definition NOT LIKE '%ETIQ%')
BEGIN
    ALTER TABLE dbo.PreparacionEscaneos DROP CONSTRAINT CK_PreparacionEscaneos_Metodo;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreparacionEscaneos_Metodo'
               AND parent_object_id = OBJECT_ID('dbo.PreparacionEscaneos'))
BEGIN
    ALTER TABLE dbo.PreparacionEscaneos WITH CHECK ADD CONSTRAINT CK_PreparacionEscaneos_Metodo
        CHECK (Metodo IN ('SCAN', 'ETIQ', 'TECLA', 'MANUAL', 'FALTA'));
END
GO

-- 2. Por qué se teclea un producto con código
IF OBJECT_ID('dbo.ProductosMotivoTecleo') IS NULL
BEGIN
    CREATE TABLE dbo.ProductosMotivoTecleo (
        Empresa char(3) NOT NULL,
        Producto char(15) NOT NULL,
        Motivo varchar(10) NOT NULL,                    -- NOSELEE / SINCODIGO / OTRO
        Codigo varchar(20) NULL,                        -- el código válido que tenía el producto al preguntar
        Veces int NOT NULL CONSTRAINT DF_ProductosMotivoTecleo_Veces DEFAULT (1),
        Usuario varchar(50) NOT NULL,                   -- el último que contestó
        Dispositivo varchar(50) NULL,
        FechaCreacion datetime NOT NULL CONSTRAINT DF_ProductosMotivoTecleo_FechaCreacion DEFAULT (GETDATE()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ProductosMotivoTecleo_FechaModificacion DEFAULT (GETDATE()),

        CONSTRAINT PK_ProductosMotivoTecleo PRIMARY KEY CLUSTERED (Empresa, Producto),
        CONSTRAINT CK_ProductosMotivoTecleo_Motivo CHECK (Motivo IN ('NOSELEE', 'SINCODIGO', 'OTRO'))
    );
END
GO

GRANT SELECT, INSERT, UPDATE ON dbo.ProductosMotivoTecleo TO [NUEVAVISION\RDS2016$];
GO

-- 3. Lecturas que no casan
IF OBJECT_ID('dbo.LecturasNoCasadasAlmacen') IS NULL
BEGIN
    CREATE TABLE dbo.LecturasNoCasadasAlmacen (
        Id bigint IDENTITY(1,1) NOT NULL,
        Empresa char(3) NOT NULL,
        Codigo varchar(50) NOT NULL,                    -- lo leído (o tecleado) tal cual
        ProductoEsperado char(15) NULL,                 -- el que tocaba
        ProductoLeido char(15) NULL,                    -- si el código es de otro producto de la lista, cuál
        Pantalla varchar(20) NULL,                      -- Recorrido, Packing, EnviarReposicion, Recibir, Ubicar
        TipoOrigen char(4) NULL,                        -- PICK / REPO / COMP…
        NumeroOrigen int NULL,
        ConEscaner bit NULL,                            -- 1 lector o cámara, 0 tecleado, NULL no se sabe
        Usuario varchar(50) NOT NULL,
        Dispositivo varchar(50) NULL,
        FechaLectura datetime NOT NULL,                 -- la hora del aparato
        FechaRegistro datetime NOT NULL CONSTRAINT DF_LecturasNoCasadasAlmacen_FechaRegistro DEFAULT (GETDATE()),

        CONSTRAINT PK_LecturasNoCasadasAlmacen PRIMARY KEY CLUSTERED (Id)
    );
    CREATE NONCLUSTERED INDEX IX_LecturasNoCasadasAlmacen_Fecha ON dbo.LecturasNoCasadasAlmacen (Empresa, FechaLectura);
    CREATE NONCLUSTERED INDEX IX_LecturasNoCasadasAlmacen_Codigo ON dbo.LecturasNoCasadasAlmacen (Empresa, Codigo);
END
GO

GRANT SELECT, INSERT ON dbo.LecturasNoCasadasAlmacen TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT definition FROM sys.check_constraints WHERE name = 'CK_PreparacionEscaneos_Metodo';
SELECT OBJECT_ID('dbo.ProductosMotivoTecleo') AS ProductosMotivoTecleo, OBJECT_ID('dbo.LecturasNoCasadasAlmacen') AS LecturasNoCasadasAlmacen;
GO
