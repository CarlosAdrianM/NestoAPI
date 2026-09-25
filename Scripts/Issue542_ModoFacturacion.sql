-- NestoAPI#542 (corte 1): modo de facturación del pedido y enlace de la nota de entrega con su pedido.
--
-- MantenerJunto (bit) se queda corto: hacen falta tres modos (1 por entregas, 2 al completar el
-- pedido, 3 todo ahora y lo pendiente después en nota de entrega). MantenerJunto SE MANTIENE y sigue
-- siendo coherente (modo 2 = 1), igual que se hizo con ServirJunto/ModoServicio en #482.
--
-- La columna es NULL a propósito: NULL = "no informado", y entonces manda MantenerJunto (1 → modo 2,
-- 0 → modo 1). El ALTER es instantáneo (sin reescribir CabPedidoVta), no hace falta backfill, y los
-- escritores que no conocen la columna (SPs, Nesto viejo, NestoApp) siguen exactamente igual.
--
-- PedidoOrigen: en la nota de entrega que se cree automáticamente (corte 2), el pedido del que sale.
-- Es lo que evita crearla dos veces y lo que permite navegar nota ↔ pedido desde Nesto.
--
-- ⚠️ EJECUTAR COMO sa EN SSMS ANTES de publicar la API: el EDMX ya mapea las dos columnas y sin
-- ellas cualquier lectura de CabPedidoVta se cae. El índice filtrado recorre la tabla una vez
-- (unos segundos): mejor fuera de horario.

ALTER TABLE dbo.CabPedidoVta ADD ModoFacturacion tinyint NULL;
GO
ALTER TABLE dbo.CabPedidoVta ADD PedidoOrigen int NULL;
GO

ALTER TABLE dbo.CabPedidoVta WITH CHECK
    ADD CONSTRAINT CK_CabPedidoVta_ModoFacturacion CHECK (ModoFacturacion IS NULL OR ModoFacturacion BETWEEN 1 AND 3);
GO

-- Solo las notas de entrega automáticas lo tienen informado: el índice filtrado es diminuto.
CREATE NONCLUSTERED INDEX IX_CabPedidoVta_PedidoOrigen
    ON dbo.CabPedidoVta (Empresa, PedidoOrigen)
    WHERE PedidoOrigen IS NOT NULL;
GO

-- Coherencia cuando alguien cambia SOLO MantenerJunto sin conocer el modo (Nesto viejo, y sobre todo
-- los triggers trgCabPedidoVtaIns/Upd, que ponen MantenerJunto = 1 al insertar o cambiar unos plazos
-- de pago que no son los de la ficha, salvo contado/CR y FDM):
--  · modo 1 o 2 guardado: sigue al bit (1 → 2, 0 → 1), como hace trgCabPedidoVtaModoServicio.
--  · modo 3 guardado: es una elección explícita del usuario y factura todo de una vez, así que la red
--    de los plazos no hace falta; el bit vuelve a 0 y el 3 se conserva. Sin esto, un pedido nuevo en
--    modo 3 con plazos especiales nacería con MantenerJunto = 1 y se comportaría como un 2.
--  · NULL: no hay nada que mantener coherente (manda el bit).
-- La API escribe siempre las dos columnas a la vez (UPDATE(ModoFacturacion) = 1) y el trigger no actúa.
IF OBJECT_ID('dbo.trgCabPedidoVtaModoFacturacion', 'TR') IS NOT NULL
    DROP TRIGGER dbo.trgCabPedidoVtaModoFacturacion;
GO
CREATE TRIGGER dbo.trgCabPedidoVtaModoFacturacion ON dbo.CabPedidoVta
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(MantenerJunto) AND NOT UPDATE(ModoFacturacion)
    BEGIN
        UPDATE c SET ModoFacturacion = CASE WHEN i.MantenerJunto = 1 THEN 2 ELSE 1 END
        FROM dbo.CabPedidoVta c
            INNER JOIN inserted i ON i.Empresa = c.Empresa AND i.Número = c.Número
            INNER JOIN deleted d ON d.Empresa = i.Empresa AND d.Número = i.Número
        WHERE i.MantenerJunto <> d.MantenerJunto
          AND i.ModoFacturacion IN (1, 2)
          AND i.ModoFacturacion <> CASE WHEN i.MantenerJunto = 1 THEN 2 ELSE 1 END;

        UPDATE c SET MantenerJunto = 0
        FROM dbo.CabPedidoVta c
            INNER JOIN inserted i ON i.Empresa = c.Empresa AND i.Número = c.Número
            INNER JOIN deleted d ON d.Empresa = i.Empresa AND d.Número = i.Número
        WHERE i.MantenerJunto = 1 AND d.MantenerJunto = 0
          AND i.ModoFacturacion = 3;
    END
END
GO

-- Comprobación: todo NULL al principio
SELECT ModoFacturacion, COUNT(*) AS Pedidos FROM dbo.CabPedidoVta GROUP BY ModoFacturacion;
GO
