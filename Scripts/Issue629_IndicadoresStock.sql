/*
    ##################################################################################################################
    ###  LANZAR ANTES DEL DEPLOY DE LA API (o en cualquier momento antes del día 1). Sin esta tabla, el job        ###
    ###  «indicadores-stock-mensual» no calcula nada (lo apunta en ELMAH) y GET api/IndicadoresStock da 503.        ###
    ##################################################################################################################

    NestoAPI#629 (corte 1 de la épica #628, control de stock obsoleto): indicadores mensuales de stock.

    Ejecutar en SSMS contra NV como sa (CREATE TABLE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar
    dos veces. La tabla NO va en el EDMX: la API la lee y la escribe con SQL directo.

    IndicadoresStock: una fila por mes (Mes = día 1) y almacén, más la fila Almacen = 'TOTAL' (todos los almacenes menos EXT).
    La rellena de noche el job de Hangfire «indicadores-stock-mensual» (día 1 a las 03:30, el mes anterior; el día 2 si el 1 es
    domingo). Cada mes se borra y se vuelve a insertar, así que relanzar un mes no duplica. Los 36 meses hacia atrás se
    reconstruyen con POST api/IndicadoresStock/Reconstruir?meses=36 (Dirección), que solo trabaja de noche (22:00 a 07:00),
    un mes cada vez, del más reciente al más antiguo.

    Los indicadores que no se hayan podido calcular (falló su consulta) quedan a NULL. Cómo se calcula cada uno: comentario de
    NestoAPI/Infraestructure/IndicadoresStock/CalculadoraIndicadoresStock.cs. En resumen (empresas 1 y 3 con la ficha de la 1,
    sin ficticios, sin EXT, valorado con el precio medio de hoy; venta = LinPedidoVta estado >= 2 sin MTP ni MMP):
      ValorStock / UnidadesStock ........ stock a fin de mes × precio medio
      ValorEstado4 / ProductosEstado4 ... con stock y estado 4 (a extinguir); Estado1 igual con estado 1 (sobre pedido)
      ValorSinVenta{6,12,24}Meses ....... con stock y sin ninguna salida a clientes en toda la empresa en esos meses
      CosteVentas12Meses ................ unidades netas vendidas en 12 meses × precio medio
      ValorStockMedio12Meses ............ media de los 12 cierres de mes (sin MTP ni MMP) × precio medio
      Rotacion .......................... CosteVentas12Meses / ValorStockMedio12Meses
      CoberturaMediaDias ................ stock ÷ venta media mensual de 6 meses, en días, ponderada por euros de stock
      LineasServidas / DiasServirMedia .. líneas albaranadas en el mes y media de (Fecha Albarán − Fecha Entrega) en días
      NivelServicio ..................... % de esas líneas que salieron enteras (sin partir en otro albarán ni quedar pendiente)
      ComprasValor / ComprasUnidades .... albaranes de compra del mes (_ALBCOMP)
      BajasPerdidasValor / ...Unidades .. diarios de mermas (Merma…) y Robo
      AltasReferencias / BajasReferencias primer movimiento en el mes / stock > 0 el mes anterior y <= 0 este
      UnidadesCompradasMes / ...CoberturaAlta  unidades compradas en el mes y las que entraron con > 6 meses de cobertura
      ValorStockAlgete / ValorStockTiendas solo en la fila TOTAL (ALG; REI + ALC)

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Tabla
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.IndicadoresStock') IS NULL
BEGIN
    CREATE TABLE dbo.IndicadoresStock (
        Mes date NOT NULL,                              -- día 1 del mes
        Almacen varchar(5) NOT NULL,                    -- Almacenes.Número o 'TOTAL'
        ValorStock decimal(18, 2) NULL,
        UnidadesStock int NULL,
        ValorEstado4 decimal(18, 2) NULL,
        ProductosEstado4 int NULL,
        ValorEstado1 decimal(18, 2) NULL,
        ProductosEstado1 int NULL,
        ValorSinVenta6Meses decimal(18, 2) NULL,
        ProductosSinVenta6Meses int NULL,
        ValorSinVenta12Meses decimal(18, 2) NULL,
        ProductosSinVenta12Meses int NULL,
        ValorSinVenta24Meses decimal(18, 2) NULL,
        ProductosSinVenta24Meses int NULL,
        CosteVentas12Meses decimal(18, 2) NULL,
        ValorStockMedio12Meses decimal(18, 2) NULL,
        Rotacion decimal(18, 2) NULL,                   -- veces al año
        CoberturaMediaDias decimal(18, 2) NULL,
        LineasServidas int NULL,
        DiasServirMedia decimal(18, 2) NULL,
        NivelServicio decimal(18, 2) NULL,              -- % (0-100)
        ComprasValor decimal(18, 2) NULL,
        ComprasUnidades int NULL,
        BajasPerdidasValor decimal(18, 2) NULL,
        BajasPerdidasUnidades int NULL,
        AltasReferencias int NULL,
        BajasReferencias int NULL,
        UnidadesCompradasMes int NULL,
        UnidadesCompradasCoberturaAlta int NULL,
        ValorStockAlgete decimal(18, 2) NULL,           -- solo en la fila TOTAL
        ValorStockTiendas decimal(18, 2) NULL,          -- solo en la fila TOTAL
        FechaCalculo datetime NOT NULL CONSTRAINT DF_IndicadoresStock_FechaCalculo DEFAULT (GETDATE()),
        Usuario varchar(30) NOT NULL CONSTRAINT DF_IndicadoresStock_Usuario DEFAULT (SUSER_SNAME()),
        CONSTRAINT PK_IndicadoresStock PRIMARY KEY CLUSTERED (Mes, Almacen),
        CONSTRAINT CK_IndicadoresStock_Mes CHECK (DAY(Mes) = 1)
    );
    PRINT 'Tabla IndicadoresStock creada.';
END
ELSE
    PRINT 'La tabla IndicadoresStock ya existía.';
GO

------------------------------------------------------------------------------------------------
-- 2. Índice para la evolución de un almacén (GET ...?almacen=ALG)
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IndicadoresStock_Almacen_Mes'
               AND object_id = OBJECT_ID('dbo.IndicadoresStock'))
BEGIN
    CREATE INDEX IX_IndicadoresStock_Almacen_Mes ON dbo.IndicadoresStock (Almacen, Mes);
    PRINT 'Índice IX_IndicadoresStock_Almacen_Mes creado.';
END
GO

------------------------------------------------------------------------------------------------
-- 3. Permisos: la API corre con la cuenta máquina del RDS2016 (borra e inserta el mes que calcula)
------------------------------------------------------------------------------------------------
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.IndicadoresStock TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- 4. Comprobación
------------------------------------------------------------------------------------------------
SELECT COUNT(*) AS Filas, MIN(Mes) AS PrimerMes, MAX(Mes) AS UltimoMes FROM dbo.IndicadoresStock;
GO
