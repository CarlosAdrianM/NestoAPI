/*
    NestoAPI#630 (stock obsoleto, corte 2 de la épica #628): tabla PropuestasStock.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. No está en el EDMX (la API la lee y escribe con SQL a mano), así que se puede lanzar antes o después del deploy:
    sin ella, el job calcula y manda el Excel igual, avisando en el correo de que no ha guardado nada.

    Una fila por mes, producto y almacén con lo que calculó el job «propuestas-stock-mensual» (día 1 a las 04:30) o el
    POST api/PropuestasStock/Generar: la fase, las ENTRADAS (venta media y ventana, factor estacional, días en servir,
    seguridad, múltiplos, cobertura objetivo, fórmula; la serie de 60 meses en EntradasJson), los indicadores, la propuesta
    y su explicación. NUNCA SE PISA: si se relanza un mes ya guardado, solo se insertan los productos que falten (la API
    lo comprueba antes de insertar; el UNIQUE lo garantiza). Así se podrá medir después si la propuesta acertó.

    Las columnas Decision* quedan a NULL: las rellenará el corte 3 (#631, acciones con supervisión de Compras).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE (el UPDATE, para la decisión del corte 3). Sin DELETE: las filas no se borran.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PropuestasStock') IS NULL
BEGIN
    CREATE TABLE dbo.PropuestasStock (
        Id int IDENTITY(1, 1) NOT NULL,
        Mes date NOT NULL,                              -- primer día del último mes cerrado que se usó
        Producto varchar(15) NOT NULL,                  -- Productos.Número (empresa 1)
        Almacen char(3) NOT NULL,                       -- de momento siempre ALG
        Nombre nvarchar(50) NULL,
        Familia varchar(10) NULL,
        Grupo varchar(3) NULL,
        Proveedor varchar(10) NULL,                     -- el primero de ProveedoresProducto por Orden
        EstadoProducto smallint NULL,                   -- Productos.Estado al calcular
        Fase char(1) NOT NULL,                          -- A nuevo, B maduro, C intermitente
        MesesDesdePrimeraVenta int NULL,

        -- Entradas
        Stock int NOT NULL,                             -- todos los almacenes menos EXT, empresas 1 y 3, a fin de mes
        PrecioMedio decimal(19, 4) NOT NULL,
        Ventas12Meses int NOT NULL,
        VentaMedia6Meses decimal(12, 2) NOT NULL,
        VentaMediaPonderada decimal(12, 2) NOT NULL,    -- (3 × media 3 m + 2 × media 6 m + media 12 m) ÷ 6
        VentaMensualReferencia decimal(12, 2) NOT NULL, -- la del exceso: media 6 m (B), velocidad (A), media 12 m (C)
        FactorEstacional decimal(6, 2) NOT NULL,
        ConEstacionalidad bit NOT NULL,                 -- 0 = la familia no tiene 3 años de historia
        DiasEnServir int NOT NULL,
        DiasEnServirSupuestos bit NOT NULL,             -- 1 = sin proveedor: se usó PropuestasStock:DiasEnServirPorDefecto
        DiasSeguridad int NOT NULL,
        Multiplos int NOT NULL,
        CoberturaObjetivoDias int NOT NULL,             -- la configurada, nunca por debajo de la alcanzable
        StockMaximoActual int NULL,                     -- ControlesStock de ALG al calcular
        StockMinimoActual int NULL,
        Formula varchar(40) NULL,
        EntradasJson nvarchar(max) NULL,                -- serie de 60 meses, stock de 12, índices de la familia, años

        -- Indicadores
        CoberturaMeses decimal(12, 1) NULL,             -- stock ÷ venta media de 6 meses; NULL = sin venta en 6 meses
        CoberturaAlcanzableDias int NOT NULL,           -- días en servir + seguridad + días que tarda en venderse un múltiplo
        PendienteRelativa decimal(12, 2) NULL,          -- % al mes, 12 meses desestacionalizados
        RatioTrimestre decimal(12, 3) NULL,             -- último trimestre ÷ el mismo del año anterior
        AnioPico int NULL,
        PorcentajeDelPico decimal(8, 1) NULL,
        AniosDesdePico int NULL,
        Tendencia varchar(20) NULL,                     -- Sube, Estable, Bajón puntual, Declive, Sin datos (solo B)
        Velocidad decimal(12, 2) NULL,                  -- A: uds/mes desde la primera venta
        TasaRecompra decimal(6, 1) NULL,                -- A: % de clientes de los primeros 60 días que repiten a 30 días
        DemandaNoServida int NOT NULL,                  -- uds en líneas de venta −1 y 1 al calcular
        PedidoCompraAbierto int NOT NULL,               -- uds en pedidos de compra −1 y 1 al calcular
        ExcesoUnidades int NOT NULL,
        ExcesoValorado decimal(18, 2) NOT NULL,         -- max(0, stock − objetivo × venta mensual) × precio medio

        -- Propuesta
        Accion varchar(30) NOT NULL,                    -- Ajustar máx/mín, Mantener, Pasar a estado 1, Parar compras, Liquidar, Nada
        StockMaximoPropuesto int NULL,
        StockMinimoPropuesto int NULL,
        Explicacion nvarchar(2000) NULL,
        FechaCalculo datetime NOT NULL CONSTRAINT DF_PropuestasStock_FechaCalculo DEFAULT (GETDATE()),

        -- Decisión (corte 3, #631)
        Decision varchar(30) NULL,                      -- qué se hizo
        DecisionUsuario varchar(50) NULL,               -- quién
        DecisionFecha datetime NULL,                    -- cuándo
        DecisionMotivo nvarchar(500) NULL,              -- por qué (sobre todo si no se sigue la propuesta)

        CONSTRAINT PK_PropuestasStock PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_PropuestasStock_Mes_Producto_Almacen UNIQUE (Mes, Producto, Almacen),
        CONSTRAINT CK_PropuestasStock_Fase CHECK (Fase IN ('A', 'B', 'C'))
    );
END
GO

-- Para el GET por mes ordenado por exceso (sin filtro: nada de índices filtrados).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PropuestasStock_Mes_Exceso' AND object_id = OBJECT_ID('dbo.PropuestasStock'))
    CREATE INDEX IX_PropuestasStock_Mes_Exceso ON dbo.PropuestasStock (Mes, ExcesoValorado DESC);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.PropuestasStock TO [NUEVAVISION\RDS2016$];
GO
