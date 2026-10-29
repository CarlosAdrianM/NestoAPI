/*
    NestoAPI#613 (Carlos, 29/10/26, a petición del director comercial): el pedido mínimo para usar el cupón que gana quien paga
    la señal de un evento ya NO es el doble del cupón: lo define CADA EVENTO. En los eventos ya creados y por defecto, 0 € (vale
    con cualquier importe de productos); en los nuevos, si se informa al crearlo, p. ej. 200 €.

    Columnas nuevas (fuera del EDMX; la API las lee y escribe por SQL y comprueba antes que existen con COL_LENGTH):
      - Eventos.PedidoMinimoCupon decimal(10, 2) NULL: el mínimo del evento (NULL = 0). La API lo expone como
        EventoDTO.PedidoMinimoCupon y Nesto lo edita en el mantenimiento de eventos.
      - Cupones.PedidoMinimo decimal(10, 2) NULL: el mínimo con el que se emitió el cupón (se copia del evento al emitirlo; si
        luego se cambia el evento, los cupones ya emitidos conservan el suyo). NULL = cupón de antes de este script o manual:
        la regla de antes (el doble del cupón), para que nada cambie sin este script.

    Lo que hace además, una sola vez (idempotente):
      - Eventos existentes → PedidoMinimoCupon = 0.
      - Cupones de origen Evento ya emitidos (el 29/10/26 hay 1: EV-2611-P3DN, del evento 1) → PedidoMinimo = 0.
      - Si alguno de esos cupones ya estaba en la tienda PrestaShop (espejo #614, EstadoEspejo = 'Activo'), se le quita la marca
        (EstadoEspejo = NULL, conservando IdCartRulePrestashop) para que el job del espejo lo vuelva a mandar con PUT y el
        cart_rule pase a minimum_amount = 0. Hoy EstadoEspejo es NULL en todos (el espejo está apagado): no hace nada.

    Sin el script la API funciona como antes: eventos con 0 en la lectura, el PUT con un mínimo mayor que 0 contesta que falta
    este script y los cupones siguen con el doble.

    Ejecutar en SSMS contra NV como sa (ALTER). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.Eventos', 'PedidoMinimoCupon') IS NULL
    ALTER TABLE dbo.Eventos ADD PedidoMinimoCupon decimal(10, 2) NULL;
IF COL_LENGTH('dbo.Cupones', 'PedidoMinimo') IS NULL
    ALTER TABLE dbo.Cupones ADD PedidoMinimo decimal(10, 2) NULL;
GO

-- Los eventos que ya existen: sin mínimo
UPDATE dbo.Eventos SET PedidoMinimoCupon = 0 WHERE PedidoMinimoCupon IS NULL;
GO

-- Los cupones de evento ya emitidos: sin mínimo; y si ya estaban en PrestaShop, que el espejo los vuelva a mandar
IF OBJECT_ID('tempdb..#CuponesSinMinimo') IS NOT NULL
    DROP TABLE #CuponesSinMinimo;
SELECT Id INTO #CuponesSinMinimo FROM dbo.Cupones WHERE Origen = 'Evento' AND PedidoMinimo IS NULL;

UPDATE c SET PedidoMinimo = 0, FechaModificacion = GETDATE()
FROM dbo.Cupones c JOIN #CuponesSinMinimo x ON x.Id = c.Id;

-- Espejo #614: con EstadoEspejo NULL el job lo ve pendiente y, como conserva IdCartRulePrestashop, hace PUT del cart_rule
-- con el mínimo nuevo. Dinámico por si las columnas del espejo (Issue614) no existieran.
IF COL_LENGTH('dbo.Cupones', 'EstadoEspejo') IS NOT NULL
    EXEC sp_executesql N'
        UPDATE c SET EstadoEspejo = NULL
        FROM dbo.Cupones c JOIN #CuponesSinMinimo x ON x.Id = c.Id
        WHERE c.EstadoEspejo = ''Activo'';
        SELECT @@ROWCOUNT AS CuponesParaVolverAMandarAPrestashop;';

DROP TABLE #CuponesSinMinimo;
GO

-- La API lee y escribe las columnas nuevas por SQL; los permisos ya los dieron Issue611 (Eventos) e Issue613_Cupones.sql
-- (Cupones), se repiten por si acaso
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Eventos TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE ON dbo.Cupones TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
------------------------------------------------------------------------------------------------
SELECT COL_LENGTH('dbo.Eventos', 'PedidoMinimoCupon') AS ColumnaEventosPedidoMinimoCupon,
       COL_LENGTH('dbo.Cupones', 'PedidoMinimo') AS ColumnaCuponesPedidoMinimo;
SELECT Id, Titulo, Fecha, PedidoMinimoCupon FROM dbo.Eventos ORDER BY Id;
SELECT Id, Codigo, Origen, EventoId, Importe, PedidoMinimo, Estado, EstadoEspejo FROM dbo.Cupones ORDER BY Id;
GO
