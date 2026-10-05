-- NestoAPI#567, 2.ª traza (05/10/26): qué consulta hace CRYSTAL al imprimir una factura desde Nesto viejo.
--
-- La 1.ª traza se filtró por la sesión de Nesto viejo y no salió ninguna consulta que lea las líneas completas:
-- el informe (F:\NVERP\INFORMES\facturas.rpt) abre su PROPIA conexión. Esta se filtra por el USUARIO (Carlos),
-- dejando fuera SSMS, para cazar esa conexión: qué tablas lee y con qué nombre de programa se conecta.
--
-- Como sa, POR PASOS. No cambia ningún dato ni objeto de NV; la sesión se borra en el paso 5.
-- Mientras la traza está en marcha, no uses SSMS para otra cosa con tu usuario Windows (sí con sa, que no entra).

------------------------------------------------------------------------------------------------
-- PASO 1. Crear y arrancar la traza (Nesto viejo abierto, sin imprimir todavía).
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue567_ConexionCrystal')
    DROP EVENT SESSION [Issue567_ConexionCrystal] ON SERVER;
GO
CREATE EVENT SESSION [Issue567_ConexionCrystal] ON SERVER
    ADD EVENT sqlserver.sql_batch_completed (
        ACTION (sqlserver.client_app_name, sqlserver.client_hostname, sqlserver.server_principal_name, sqlserver.session_id)
        WHERE sqlserver.server_principal_name = N'NUEVAVISION\Carlos'
          AND NOT sqlserver.like_i_sql_unicode_string(sqlserver.client_app_name, N'%Management Studio%')),
    ADD EVENT sqlserver.rpc_completed (
        ACTION (sqlserver.client_app_name, sqlserver.client_hostname, sqlserver.server_principal_name, sqlserver.session_id)
        WHERE sqlserver.server_principal_name = N'NUEVAVISION\Carlos'
          AND NOT sqlserver.like_i_sql_unicode_string(sqlserver.client_app_name, N'%Management Studio%')),
    -- Conexiones nuevas: así se ve con qué nombre de programa entra Crystal aunque no lance nada raro
    ADD EVENT sqlserver.login (
        ACTION (sqlserver.client_app_name, sqlserver.client_hostname, sqlserver.server_principal_name, sqlserver.session_id)
        WHERE sqlserver.server_principal_name = N'NUEVAVISION\Carlos')
    ADD TARGET package0.event_file (SET filename = N'Issue567_ConexionCrystal.xel', max_file_size = 20, max_rollover_files = 2)
    WITH (MAX_MEMORY = 4096 KB, EVENT_RETENTION_MODE = ALLOW_SINGLE_EVENT_LOSS, MAX_DISPATCH_LATENCY = 3 SECONDS, STARTUP_STATE = OFF);
GO
ALTER EVENT SESSION [Issue567_ConexionCrystal] ON SERVER STATE = START;
PRINT 'Traza en marcha. Ahora: imprimir desde Nesto viejo la factura NV2616038 (una sola vez).';
GO

------------------------------------------------------------------------------------------------
-- PASO 2. En Nesto viejo: imprimir (o previsualizar) la NV2616038. Nada más.
------------------------------------------------------------------------------------------------

------------------------------------------------------------------------------------------------
-- PASO 3. Parar la traza.
------------------------------------------------------------------------------------------------
ALTER EVENT SESSION [Issue567_ConexionCrystal] ON SERVER STATE = STOP;
GO

------------------------------------------------------------------------------------------------
-- PASO 4. Leer lo capturado. Lo importante: las filas cuyo programa NO es «Nesto» (la conexión de Crystal).
------------------------------------------------------------------------------------------------
;WITH eventos AS (
    SELECT CAST(f.event_data AS xml) AS x
    FROM sys.fn_xe_file_target_read_file(N'Issue567_ConexionCrystal*.xel', NULL, NULL, NULL) f
)
SELECT x.value('(event/@timestamp)[1]', 'datetime2(3)')                                        AS momento_utc,
       x.value('(event/@name)[1]', 'nvarchar(60)')                                            AS evento,
       x.value('(event/action[@name="client_app_name"]/value)[1]', 'nvarchar(200)')           AS programa,
       x.value('(event/action[@name="session_id"]/value)[1]', 'int')                          AS sesion,
       x.value('(event/data[@name="row_count"]/value)[1]', 'bigint')                          AS filas,
       COALESCE(x.value('(event/data[@name="batch_text"]/value)[1]', 'nvarchar(max)'),
                x.value('(event/data[@name="statement"]/value)[1]', 'nvarchar(max)'))         AS texto
FROM eventos
ORDER BY momento_utc;
GO

------------------------------------------------------------------------------------------------
-- PASO 5. Borrar la traza.
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue567_ConexionCrystal')
    DROP EVENT SESSION [Issue567_ConexionCrystal] ON SERVER;
GO
