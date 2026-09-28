-- NestoAPI#547 (corte b): tabla de la SOMBRA de precios medios.
-- El job 'precios-medios-sombra' de Hangfire (domingo 06:30) calcula la media de cada producto con la
-- calculadora en C# y la compara al diezmilésimo con lo que ha dejado el SP «Precios Medios» de msdb.
-- NUNCA escribe en Productos, LinPedidoCmp ni LinPedidoVta: lo único que escribe es ESTA tabla.
-- Solo guarda los productos con alguna diferencia o aviso, más una fila resumen por empresa y pasada
-- (Producto = '(resumen)'). Repetir la pasada el mismo día borra y vuelve a escribir las filas de ese día.
--
-- Mientras no se ejecute este script, el job (si está encendido) no lee nada y deja el aviso en ELMAH.
-- Ejecutar en NV (NestoConnection) como sa. Idempotente.
USE NV;
GO

IF OBJECT_ID('dbo.PreciosMediosSombra', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PreciosMediosSombra (
        Id                    int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreciosMediosSombra PRIMARY KEY,
        FechaPasada           date          NOT NULL,
        Empresa               char(3)       NOT NULL,
        Producto              varchar(15)   NOT NULL,   -- '(resumen)' en la fila resumen
        EsResumen             bit           NOT NULL CONSTRAINT DF_PreciosMediosSombra_EsResumen DEFAULT (0),
        -- Igual / IgualConAvisos / Pendiente / Empate / Distinto / NoProcesado / Error
        Clasificacion         varchar(20)   NOT NULL,
        PrecioMedioBD         money         NULL,       -- Productos.PrecioMedio (lo que dejó el SP)
        PrecioMedioCalculado  money         NULL,       -- CalculadoraPrecioMedio
        LineasComparadas      int           NOT NULL CONSTRAINT DF_PreciosMediosSombra_LineasComparadas DEFAULT (0),
        LineasDistintas       int           NOT NULL CONSTRAINT DF_PreciosMediosSombra_LineasDistintas DEFAULT (0),
        PrimeraLineaDistinta  int           NULL,       -- LinPedidoCmp.NºOrden donde empieza a divergir
        LineasPendientes      int           NOT NULL CONSTRAINT DF_PreciosMediosSombra_LineasPendientes DEFAULT (0),
        VentasComparadas      int           NULL,       -- solo en los productos del muestreo de LinPedidoVta
        VentasDistintas       int           NULL,
        Avisos                nvarchar(max) NULL,       -- avisos de la calculadora (empates, media negativa...)
        Detalle               nvarchar(max) NULL,       -- JSON: diferencias línea a línea / resumen de la pasada
        FechaCalculo          datetime      NOT NULL CONSTRAINT DF_PreciosMediosSombra_FechaCalculo DEFAULT (GETDATE())
    );

    CREATE UNIQUE INDEX UX_PreciosMediosSombra_Pasada ON dbo.PreciosMediosSombra (FechaPasada, Empresa, Producto);
END
GO

-- El API (Hangfire) corre en RDS2016 con la cuenta de máquina.
GRANT SELECT, INSERT, DELETE ON dbo.PreciosMediosSombra TO [NUEVAVISION\RDS2016$];
GO

-- OPCIONAL: el job mira en msdb si el SP «Precios Medios» sigue corriendo (entonces se aplaza) y cuándo empezó
-- su última pasada (lo facturado después es «pendiente»). Si la cuenta del API no puede leer msdb, lo supone
-- (domingo 00:30) y sigue. Para que lo lea de verdad:
-- USE msdb;
-- IF USER_ID('NUEVAVISION\RDS2016$') IS NULL CREATE USER [NUEVAVISION\RDS2016$] FOR LOGIN [NUEVAVISION\RDS2016$];
-- GRANT SELECT ON dbo.sysjobs TO [NUEVAVISION\RDS2016$];
-- GRANT SELECT ON dbo.sysjobactivity TO [NUEVAVISION\RDS2016$];
-- USE NV;

-- Comprobación
SELECT OBJECT_ID('dbo.PreciosMediosSombra') AS Tabla;
SELECT Empresa, Usuario, Clave, Valor FROM dbo.ParámetrosUsuario WHERE Clave = 'PreciosMediosSombra';
GO

-- ENCENDER el job semanal (sin fila = APAGADO, que es como nace al publicar). Se lee en cada ejecución.
-- IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PreciosMediosSombra')
--     INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
--     VALUES ('1', '(defecto)', 'PreciosMediosSombra', '1', 'NestoAPI#547', GETDATE());
-- ELSE
--     UPDATE dbo.ParámetrosUsuario SET Valor = '1', Usuario2 = 'NestoAPI#547', [Fecha Modificación] = GETDATE()
--     WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PreciosMediosSombra';

-- APAGAR:
-- UPDATE dbo.ParámetrosUsuario SET Valor = '0', Usuario2 = 'NestoAPI#547', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PreciosMediosSombra';

-- PROBAR SIN ESPERAR AL DOMINGO (Dirección/Informática, con token; no escriben nada salvo la pasada completa en esta tabla):
--   GET  api/PreciosMedios/1/41281?ventas=true                    -> cálculo y diferencia de un producto
--   POST api/PreciosMedios/Sombra?empresa=1&productos=41281,44904  -> varios productos (máx. 50), sin registrar
--   POST api/PreciosMedios/Sombra?completa=true                    -> encola la pasada completa (registra aquí)
-- O en el panel de Hangfire: Recurring jobs -> 'precios-medios-sombra' -> Trigger now (con el parámetro a 1).

-- CONSULTAS ÚTILES
-- Resumen de las últimas pasadas:
--   SELECT FechaPasada, Empresa, Clasificacion, LineasComparadas AS Revisados, LineasDistintas AS Distintos, Detalle
--   FROM dbo.PreciosMediosSombra WHERE EsResumen = 1 ORDER BY FechaPasada DESC, Empresa;
-- Diferencias no esperadas de la última pasada:
--   SELECT * FROM dbo.PreciosMediosSombra
--   WHERE Clasificacion IN ('Distinto', 'Error') AND EsResumen = 0
--     AND FechaPasada = (SELECT MAX(FechaPasada) FROM dbo.PreciosMediosSombra) ORDER BY Empresa, Producto;
