/*
    NestoAPI#631 (stock obsoleto, corte 3 de la épica #628): decidir las propuestas de stock con supervisión de Compras.

    Ejecutar en SSMS contra NV como sa (ALTER, CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar
    dos veces. Requiere Scripts/Issue630_PropuestasStock.sql (la tabla PropuestasStock). Lanzarlo ANTES del deploy de la API
    del corte 3: sin él, GET api/PropuestasStock sigue funcionando como en el corte 2, pero el filtro por estado, el resumen
    y las decisiones responden 404 diciendo que falta este script.

    1) Columnas nuevas en PropuestasStock (las Decision* ya estaban del corte 2): lo que se aplicó (valor anterior y nuevo
       del máximo y el mínimo del almacén de la propuesta, y del estado del producto), el silencio (motivo y caducidad) y el
       canal de liquidación propuesto (Outlet o Ganavisiones; de momento solo se apunta).
    2) Tabla PropuestasStockDecisiones: histórico de TODAS las decisiones (una propuesta pospuesta o rechazada se puede volver
       a decidir y en la fila solo queda la última). Nunca se borra ni se modifica: GRANT solo SELECT e INSERT.
    3) Índice por producto, almacén y mes para saber si hay una propuesta más reciente y si hay un silencio vigente.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PropuestasStock') IS NULL
BEGIN
    RAISERROR('Falta la tabla PropuestasStock: lance antes Scripts/Issue630_PropuestasStock.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

IF COL_LENGTH('dbo.PropuestasStock', 'MotivoSilencio') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD MotivoSilencio varchar(20) NULL;       -- Nuevo, Estacional, Aparato
IF COL_LENGTH('dbo.PropuestasStock', 'SilenciadaHasta') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD SilenciadaHasta date NULL;             -- caducidad del silencio (incluida)
IF COL_LENGTH('dbo.PropuestasStock', 'CanalLiquidacion') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD CanalLiquidacion varchar(20) NULL;     -- Outlet, Ganavisiones
IF COL_LENGTH('dbo.PropuestasStock', 'StockMaximoAnterior') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD StockMaximoAnterior int NULL;          -- ControlesStock del almacén al aplicar
IF COL_LENGTH('dbo.PropuestasStock', 'StockMinimoAnterior') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD StockMinimoAnterior int NULL;
IF COL_LENGTH('dbo.PropuestasStock', 'StockMaximoAplicado') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD StockMaximoAplicado int NULL;          -- lo que se escribió (el propuesto u otro, con motivo)
IF COL_LENGTH('dbo.PropuestasStock', 'StockMinimoAplicado') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD StockMinimoAplicado int NULL;
IF COL_LENGTH('dbo.PropuestasStock', 'EstadoAnterior') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD EstadoAnterior smallint NULL;          -- Productos.Estado al aplicar «Pasar a estado 1»
IF COL_LENGTH('dbo.PropuestasStock', 'EstadoAplicado') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD EstadoAplicado smallint NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PropuestasStock_Decision' AND parent_object_id = OBJECT_ID('dbo.PropuestasStock'))
    ALTER TABLE dbo.PropuestasStock WITH CHECK ADD CONSTRAINT CK_PropuestasStock_Decision
        CHECK (Decision IS NULL OR Decision IN ('Aplicar', 'Rechazar', 'Posponer', 'Silenciar'));
GO

-- Un silencio lleva siempre motivo y caducidad.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PropuestasStock_Silencio' AND parent_object_id = OBJECT_ID('dbo.PropuestasStock'))
    ALTER TABLE dbo.PropuestasStock WITH CHECK ADD CONSTRAINT CK_PropuestasStock_Silencio
        CHECK ((Decision = 'Silenciar' AND MotivoSilencio IS NOT NULL AND SilenciadaHasta IS NOT NULL)
            OR (ISNULL(Decision, '') <> 'Silenciar' AND MotivoSilencio IS NULL AND SilenciadaHasta IS NULL));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PropuestasStock_Producto_Almacen_Mes' AND object_id = OBJECT_ID('dbo.PropuestasStock'))
    CREATE INDEX IX_PropuestasStock_Producto_Almacen_Mes ON dbo.PropuestasStock (Producto, Almacen, Mes)
        INCLUDE (Decision, SilenciadaHasta, MotivoSilencio);
GO

IF OBJECT_ID('dbo.PropuestasStockDecisiones') IS NULL
BEGIN
    CREATE TABLE dbo.PropuestasStockDecisiones (
        Id int IDENTITY(1, 1) NOT NULL,
        PropuestaId int NOT NULL,                       -- PropuestasStock.Id
        Mes date NOT NULL,
        Producto varchar(15) NOT NULL,
        Almacen char(3) NOT NULL,
        Decision varchar(30) NOT NULL,                  -- Aplicar, Rechazar, Posponer, Silenciar
        Usuario varchar(50) NOT NULL,                   -- quién (usuario del Identity / dominio)
        Fecha datetime NOT NULL,                        -- cuándo
        Motivo nvarchar(500) NULL,
        MotivoSilencio varchar(20) NULL,
        SilenciadaHasta date NULL,
        CanalLiquidacion varchar(20) NULL,
        Cambios nvarchar(1000) NULL,                    -- lo escrito: «ALG: máximo 555 → 364, mínimo 295 → 220; Estado 0 → 1»

        CONSTRAINT PK_PropuestasStockDecisiones PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_PropuestasStockDecisiones_PropuestasStock FOREIGN KEY (PropuestaId) REFERENCES dbo.PropuestasStock (Id),
        CONSTRAINT CK_PropuestasStockDecisiones_Decision CHECK (Decision IN ('Aplicar', 'Rechazar', 'Posponer', 'Silenciar'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PropuestasStockDecisiones_Producto' AND object_id = OBJECT_ID('dbo.PropuestasStockDecisiones'))
    CREATE INDEX IX_PropuestasStockDecisiones_Producto ON dbo.PropuestasStockDecisiones (Producto, Fecha);
GO

-- La API cambia máximos y mínimos (ya lo hacía con PUT api/ControlesStock) y el estado del producto al aplicar «Pasar a estado 1».
GRANT SELECT, INSERT, UPDATE ON dbo.PropuestasStock TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT ON dbo.PropuestasStockDecisiones TO [NUEVAVISION\RDS2016$];
GO

SET NOEXEC OFF;
GO
