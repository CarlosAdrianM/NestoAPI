-- NestoAPI#451: el job del SQL Agent "Primer dia del mes" (DC2016, día 1 a las 7:20) aparece en
-- rojo cada mes desde marzo de 2025: "JobManager tried to run a non-existent step (2)". El único
-- paso (exec prdAbrirFechasVenta) termina bien, pero su acción de éxito es "ir al paso siguiente"
-- (on_success_action = 3) y no hay paso 2.
--
-- Ejecutar en SSMS como sa contra DC2016\SQL2017 (msdb). Solo cambia la acción de éxito del paso 1
-- a "salir del trabajo notificando éxito" (1). Idempotente.

USE msdb;
GO

DECLARE @job_id uniqueidentifier = (SELECT job_id FROM dbo.sysjobs WHERE name = N'Primer dia del mes');
IF @job_id IS NULL
BEGIN
    RAISERROR('No existe el job "Primer dia del mes"', 16, 1);
    RETURN;
END

EXEC dbo.sp_update_jobstep
    @job_id = @job_id,
    @step_id = 1,
    @on_success_action = 1,     -- Salir notificando éxito
    @on_success_step_id = 0;
GO

-- Comprobación
SELECT s.step_id, s.step_name, s.on_success_action, s.on_success_step_id,
       CASE WHEN s.on_success_action = 1 THEN 'OK' ELSE 'SIN CAMBIAR' END AS estado
FROM dbo.sysjobs j JOIN dbo.sysjobsteps s ON s.job_id = j.job_id
WHERE j.name = N'Primer dia del mes';
GO

-- Verificar el 01/10/26: en sysjobhistory la fila step_id = 0 del job con run_status = 1.
