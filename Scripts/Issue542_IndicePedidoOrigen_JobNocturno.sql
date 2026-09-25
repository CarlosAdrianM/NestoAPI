-- NestoAPI#542: programa en el Agente SQL la creación del índice filtrado IX_CabPedidoVta_PedidoOrigen para esta
-- madrugada (26/09/26 a las 02:30), cuando nadie graba pedidos. SQL Server Standard no crea índices ONLINE, así que
-- construirlo en horario bloquearía las escrituras en CabPedidoVta unos segundos (666.000 cabeceras, 359 MB).
--
-- Ejecutar como sa DESPUÉS de Issue542_ModoFacturacion.sql (el paso comprueba que existan las columnas).
-- Es de una sola vez: si termina bien, el trabajo se borra solo (@delete_level = 1). Si falla, se queda en el
-- Agente con el error en su historial para mirarlo. Idempotente: si el índice ya existe, no hace nada.
USE msdb;
GO

DECLARE @nombre sysname = N'NestoAPI#542 - Indice IX_CabPedidoVta_PedidoOrigen';
IF EXISTS (SELECT 1 FROM dbo.sysjobs WHERE name = @nombre)
    EXEC dbo.sp_delete_job @job_name = @nombre;

EXEC dbo.sp_add_job
    @job_name = @nombre,
    @enabled = 1,
    @description = N'NestoAPI#542: índice filtrado para encontrar la nota de entrega automática de un pedido. Una sola vez, de madrugada.',
    @delete_level = 1;  -- se borra solo si termina bien

EXEC dbo.sp_add_jobstep
    @job_name = @nombre,
    @step_name = N'Crear índice',
    @subsystem = N'TSQL',
    @database_name = N'NV',
    @command = N'SET LOCK_TIMEOUT 60000;
IF COL_LENGTH(''dbo.CabPedidoVta'', ''PedidoOrigen'') IS NULL OR COL_LENGTH(''dbo.CabPedidoVta'', ''AlbaranOrigen'') IS NULL
    RAISERROR(''Faltan las columnas de NestoAPI#542: ejecutar antes Issue542_ModoFacturacion.sql'', 16, 1);
ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(''dbo.CabPedidoVta'') AND name = ''IX_CabPedidoVta_PedidoOrigen'')
    CREATE NONCLUSTERED INDEX IX_CabPedidoVta_PedidoOrigen
        ON dbo.CabPedidoVta (Empresa, PedidoOrigen, AlbaranOrigen)
        WHERE PedidoOrigen IS NOT NULL;',
    @retry_attempts = 2,
    @retry_interval = 10;  -- si choca con algo (p. ej. un proceso nocturno), reintenta a los 10 minutos

EXEC dbo.sp_add_jobschedule
    @job_name = @nombre,
    @name = N'Una vez, 26/09/26 02:30',
    @freq_type = 1,              -- una sola vez
    @active_start_date = 20260926,
    @active_start_time = 023000;

EXEC dbo.sp_add_jobserver @job_name = @nombre, @server_name = N'(local)';
GO

-- Comprobación: el trabajo programado
SELECT j.name, j.enabled, s.next_run_date, s.next_run_time
FROM msdb.dbo.sysjobs j
JOIN msdb.dbo.sysjobschedules s ON s.job_id = j.job_id
WHERE j.name = N'NestoAPI#542 - Indice IX_CabPedidoVta_PedidoOrigen';

-- Mañana, para confirmar que se creó (el trabajo ya no estará: se borra al terminar bien):
-- SELECT name, filter_definition FROM NV.sys.indexes WHERE name = 'IX_CabPedidoVta_PedidoOrigen';
