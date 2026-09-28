-- Precios medios, PASO 0 (28/09/26). Ejecutar como sa. Sin cambiar código: el trabajo «Precios Medios» pasa del
-- domingo a las 07:10 (terminaba ~12:10, con usuarios y clientes de la tienda conectados) al domingo a las 00:30, para
-- que termine de madrugada aunque siga tardando lo mismo. Los demás trabajos nocturnos (copias 20:30/20:50, cuadres
-- 20:25, estados de productos 20:00) terminan antes de medianoche.
-- Vuelta atrás: el mismo sp_update_schedule con @active_start_time = 071000.
USE msdb;
GO
DECLARE @schedule_id int = (SELECT s.schedule_id FROM dbo.sysschedules s
    JOIN dbo.sysjobschedules js ON js.schedule_id = s.schedule_id
    JOIN dbo.sysjobs j ON j.job_id = js.job_id
    WHERE j.name = N'Precios Medios' AND s.name = N'Domingo');
IF @schedule_id IS NULL
    RAISERROR('No encuentro la programación «Domingo» del trabajo «Precios Medios».', 16, 1);
ELSE
    EXEC dbo.sp_update_schedule @schedule_id = @schedule_id, @active_start_time = 003000;
GO
-- Comprobación: next_run_time debe ser 3000 (00:30) del domingo 04/10
SELECT j.name, s.name, s.active_start_time, js.next_run_date, js.next_run_time
FROM dbo.sysjobs j JOIN dbo.sysjobschedules js ON js.job_id = j.job_id JOIN dbo.sysschedules s ON s.schedule_id = js.schedule_id
WHERE j.name = N'Precios Medios';
