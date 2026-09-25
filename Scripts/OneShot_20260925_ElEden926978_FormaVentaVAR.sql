-- NestoAPI#543 (25/09/26): El Edén (15191) cuenta como «Varios» aunque pida por la app. Dos pedidos del 24/09
-- salieron con forma de venta APP:
--   · 926978 (20 líneas): el pedido rehecho del 926936, creado por api/Pedidos/Cliente.
--   · 927041 (3 líneas): pedido de El Edén hecho desde la tienda (TNV).
-- Las líneas están en albarán (estado 2), sin facturar: el trigger no impide cambiar la forma de venta.
-- Ejecutar como sa. Si no son exactamente 23 líneas, no cambia nada.
SET NOCOUNT ON;
BEGIN TRAN;
UPDATE dbo.LinPedidoVta SET [Forma Venta] = 'VAR'
WHERE Empresa = '1' AND Número IN (926978, 927041) AND [Forma Venta] = 'APP' AND [Nº Cliente] = '15191';
IF @@ROWCOUNT <> 23
BEGIN
    ROLLBACK;
    RAISERROR('No son 23 líneas (20 + 3): no se ha cambiado nada', 16, 1);
END
ELSE
    COMMIT;

SELECT Número, Estado, [Forma Venta], COUNT(*) AS Lineas
FROM dbo.LinPedidoVta WITH (NOLOCK)
WHERE Empresa = '1' AND Número IN (926978, 927041)
GROUP BY Número, Estado, [Forma Venta];
