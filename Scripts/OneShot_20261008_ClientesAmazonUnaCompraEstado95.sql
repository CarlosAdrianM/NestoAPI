-- 08/10/26 (Carlos): los clientes cuya ÚNICA compra es un pedido de Amazon (todas sus líneas con Forma Venta = 'STK')
-- pasan al estado nuevo 95 «Una sola compra, por Amazon». Al facturarles un pedido que no sea de Amazon vuelven
-- solos a estado 0 (OneShot_20261008_prdCrearFacturaVta_Estado95.sql, el mismo bloque que recupera 11, 47, 53...).
--
-- Solo se tocan los que están en estado 0 (Visita de comercial) o 9 (En vías de recuperación). El resto se deja
-- como está porque su estado ya dice algo: 8 particular web, 93 solo estética sin compra, 11 solo peluquería,
-- -1 nulo, 7 comisiona vendedor, 42-71 tipos de centro, etc. (recuento del 08/10: ~460 de 0/9; 498 en 8, 177 en 93).
-- «Compra» = pedido facturado (LinPedidoVta.Estado = 4), por cliente y contacto.
--
-- Lanzar como sa. Orden: este script → OneShot_20261008_prdCrearFacturaVta_Estado95.sql.
-- El trigger trgClientesUpd apunta cada cambio en SeguimientoCambiosEstadoCliente. Mejor fuera de horas punta
-- (agrega LinPedidoVta de los clientes que tienen alguna línea STK).

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 1) El estado nuevo
IF NOT EXISTS (SELECT * FROM EstadosCliente WHERE Empresa = '1' AND Número = 95)
    INSERT INTO EstadosCliente (Empresa, Número, Descripción, Provisional, [%Comision], Usuario, [Fecha Modificación])
    VALUES ('1', 95, 'Una sola compra, por Amazon', 0, 0, SYSTEM_USER, GETDATE());

-- 2) Los candidatos
IF OBJECT_ID('tempdb..#Pedidos') IS NOT NULL DROP TABLE #Pedidos;
IF OBJECT_ID('tempdb..#Candidatos') IS NOT NULL DROP TABLE #Candidatos;

SELECT l.[Nº Cliente] Cliente, l.Contacto, l.Número,
       SUM(CASE WHEN l.[Forma Venta] = 'STK' THEN 1 ELSE 0 END) LineasAmazon, COUNT(*) Lineas
INTO #Pedidos
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = '1' AND l.Estado = 4
  AND l.[Nº Cliente] IN (SELECT DISTINCT [Nº Cliente] FROM LinPedidoVta WITH (NOLOCK) WHERE Empresa = '1' AND [Forma Venta] = 'STK')
GROUP BY l.[Nº Cliente], l.Contacto, l.Número;

SELECT c.[Nº Cliente] Cliente, c.Contacto, c.Estado, c.Vendedor, c.Nombre
INTO #Candidatos
FROM Clientes c
INNER JOIN (SELECT Cliente, Contacto FROM #Pedidos GROUP BY Cliente, Contacto
            HAVING COUNT(*) = 1 AND SUM(CASE WHEN LineasAmazon = Lineas THEN 1 ELSE 0 END) = 1) u
        ON u.Cliente = c.[Nº Cliente] AND u.Contacto = c.Contacto
WHERE c.Empresa = '1' AND c.Estado IN (0, 9);

-- Revisión antes de cambiar nada
SELECT Estado, RTRIM(Vendedor) Vendedor, COUNT(*) Clientes FROM #Candidatos GROUP BY Estado, Vendedor ORDER BY Clientes DESC;
SELECT COUNT(*) TotalAPasarA95 FROM #Candidatos;

-- 3) El cambio
BEGIN TRANSACTION;

UPDATE c SET Estado = 95
FROM Clientes c
INNER JOIN #Candidatos x ON x.Cliente = c.[Nº Cliente] AND x.Contacto = c.Contacto
WHERE c.Empresa = '1' AND c.Estado IN (0, 9);

SELECT @@ROWCOUNT Actualizados;

-- Si el número coincide con TotalAPasarA95: COMMIT. Si no: ROLLBACK.
-- COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;
