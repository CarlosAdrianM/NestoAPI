-- NestoAPI#762 (punto 7, limpieza inicial): los productos repuestos en los últimos 3 meses sin ningún código de barras
-- válido, los de más unidades primero. A estos hay que darles su código interno e imprimir sus etiquetas de una vez
-- (POST api/Almacen/EtiquetasProducto/Imprimir con Formato = Ean13; lo hace también la API sola al imprimir).
--
-- SOLO LEE (no escribe nada). Es lo mismo que GET api/Productos/RepuestosSinCodigoBarras?meses=3 una vez publicada la API,
-- salvo que aquí «válido» se aproxima (todo cifras, 8/12/13/14 de largo, sin comprobar el dígito de control; la API sí
-- lo comprueba). Lee ExtractoProducto con NOLOCK: lanzarlo fuera de horas punta.
--
-- No hace falta ningún script de estructura para NestoAPI#762: el código interno va en ProductosCodigosBarras (Origen
-- 'Almacen', que la restricción CK_ProductosCodigosBarras_Origen ya admite) y la API ya escribe en esa tabla (NestoAPI#605),
-- así que tampoco hay GRANT nuevo. El bloqueo de aplicación (sp_getapplock) lo puede usar cualquier usuario.

DECLARE @empresa char(3) = '1';
DECLARE @desde datetime = DATEADD(MONTH, -3, CAST(GETDATE() AS date));

WITH Repuestos AS (
    SELECT e.[Número] AS Producto, COUNT(DISTINCT e.[NºTraspaso]) AS Reposiciones, SUM(e.Cantidad) AS Unidades, MAX(e.Fecha) AS Ultima
    FROM ExtractoProducto e WITH (NOLOCK)
    WHERE e.Empresa = @empresa AND e.Fecha >= @desde AND e.[NºTraspaso] IS NOT NULL AND e.[NºTraspaso] <> 0 AND e.Cantidad > 0
    GROUP BY e.[Número]
)
SELECT RTRIM(r.Producto) AS Producto, RTRIM(p.Nombre) AS Nombre, RTRIM(p.Familia) AS Familia, r.Reposiciones, r.Unidades,
       CONVERT(varchar(10), r.Ultima, 103) AS Ultima
FROM Repuestos r
     JOIN Productos p WITH (NOLOCK) ON p.Empresa = @empresa AND p.[Número] = r.Producto
WHERE p.Estado >= 0 AND ISNULL(p.Ficticio, 0) = 0
  AND NOT EXISTS (
      SELECT 1 FROM ProductosCodigosBarras c WITH (NOLOCK)
      WHERE c.Empresa = p.Empresa AND c.Producto = p.[Número] AND c.Activo = 1
        AND LEN(c.Codigo) IN (8, 12, 13, 14) AND c.Codigo NOT LIKE '%[^0-9]%' AND c.Codigo NOT LIKE '0000000%')
ORDER BY r.Unidades DESC, r.Producto;

-- Los códigos internos que ya se han dado (empiezan por 29; 290 = referencia, 299 = contador):
-- SELECT RTRIM(Producto) AS Producto, Codigo, Usuario, Fecha FROM ProductosCodigosBarras WITH (NOLOCK)
-- WHERE Empresa = '1' AND Codigo LIKE '29%' AND LEN(Codigo) = 13 ORDER BY Fecha DESC;
