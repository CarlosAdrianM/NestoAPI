-- Cheques regalo canjeados por vendedor y la venta que han supuesto (campaña CHEQUE50_OCT_2026; cambiar @campana para otra).
-- Carlos (04/11/26): para los correos de motivación de la campaña.
-- Canjeado = línea del producto del cheque en un pedido con Estado entre -1 (pendiente) y 4 (facturado): fuera presupuestos (-3)
--   y cancelados. Se atribuye al vendedor de la CABECERA del pedido de canje.
-- Venta = base imponible de TODAS las líneas (Estado -1..4) de los pedidos de ese vendedor que llevan cheque:
--   VentaProducto = sin la línea del cheque; VentaNeta = con el cheque descontado (−50 € por cheque).
-- ChequesGenerados = cheques de la campaña de los clientes cuyo contacto principal tiene ese vendedor (para ver quién tiene
--   cheques en la calle sin mover). Todos los vendedores con algo que contar; Activo = Vendedores.Estado = 0 (los demás son
--   tiendas, General, canales… para que cuadre el total de la campaña).
SET NOCOUNT ON;
DECLARE @campana varchar(20) = 'CHEQUE50_OCT_2026';
DECLARE @producto char(15) = (SELECT TOP 1 Producto FROM ChequesRegaloCampanas WHERE Codigo = @campana);

WITH Canjes AS (
    SELECT l.Empresa, l.Número Pedido, c.Vendedor, COUNT(*) Cheques
    FROM LinPedidoVta l WITH (NOLOCK)
    JOIN CabPedidoVta c WITH (NOLOCK) ON c.Empresa = l.Empresa AND c.Número = l.Número
    WHERE l.Producto = @producto AND l.Estado BETWEEN -1 AND 4
    GROUP BY l.Empresa, l.Número, c.Vendedor
),
VentaPedido AS (
    SELECT k.Empresa, k.Pedido, k.Vendedor, k.Cheques,
           SUM(CASE WHEN l.Producto <> @producto THEN l.[Base Imponible] ELSE 0 END) VentaProducto,
           SUM(l.[Base Imponible]) VentaNeta
    FROM Canjes k
    JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = k.Empresa AND l.Número = k.Pedido AND l.Estado BETWEEN -1 AND 4
    GROUP BY k.Empresa, k.Pedido, k.Vendedor, k.Cheques
),
Venta AS (
    SELECT Vendedor, COUNT(*) Pedidos, SUM(Cheques) Cheques, SUM(VentaProducto) VentaProducto, SUM(VentaNeta) VentaNeta
    FROM VentaPedido GROUP BY Vendedor
),
Generados AS (
    SELECT cl.Vendedor, COUNT(*) ChequesGenerados,
           SUM(CASE WHEN ch.Estado = 'Generado' THEN 1 ELSE 0 END) SinCanjear
    FROM ChequesRegalo ch WITH (NOLOCK)
    JOIN Clientes cl WITH (NOLOCK) ON cl.Empresa = ch.Empresa AND cl.[Nº Cliente] = ch.Cliente AND cl.ClientePrincipal = 1
    WHERE ch.Campana = @campana
    GROUP BY cl.Vendedor
)
SELECT RTRIM(v.Número) Vendedor, RTRIM(v.Descripción) Nombre, RTRIM(v.Mail) Correo, CASE WHEN v.Estado = 0 THEN 'Sí' ELSE 'No' END Activo,
       ISNULL(g.ChequesGenerados, 0) ChequesGenerados,
       ISNULL(g.SinCanjear, 0) SinCanjear,
       ISNULL(x.Cheques, 0) ChequesCanjeados,
       ISNULL(x.Pedidos, 0) PedidosConCheque,
       CAST(ISNULL(x.VentaProducto, 0) AS decimal(12,2)) VentaProducto,
       CAST(ISNULL(x.VentaNeta, 0) AS decimal(12,2)) VentaNeta,
       CAST(CASE WHEN ISNULL(x.Pedidos, 0) = 0 THEN 0 ELSE x.VentaProducto / x.Pedidos END AS decimal(12,2)) VentaMediaPorPedido
FROM Vendedores v WITH (NOLOCK)
LEFT JOIN Venta x ON x.Vendedor = v.Número
LEFT JOIN Generados g ON g.Vendedor = v.Número
WHERE v.Empresa = '1'
  AND (ISNULL(g.ChequesGenerados, 0) > 0 OR ISNULL(x.Cheques, 0) > 0)
ORDER BY Activo DESC, VentaProducto DESC, ChequesGenerados DESC;
