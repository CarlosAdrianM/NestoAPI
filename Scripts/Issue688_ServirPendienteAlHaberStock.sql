/*
    NestoAPI#688 (novedad 715, Marta): «¿Servir lo pendiente en cuanto haya stock?» en los pedidos con facturación
    «Todo ahora» (ModoFacturacion = 3), y aviso «lo pendiente ya está completo».

    Dos columnas nuevas en CabPedidoVta:
      - ServirPendienteAlHaberStock bit NULL
            En el PEDIDO: la casilla. NULL = como hasta ahora (sin marcar): los pedidos existentes no se tocan.
            En la NOTA DE ENTREGA automática (#542): copiada del pedido. Marcada = la nota nace con fecha y sale todo
            junto en cuanto haya stock; sin marcar = nace sin fecha (31/12/2099) y se avisa cuando todo tenga stock.
      - AvisoPendienteCompleto datetime NULL
            En la NOTA: cuándo se avisó (buzón + campana a quien hizo el pedido, push al vendedor) de que todo lo pendiente
            ya tiene stock. NULL = no se ha avisado. El job horario «aviso-pendiente-completo» solo mira las que están a NULL.

    NO están en el EDMX a propósito (mismo patrón que FechaEntregaAgenciaPrometida, #606): la API las lee y escribe con
    SQL parametrizado y comprueba antes que existen (COL_LENGTH). Si este script aún no se ha lanzado, la API funciona
    igual que antes: la casilla se lee como sin marcar y no se guarda, la nota nace sin fecha como hoy y el job no hace
    nada. Por eso puede lanzarse ANTES o DESPUÉS del deploy de la API (mejor antes, para que la casilla se guarde desde
    el primer pedido).

    POR QUÉ ES INSTANTÁNEO: ADD de columnas que admiten NULL y SIN DEFAULT es un cambio solo de metadatos (las filas
    existentes no se tocan y se leen como NULL). Necesita un Sch-M sobre CabPedidoVta un instante: por eso va con
    SET LOCK_TIMEOUT (si en 5 s no lo consigue, error 1222, no se ha hecho nada y se vuelve a lanzar). Mejor fuera de las
    horas de más pedidos (antes de las 8:00 o a mediodía).

    Triggers y SPs (comprobado para #606 el 08/10/26, mismo caso): lo que hacen los triggers de CabPedidoVta en UPDATE va
    dentro de IF UPDATE(<otra columna>); nadie inserta en CabPedidoVta sin lista de columnas; EF (NestoAPI y Nesto) inserta
    con la lista de columnas mapeadas, así que las columnas nuevas quedan a NULL y las escribe un UPDATE de después.

    Sin GRANT: ALTER TABLE ... ADD no lo necesita (la API ya lee y escribe CabPedidoVta).

    Ejecutar en SSMS contra NV como sa (el login nuevavision no tiene ALTER). Idempotente.
    Sin índices: el job filtra por las líneas con fecha 31/12/2099 de las notas (pocas decenas).
*/

SET NOCOUNT ON;
USE NV;
GO

SET LOCK_TIMEOUT 5000;  -- si en 5 s no consigue el Sch-M, error 1222 y no se ha hecho nada: volver a lanzar
GO

IF COL_LENGTH('dbo.CabPedidoVta', 'ServirPendienteAlHaberStock') IS NULL
BEGIN
    -- Sin DEFAULT y admitiendo NULL: solo metadatos
    ALTER TABLE dbo.CabPedidoVta
        ADD ServirPendienteAlHaberStock bit NULL;
END
GO

IF COL_LENGTH('dbo.CabPedidoVta', 'AvisoPendienteCompleto') IS NULL
BEGIN
    ALTER TABLE dbo.CabPedidoVta
        ADD AvisoPendienteCompleto datetime NULL;
END
GO

SET LOCK_TIMEOUT -1;
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
--   1) Las dos columnas existen y admiten NULL.
--   2) Tras el primer pedido «Todo ahora» creado con la API nueva, lleva ServirPendienteAlHaberStock = 1 (o 0 si se
--      desmarcó), y su nota de entrega automática la copia.
------------------------------------------------------------------------------------------------
SELECT c.name, t.name AS tipo, c.is_nullable
FROM sys.columns c
     JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.CabPedidoVta') AND c.name IN ('ServirPendienteAlHaberStock', 'AvisoPendienteCompleto');

SELECT TOP 20 Empresa, Número, Fecha, NotaEntrega, ModoFacturacion, PedidoOrigen, ServirPendienteAlHaberStock, AvisoPendienteCompleto
FROM dbo.CabPedidoVta WITH (NOLOCK)
WHERE ModoFacturacion = 3 OR PedidoOrigen IS NOT NULL
ORDER BY Número DESC;
GO
