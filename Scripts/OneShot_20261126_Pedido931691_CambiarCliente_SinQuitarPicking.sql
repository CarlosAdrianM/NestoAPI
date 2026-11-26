/*
    Carlos 26/11/26: el pedido 931691 se hizo a nombre del 37283/0 y tiene que salir a nombre del 42178/0 (el mismo cliente
    con su forma fiscal nueva), SIN quitar el picking 100198 (ya terminado en la PDA) y con EL MISMO IMPORTE.
    La API no deja cambiar el cliente con picking y, además, recalcularía precios. Aquí solo se cambia de quién es el pedido:
    cliente y contacto en las líneas y en la cabecera (y el contacto de cobro). Precios, descuentos, IVA, vendedor, forma y
    plazos de pago, CCC (los dos clientes tienen el CCC «1»), picking y reservas NO se tocan.

    ANTES hay que lanzar OneShot_20261126_Cliente42178_ComoEl37283.sql (si la ficha nueva no tiene el mismo IVA, estado y
    vendedor que la anterior, este script no hace nada y lo dice).
    Tampoco hace nada si el pedido ya no es del 37283, si alguna línea ya está en albarán o factura, o si tiene envío de agencia.
    Las líneas van primero: el disparador de la cabecera no deja cambiarle el cliente con líneas de otro (lo mismo hace la API).
    Lanzar como sa en la BD NV. Se puede lanzar más de una vez.
*/
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @pedido int = 931691, @viejo char(10) = '37283', @nuevo char(10) = '42178', @contacto char(3) = '0';
DECLARE @baseAntes money, @totalAntes money, @baseDespues money, @totalDespues money, @anterior nvarchar(max);

IF NOT EXISTS (SELECT 1 FROM CabPedidoVta WHERE Empresa = '1' AND Número = @pedido AND [Nº Cliente] = @viejo)
BEGIN
    PRINT 'El pedido 931691 ya no es del cliente 37283 (o no existe). No se ha cambiado nada.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM LinPedidoVta WHERE Empresa = '1' AND Número = @pedido AND Estado >= 2)
BEGIN
    PRINT 'El pedido 931691 ya tiene líneas en albarán o factura. No se ha cambiado nada: hay que rectificar la factura.';
    RETURN;
END
IF EXISTS (SELECT 1 FROM EnviosAgencia WHERE Empresa = '1' AND Pedido = @pedido)
BEGIN
    PRINT 'El pedido 931691 ya tiene un envío de agencia (etiqueta). No se ha cambiado nada: avise.';
    RETURN;
END
IF NOT EXISTS (SELECT 1 FROM Clientes n INNER JOIN Clientes v ON v.Empresa = n.Empresa AND v.[Nº Cliente] = @viejo AND v.Contacto = @contacto
               WHERE n.Empresa = '1' AND n.[Nº Cliente] = @nuevo AND n.Contacto = @contacto
                 AND ISNULL(RTRIM(n.IVA), '') = ISNULL(RTRIM(v.IVA), '') AND n.Estado = v.Estado AND RTRIM(n.Vendedor) = RTRIM(v.Vendedor))
BEGIN
    PRINT 'La ficha 42178/0 no tiene el mismo IVA, estado y vendedor que la 37283/0: lance antes OneShot_20261126_Cliente42178_ComoEl37283.sql. No se ha cambiado nada.';
    RETURN;
END

SELECT @baseAntes = SUM([Base Imponible]), @totalAntes = SUM(Total) FROM LinPedidoVta WHERE Empresa = '1' AND Número = @pedido;
SELECT @anterior = N'{"Pedido":931691,"Cliente":"' + RTRIM([Nº Cliente]) + N'","Contacto":"' + RTRIM(Contacto) + N'","ContactoCobro":"' + ISNULL(RTRIM(ContactoCobro), '')
       + N'","IVA":"' + ISNULL(RTRIM(IVA), '') + N'","Vendedor":"' + RTRIM(Vendedor) + N'"}'
FROM CabPedidoVta WHERE Empresa = '1' AND Número = @pedido;

BEGIN TRANSACTION;
    -- 1) Las líneas
    UPDATE LinPedidoVta SET [Nº Cliente] = @nuevo, Contacto = @contacto WHERE Empresa = '1' AND Número = @pedido;
    -- 2) La cabecera (solo de quién es; lo demás no se toca)
    UPDATE CabPedidoVta SET [Nº Cliente] = @nuevo, Contacto = @contacto, ContactoCobro = @contacto WHERE Empresa = '1' AND Número = @pedido;

    SELECT @baseDespues = SUM([Base Imponible]), @totalDespues = SUM(Total) FROM LinPedidoVta WHERE Empresa = '1' AND Número = @pedido;
    IF ISNULL(@baseDespues, -1) <> ISNULL(@baseAntes, -2) OR ISNULL(@totalDespues, -1) <> ISNULL(@totalAntes, -2)
       OR EXISTS (SELECT 1 FROM LinPedidoVta WHERE Empresa = '1' AND Número = @pedido AND ([Nº Cliente] <> @nuevo OR ISNULL(Picking, 0) = 0))
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT 'Algo ha cambiado al guardar (importes, cliente de alguna línea o picking). Se ha DESHECHO todo: avise.';
        RETURN;
    END

    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha)
    VALUES (N'Pedidos', @anterior,
            N'{"Operacion":"Cambiar cliente sin quitar el picking (script, Carlos 26/11/26)","Datos":{"Pedido":931691,"Cliente":"42178","Contacto":"0","Base":'
            + CONVERT(nvarchar(30), @baseDespues) + N',"Total":' + CONVERT(nvarchar(30), @totalDespues) + N'}}',
            SUSER_SNAME(), GETDATE());
COMMIT TRANSACTION;
PRINT 'Pedido 931691 pasado al cliente 42178/0. Base ' + CONVERT(varchar(30), @baseDespues) + ' y total ' + CONVERT(varchar(30), @totalDespues) + ', los mismos que antes.';
GO
SELECT c.Número AS Pedido, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto, RTRIM(c.IVA) AS IVA, RTRIM(c.Vendedor) AS Vendedor,
       RTRIM(c.PlazosPago) AS Plazos, RTRIM(c.[Forma Pago]) AS FormaPago, RTRIM(c.CCC) AS CCC,
       (SELECT SUM(l.[Base Imponible]) FROM LinPedidoVta l WHERE l.Empresa = c.Empresa AND l.Número = c.Número) AS Base,
       (SELECT SUM(l.Total) FROM LinPedidoVta l WHERE l.Empresa = c.Empresa AND l.Número = c.Número) AS Total,
       (SELECT COUNT(*) FROM LinPedidoVta l WHERE l.Empresa = c.Empresa AND l.Número = c.Número AND l.Picking = 100198) AS LineasConPicking
FROM CabPedidoVta c WHERE c.Empresa = '1' AND c.Número = 931691;
GO
