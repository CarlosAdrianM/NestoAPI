/*
    NestoAPI#707 (cortes 1 y 2): tabla CorreosPostCompraRecomendaciones.

    Lo que lleva cada correo post-compra de los sábados («Gracias por su compra de …») que ha salido: una fila por producto,
    los comprados (Tipo = 'Comprado') y los recomendados (Tipo = 'Recomendado'), en el orden en que salen en el correo
    (Posicion 1, 2, 3… dentro de cada bloque). La escribe la API justo después de enviar cada correo
    (CorreosPostCompraJobsService.EnviarCorreoPostCompraConPlantilla), best-effort: sin la tabla o con un fallo al insertar,
    un aviso a ELMAH y el correo sale igual. En modo test (CorreosPostCompra:ModoTest) no se guarda nada.

    Regla:          solo en los recomendados: pares (comprados juntos con lo que ha comprado), afinidad (item-kNN del cliente),
                    popularidad u orden_original (sin datos: el orden de la consulta de siempre).
    Puntuacion:     0-1 con la que se ordenó (NULL con orden_original).
    VersionModelo:  con qué se ordenó ('reglas-1' en el corte 2; el corte 3 pondrá la versión del zip de ML.NET).
    GrupoControl:   1 en todas las filas de un cliente del grupo control (CorreosPostCompra:PorcentajeGrupoControl, nace a 0):
                    sus recomendados NO salieron en el correo; se guardan para comparar (uplift).

    El informe de conversión (GET api/CorreosPostCompra/Conversion) cruza cada fila con LinPedidoVta (mismo cliente y producto,
    pedidos de los 30 días siguientes al envío, sin presupuestos).

    No está en el EDMX (la API la lee y escribe con SQL a mano, como PedidosPromesaRiesgo #693): se puede lanzar antes o
    después del deploy. Sin ella la API no registra nada (y el informe sale vacío).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT e INSERT. Sin UPDATE ni DELETE: las filas no se tocan (son la constancia de lo enviado).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.CorreosPostCompraRecomendaciones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CorreosPostCompraRecomendaciones (
        Id int IDENTITY(1, 1) NOT NULL,
        FechaEnvio datetime NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente varchar(10) NOT NULL,
        Contacto varchar(3) NULL,                       -- el de la persona de contacto cuyo correo se usó
        Correo nvarchar(200) NULL,
        Producto varchar(15) NOT NULL,
        Tipo varchar(12) NOT NULL,                      -- Comprado / Recomendado
        Posicion tinyint NOT NULL,                      -- 1, 2, 3… dentro de su bloque
        Regla varchar(30) NULL,                         -- pares / afinidad / popularidad / orden_original (solo recomendados)
        Puntuacion decimal(9, 4) NULL,
        VersionModelo varchar(30) NULL,
        GrupoControl bit NOT NULL CONSTRAINT DF_CorreosPostCompraRecomendaciones_GrupoControl DEFAULT (0),

        CONSTRAINT PK_CorreosPostCompraRecomendaciones PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_CorreosPostCompraRecomendaciones_Tipo CHECK (Tipo IN ('Comprado', 'Recomendado'))
    );
END
GO

-- Para el informe de conversión (por fecha de envío). Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CorreosPostCompraRecomendaciones_FechaEnvio' AND object_id = OBJECT_ID('dbo.CorreosPostCompraRecomendaciones'))
    CREATE INDEX IX_CorreosPostCompraRecomendaciones_FechaEnvio ON dbo.CorreosPostCompraRecomendaciones (FechaEnvio)
        INCLUDE (Empresa, Cliente, Producto, Tipo, Regla, GrupoControl);
GO

-- Para buscar lo que se le ha enviado a un cliente (y, en el corte 3, los ejemplos de entrenamiento por cliente y producto).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CorreosPostCompraRecomendaciones_Cliente_Producto' AND object_id = OBJECT_ID('dbo.CorreosPostCompraRecomendaciones'))
    CREATE INDEX IX_CorreosPostCompraRecomendaciones_Cliente_Producto ON dbo.CorreosPostCompraRecomendaciones (Empresa, Cliente, Producto, FechaEnvio);
GO

GRANT SELECT, INSERT ON dbo.CorreosPostCompraRecomendaciones TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.CorreosPostCompraRecomendaciones', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.CorreosPostCompraRecomendaciones) AS Filas;
GO

-- Lo del último sábado:
-- SELECT FechaEnvio, Cliente, Correo, Tipo, Posicion, Producto, Regla, Puntuacion, GrupoControl
-- FROM dbo.CorreosPostCompraRecomendaciones WHERE FechaEnvio >= DATEADD(day, -7, GETDATE()) ORDER BY Id;
