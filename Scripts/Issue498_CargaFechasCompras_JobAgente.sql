/*
    NestoAPI#498 — resto de la carga inicial de fechas de compras a Odoo, FUERA DE HORARIO.

    El 23/09/26 se publicaron 3.150 de 5.569 clientes (compradores de los últimos 2 años). Las tandas
    de 500 suben la CPU de la API en horario (#521), así que el resto va en un job del Agente de SQL
    Server que arranca a las 18:00 y encola tandas de 500: cada tanda solo entra cuando la anterior
    está ENTERA publicada por el job 'sincronizar-clientes' (Hangfire, cada 5 min). Así nunca hay dos
    pasadas con trabajo a la vez. Termina solo cuando no quedan clientes o a las 21:00, lo que antes llegue.

    Ejecutar UNA vez en SSMS como sa (crea el job; no encola nada hasta las 18:00).
    Para cancelarlo antes: EXEC msdb.dbo.sp_delete_job @job_name = N'NestoAPI#498 carga fechas compras';
    Se borra solo al terminar (@delete_level = 1).
*/
USE msdb;
GO

IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'NestoAPI#498 carga fechas compras')
    EXEC msdb.dbo.sp_delete_job @job_name = N'NestoAPI#498 carga fechas compras';
GO

EXEC msdb.dbo.sp_add_job
    @job_name = N'NestoAPI#498 carga fechas compras',
    @description = N'Encola en Nesto_sync, en tandas de 500, los clientes con compras en los últimos 2 años que faltan (NestoAPI#498).',
    @delete_level = 1; -- se borra solo tras ejecutarse con éxito
GO

EXEC msdb.dbo.sp_add_jobstep
    @job_name = N'NestoAPI#498 carga fechas compras',
    @step_name = N'Tandas de 500',
    @subsystem = N'TSQL',
    @database_name = N'NV',
    @command = N'
SET NOCOUNT ON;
DECLARE @usuario varchar(50) = ''Carga fechas compras NestoAPI#498'';
DECLARE @tamano int = 500;
DECLARE @fin datetime = DATEADD(hour, 21, CAST(CAST(GETDATE() AS date) AS datetime)); -- 21:00 de hoy
DECLARE @encolados int = 1;

WHILE GETDATE() < @fin
BEGIN
    -- Esperar a que la tanda anterior esté entera publicada.
    IF EXISTS (SELECT 1 FROM Nesto_sync WITH (NOLOCK) WHERE Usuario = @usuario AND Sincronizado IS NULL)
    BEGIN
        WAITFOR DELAY ''00:01:00'';
        CONTINUE;
    END;

    INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
    SELECT TOP (@tamano) ''Clientes'', x.cliente, @usuario, GETDATE()
    FROM (SELECT DISTINCT LTRIM(RTRIM(c.[Nº Cliente])) cliente
          FROM CabPedidoVta c WITH (NOLOCK)
          WHERE c.[Nº Cliente] IS NOT NULL AND LTRIM(RTRIM(c.[Nº Cliente])) <> ''''
            AND c.Empresa IN (''1'',''3'') AND c.Fecha >= DATEADD(year, -2, GETDATE())) x
    WHERE NOT EXISTS (SELECT 1 FROM Nesto_sync s WITH (NOLOCK)
                      WHERE s.Tabla = ''Clientes'' AND s.ModificadoId = x.cliente
                        AND (s.Sincronizado IS NULL OR s.Usuario = @usuario))
    ORDER BY x.cliente;

    SET @encolados = @@ROWCOUNT;
    IF @encolados = 0 BREAK; -- no quedan clientes por encolar

    WAITFOR DELAY ''00:01:00'';
END;
';
GO

-- Hoy a las 18:00 (una sola vez).
DECLARE @hoy int = CONVERT(int, CONVERT(char(8), GETDATE(), 112));
EXEC msdb.dbo.sp_add_jobschedule
    @job_name = N'NestoAPI#498 carga fechas compras',
    @name = N'Hoy 18:00',
    @freq_type = 1,            -- una vez
    @active_start_date = @hoy,
    @active_start_time = 180000;
GO

EXEC msdb.dbo.sp_add_jobserver @job_name = N'NestoAPI#498 carga fechas compras';
GO

-- Comprobación: debe salir el job con la próxima ejecución hoy a las 18:00.
SELECT j.name, j.enabled, s.next_run_date, s.next_run_time
FROM msdb.dbo.sysjobs j
JOIN msdb.dbo.sysjobschedules s ON s.job_id = j.job_id
WHERE j.name = N'NestoAPI#498 carga fechas compras';

-- Seguimiento (mañana, o durante la tarde):
-- SELECT COUNT(*) total, SUM(CASE WHEN Sincronizado IS NULL THEN 1 ELSE 0 END) pendientes, MAX(Sincronizado) ultimo
-- FROM NV.dbo.Nesto_sync WITH (NOLOCK) WHERE Usuario = 'Carga fechas compras NestoAPI#498';   -- total esperado: 5.569
