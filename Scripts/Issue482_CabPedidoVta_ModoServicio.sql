-- NestoAPI#482 (slice 1): modo de servicio del pedido. ServirJunto (bit) se queda corto: hacen
-- falta cuatro modos (1 todo junto, 2 según vaya entrando, 3 tras reponer de tiendas, 4 ahora lo
-- que hay y el resto de una vez). ServirJunto SE MANTIENE y sigue siendo coherente (modo 1 = 1).
--
-- La columna es NULL a propósito: NULL = "no informado", y entonces manda ServirJunto (1 → modo 1,
-- 0 → modo 2). Así el ALTER es instantáneo (sin reescribir ~1M filas de CabPedidoVta), no hace
-- falta backfill, y cualquier escritor que no conozca la columna (SPs, Nesto viejo, la app) sigue
-- funcionando exactamente igual que hoy. Solo los clientes que mandan modoServicio la rellenan.
--
-- ⚠️ EJECUTAR COMO sa EN SSMS ANTES de publicar la API: el EDMX ya mapea la columna y sin ella
-- cualquier lectura de CabPedidoVta se cae.

ALTER TABLE dbo.CabPedidoVta ADD ModoServicio tinyint NULL;
GO

ALTER TABLE dbo.CabPedidoVta WITH CHECK
    ADD CONSTRAINT CK_CabPedidoVta_ModoServicio CHECK (ModoServicio IS NULL OR ModoServicio BETWEEN 1 AND 4);
GO

-- Comprobación: todo NULL al principio
SELECT ModoServicio, COUNT(*) AS Pedidos FROM dbo.CabPedidoVta GROUP BY ModoServicio;
GO
