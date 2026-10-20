/*
    NestoAPI#671 (corte 2a de la épica #669): cuarta fuente «Parecidos» (item-kNN por coseno), popularidad y registro de
    lo que verá el modelo.

    Ejecutar en SSMS contra NV como sa (CREATE, ALTER y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar
    dos veces. Requiere el script Issue670_SugerenciasProductos.sql (ya lanzado el 19/10/26). Ninguna tabla está en el EDMX
    (la API las lee y escribe con SQL a mano) y la API mira con OBJECT_ID/COL_LENGTH si existen, así que se puede lanzar
    antes o después del deploy: sin este script la API funciona como el corte 1 (sin «Parecidos» y sin registrar Fuentes ni
    TamanoCarrito) y el job nocturno solo calcula los pares y los perfiles.

    1) SugerenciasProductosAfinidad: por cliente, los 50 productos que NO ha comprado en 24 meses con más afinidad
       item-kNN (coseno entre productos sobre los clientes que los compran; afinidad = suma de las similitudes con lo que
       el cliente sí compra). Rango 1 = el más afín. La rellena el job «sugerencias-productos-pares» (05:00, L-S) en la
       misma pasada que los pares: se vacía y se rellena entera en una transacción.
    2) SugerenciasProductosPopularidad: por producto, clientes distintos en 12 y en 3 meses y cestas (cliente y día) en 12
       meses. Rango 1 = el que más clientes tiene en 12 meses. La rellena el mismo job.
    3) SugerenciasProductosMostradas: columnas nuevas para el modelo (corte 2c):
       - Fuentes varchar(50): todas las fuentes que propusieron el producto («CompradoJunto,Parecidos»); Motivo sigue
         siendo la que se enseña al vendedor.
       - TamanoCarrito int: productos distintos en el carrito cuando se enseñó.
       - Exploracion bit: 1 si el hueco se eligió al azar (ε-greedy del corte 2c). Hasta el 2c, siempre 0.
    4) CK de Motivo con «Parecidos».

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT, DELETE en las tablas del job (como pares y perfiles).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.SugerenciasProductosAfinidad') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasProductosAfinidad (
        Cliente varchar(10) NOT NULL,
        Producto varchar(15) NOT NULL,                  -- uno que el cliente no ha comprado en 24 meses
        Afinidad decimal(12, 6) NOT NULL,               -- suma de similitudes coseno con lo que sí compra
        Rango smallint NOT NULL,                        -- 1 = el más afín de ese cliente
        FechaCalculo datetime NOT NULL,

        CONSTRAINT PK_SugerenciasProductosAfinidad PRIMARY KEY CLUSTERED (Cliente, Producto)
    );
END
GO

IF OBJECT_ID('dbo.SugerenciasProductosPopularidad') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasProductosPopularidad (
        Producto varchar(15) NOT NULL,
        Clientes12Meses int NOT NULL,                   -- clientes distintos que lo han comprado en 12 meses
        Clientes3Meses int NOT NULL,                    -- ídem en 3 meses (tendencia)
        Cestas12Meses int NOT NULL,                     -- cestas (cliente y día) con el producto en 12 meses
        Rango int NOT NULL,                             -- 1 = el de más clientes en 12 meses
        FechaCalculo datetime NOT NULL,

        CONSTRAINT PK_SugerenciasProductosPopularidad PRIMARY KEY CLUSTERED (Producto)
    );
END
GO

IF COL_LENGTH('dbo.SugerenciasProductosMostradas', 'Fuentes') IS NULL
    ALTER TABLE dbo.SugerenciasProductosMostradas ADD Fuentes varchar(50) NULL;
GO

IF COL_LENGTH('dbo.SugerenciasProductosMostradas', 'TamanoCarrito') IS NULL
    ALTER TABLE dbo.SugerenciasProductosMostradas ADD TamanoCarrito int NULL;
GO

IF COL_LENGTH('dbo.SugerenciasProductosMostradas', 'Exploracion') IS NULL
    ALTER TABLE dbo.SugerenciasProductosMostradas ADD Exploracion bit NOT NULL
        CONSTRAINT DF_SugerenciasProductosMostradas_Exploracion DEFAULT (0);
GO

-- El CK de Motivo con «Parecidos» (se rehace solo si todavía no lo admite)
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_SugerenciasProductosMostradas_Motivo'
                 AND parent_object_id = OBJECT_ID('dbo.SugerenciasProductosMostradas')
                 AND definition LIKE '%Parecidos%')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_SugerenciasProductosMostradas_Motivo'
                 AND parent_object_id = OBJECT_ID('dbo.SugerenciasProductosMostradas'))
        ALTER TABLE dbo.SugerenciasProductosMostradas DROP CONSTRAINT CK_SugerenciasProductosMostradas_Motivo;
    ALTER TABLE dbo.SugerenciasProductosMostradas ADD CONSTRAINT CK_SugerenciasProductosMostradas_Motivo
        CHECK (Motivo IN ('CompradoJunto', 'LeToca', 'Potenciar', 'Parecidos'));
END
GO

GRANT SELECT, INSERT, DELETE ON dbo.SugerenciasProductosAfinidad TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, DELETE ON dbo.SugerenciasProductosPopularidad TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación:
-- SELECT COUNT(*) AS Filas, COUNT(DISTINCT Cliente) AS Clientes, MAX(FechaCalculo) AS Calculo FROM dbo.SugerenciasProductosAfinidad;
-- SELECT TOP 20 * FROM dbo.SugerenciasProductosPopularidad ORDER BY Rango;
-- SELECT Motivo, Fuentes, TamanoCarrito, Exploracion, COUNT(*) FROM dbo.SugerenciasProductosMostradas
-- WHERE Fuentes IS NOT NULL GROUP BY Motivo, Fuentes, TamanoCarrito, Exploracion;
