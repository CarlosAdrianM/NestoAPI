-- =====================================================================================================
-- NestoAPI#756: numeración duplicada de las notas de entrega (ContadoresGlobales.NotaEntrega)
--
-- Semántica (deducida de los datos 30/10/26 y la que sigue ya la API con NumeradorNotasEntregaSql):
--   ContadoresGlobales.NotaEntrega guarda el ÚLTIMO número usado (Nesto viejo suma y usa el valor nuevo;
--   la API, desde #756: UPDATE ... SET NotaEntrega = NotaEntrega + 1 OUTPUT inserted.NotaEntrega).
--   Por tanto basta con que Contador >= MAX(NotasEntrega.NotaEntrega). Si fuera menor, el siguiente
--   número saldría repetido.
--
-- Lanzar como sa en NV. Con @Aplicar = 0 (por defecto) SOLO MUESTRA. Con @Aplicar = 1 sube el contador
-- al máximo usado si está por debajo (nunca lo baja). Los duplicados existentes NO se tocan (son histórico).
-- =====================================================================================================
SET NOCOUNT ON;
DECLARE @Aplicar BIT = 0;

DECLARE @Contador INT = (SELECT NotaEntrega FROM ContadoresGlobales);
DECLARE @MaxNota INT = (SELECT ISNULL(MAX(NotaEntrega), 0) FROM NotasEntrega);

SELECT @Contador AS ContadorActual_UltimoUsado,
       @MaxNota AS MaxNotaEntregaUsada,
       @Contador + 1 AS SiguienteQueSaldra,
       CASE WHEN @Contador >= @MaxNota THEN 'OK: no hace falta tocarlo'
            ELSE 'MAL: el siguiente número saldría repetido -> lanzar con @Aplicar = 1' END AS Diagnostico;

-- Duplicados: el mismo número de nota en pedidos distintos (las de Fecha a las 00:00 son de Nesto viejo)
;WITH PorPedido AS (
    SELECT n.NotaEntrega, l.Número AS Pedido, MIN(n.Fecha) AS Fecha
    FROM NotasEntrega n
    JOIN LinPedidoVta l ON l.[Nº Orden] = n.NºOrden
    GROUP BY n.NotaEntrega, l.Número
), Repetidas AS (
    SELECT NotaEntrega FROM PorPedido GROUP BY NotaEntrega HAVING COUNT(*) > 1
)
SELECT p.NotaEntrega, p.Pedido, p.Fecha,
       CASE WHEN CAST(p.Fecha AS time) = '00:00:00' THEN 'Nesto viejo' ELSE 'API' END AS Origen
FROM PorPedido p
JOIN Repetidas r ON r.NotaEntrega = p.NotaEntrega
ORDER BY p.NotaEntrega DESC, p.Fecha;

IF @Aplicar = 1
BEGIN
    BEGIN TRAN;
    -- Atómico y sin bajar nunca el contador (por si alguien numera entre la lectura y el UPDATE)
    UPDATE ContadoresGlobales
       SET NotaEntrega = (SELECT MAX(NotaEntrega) FROM NotasEntrega)
     WHERE NotaEntrega < (SELECT MAX(NotaEntrega) FROM NotasEntrega);
    PRINT CONCAT('Filas actualizadas: ', @@ROWCOUNT);
    COMMIT;

    SELECT NotaEntrega AS ContadorDespues FROM ContadoresGlobales;
END
ELSE
    PRINT 'Solo lectura (@Aplicar = 0). Nada modificado.';
