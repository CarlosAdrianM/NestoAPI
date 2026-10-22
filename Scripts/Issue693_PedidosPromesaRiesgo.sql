/*
    NestoAPI#693 (corte 1): tabla PedidosPromesaRiesgo.

    Un pedido cuya fecha de entrega a la agencia prometida (CabPedidoVta.FechaEntregaAgenciaPrometida, #606) se retrasa o se
    queda sin fecha. La escribe la API al recalcular por eventos (reposición terminada, movimientos de stock por
    prdExtrProducto, pedidos de venta grabados, pedidos a proveedor retrasados) y en la pasada nocturna (L-S 5:40). Una fila
    viva (Resuelto NULL) por pedido como mucho: se actualiza mientras dure y se resuelve sola cuando vuelve a cumplir la
    fecha, se sirve o la fecha se renegocia (la fila resuelta se queda: es la constancia para medir).

    FechaNueva:       NULL = sin fecha.
    Motivo:           qué productos fallan y por qué, de usted («42485 (3 uds.): Reina mandó 1 de 3 en la reposición 81094; …»).
    Evento:           lo que lo detectó o lo empeoró por última vez.
    AvisadoCampana:   cuándo se avisó por la campana de Nesto (NULL en modo Sombra). Si empeora, vuelve a NULL y se avisa otra vez.
    AvisadoCorreo:    cuándo salió en el correo resumen (9:00 y 15:00 L-V). Si empeora, vuelve a NULL.

    No está en el EDMX (la API la lee y escribe con SQL a mano, como DiferenciasReposiciones #634): se puede lanzar antes o
    después del deploy. Sin ella la API no registra nada ni avisa, aunque el interruptor esté encendido.
    Interruptor: Pedidos:AvisoPromesaRiesgo en Web.config (Apagado / Sombra / Activo; nace Apagado).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE. Sin DELETE: las filas no se borran (son la constancia).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PedidosPromesaRiesgo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidosPromesaRiesgo (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        FechaPrometida datetime NOT NULL,               -- la de CabPedidoVta.FechaEntregaAgenciaPrometida al detectarlo
        FechaNueva datetime NULL,                       -- NULL = sin fecha
        Motivo nvarchar(max) NULL,                      -- productos que fallan y por qué
        Evento nvarchar(200) NULL,                      -- qué lo detectó (o empeoró) por última vez
        Detectado datetime NOT NULL CONSTRAINT DF_PedidosPromesaRiesgo_Detectado DEFAULT (GETDATE()),
        Actualizado datetime NOT NULL CONSTRAINT DF_PedidosPromesaRiesgo_Actualizado DEFAULT (GETDATE()),
        AvisadoCampana datetime NULL,
        AvisadoCorreo datetime NULL,
        Resuelto datetime NULL,
        MotivoResolucion nvarchar(200) NULL,           -- Vuelve a cumplir la fecha prometida / Servido / Fecha prometida renegociada

        CONSTRAINT PK_PedidosPromesaRiesgo PRIMARY KEY CLUSTERED (Id)
    );
END
GO

-- Para buscar las vivas de un pedido (eventos, listado y pasada). Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PedidosPromesaRiesgo_Empresa_Pedido' AND object_id = OBJECT_ID('dbo.PedidosPromesaRiesgo'))
    CREATE INDEX IX_PedidosPromesaRiesgo_Empresa_Pedido ON dbo.PedidosPromesaRiesgo (Empresa, Pedido, Resuelto);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.PedidosPromesaRiesgo TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.PedidosPromesaRiesgo', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.PedidosPromesaRiesgo) AS Filas;
GO

-- Cómo va (medir en Sombra antes de encender):
-- SELECT Pedido, FechaPrometida, FechaNueva, Evento, Detectado, Resuelto, MotivoResolucion, Motivo
-- FROM dbo.PedidosPromesaRiesgo ORDER BY Detectado DESC;
