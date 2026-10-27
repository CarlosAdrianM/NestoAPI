/*
    NestoAPI#715 (corte 1, épica #689): tabla PedidosAnomalias.

    Cada vez que la API graba un pedido de venta (POST y PUT de api/PedidosVenta), DESPUÉS de validarlo y guardarlo, un trabajo
    en segundo plano evalúa unas reglas sobre el histórico cliente-producto de 24 meses y deja aquí:
      - una fila de CONTEXTO por pedido (Regla = 'Contexto', Linea NULL): la aplicación con la que se grabó (#716 2.5), la deuda del
        cliente en ese momento (#716 2.6), la forma de pago de la ficha frente a la del pedido, el importe bruto y el número de líneas;
      - una fila por SEÑAL de anomalía (CantidadAlta, CantidadVsMaximo, PrecioBajo, PrecioAlto, DescuentoAlto, ImportePedido,
        GrupoNuevo) con Valor, Esperado y Puntuacion, y la cantidad, precio y descuento con los que se grabó la línea;
      - una fila 'SinStock' por línea que se graba con más cantidad que stock en su almacén (#716 2.10, demanda insatisfecha).
    En SOMBRA (Anomalias:Modo del Web.config) no se avisa a nadie: Mostrado = 0 y Respuesta NULL. Cuando haya modo Activo (corte 4),
    Mostrado dirá si se le preguntó al usuario y Respuesta qué contestó (#716 2.4).

    Etiquetado (job nocturno a las 5:30, AnomaliasJobsService): Resultado = CorregidoEn48h (la línea se cambió o se borró en 48 h,
    según LinPedidoVta y la tabla Modificaciones con Tabla = 'Pedidos'), Abonado (hay una línea negativa del mismo producto para el
    cliente después de grabar) o SinCambios (a los 7 días). ResueltoEl: cuándo. Con eso se mide la precisión de cada regla
    (GET api/PedidosVenta/Anomalias/Informe) y se entrena el modelo del corte 3.

    No está en el EDMX (la API la lee y escribe con SQL a mano, como PedidosPromesaRiesgo #693 y CorreosPostCompraRecomendaciones
    #707): se puede lanzar antes o después del deploy. Sin ella la API no guarda nada (un aviso a ELMAH por proceso) y los pedidos se
    graban igual.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE (el etiquetado nocturno rellena Resultado y ResueltoEl). Sin DELETE: las filas son el dataset.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PedidosAnomalias', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidosAnomalias (
        Id int IDENTITY(1, 1) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_PedidosAnomalias_Fecha DEFAULT (GETDATE()),
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Cliente char(10) NULL,
        Contacto char(3) NULL,
        Linea int NULL,                          -- LinPedidoVta.[Nº Orden]; NULL = fila del pedido (Contexto, ImportePedido)
        Producto char(15) NULL,
        Regla varchar(30) NOT NULL,              -- Contexto, CantidadAlta, CantidadVsMaximo, PrecioBajo, PrecioAlto, DescuentoAlto, ImportePedido, GrupoNuevo, SinStock
        Valor decimal(18, 4) NULL,               -- lo que trae el pedido (cantidad, precio, descuento, importe, stock…)
        Esperado decimal(18, 4) NULL,            -- el umbral que supera
        Puntuacion decimal(9, 4) NULL,           -- cuántas veces supera el umbral (Valor / Esperado), tope 99
        CantidadGrabada int NULL,                -- la línea tal y como se grabó (para el etiquetado)
        PrecioGrabado decimal(18, 4) NULL,
        DescuentoGrabado decimal(9, 6) NULL,     -- fracción (0,22 = 22 %), sin pronto pago
        ImportePedido decimal(18, 2) NULL,       -- bruto (cantidad × precio) de las líneas de producto del pedido al grabar
        NumeroLineas int NULL,
        Usuario nvarchar(50) NULL,
        Aplicacion varchar(20) NULL,             -- Nesto, NestoApp, TiendasNuevaVision, Desconocida (cabecera X-Aplicacion o inferida)
        Vendedor char(3) NULL,
        Operacion varchar(10) NOT NULL,          -- Crear, Modificar
        Mostrado bit NOT NULL CONSTRAINT DF_PedidosAnomalias_Mostrado DEFAULT (0),
        Respuesta varchar(20) NULL,              -- en Activo: Confirmado, Corregido… (corte 4)
        VersionModelo varchar(30) NULL,          -- reglas-v1 (corte 1); el modelo del corte 3 pondrá la suya
        DeudaTotal decimal(18, 2) NULL,          -- ExtractoCliente.ImportePdte del cliente al grabar
        DeudaVencida decimal(18, 2) NULL,        -- lo mismo, con FechaVto anterior a hoy
        DiasVencidoMasAntiguo int NULL,
        FormaPagoFicha char(3) NULL,             -- CondPagoClientes (la de menor ImporteMínimo)
        FormaPagoPedido char(3) NULL,
        StockAlGrabar int NULL,                  -- en las filas de línea: stock del producto en el almacén de la línea al grabar
        Resultado varchar(20) NULL,              -- CorregidoEn48h, Abonado, SinCambios (lo pone el job nocturno)
        ResueltoEl datetime NULL,

        CONSTRAINT PK_PedidosAnomalias PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_PedidosAnomalias_Operacion CHECK (Operacion IN ('Crear', 'Modificar')),
        CONSTRAINT CK_PedidosAnomalias_Resultado CHECK (Resultado IS NULL OR Resultado IN ('CorregidoEn48h', 'Abonado', 'SinCambios'))
    );
END
GO

-- Por pedido (etiquetado y consulta de un pedido concreto).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PedidosAnomalias_Pedido' AND object_id = OBJECT_ID('dbo.PedidosAnomalias'))
    CREATE INDEX IX_PedidosAnomalias_Pedido ON dbo.PedidosAnomalias (Empresa, Pedido);
GO

-- Por fecha (informe) y por resultado (las pendientes de etiquetar: Resultado IS NULL). Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PedidosAnomalias_Fecha' AND object_id = OBJECT_ID('dbo.PedidosAnomalias'))
    CREATE INDEX IX_PedidosAnomalias_Fecha ON dbo.PedidosAnomalias (Fecha) INCLUDE (Regla, Resultado, Aplicacion);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PedidosAnomalias_Resultado' AND object_id = OBJECT_ID('dbo.PedidosAnomalias'))
    CREATE INDEX IX_PedidosAnomalias_Resultado ON dbo.PedidosAnomalias (Resultado, Fecha);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.PedidosAnomalias TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.PedidosAnomalias', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.PedidosAnomalias) AS Filas;
GO

-- Lo de hoy, con las señales primero:
-- SELECT a.Fecha, a.Pedido, a.Linea, a.Producto, a.Regla, a.Valor, a.Esperado, a.Puntuacion, a.Aplicacion, a.Usuario, a.Resultado
-- FROM dbo.PedidosAnomalias a WHERE a.Fecha >= CAST(GETDATE() AS date) ORDER BY CASE WHEN a.Regla = 'Contexto' THEN 1 ELSE 0 END, a.Fecha;
