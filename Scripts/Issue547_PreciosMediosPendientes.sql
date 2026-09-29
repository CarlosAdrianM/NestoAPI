-- NestoAPI#547, incremental de precios medios, corte (a): tabla de productos PENDIENTES.
-- El incremental escribe como mucho 10.000 filas por producto y pasada (EjecutorComandosPreciosMedios.
-- TOPE_FILAS_POR_PRODUCTO_Y_PASADA). Si un producto llega al tope (un bestseller con un cambio en una compra
-- antigua), se confirma lo escrito y el producto se apunta AQUÍ; la siguiente pasada nocturna lo vuelve a coger
-- (aunque no tenga cambios nuevos), continúa donde se quedó y, cuando termina, borra la fila.
--
-- Mientras no se ejecute este script, la pasada nocturna (si está encendida) sigue sin pendientes y lo dice en el
-- resumen de ELMAH; un producto que llegue al tope se reintenta por Hangfire en vez de apuntarse.
-- El incremental sigue APAGADO (PreciosMedios:EscribirIncremental=false): el script se puede ejecutar cuando se quiera.
-- Ejecutar en NV (NestoConnection) como sa. Idempotente. Solo crea una tabla nueva: no toca ninguna tabla existente
-- ni crea índices en ellas.
USE NV;
GO

IF OBJECT_ID('dbo.PreciosMediosPendientes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PreciosMediosPendientes (
        Empresa               varchar(3)    NOT NULL,   -- empresa principal (1, 4, 5), nunca la espejo
        Producto              varchar(15)   NOT NULL,
        Motivo                varchar(200)  NULL,       -- «Tope de 10000 filas por pasada: escritas ...»
        Veces                 int           NOT NULL CONSTRAINT DF_PreciosMediosPendientes_Veces DEFAULT (1),
        Fecha                 datetime      NOT NULL CONSTRAINT DF_PreciosMediosPendientes_Fecha DEFAULT (GETDATE()),  -- primera vez
        [Fecha Modificación]  datetime      NOT NULL CONSTRAINT DF_PreciosMediosPendientes_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_PreciosMediosPendientes PRIMARY KEY (Empresa, Producto)
    );
END
GO

-- El API (Hangfire) corre en RDS2016 con la cuenta de máquina.
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PreciosMediosPendientes TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.PreciosMediosPendientes') AS Tabla;
SELECT * FROM dbo.PreciosMediosPendientes;
GO
