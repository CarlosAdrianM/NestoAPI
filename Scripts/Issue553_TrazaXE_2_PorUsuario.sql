-- NestoAPI#553, traza por USUARIO (06/10/26): qué hace Nesto viejo al CREAR una reposición, en los dos sentidos,
-- cuando la hace quien la hace de verdad:
--   · Algete → tienda: Andre (rellenar). El 05/10 vimos que llama a prdRellenarReposicionStock, pero no se llegó a
--     rellenar (había otra pendiente): falta ver el resto (contador, PreExtrProducto, prdUbicarReposicion…).
--   · Tienda → Algete: Paloma (Alcobendas). Casi seguro que es otro camino (devolución elegida a mano).
--
-- Se filtra por el LOGIN de esa persona (no por sesión): así se cazan todas sus conexiones, también la de los
-- informes, y no hace falta buscar el session_id. Ella trabaja como siempre; nada cambia en su pantalla.
--
-- Como sa, POR PASOS. No cambia ningún dato ni objeto de NV; la sesión se borra en el paso 4.

------------------------------------------------------------------------------------------------
-- PASO 1. Arrancar la traza. Poner el usuario (NUEVAVISION\Paloma o NUEVAVISION\Andre).
------------------------------------------------------------------------------------------------
DECLARE @Usuario nvarchar(128) = N'NUEVAVISION\Paloma';   -- <-- quien va a crear la reposición

IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue553_CrearReposicionPorUsuario')
    DROP EVENT SESSION [Issue553_CrearReposicionPorUsuario] ON SERVER;

DECLARE @filtro nvarchar(400) = N'WHERE sqlserver.server_principal_name = N''' + REPLACE(@Usuario, '''', '''''') + N'''';
DECLARE @acciones nvarchar(400) = N'ACTION (sqlserver.client_app_name, sqlserver.server_principal_name, sqlserver.session_id)';
DECLARE @sql nvarchar(max) = N'
CREATE EVENT SESSION [Issue553_CrearReposicionPorUsuario] ON SERVER
    ADD EVENT sqlserver.sql_batch_completed (' + @acciones + N' ' + @filtro + N'),
    ADD EVENT sqlserver.rpc_completed (' + @acciones + N' ' + @filtro + N'),
    ADD EVENT sqlserver.sp_statement_completed (SET collect_object_name = (1), collect_statement = (1) ' + @acciones + N' ' + @filtro + N')
    ADD TARGET package0.event_file (SET filename = N''Issue553_CrearReposicionPorUsuario.xel'', max_file_size = 50, max_rollover_files = 2)
    WITH (MAX_MEMORY = 4096 KB, EVENT_RETENTION_MODE = ALLOW_SINGLE_EVENT_LOSS, MAX_DISPATCH_LATENCY = 3 SECONDS, STARTUP_STATE = OFF);';
EXEC (@sql);
ALTER EVENT SESSION [Issue553_CrearReposicionPorUsuario] ON SERVER STATE = START;
PRINT 'Traza en marcha. Que cree la reposición en Nesto viejo como siempre (sin contabilizar si se puede) y avise al terminar.';
GO

------------------------------------------------------------------------------------------------
-- PASO 2. Parar la traza en cuanto diga que ha terminado (apuntar el número de traspaso).
------------------------------------------------------------------------------------------------
ALTER EVENT SESSION [Issue553_CrearReposicionPorUsuario] ON SERVER STATE = STOP;
GO

------------------------------------------------------------------------------------------------
-- PASO 3. Leer lo capturado. Primero el resumen (qué procedimientos han intervenido) y después el detalle SIN las
--         lecturas de parámetros y rejillas (ruido). Guardar los dos resultados (CSV) y pasárselos a Claude.
------------------------------------------------------------------------------------------------
;WITH eventos AS (
    SELECT CAST(f.event_data AS xml) AS x
    FROM sys.fn_xe_file_target_read_file(N'Issue553_CrearReposicionPorUsuario*.xel', NULL, NULL, NULL) f
), valores AS (
    SELECT x.value('(event/@name)[1]', 'nvarchar(60)')                            AS evento,
           x.value('(event/data[@name="object_name"]/value)[1]', 'nvarchar(200)') AS objeto
    FROM eventos
)
SELECT evento, objeto, COUNT(*) AS veces FROM valores GROUP BY evento, objeto ORDER BY veces DESC;

;WITH eventos AS (
    SELECT CAST(f.event_data AS xml) AS x
    FROM sys.fn_xe_file_target_read_file(N'Issue553_CrearReposicionPorUsuario*.xel', NULL, NULL, NULL) f
), v AS (
    SELECT x.value('(event/@timestamp)[1]', 'datetime2(3)')                                AS momento_utc,
           x.value('(event/@name)[1]', 'nvarchar(60)')                                    AS evento,
           x.value('(event/action[@name="client_app_name"]/value)[1]', 'nvarchar(200)')   AS programa,
           x.value('(event/action[@name="session_id"]/value)[1]', 'int')                  AS sesion,
           x.value('(event/data[@name="object_name"]/value)[1]', 'nvarchar(200)')         AS objeto,
           x.value('(event/data[@name="row_count"]/value)[1]', 'bigint')                  AS filas,
           COALESCE(x.value('(event/data[@name="batch_text"]/value)[1]', 'nvarchar(max)'),
                    x.value('(event/data[@name="statement"]/value)[1]', 'nvarchar(max)'))   AS texto
    FROM eventos
)
SELECT * FROM v
WHERE texto NOT LIKE N'%parámetrosusuario%' AND texto NOT LIKE N'%camposgrid%' AND texto NOT LIKE N'SET NO_BROWSETABLE%'
ORDER BY momento_utc;
GO

------------------------------------------------------------------------------------------------
-- PASO 4. Borrar la traza.
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue553_CrearReposicionPorUsuario')
    DROP EVENT SESSION [Issue553_CrearReposicionPorUsuario] ON SERVER;
GO
