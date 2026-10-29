/*
    NestoAPI#734: tabla ConsultasReembolsoPedido («Enlace de pago pendiente en un pedido contra reembolso: el almacén retiene y
    consulta a Administración, que decide con o sin reembolso»).

    La propuesta de envío (GET api/EnviosAgencias/Propuesta, aviso EnlacePagoPendiente de NestoAPI#679) avisaba al almacén de que
    el pedido tenía un enlace de pago pendiente, pero el almacén no sabe qué hacer con eso. Decisión de Carlos (29/10/26): el aviso
    ofrece «Retener y consultar a Administración»; la API retiene el pedido (PedidosRetenidos, modo 5, #698) y avisa al grupo
    Administración por la campana; Administración contesta «sale con reembolso» (y, si lo pide, se anulan los enlaces) o «sale sin
    reembolso» (con la forma de pago pactada); la API libera el pedido y avisa a quien preguntó. Esta tabla guarda cada consulta y
    su respuesta; la propuesta de envío la mira para no volver a avisar.

    Lo escribe solo la API:
      POST api/PedidosVenta/{empresa}/{numero}/ConsultaReembolso            (Almacén, Tiendas, Dirección)
      POST api/PedidosVenta/{empresa}/{numero}/ConsultaReembolso/Respuesta  (Administración, Dirección)
    El cambio de forma de pago deja además una fila en Modificaciones (Tabla CabPedidoVta) con el usuario de verdad.

    ImporteEnlaces / NumeroEnlaces: foto de los enlaces pendientes vivos al preguntar.
    RetencionId:          PedidosRetenidos.Id que puso la consulta (NULL si el pedido ya estaba retenido por otra cosa: entonces al
                          contestar no se libera).
    Respondida:           NULL = abierta. Una abierta por pedido como mucho (la API no duplica).
    ConReembolso:         1 = sale con reembolso; 0 = sale sin reembolso (FormaPago/PlazosPago = lo pactado, *Anterior = lo que tenía).
    AnularEnlaces / EnlacesAnulados: si Administración pidió anular los enlaces y los Id de PagosTPV anulados (Estado 'Cancelado').

    No está en el EDMX (SQL a mano, como PedidosRetenidos #698): se puede lanzar antes o después del deploy. Sin ella, los endpoints
    contestan que falta la tabla y la propuesta de envío sigue como hasta ahora.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE. Sin DELETE: las filas contestadas se quedan (son la constancia).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ConsultasReembolsoPedido', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConsultasReembolsoPedido (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        ImporteEnlaces decimal(12, 2) NOT NULL CONSTRAINT DF_ConsultasReembolsoPedido_ImporteEnlaces DEFAULT (0),
        NumeroEnlaces int NOT NULL CONSTRAINT DF_ConsultasReembolsoPedido_NumeroEnlaces DEFAULT (0),
        RetencionId int NULL,                           -- PedidosRetenidos.Id que puso la consulta
        Comentario nvarchar(500) NULL,                  -- lo que añade quien pregunta
        Usuario nvarchar(50) NOT NULL,                  -- quien preguntó (NUEVAVISION\Alfredo, o el usuario de Ariadna)
        Fecha datetime NOT NULL CONSTRAINT DF_ConsultasReembolsoPedido_Fecha DEFAULT (GETDATE()),
        Respondida datetime NULL,                       -- NULL = abierta
        UsuarioRespuesta nvarchar(50) NULL,
        ConReembolso bit NULL,
        AnularEnlaces bit NULL,
        EnlacesAnulados nvarchar(500) NULL,             -- Id de PagosTPV separados por comas
        FormaPagoAnterior char(3) NULL,
        PlazosPagoAnterior char(10) NULL,
        FormaPago char(3) NULL,
        PlazosPago char(10) NULL,
        ComentarioRespuesta nvarchar(500) NULL,

        CONSTRAINT PK_ConsultasReembolsoPedido PRIMARY KEY CLUSTERED (Id)
    );
END
GO

-- Para la consulta abierta / última de un pedido (propuesta de envío, POST). Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConsultasReembolsoPedido_Empresa_Pedido' AND object_id = OBJECT_ID('dbo.ConsultasReembolsoPedido'))
    CREATE INDEX IX_ConsultasReembolsoPedido_Empresa_Pedido ON dbo.ConsultasReembolsoPedido (Empresa, Pedido, Respondida);
GO

-- Para el listado de abiertas de Administración.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConsultasReembolsoPedido_Respondida' AND object_id = OBJECT_ID('dbo.ConsultasReembolsoPedido'))
    CREATE INDEX IX_ConsultasReembolsoPedido_Respondida ON dbo.ConsultasReembolsoPedido (Empresa, Respondida, Fecha);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.ConsultasReembolsoPedido TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.ConsultasReembolsoPedido', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.ConsultasReembolsoPedido) AS Filas,
       (SELECT COUNT(*) FROM dbo.ConsultasReembolsoPedido WHERE Respondida IS NULL) AS Abiertas;
GO

-- Cómo va:
-- SELECT r.Pedido, r.ImporteEnlaces, r.Usuario, r.Fecha, r.Respondida, r.UsuarioRespuesta, r.ConReembolso, r.AnularEnlaces,
--        r.EnlacesAnulados, r.FormaPagoAnterior, r.PlazosPagoAnterior, r.FormaPago, r.PlazosPago, r.Comentario, r.ComentarioRespuesta
-- FROM dbo.ConsultasReembolsoPedido r ORDER BY r.Fecha DESC;
