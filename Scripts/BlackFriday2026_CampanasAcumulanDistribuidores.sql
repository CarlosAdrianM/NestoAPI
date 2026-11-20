/*
    Black Friday 2026 (Carlos 20/11/26): campañas cuyo descuento se aplica a los DISTRIBUIDORES (estado 6 o 67) ENCIMA del suyo:
    primero el suyo y luego el de la campaña sobre lo que queda (30 % + 20 % → 44 %). Para los demás clientes, nada cambia.
    La marca es POR DATO: una fila por nombre de campaña (el de DescuentosProducto.Campana) con sus fechas. Sin esta tabla, o sin
    fila, la API calcula y valida como siempre. La rellena POST api/Campanas/Bloque con AcumulaParaDistribuidores = true (en la
    misma transacción) y la quitan DELETE api/Campanas/PorNombre/{nombre} (borra) y PUT …/Cerrar (cierra). Caché de 5 min en la API.
    Lanzar como sa en NV. Idempotente.

    Para marcar a mano una campaña que ya existe (ejemplo, comentado):
        INSERT INTO dbo.CampanasAcumulanDistribuidores (Campana, FechaDesde, FechaHasta, Usuario)
        VALUES (N'Black Friday 2026', '20261125', '20261130', 'NUEVAVISION\Carlos');
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.CampanasAcumulanDistribuidores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CampanasAcumulanDistribuidores (
        Campana nvarchar(50) NOT NULL,
        FechaDesde date NOT NULL,
        FechaHasta date NOT NULL,
        Usuario varchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_CampanasAcumulanDistribuidores_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_CampanasAcumulanDistribuidores PRIMARY KEY CLUSTERED (Campana),
        CONSTRAINT CK_CampanasAcumulanDistribuidores_Fechas CHECK (FechaDesde <= FechaHasta)
    );
    PRINT N'Creada dbo.CampanasAcumulanDistribuidores';
END
ELSE
    PRINT N'dbo.CampanasAcumulanDistribuidores ya existía';
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CampanasAcumulanDistribuidores TO [NUEVAVISION\RDS2016$];
GO
SELECT Campana, FechaDesde, FechaHasta, Usuario, FechaModificacion FROM dbo.CampanasAcumulanDistribuidores ORDER BY FechaDesde DESC;
