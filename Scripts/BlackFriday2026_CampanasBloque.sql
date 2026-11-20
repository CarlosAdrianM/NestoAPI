/*
    Black Friday 2026 (20/11/26): alta EN BLOQUE de campañas (POST api/Campanas/Bloque) y su baja por nombre
    (DELETE api/Campanas/PorNombre/{nombre} y PUT …/Cerrar). Lanzar como sa en NV ANTES de crear la campaña de verdad.
    Idempotente: se puede relanzar entero.

    La API escribe ahora en las dos tablas de la feria online (Scripts/FeriaOnline_CampanasCondicionContado.sql) en la
    MISMA transacción que las filas de DescuentosProducto:
      - dbo.CampanasAplicarDto: INSERT (fichas con «Aplicar Dto» = 0 que entran), DELETE (baja de lo no activado),
        UPDATE (cierre; ya lo tenía).
      - dbo.CampanasCondicionContado: INSERT, DELETE y UPDATE (antes solo SELECT).
    Si las tablas no existen, se crean igual que en el script de la feria (sin sus filas).
    DescuentosProducto y Modificaciones no necesitan nada: la API ya escribía en ellas.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.CampanasCondicionContado', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CampanasCondicionContado (
        Campana varchar(50) NOT NULL,
        Empresa char(3) NOT NULL,
        Producto char(15) NOT NULL,
        FechaDesde date NOT NULL,
        FechaHasta date NOT NULL,
        TextoAviso nvarchar(500) NULL,
        Usuario varchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_CampanasCondicionContado_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_CampanasCondicionContado PRIMARY KEY CLUSTERED (Campana, Empresa, Producto),
        CONSTRAINT CK_CampanasCondicionContado_Fechas CHECK (FechaDesde <= FechaHasta)
    );
    PRINT N'Creada dbo.CampanasCondicionContado';
END
ELSE
    PRINT N'dbo.CampanasCondicionContado ya existía';
GO
IF OBJECT_ID(N'dbo.CampanasAplicarDto', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CampanasAplicarDto (
        Campana varchar(50) NOT NULL,
        Empresa char(3) NOT NULL,
        Producto char(15) NOT NULL,
        FechaDesde date NOT NULL,
        FechaHasta date NOT NULL,
        AplicarDtoAnterior bit NULL,
        FechaActivado datetime NULL,
        FechaRestaurado datetime NULL,
        Usuario varchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_CampanasAplicarDto_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_CampanasAplicarDto PRIMARY KEY CLUSTERED (Campana, Empresa, Producto),
        CONSTRAINT CK_CampanasAplicarDto_Fechas CHECK (FechaDesde <= FechaHasta)
    );
    PRINT N'Creada dbo.CampanasAplicarDto';
END
ELSE
    PRINT N'dbo.CampanasAplicarDto ya existía';
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CampanasCondicionContado TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CampanasAplicarDto TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT 'CampanasCondicionContado' AS Tabla, Campana, COUNT(*) AS Filas FROM dbo.CampanasCondicionContado GROUP BY Campana
UNION ALL
SELECT 'CampanasAplicarDto', Campana, COUNT(*) FROM dbo.CampanasAplicarDto GROUP BY Campana;
