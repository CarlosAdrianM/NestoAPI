/*
    NestoAPI#858 punto 4 (12/11/26, Carlos: «Hacerlo, por defecto esperar»): respetar «todo junto» cuando falta un producto de pago.

    Cuando una falta DEFINITIVA (al terminar el picking o en el «No está» sin más huecos) es de un producto de pago (no muestra MMP, no
    ficticio, base imponible > 0) de un pedido «todo junto» (modo de servicio efectivo 1) del que sale algo más, el pedido queda RETENIDO
    POR FALTA: lo recogido no se toca, la línea que falta queda pendiente como siempre, y no se empaqueta, ni se albaranea, ni se factura
    mientras la retención esté «Pendiente». Decide quien pasó el pedido, su vendedor o Almacén/Administración/Dirección/Informática
    (POST api/PedidosVenta/{empresa}/{pedido}/RetencionPorFalta/Decidir): «ServirLoQueHay» (sigue sin lo que falta) o «Esperar» (el
    pedido sale del picking y espera completo). Al vencer el plazo (2 horas laborables de Algete por defecto), el job
    pedidos-retenidos-por-falta aplica «Esperar».

    PedidosRetenidosPorFalta: una fila por retención.
      - Estado: Pendiente, ServirLoQueHay, Esperar. Solo una Pendiente por pedido (lo controla la API: nada de índices filtrados, #542).
      - Productos: los que faltan, «17404 x2, 30111 x1».
      - Plazo: hasta cuándo puede decidir el vendedor; PorPlazo = 1 si lo aplicó el job.

    Fuera del EDMX. Sin el script, la API no retiene nada (todo como antes; una línea en ELMAH una vez) y los endpoints dan 503.
    Parámetros (ParametrosUsuario, empresa 1, usuario «(defecto)», sin fila = valor por defecto):
      - Picking.TodoJuntoRetener: encendido salvo «0» (apagado de emergencia sin publicar).
      - Picking.TodoJuntoPlazoHoras: horas laborables para decidir (2).

    Lanzar en SSMS contra NV como sa ANTES de publicar la API. Idempotente.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID(N'dbo.PedidosRetenidosPorFalta', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidosRetenidosPorFalta (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PedidosRetenidosPorFalta PRIMARY KEY,
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Picking int NOT NULL,
        Productos nvarchar(400) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_PedidosRetenidosPorFalta_Fecha DEFAULT (GETDATE()),
        Plazo datetime NOT NULL,
        Estado varchar(15) NOT NULL CONSTRAINT DF_PedidosRetenidosPorFalta_Estado DEFAULT ('Pendiente'),
        UsuarioDecision varchar(50) NULL,
        FechaDecision datetime NULL,
        PorPlazo bit NOT NULL CONSTRAINT DF_PedidosRetenidosPorFalta_PorPlazo DEFAULT (0),
        CONSTRAINT CK_PedidosRetenidosPorFalta_Estado CHECK (Estado IN ('Pendiente', 'ServirLoQueHay', 'Esperar'))
    );
    PRINT 'PedidosRetenidosPorFalta: creada.';
END
ELSE
    PRINT 'PedidosRetenidosPorFalta: ya existía.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PedidosRetenidosPorFalta_Pedido' AND object_id = OBJECT_ID(N'dbo.PedidosRetenidosPorFalta'))
    CREATE INDEX IX_PedidosRetenidosPorFalta_Pedido ON dbo.PedidosRetenidosPorFalta (Empresa, Pedido, Estado);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PedidosRetenidosPorFalta_Estado' AND object_id = OBJECT_ID(N'dbo.PedidosRetenidosPorFalta'))
    CREATE INDEX IX_PedidosRetenidosPorFalta_Estado ON dbo.PedidosRetenidosPorFalta (Estado, Plazo);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.PedidosRetenidosPorFalta TO [NUEVAVISION\RDS2016$];
GO

-- Los dos parámetros con su valor por defecto (fila «(defecto)», como el resto de interruptores)
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'Picking.TodoJuntoRetener')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Picking.TodoJuntoRetener', '(defecto)', '1', 'Issue858', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'Picking.TodoJuntoPlazoHoras')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Picking.TodoJuntoPlazoHoras', '(defecto)', '2', 'Issue858', GETDATE());
GO

SELECT 'PedidosRetenidosPorFalta' AS Tabla, COUNT(*) AS Filas FROM dbo.PedidosRetenidosPorFalta;
