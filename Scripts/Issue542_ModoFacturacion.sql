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
-- ⚠️ EJECUTAR COMO sa EN SSMS ANTES de publicar la API: el EDMX ya mapea las columnas y sin
-- ellas cualquier lectura de CabPedidoVta se cae.
--
-- Se puede lanzar en horario (25/09/26): añadir columnas que admiten NULL es solo metadatos (no se
-- reescribe la tabla), y el CHECK va WITH NOCHECK (las columnas son nuevas y están todas a NULL: no hay
-- nada que validar, y así no recorre las 666.000 cabeceras con la tabla bloqueada). Cada ALTER necesita
-- un instante de bloqueo exclusivo de esquema: con LOCK_TIMEOUT, si hay una consulta larga sobre la
-- tabla, el ALTER falla a los 5 s en vez de quedarse esperando y dejar a todos en cola detrás. Si falla,
-- volver a lanzarlo un poco después (es idempotente). El índice filtrado sí recorre la tabla: va aparte,
-- al final, para lanzarlo fuera de horario (no hace falta para publicar).

SET LOCK_TIMEOUT 5000;
GO

IF COL_LENGTH('dbo.CabPedidoVta', 'ModoFacturacion') IS NULL
    ALTER TABLE dbo.CabPedidoVta ADD ModoFacturacion tinyint NULL;
GO
IF COL_LENGTH('dbo.CabPedidoVta', 'PedidoOrigen') IS NULL
    ALTER TABLE dbo.CabPedidoVta ADD PedidoOrigen int NULL;
GO
-- El albarán de ese pedido del que sale lo pendiente: un pedido puede dar varios albaranes con Recoger
-- (modo de servicio «según vaya entrando»), y (PedidoOrigen, AlbaranOrigen) es lo que hace única la nota.
IF COL_LENGTH('dbo.CabPedidoVta', 'AlbaranOrigen') IS NULL
    ALTER TABLE dbo.CabPedidoVta ADD AlbaranOrigen int NULL;
GO

IF OBJECT_ID('dbo.CK_CabPedidoVta_ModoFacturacion', 'C') IS NULL
    ALTER TABLE dbo.CabPedidoVta WITH NOCHECK
        ADD CONSTRAINT CK_CabPedidoVta_ModoFacturacion CHECK (ModoFacturacion IS NULL OR ModoFacturacion BETWEEN 1 AND 3);
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

-- Comprobación (solo metadatos, no recorre la tabla): las tres columnas, el CHECK y el trigger
SELECT c.name AS Columna, TYPE_NAME(c.user_type_id) AS Tipo, c.is_nullable AS AdmiteNull
FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.CabPedidoVta') AND c.name IN ('ModoFacturacion', 'PedidoOrigen', 'AlbaranOrigen');
SELECT name FROM sys.objects WHERE name IN ('CK_CabPedidoVta_ModoFacturacion', 'trgCabPedidoVtaModoFacturacion');
GO

-- ============================================================================================
-- FUERA DE HORARIO (no hace falta para publicar): índice para encontrar la nota de entrega de un pedido.
-- Recorre CabPedidoVta una vez (unos segundos, bloqueando escrituras mientras se construye). Mientras no
-- exista, la comprobación de «ya hay nota para este albarán» funciona igual, solo que más lenta.
-- CREATE NONCLUSTERED INDEX IX_CabPedidoVta_PedidoOrigen
--     ON dbo.CabPedidoVta (Empresa, PedidoOrigen, AlbaranOrigen)
--     WHERE PedidoOrigen IS NOT NULL;

-- ============================================================================================
-- Corte 2: nota de entrega automática (NestoAPI#542). NACE APAGADA: sin fila = apagado.
-- Actúa sobre CUALQUIER albarán con líneas con Recoger, también los que se ponen a mano desde el Nesto
-- viejo (unos 100 pedidos al año), así que antes de encenderla hay que avisar a almacén (Alfredo) para
-- que no haga la nota a mano y salga duplicada. Nunca es retroactiva: solo albaranes creados después.
--
-- Paso 1, SOMBRA (solo deja en ELMAH la nota que habría creado; comparar con las que hace Alfredo):
-- IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NotaEntregaAutomatica')
--     INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
--     VALUES ('1', '(defecto)', 'NotaEntregaAutomatica', 'Sombra', 'NestoAPI#542', GETDATE());
-- ELSE
--     UPDATE dbo.ParámetrosUsuario SET Valor = 'Sombra', Usuario2 = 'NestoAPI#542', [Fecha Modificación] = GETDATE()
--     WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NotaEntregaAutomatica';
--
-- Paso 2, ENCENDER (crea la nota): mismo UPDATE con Valor = '1'.  Apagar: Valor = '0'.
-- Diagnóstico de las notas creadas:
-- SELECT Número, Fecha, PedidoOrigen, AlbaranOrigen, Usuario FROM dbo.CabPedidoVta WHERE PedidoOrigen IS NOT NULL ORDER BY Número DESC;
