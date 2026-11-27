/*
    NestoAPI#914, corte «tablero de KPI con nota semanal» (Carlos 27/11/26): dbo.CalidadKpiSemanas y dbo.CalidadKpiUmbrales.

    dbo.CalidadKpiSemanas: una fila por KPI, dueño y semana CERRADA (Semana = el lunes). La escribe solo la API (job semanal de los lunes a
      las 4:20 y POST api/Calidad/Tablero/Recalcular): cada semana de un KPI se sustituye entera (DELETE + INSERT en una transacción).
      Kpi          clave estable del catálogo (PerdidaDesconocida, DiferenciasReposicion, DiferenciasInventario, EntregasIncompletas,
                   PedidoPerfecto, ReenviadoSinCoste).
      TipoDueno    Empresa | Almacen | Vendedor | Preparador.
      Dueno        el código: «1» (empresa), ALG/REI/ALC, el vendedor, el usuario de Ariadna.
      Valor        el valor del KPI (porcentaje, euros o unidades según el KPI); NULL si no hay base (p. ej. ningún pedido).
      Numerador, Denominador   de dónde sale (unidades o pedidos; Denominador NULL si el KPI no es un porcentaje).
      Detalle      explicación corta de usted (el motivo que más resta, lo que queda sin imputar…).
      CalculadoEl  cuándo se calculó.
    dbo.CalidadKpiUmbrales: los tres cortes que separan POOR/FAIR/GOOD/FANTASTIC de un KPI y tipo de dueño, con HISTÓRICO: la nota de una
      semana se saca con el umbral vigente ESA semana (VigenteDesde ≤ lunes), así que aprobar uno nuevo no reescribe las notas pasadas. Se
      escribe solo por PUT api/Calidad/Tablero/Umbrales (Dirección/Informática), con rastro en Modificaciones.

    Fuera del EDMX. Sin el script la API funciona igual: los endpoints del tablero dan 503 y el job no hace nada (ELMAH una vez).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces. Antes de lanzarlo, comprobar que no existen
    ya con otras columnas: SELECT name, create_date FROM sys.tables WHERE name LIKE 'CalidadKpi%';
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: CalidadKpiSemanas SELECT, INSERT, DELETE (se sustituye la semana entera); CalidadKpiUmbrales SELECT, INSERT, UPDATE (UPDATE solo
    si se aprueba dos veces con el mismo VigenteDesde). Modificaciones ya tiene INSERT (la usan otros endpoints).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.CalidadKpiSemanas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CalidadKpiSemanas (
        Id int IDENTITY(1,1) NOT NULL,
        Kpi varchar(40) NOT NULL,
        TipoDueno varchar(12) NOT NULL,
        Dueno varchar(30) NOT NULL,
        Semana date NOT NULL,
        Valor decimal(19, 4) NULL,
        Numerador decimal(19, 4) NULL,
        Denominador decimal(19, 4) NULL,
        Detalle nvarchar(400) NULL,
        CalculadoEl datetime NOT NULL CONSTRAINT DF_CalidadKpiSemanas_CalculadoEl DEFAULT (GETDATE()),
        CONSTRAINT PK_CalidadKpiSemanas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_CalidadKpiSemanas UNIQUE (Kpi, TipoDueno, Dueno, Semana),
        CONSTRAINT CK_CalidadKpiSemanas_TipoDueno CHECK (TipoDueno IN ('Empresa', 'Almacen', 'Vendedor', 'Preparador')),
        CONSTRAINT CK_CalidadKpiSemanas_Lunes CHECK (DATEDIFF(day, '20000103', Semana) % 7 = 0)
    );
    PRINT 'CalidadKpiSemanas: creada.';
END
ELSE
    PRINT 'CalidadKpiSemanas: ya existía.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CalidadKpiSemanas_Semana' AND object_id = OBJECT_ID('dbo.CalidadKpiSemanas'))
    CREATE INDEX IX_CalidadKpiSemanas_Semana ON dbo.CalidadKpiSemanas (Semana, Kpi);
GO

IF OBJECT_ID('dbo.CalidadKpiUmbrales', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CalidadKpiUmbrales (
        Id int IDENTITY(1,1) NOT NULL,
        Kpi varchar(40) NOT NULL,
        TipoDueno varchar(12) NOT NULL,
        CortePoorFair decimal(19, 4) NOT NULL,       -- POOR | FAIR
        CorteFairGood decimal(19, 4) NOT NULL,       -- FAIR | GOOD
        CorteGoodFantastic decimal(19, 4) NOT NULL,  -- GOOD | FANTASTIC
        VigenteDesde date NOT NULL,                  -- un lunes; el primero de un KPI, 03/01/2000 («desde siempre»)
        Usuario nvarchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_CalidadKpiUmbrales_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_CalidadKpiUmbrales PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_CalidadKpiUmbrales UNIQUE (Kpi, TipoDueno, VigenteDesde),
        CONSTRAINT CK_CalidadKpiUmbrales_TipoDueno CHECK (TipoDueno IN ('Empresa', 'Almacen', 'Vendedor', 'Preparador'))
    );
    PRINT 'CalidadKpiUmbrales: creada.';
END
ELSE
    PRINT 'CalidadKpiUmbrales: ya existía.';
GO

GRANT SELECT, INSERT, DELETE ON dbo.CalidadKpiSemanas TO [NUEVAVISION\RDS2016$];
GO
GRANT SELECT, INSERT, UPDATE ON dbo.CalidadKpiUmbrales TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT t.name, t.create_date, (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS Columnas
FROM sys.tables t WHERE t.name IN ('CalidadKpiSemanas', 'CalidadKpiUmbrales');
GO
