-- 21/10/26: Paloma no pudo guardar «Su pedido» en estos tres pedidos (fallo del PUT con portes ya albaranados,
-- arreglado en la API en la publicación del 21/10). Sin la PO quedarían fuera de la agrupación por PO al facturar.
-- Lanzar como sa en NV.

UPDATE CabPedidoVta SET SuPedido = 'PO00495702'
WHERE Empresa = '1' AND Número IN (928974, 928975, 928977) AND (SuPedido IS NULL OR LTRIM(RTRIM(SuPedido)) = '');
-- Debe decir: 3 filas afectadas.

SELECT Número, [Nº Cliente], Contacto, SuPedido FROM CabPedidoVta WHERE Empresa = '1' AND Número IN (928973, 928974, 928975, 928976, 928977, 928978);
-- Los seis deben salir con PO00495702.
