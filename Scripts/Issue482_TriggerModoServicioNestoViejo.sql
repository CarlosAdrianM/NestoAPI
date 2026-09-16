-- NestoAPI#482 (16/09/26): el Nesto viejo (VB6) escribe CabPedidoVta.ServirJunto directamente, sin
-- conocer ModoServicio (pedidos 926269 y 926309: modo 1 con ServirJunto = 0). La API ya lee con la regla
-- «ServirJunto marcado = todo junto; desmarcado = modo parcial guardado (3/4) o 2», así que se sirven
-- bien; este trigger deja además la COLUMNA coherente cuando alguien toca ServirJunto sin tocar el modo.
-- La API actualiza las dos columnas en la misma sentencia (EF manda todas), así que no entra aquí.
-- Ejecutar en SSMS como sa (nuevavision no tiene ALTER). Idempotente.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.trgCabPedidoVtaModoServicio', 'TR') IS NOT NULL
    DROP TRIGGER dbo.trgCabPedidoVtaModoServicio;
GO
CREATE TRIGGER dbo.trgCabPedidoVtaModoServicio ON dbo.CabPedidoVta
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(ServirJunto) AND NOT UPDATE(ModoServicio)
    BEGIN
        UPDATE c
        SET ModoServicio = CASE
                WHEN i.ServirJunto = 1 THEN 1
                WHEN i.ModoServicio IN (3, 4) THEN i.ModoServicio
                ELSE 2
            END
        FROM dbo.CabPedidoVta c
            INNER JOIN inserted i ON i.Empresa = c.Empresa AND i.Número = c.Número
            INNER JOIN deleted d ON d.Empresa = i.Empresa AND d.Número = i.Número
        WHERE i.ServirJunto <> d.ServirJunto
          AND ISNULL(i.ModoServicio, 0) <> CASE
                WHEN i.ServirJunto = 1 THEN 1
                WHEN i.ModoServicio IN (3, 4) THEN i.ModoServicio
                ELSE 2
            END;
    END
END
GO
-- One-shot: los pedidos vivos que ya quedaron contradictorios antes del trigger (modo 1 desmarcado).
UPDATE dbo.CabPedidoVta SET ModoServicio = 2
WHERE ModoServicio = 1 AND ServirJunto = 0;
-- (No hay ningún modo 2/3/4 con ServirJunto = 1 al 16/09/26; si los hubiera, la regla los serviría como 1.)
SELECT ISNULL(CAST(ModoServicio AS varchar), 'NULL') AS Modo, ServirJunto, COUNT(*) AS Pedidos
FROM dbo.CabPedidoVta c
WHERE EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WHERE l.Empresa = c.Empresa AND l.Número = c.Número AND l.Estado BETWEEN -1 AND 1)
GROUP BY ModoServicio, ServirJunto ORDER BY 1, 2;
GO
