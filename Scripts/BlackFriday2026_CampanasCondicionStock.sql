/*
    NestoAPI#889 (Black Friday 2026, 24/11/26): campañas cuyo descuento es «solo para lo que haya en stock».
    Lanzar como sa en NV ANTES de crear (o relanzar) la campaña de verdad con "CondicionStock": true en POST api/Campanas/Bloque.
    Idempotente: se puede relanzar entero.

    Una fila por campaña + empresa + producto, como dbo.CampanasCondicionContado (no se reutiliza esa tabla: una fila allí
    significa «exige contado»). La API:
      - la rellena el alta en bloque (INSERT … NOT EXISTS) en la MISMA transacción que DescuentosProducto;
      - la borra (DELETE api/Campanas/PorNombre/{n}) o la cierra (PUT …/Cerrar: FechaHasta) con la campaña;
      - la lee (caché de 10 min) al grabar un pedido: si una línea con descuento de un producto de aquí, vigente hoy, no se puede
        servir con lo que hay (ALG + REI + ALC, empresas 1 y 3, menos lo pendiente de otros pedidos), avisa. Nunca bloquea.
    Sin esta tabla la API funciona como antes (ningún aviso) y el alta con CondicionStock da 503 «falta la tabla».

    Si la campaña ya está creada sin la marca: relanzar el MISMO POST api/Campanas/Bloque con "CondicionStock": true (ensayo=false):
    no duplica descuentos («ya estaba») y solo añade las filas de esta tabla.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.CampanasCondicionStock', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CampanasCondicionStock (
        Campana varchar(50) NOT NULL,
        Empresa char(3) NOT NULL,
        Producto char(15) NOT NULL,
        FechaDesde date NOT NULL,
        FechaHasta date NOT NULL,
        Usuario varchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_CampanasCondicionStock_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_CampanasCondicionStock PRIMARY KEY CLUSTERED (Campana, Empresa, Producto),
        CONSTRAINT CK_CampanasCondicionStock_Fechas CHECK (FechaDesde <= FechaHasta)
    );
    PRINT N'Creada dbo.CampanasCondicionStock';
END
ELSE
    PRINT N'dbo.CampanasCondicionStock ya existía';
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CampanasCondicionStock TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT Campana, Empresa, COUNT(*) AS Filas, MIN(FechaDesde) AS Desde, MAX(FechaHasta) AS Hasta
FROM dbo.CampanasCondicionStock
GROUP BY Campana, Empresa;
