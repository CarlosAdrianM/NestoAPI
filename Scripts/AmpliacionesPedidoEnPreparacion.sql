/*
    Caso 931007 (Carlos 18/11/26): añadir líneas a un pedido EN PREPARACIÓN (picking vivo o albarán de hoy) que sale hoy ya no pasa en
    silencio. El PUT de PedidosVenta pide elegir (409 PEDIDO_EN_PREPARACION): un pedido nuevo con lo añadido (no necesita este script) o
    pedir a Almacén que lo meta en el envío de hoy (PedirAlmacen: necesita esta tabla).

    AmpliacionesPedidoEnPreparacion: una fila por cada vez que se pide a Almacén.
      - Lineas: los Nº de orden de las líneas añadidas (separados por comas); Productos: «39284 (1 ud.)…» para los avisos.
      - Pickings: los del pedido cuando se pidió; Envio: el que lo lleva (el que está en curso), si lo hay.
      - Usuario: quien lo pidió (recibe la respuesta en la campana de Nesto y en NestoApp).
      - Estado: Pendiente (ventana roja con pitido en los Nesto de Almacén), Entra («Sí, lo metemos»: se saca el picking del pedido y el
        envío se suma al que está en curso), NoEntra («No llega», o el picking no ha cogido lo añadido) o SinRespuesta (60 minutos sin
        contestar, job de Hangfire). Con NoEntra y SinRespuesta lo añadido pasa a un pedido nuevo (PedidoNuevo) con sus propios portes.
      - PickingNuevo: el picking de lo añadido (Entra). Motivo: el de «No llega» o por qué no se ha podido pasar a un pedido nuevo.

    Fuera del EDMX. Sin el script, PedirAlmacen da 503 «falta la tabla» y todo lo demás (409 y pedido nuevo) funciona igual.

    Lanzar en SSMS contra NV como sa (cuando se quiera la opción de Almacén; puede ir antes o después de publicar la API). Idempotente.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID(N'dbo.AmpliacionesPedidoEnPreparacion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AmpliacionesPedidoEnPreparacion (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AmpliacionesPedidoEnPreparacion PRIMARY KEY,
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Lineas varchar(200) NOT NULL,                       -- Nº de orden de las líneas añadidas, separados por comas
        Productos nvarchar(500) NULL,                       -- «39284 (1 ud.) y 40010 (3 uds.)»
        Pickings varchar(100) NULL,                         -- los del pedido al pedirlo
        Envio int NULL,
        Usuario varchar(50) NOT NULL,                       -- quien lo pidió
        Fecha datetime NOT NULL CONSTRAINT DF_AmpliacionesPedidoEnPreparacion_Fecha DEFAULT (GETDATE()),
        Estado varchar(12) NOT NULL CONSTRAINT DF_AmpliacionesPedidoEnPreparacion_Estado DEFAULT ('Pendiente'),
        Respondida datetime NULL,
        UsuarioRespuesta varchar(50) NULL,
        PickingNuevo int NULL,
        PedidoNuevo int NULL,
        Motivo nvarchar(500) NULL,
        CONSTRAINT CK_AmpliacionesPedidoEnPreparacion_Estado CHECK (Estado IN ('Pendiente', 'Entra', 'NoEntra', 'SinRespuesta'))
    );
    PRINT 'AmpliacionesPedidoEnPreparacion: creada.';
END
ELSE
    PRINT 'AmpliacionesPedidoEnPreparacion: ya existía.';
GO

-- Las pendientes (ventana de Almacén, GET api/PedidosVenta/AmpliacionesEnPreparacion) y las de un pedido (correo de la modificación)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AmpliacionesPedidoEnPreparacion_Estado' AND object_id = OBJECT_ID(N'dbo.AmpliacionesPedidoEnPreparacion'))
    CREATE INDEX IX_AmpliacionesPedidoEnPreparacion_Estado ON dbo.AmpliacionesPedidoEnPreparacion (Estado, Fecha);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AmpliacionesPedidoEnPreparacion_Pedido' AND object_id = OBJECT_ID(N'dbo.AmpliacionesPedidoEnPreparacion'))
    CREATE INDEX IX_AmpliacionesPedidoEnPreparacion_Pedido ON dbo.AmpliacionesPedidoEnPreparacion (Empresa, Pedido);
GO

-- La API corre con la cuenta máquina de RDS2016 (integrated security). Lee, inserta y actualiza (no borra: es la historia).
GRANT SELECT, INSERT, UPDATE ON dbo.AmpliacionesPedidoEnPreparacion TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID(N'dbo.AmpliacionesPedidoEnPreparacion', N'U') AS Tabla;
SELECT TOP 20 Id, Empresa, Pedido, Lineas, Productos, Pickings, Envio, Usuario, Fecha, Estado, UsuarioRespuesta, PickingNuevo, PedidoNuevo, Motivo
FROM dbo.AmpliacionesPedidoEnPreparacion ORDER BY Id DESC;
