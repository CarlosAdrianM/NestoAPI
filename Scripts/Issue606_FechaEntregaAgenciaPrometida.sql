/*
    NestoAPI#606 (corte 1): CabPedidoVta.FechaEntregaAgenciaPrometida, la fecha de entrega a la agencia que se le dio al
    usuario al CREAR el pedido.

    Para qué: la plantilla y el detalle del pedido enseñan una fecha CALCULADA (GET/POST api/PedidosVenta/.../
    FechaEntregaAgencia) que se mueve con el stock, las reposiciones y los cambios del pedido. Esta columna guarda la que
    se prometió al crearlo y no se vuelve a tocar; la real está en el albarán. Con las tres se mide si acertamos.

    Quién la escribe: SOLO PostPedidoVenta (Nesto, NestoApp y TiendasNuevaVision vía PedidosClienteController), una vez,
    con un UPDATE ... WHERE FechaEntregaAgenciaPrometida IS NULL justo después de grabar el pedido. Los presupuestos no.
    NULL = pedido anterior a la columna, presupuesto, o pedido que nació sin fecha (algo sin stock ni fecha de llegada).

    NO está en el EDMX a propósito: la API la lee y la escribe con SQL parametrizado. Si este script aún no se ha lanzado,
    crear pedidos sigue funcionando (el UPDATE falla, se traga y va a ELMAH como mucho una vez cada media hora) y el GET
    devuelve FechaPrometida = null. Por eso puede lanzarse ANTES o DESPUÉS del deploy de la API.

    POR QUÉ ES INSTANTÁNEO: ADD de una columna que admite NULL y SIN DEFAULT es un cambio solo de metadatos (las filas
    existentes no se tocan y se leen como NULL). Necesita un Sch-M sobre CabPedidoVta un instante: por eso va con
    SET LOCK_TIMEOUT (si en 5 s no lo consigue, error 1222, no se ha hecho nada y se vuelve a lanzar). Mejor fuera de las
    horas de más pedidos (antes de las 8:00 o a mediodía).

    Comprobado el 08/10/26 contra NV (solo lectura):
      - Triggers de CabPedidoVta (trgCabPedidoVtaUpd, trgCabPedidoVtaIns, trgCabPedidoVtaModoServicio,
        trgCabPedidoVtaModoFacturacion): todo lo que hacen en UPDATE va dentro de IF UPDATE(<otra columna>); un UPDATE que
        solo toca esta columna no dispara nada.
      - Ningún procedimiento ni trigger hace INSERT en CabPedidoVta sin lista de columnas ni «INSERT … SELECT *» desde
        ella. Las vistas con SELECT * (nadaPicking, vstAgruparAlbaranesVta, vstCabPedidoVtaSinLineas) no la verán hasta un
        sp_refreshview: no hace falta, nadie la necesita en una vista. prdModificarComentariosPicking devuelve un
        SELECT * (una columna más en el resultado; quien lo lee, lo lee por nombre).
      - EF (NestoAPI y Nesto) inserta con la lista de columnas mapeadas: la columna nueva queda a NULL y la escribe el
        UPDATE de después.

    Ejecutar en SSMS contra NV como sa (el login nuevavision no tiene ALTER). Idempotente.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas. No se crea ningún índice.
*/

SET NOCOUNT ON;
USE NV;
GO

SET LOCK_TIMEOUT 5000;  -- si en 5 s no consigue el Sch-M, error 1222 y no se ha hecho nada: volver a lanzar
GO

IF COL_LENGTH('dbo.CabPedidoVta', 'FechaEntregaAgenciaPrometida') IS NULL
BEGIN
    -- Sin DEFAULT y admitiendo NULL: solo metadatos
    ALTER TABLE dbo.CabPedidoVta
        ADD FechaEntregaAgenciaPrometida datetime NULL;
END
GO

SET LOCK_TIMEOUT -1;
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
--   1) La columna existe y admite NULL.
--   2) Tras el primer pedido creado con la API nueva, lleva su fecha (día sin hora).
------------------------------------------------------------------------------------------------
SELECT c.name, t.name AS tipo, c.is_nullable
FROM sys.columns c
     JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.CabPedidoVta') AND c.name = 'FechaEntregaAgenciaPrometida';

SELECT TOP 10 Empresa, Número, Fecha, ModoServicio, ModoFacturacion, Ruta, FechaEntregaAgenciaPrometida
FROM dbo.CabPedidoVta WITH (NOLOCK)
ORDER BY Número DESC;
GO
