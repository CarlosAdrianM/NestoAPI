/*
    NestoAPI#732 (sugerencia 863, Paloma; decisión de Carlos 29/10/26): eventos «gratuitos por compra».

    1. EventosProductosRequeridos: los productos que hay que haber comprado o pedido para apuntarse al evento (p. ej. el
       evento 4 «Formación por compra Lifting Coreano», ref. 45363). Se mantienen con PUT api/Eventos/{id}
       (ProductosRequeridos) y salen en el DTO del evento. Sin filas, el evento no exige compra (como hasta ahora).
       - Basta con UNO de los productos de la lista (son alternativas: tallas, kits…).
       - Vale cualquier línea de venta del cliente (empresa + cliente, cualquier contacto) de ese producto con cantidad
         positiva y estado pendiente (-1), en curso (1), albarán (2) o factura (4), en un pedido con fecha desde 60 días
         antes de que se pusiera el requisito (FechaCreacion más antigua de las filas del evento). Los presupuestos (-3)
         y las notas de entrega (-2) no cuentan.
    2. EventosInscripciones.PedidoJustificante: el pedido (CabPedidoVta.Número) que justifica la inscripción. Al borrar
       ese pedido, o al quitarle la línea que la justificaba, la API avisa (409 PEDIDO_JUSTIFICA_INSCRIPCIONES_EVENTO) y, si
       se confirma, anula la inscripción (Estado Anulada + fila en Modificaciones) y avisa a la tienda del evento.

    Fuera del EDMX: la API lee y escribe por SQL y comprueba que existen (OBJECT_ID / COL_LENGTH). Sin el script, la API
    funciona como antes: los eventos no exigen compra y el PUT con ProductosRequeridos contesta que falta el script.

    Ejecutar en SSMS contra NV como sa (CREATE y ALTER). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. EventosProductosRequeridos
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.EventosProductosRequeridos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventosProductosRequeridos (
        EventoId int NOT NULL,
        Producto char(15) NOT NULL,                     -- Productos.Número (de la empresa del evento)
        Usuario varchar(50) NOT NULL CONSTRAINT DF_EventosProductosRequeridos_Usuario DEFAULT (SUSER_SNAME()),
        FechaCreacion datetime NOT NULL CONSTRAINT DF_EventosProductosRequeridos_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_EventosProductosRequeridos PRIMARY KEY CLUSTERED (EventoId, Producto),
        CONSTRAINT FK_EventosProductosRequeridos_Eventos FOREIGN KEY (EventoId) REFERENCES dbo.Eventos (Id)
    );
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosProductosRequeridos TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- 2. EventosInscripciones.PedidoJustificante
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.EventosInscripciones', 'PedidoJustificante') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD PedidoJustificante int NULL;
GO

-- Cada PUT/DELETE de un pedido pregunta si justifica alguna inscripción: que sea una búsqueda por índice
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventosInscripciones_PedidoJustificante'
               AND object_id = OBJECT_ID('dbo.EventosInscripciones'))
    CREATE INDEX IX_EventosInscripciones_PedidoJustificante ON dbo.EventosInscripciones (Empresa, PedidoJustificante);
GO

-- EventosInscripciones ya tenía SELECT/INSERT/UPDATE/DELETE (#611); se repite por si acaso
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosInscripciones TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
------------------------------------------------------------------------------------------------
SELECT OBJECT_ID('dbo.EventosProductosRequeridos', 'U') AS TablaProductosRequeridos,
       COL_LENGTH('dbo.EventosInscripciones', 'PedidoJustificante') AS ColumnaPedidoJustificante;
GO
