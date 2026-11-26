/*
    NestoAPI#883 (corte 1; decisión de Carlos 20/11/26): fecha de inventario por almacén y aviso de las reposiciones de la víspera.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces. Fuera del EDMX (la API la lee y escribe
    con SQL a mano): sin estas tablas, api/Inventarios/Fechas responde 503 y el job «inventario-fechas-aviso» no hace nada.

    - InventariosFechas: una fila por empresa + almacén (ALG, REI, ALC) + día de inventario. AvisoEnviado = cuándo salió el aviso (tres
      laborables antes; una vez).
    - InventariosFechasViajes: la decisión de cada reposición que toca ese inventario (Quitar = se puso la excepción SinReposicion de #867;
      Mantener = confirmada, deja de avisar).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.InventariosFechas') IS NULL
BEGIN
    CREATE TABLE dbo.InventariosFechas (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Almacen char(3) NOT NULL,
        Fecha date NOT NULL,
        Comentario nvarchar(200) NULL,
        Usuario nvarchar(50) NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_InventariosFechas_FechaModificacion DEFAULT (GETDATE()),
        AvisoEnviado datetime NULL,

        CONSTRAINT PK_InventariosFechas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_InventariosFechas_Clave UNIQUE (Empresa, Almacen, Fecha)
    );
END
GO

IF OBJECT_ID('dbo.InventariosFechasViajes') IS NULL
BEGIN
    CREATE TABLE dbo.InventariosFechasViajes (
        InventarioFechaId int NOT NULL,
        Origen char(3) NOT NULL,
        Destino char(3) NOT NULL,
        FechaLlegada date NOT NULL,
        Decision varchar(20) NOT NULL,
        Usuario nvarchar(50) NULL,
        Fecha datetime NOT NULL,

        CONSTRAINT PK_InventariosFechasViajes PRIMARY KEY CLUSTERED (InventarioFechaId, Origen, Destino, FechaLlegada),
        CONSTRAINT FK_InventariosFechasViajes_Fecha FOREIGN KEY (InventarioFechaId) REFERENCES dbo.InventariosFechas (Id),
        CONSTRAINT CK_InventariosFechasViajes_Decision CHECK (Decision IN ('Quitar', 'Mantener'))
    );
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.InventariosFechas TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.InventariosFechasViajes TO [NUEVAVISION\RDS2016$];
GO

SELECT 'InventariosFechas' AS Tabla, CASE WHEN OBJECT_ID('dbo.InventariosFechas') IS NULL THEN 'FALTA' ELSE 'OK' END AS Estado
UNION ALL
SELECT 'InventariosFechasViajes', CASE WHEN OBJECT_ID('dbo.InventariosFechasViajes') IS NULL THEN 'FALTA' ELSE 'OK' END;
GO
