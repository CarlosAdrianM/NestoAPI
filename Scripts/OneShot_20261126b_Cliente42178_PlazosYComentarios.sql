/*
    Carlos 26/11/26: lo que quedaba por copiar del 37283 al 42178 (cambio de forma fiscal): los plazos de pago (1/30, como el
    anterior; el nuevo nació al contado) y los comentarios de la ficha. No toca pedidos (el 931691 se queda al contado, como se
    hizo). Lanzar como sa en la BD NV. Se puede lanzar más de una vez.
*/
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRANSACTION;
-- 1) Plazos de pago como el anterior (misma forma de pago: recibo)
UPDATE n SET n.PlazosPago = v.PlazosPago
FROM CondPagoClientes n
INNER JOIN CondPagoClientes v ON v.Empresa = n.Empresa AND v.[Nº Cliente] = '37283' AND v.Contacto = n.Contacto AND v.ImporteMínimo = n.ImporteMínimo
WHERE n.Empresa = '1' AND n.[Nº Cliente] = '42178' AND n.Contacto = '0' AND RTRIM(n.FormaPago) = RTRIM(v.FormaPago) AND RTRIM(n.PlazosPago) <> RTRIM(v.PlazosPago);
PRINT 'Condiciones de pago cambiadas: ' + CAST(@@ROWCOUNT AS varchar(10));
-- 2) Comentarios de la ficha (solo si el nuevo no tiene)
UPDATE n SET n.Comentarios = v.Comentarios
FROM Clientes n
INNER JOIN Clientes v ON v.Empresa = n.Empresa AND v.[Nº Cliente] = '37283' AND v.Contacto = '0'
WHERE n.Empresa = '1' AND n.[Nº Cliente] = '42178' AND n.Contacto = '0' AND NULLIF(RTRIM(CAST(n.Comentarios AS nvarchar(max))), '') IS NULL;
PRINT 'Fichas con los comentarios copiados: ' + CAST(@@ROWCOUNT AS varchar(10));
COMMIT TRANSACTION;
GO
SELECT RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(p.FormaPago) AS FormaPago, RTRIM(p.PlazosPago) AS Plazos, CAST(c.Comentarios AS nvarchar(300)) AS Comentarios
FROM Clientes c LEFT JOIN CondPagoClientes p ON p.Empresa = c.Empresa AND p.[Nº Cliente] = c.[Nº Cliente] AND p.Contacto = c.Contacto
WHERE c.Empresa = '1' AND c.[Nº Cliente] IN ('37283', '42178') AND c.Contacto = '0';
GO
