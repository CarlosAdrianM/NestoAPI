-- OneShot 14/11/26 (NestoAPI#857): deshacer la fecha que el job de correos de seguimiento puso el 13/11 a las 16:00 en el
-- pedido de compra 220181 (Weelko). Los correos de UPS/MBE eran del envío del 220873 (albarán el 13/11 a las 10:58); al
-- recibirse ese, el job tomó por inequívoco el 220181, que era el único que quedaba en el aire, y le adelantó las 4 líneas
-- del 23/11 al 16/11. Aquí: las líneas vuelven al 23/11 (lo que tenían, según Modificaciones Id 110085) y los correos del
-- envío pasan a apuntar al 220873, que es el suyo (así los que lleguen después no vuelven a tocar el 220181).
-- Lanzar como sa. Idempotente: solo toca líneas que sigan en 16/11 y correos que sigan apuntando al 220181.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @lineas int, @correos int;

UPDATE LinPedidoCmp
SET FechaRecepción = '20261123', Usuario = 'Correo seguimiento deshecho', [Fecha Modificación] = GETDATE()
WHERE Empresa = '1' AND Número = 220181 AND Estado IN (-1, 1) AND FechaRecepción = '20261116'
  AND NºOrden IN (141325551, 141326051, 141655551, 141655751);
SET @lineas = @@ROWCOUNT;

UPDATE dbo.ComprasCorreosSeguimiento
SET PedidoAplicado = 220873, FechaRecepcionAplicada = '20261113', AplicadoEl = GETDATE(), AplicadoPor = 'NUEVAVISION\Carlos (corrección)'
WHERE EmpresaAplicada = '1' AND PedidoAplicado = 220181;
SET @correos = @@ROWCOUNT;

IF @lineas > 0 OR @correos > 0
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha)
    VALUES ('CompraFechaPorCorreo',
            N'{"Empresa":"1","Pedido":220181,"FechaRecepcion":"2026-11-16","DeshaceModificacion":110085}',
            N'{"Empresa":"1","Pedido":220181,"FechaRecepcion":"2026-11-23","LineasCambiadas":' + CAST(@lineas AS nvarchar(10))
                + N',"CorreosAlPedidoReal":220873,"Correos":' + CAST(@correos AS nvarchar(10))
                + N',"Motivo":"El envío era del 220873, recibido el 13/11; el job lo aplicó al único pedido que quedaba en el aire"}',
            N'NUEVAVISION\Carlos', GETDATE());

COMMIT TRANSACTION;

SELECT @lineas AS LineasDevueltasAl23, @correos AS CorreosAl220873;
SELECT NºOrden, Producto, Estado, FechaRecepción FROM LinPedidoCmp WHERE Empresa = '1' AND Número = 220181;
