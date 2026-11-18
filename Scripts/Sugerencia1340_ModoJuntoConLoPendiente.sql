/*
    Sugerencia 1340 (Paloma, tienda de Alcobendas; decisiones de Carlos 18/11/26): modo de servicio 6, «Junto con lo pendiente del cliente».

    Un pedido en modo 6 no sale hasta que pueda salir lo que el mismo cliente y contacto tiene pendiente (notas de entrega y pedidos con
    líneas por servir en Algete); ese día salen en el mismo picking y van en un solo envío. Sin nada pendiente sale como «Todo junto».

    1) Amplía la restricción CK_CabPedidoVta_ModoServicio (Issue482; Issue698 la dejó en 1..5) para que admita el 6. Lección de #698: sin
       esto, el primer pedido que se pasara al 6 fallaría con «Instrucción UPDATE en conflicto con la restricción CHECK».
    2) Crea dbo.PedidosJuntoConPendienteAvisos: el rastro del aviso de los 15 días (una fila por pedido avisado; job
       pedidos-junto-con-pendiente-esperando). Se crea DESPUÉS de ampliar la restricción: la API solo ofrece el 6 si existe esta tabla y la
       restricción admite el 6 (RepositorioJuntoConPendiente.SQL_ADMITIDO, cacheado; mientras no, lo mira como mucho cada 5 minutos).

    Sin este script todo sigue como hoy: el modo 6 no se ofrece, elegirlo da 400 y el job no hace nada.
    Se puede lanzar antes o después del deploy. Ejecutar en SSMS contra NV como sa (ALTER TABLE, CREATE y GRANT). Idempotente.
    Nada de índices filtrados ni de columnas calculadas indexadas (#542, #294).
*/

SET NOCOUNT ON;
USE NV;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CabPedidoVta_ModoServicio' AND definition NOT LIKE '%(6)%')
BEGIN
    ALTER TABLE dbo.CabPedidoVta DROP CONSTRAINT CK_CabPedidoVta_ModoServicio;
    ALTER TABLE dbo.CabPedidoVta WITH CHECK
        ADD CONSTRAINT CK_CabPedidoVta_ModoServicio CHECK (ModoServicio IS NULL OR ModoServicio BETWEEN 1 AND 6);
    PRINT 'CK_CabPedidoVta_ModoServicio: ahora admite el 6 (Junto con lo pendiente del cliente).';
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CabPedidoVta_ModoServicio')
    PRINT 'No existe CK_CabPedidoVta_ModoServicio: nada que ampliar.';
ELSE
    PRINT 'CK_CabPedidoVta_ModoServicio ya admite el 6.';
GO

-- Si la restricción sigue sin admitir el 6 (algo ha fallado arriba), NO se crea la tabla: la API no ofrecerá el modo.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CabPedidoVta_ModoServicio' AND definition NOT LIKE '%(6)%')
BEGIN
    RAISERROR('CK_CabPedidoVta_ModoServicio no admite el 6: no se crea PedidosJuntoConPendienteAvisos. Revise el paso anterior.', 16, 1);
    SET NOEXEC ON;
END
GO

IF OBJECT_ID('dbo.PedidosJuntoConPendienteAvisos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidosJuntoConPendienteAvisos (
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Avisado datetime NOT NULL CONSTRAINT DF_PedidosJuntoConPendienteAvisos_Avisado DEFAULT (GETDATE()),
        CONSTRAINT PK_PedidosJuntoConPendienteAvisos PRIMARY KEY CLUSTERED (Empresa, Pedido)
    );
    PRINT 'Creada dbo.PedidosJuntoConPendienteAvisos.';
END
GO

GRANT SELECT, INSERT ON dbo.PedidosJuntoConPendienteAvisos TO [NUEVAVISION\RDS2016$];
GO

SET NOEXEC OFF;
GO

-- Comprobación
SELECT (SELECT definition FROM sys.check_constraints WHERE name = 'CK_CabPedidoVta_ModoServicio') AS Restriccion,
       OBJECT_ID('dbo.PedidosJuntoConPendienteAvisos', 'U') AS TablaAvisos;
GO

-- Los pedidos en modo 6 vivos y desde cuándo (la última vez que se dejaron en 6, #704):
-- SELECT c.Empresa, c.Número, c.[Nº Cliente], c.Contacto, c.Usuario, c.Fecha,
--        (SELECT MAX(o.Fecha) FROM dbo.PedidosModoServicioOrigen o WHERE o.Empresa = c.Empresa AND o.Pedido = c.Número AND o.ModoGuardado = 6) AS Desde,
--        a.Avisado
-- FROM dbo.CabPedidoVta c WITH (NOLOCK)
-- LEFT JOIN dbo.PedidosJuntoConPendienteAvisos a ON a.Empresa = c.Empresa AND a.Pedido = c.Número
-- WHERE c.ModoServicio = 6
--   AND EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.Número = c.Número AND l.Estado BETWEEN -1 AND 1);
