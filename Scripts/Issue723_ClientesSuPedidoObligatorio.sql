/*
    NestoAPI#723 (28/10/26): P.O. («Su pedido») obligatorio por cliente. Laura Magán: «no se puede facturar ningún pedido de
    Chanel sin O.P.». Tabla aparte para no tocar Clientes (trigger trgClientesUpd, Nesto_sync, EDMX): una fila = el cliente
    (todos sus contactos) exige el P.O. en cada pedido; sin fila, como siempre.
    La API: no graba pedidos (POST/PUT) sin «Su pedido» para esos clientes (los presupuestos sí), no los factura (rutas y
    botones del detalle) hasta que lo tengan, y PUT api/Facturas/{empresa}/{numero}/SuPedido pone el P.O. en una factura
    ya emitida (el P.O. no entra en el registro de Verifactu). Interruptor Pedidos:SuPedidoObligatorio en Web.config.
    GRANT: SELECT, INSERT y DELETE (la marca se quita borrando la fila). Lanzar como sa en NV. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID('dbo.ClientesSuPedidoObligatorio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesSuPedidoObligatorio (
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        Usuario nvarchar(50) NOT NULL,                  -- quien puso la marca
        Fecha datetime NOT NULL CONSTRAINT DF_ClientesSuPedidoObligatorio_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_ClientesSuPedidoObligatorio PRIMARY KEY CLUSTERED (Empresa, Cliente)
    );
END
GO
GRANT SELECT, INSERT, DELETE ON dbo.ClientesSuPedidoObligatorio TO [NUEVAVISION\RDS2016$];
GO
-- Chanel: cuando Laura diga los números de cliente, una fila por cada uno (empresa 1):
-- INSERT INTO dbo.ClientesSuPedidoObligatorio (Empresa, Cliente, Usuario) VALUES ('1', '12345', 'NUEVAVISION\Carlos');
