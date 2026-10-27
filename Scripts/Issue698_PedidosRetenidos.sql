/*
    NestoAPI#698: tabla PedidosRetenidos («Retener un pedido hasta nuevo aviso»).

    Hasta ahora, para que un pedido no saliera, se le ponía la fecha de entrega al año que viene (927991, 928141, 929057,
    929117 el 23/10/26): invisible, dependía de que alguien se acordara, parecía una fecha mal puesta (#712 avisa a partir de
    60 días) y #693 lo contaba como promesa incumplida. Decisión de Carlos (23/10/26): un modo de servicio nuevo,
    CabPedidoVta.ModoServicio = 5 («Retenido»), que nunca sale en el picking (PedidoPicking.saleEnPicking, MotivoNoSale
    Retenido), y esta tabla con el porqué, quién lo retiene, hasta cuándo y el modo que tenía, para que «Liberar» lo devuelva
    a su modo. La fecha de entrega de las líneas ya no se toca.

    Lo escribe solo la API (POST api/PedidosVenta/{empresa}/{numero}/Retener y /Liberar; la pasada nocturna L-S 5:35 libera
    los que llegan a HastaFecha y recuerda los que no tienen fecha cada Pedidos:Retenidos:DiasRecordatorio días, 7). Cada
    Retener/Liberar deja además una fila en Modificaciones (Tabla CabPedidoVta) con el usuario de verdad.

    Motivo:                texto libre («El cliente lo quiere para noviembre», «Pendiente de pago»…).
    HastaFecha:            NULL = hasta nuevo aviso. Con fecha, la pasada nocturna lo libera sola ese día.
    ModoServicioAnterior:  CabPedidoVta.ModoServicio al retener (NULL si no estaba informado); al liberar se restaura tal cual.
    Liberado / UsuarioLiberacion / MotivoLiberacion: NULL mientras siga retenido. Una fila viva por pedido como mucho.
    UltimoRecordatorio:    la última vez que la campana recordó un retenido sin fecha.

    No está en el EDMX (la API la lee y escribe con SQL a mano, como PedidosPromesaRiesgo #693): se puede lanzar antes o
    después del deploy. Sin ella, Retener contesta que falta la tabla y nada más cambia.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE. Sin DELETE: las filas liberadas se quedan (son la constancia).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PedidosRetenidos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidosRetenidos (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Motivo nvarchar(500) NOT NULL,
        HastaFecha datetime NULL,                        -- NULL = hasta nuevo aviso
        ModoServicioAnterior tinyint NULL,              -- lo que tenía CabPedidoVta.ModoServicio al retener
        Usuario nvarchar(50) NOT NULL,                  -- quien lo retuvo (NUEVAVISION\Alfredo…)
        Fecha datetime NOT NULL CONSTRAINT DF_PedidosRetenidos_Fecha DEFAULT (GETDATE()),
        Liberado datetime NULL,
        UsuarioLiberacion nvarchar(50) NULL,
        MotivoLiberacion nvarchar(200) NULL,            -- Liberado a mano / Llegó la fecha de revisión / Sin nada pendiente
        UltimoRecordatorio datetime NULL,

        CONSTRAINT PK_PedidosRetenidos PRIMARY KEY CLUSTERED (Id)
    );
END
GO

-- Para buscar la viva de un pedido (Retener, Liberar, picking, lectura del pedido). Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PedidosRetenidos_Empresa_Pedido' AND object_id = OBJECT_ID('dbo.PedidosRetenidos'))
    CREATE INDEX IX_PedidosRetenidos_Empresa_Pedido ON dbo.PedidosRetenidos (Empresa, Pedido, Liberado);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.PedidosRetenidos TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.PedidosRetenidos', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.PedidosRetenidos) AS Filas,
       (SELECT COUNT(*) FROM dbo.PedidosRetenidos WHERE Liberado IS NULL) AS Vivas;
GO

/*
    ONE-OFF (NO LANZAR hasta que #698 esté en producción y Carlos lo diga; decisión 4 del 23/10/26: los pedidos de hoy no se tocan).

    Convierte los retenidos «a la antigua» (pendientes con la fecha de entrega a más de 60 días) al modo 5 con una fila que diga
    de dónde vienen, y deja la fecha de entrega de sus líneas en HOY, para que al liberarlos salgan sin que nadie tenga que
    acordarse de la fecha. De los cuatro del 23/10/26 solo 927991 (Paloma, 13/10/2027) seguía pendiente el 27/10/26; 928141,
    929057 y 929117 ya se habían servido. Primero mirar qué cogería:

    SELECT c.Empresa, c.Número, RTRIM(c.[Nº Cliente]) Cliente, RTRIM(c.Vendedor) Vendedor, c.ModoServicio, c.ServirJunto,
           MIN(l.[Fecha Entrega]) FechaEntrega, COUNT(*) Lineas, RTRIM(c.Usuario) Usuario
    FROM dbo.CabPedidoVta c JOIN dbo.LinPedidoVta l ON l.Empresa = c.Empresa AND l.Número = c.Número
    WHERE l.Estado BETWEEN -1 AND 1 AND ISNULL(c.ModoServicio, 0) <> 5
    GROUP BY c.Empresa, c.Número, c.[Nº Cliente], c.Vendedor, c.ModoServicio, c.ServirJunto, c.Usuario
    HAVING MIN(l.[Fecha Entrega]) > DATEADD(day, 60, GETDATE()) AND MIN(l.[Fecha Entrega]) < '20990101';

    Y después, por cada pedido (ejemplo con 927991; cambiar el motivo por lo que diga quien lo retuvo):

    BEGIN TRAN;
    DECLARE @empresa char(3) = '1', @pedido int = 927991, @usuario nvarchar(50) = N'NUEVAVISION\Paloma',
            @motivo nvarchar(500) = N'Retenido a la antigua (fecha de entrega 13/10/2027). Convertido al modo Retenido (#698).';
    INSERT INTO dbo.PedidosRetenidos (Empresa, Pedido, Motivo, HastaFecha, ModoServicioAnterior, Usuario)
    SELECT Empresa, Número, @motivo, NULL, ModoServicio, @usuario FROM dbo.CabPedidoVta WHERE Empresa = @empresa AND Número = @pedido;
    INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario)
    SELECT 'CabPedidoVta', 'Pedido ' + CAST(@pedido AS varchar) + ' ModoServicio=' + ISNULL(CAST(ModoServicio AS varchar), 'NULL')
         + ' FechaEntrega=' + CONVERT(varchar, (SELECT MIN([Fecha Entrega]) FROM dbo.LinPedidoVta WHERE Empresa = @empresa AND Número = @pedido AND Estado BETWEEN -1 AND 1), 103),
           'ModoServicio=5 (Retenido #698, one-off) FechaEntrega=hoy', @usuario
    FROM dbo.CabPedidoVta WHERE Empresa = @empresa AND Número = @pedido;
    UPDATE dbo.CabPedidoVta SET ModoServicio = 5 WHERE Empresa = @empresa AND Número = @pedido;
    UPDATE dbo.LinPedidoVta SET [Fecha Entrega] = CAST(GETDATE() AS date) WHERE Empresa = @empresa AND Número = @pedido AND Estado BETWEEN -1 AND 1;
    -- Comprobar y luego COMMIT (o ROLLBACK)
    SELECT * FROM dbo.PedidosRetenidos WHERE Empresa = @empresa AND Pedido = @pedido;
    ROLLBACK; -- cambiar por COMMIT cuando se vea bien
*/

-- Cómo va:
-- SELECT r.Pedido, r.Motivo, r.HastaFecha, r.Usuario, r.Fecha, r.Liberado, r.UsuarioLiberacion, r.MotivoLiberacion, r.UltimoRecordatorio
-- FROM dbo.PedidosRetenidos r ORDER BY r.Fecha DESC;
