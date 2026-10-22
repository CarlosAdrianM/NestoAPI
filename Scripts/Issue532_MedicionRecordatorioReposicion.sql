-- NestoAPI#532: ¿sirve el recordatorio de reposición? SOLO LECTURA.
-- Por cada producto recordado (Grupo 'Enviado') o apuntado en el grupo de control ('Control') en RecordatoriosReposicionEnvios,
-- si hubo albarán de ese producto para ese cliente (por cualquier canal) en los @dias siguientes. Si el correo sirve, el
-- porcentaje de 'Enviado' tiene que salir claramente por encima del de 'Control'.
-- El cruce con LinPedidoVta va por cliente y producto concretos (índices que empiezan por Nº Cliente o Producto) y con
-- NOLOCK: se puede lanzar en horario. Los envíos de los últimos @dias días todavía no han tenido tiempo: se excluyen.

USE NV;
GO

SET NOCOUNT ON;

DECLARE @dias int = 14;
DECLARE @desde datetime = DATEADD(month, -3, CAST(GETDATE() AS date));
DECLARE @hasta datetime = DATEADD(day, -@dias, CAST(GETDATE() AS date));

;WITH Recordados AS (
    SELECT e.Id, e.Grupo, e.VersionTexto, RTRIM(e.Cliente) AS Cliente, RTRIM(e.Producto) AS Producto, e.Fecha,
           CASE WHEN EXISTS (
               SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK)
               WHERE l.Empresa = e.Empresa AND l.[Nº Cliente] = e.Cliente AND l.Producto = e.Producto
                 AND l.Estado >= 2 AND l.Cantidad > 0
                 AND l.[Fecha Albarán] >= CAST(e.Fecha AS date) AND l.[Fecha Albarán] < DATEADD(day, @dias + 1, CAST(e.Fecha AS date))
           ) THEN 1 ELSE 0 END AS Repuesto
    FROM dbo.RecordatoriosReposicionEnvios e WITH (NOLOCK)
    WHERE e.Empresa = '1' AND e.Fecha >= @desde AND e.Fecha < @hasta
)
SELECT Grupo, COUNT(*) AS Productos, COUNT(DISTINCT Cliente) AS Clientes, SUM(Repuesto) AS Repuestos,
       CAST(100.0 * SUM(Repuesto) / NULLIF(COUNT(*), 0) AS decimal(5, 1)) AS PorcentajeRepuesto
FROM Recordados
GROUP BY Grupo
ORDER BY Grupo DESC;
-- Para ver si algún texto semanal funciona mejor: cambiar «GROUP BY Grupo» por «GROUP BY VersionTexto, Grupo» (y el SELECT).
