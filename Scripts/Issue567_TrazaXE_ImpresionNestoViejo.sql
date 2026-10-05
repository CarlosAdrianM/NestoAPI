-- NestoAPI#567: traza para saber QUÉ lee Nesto viejo cuando imprime una factura.
--
-- Con el resultado se decide cómo impedir que imprima las facturas creadas en Nesto: si pasa por
-- un procedimiento o una función, una guarda de SYSTEM_USER como la de prdCrearFacturaVta; si lee
-- las tablas directamente, seguridad por filas (RLS) u otra vía.
--
-- La traza se filtra por SESIÓN (la de Nesto viejo), no por usuario: con el mismo login están
-- abiertos SSMS y Nesto, y ensuciarían la captura.
--
-- Hay que lanzarlo como `sa` (crear una sesión de Extended Events pide ALTER ANY EVENT SESSION).
-- Se ejecuta POR PASOS, seleccionando cada bloque: no es un script para lanzar entero.
-- No cambia ningún dato ni ningún objeto de la base de datos NV; la sesión se borra en el paso 6.

------------------------------------------------------------------------------------------------
-- PASO 1. Con Nesto viejo ya abierto (sin imprimir todavía): localizar su sesión.
--         Quedarse con el session_id de la fila cuyo program_name sea el de Nesto viejo.
------------------------------------------------------------------------------------------------
SELECT s.session_id, s.login_name, s.program_name, s.host_name, s.login_time, s.last_request_end_time,
       DB_NAME(s.database_id) AS base_de_datos
FROM sys.dm_exec_sessions s
WHERE s.is_user_process = 1
  AND s.login_name = N'NUEVAVISION\Carlos'      -- <-- el login con el que está abierto Nesto viejo
ORDER BY s.login_time;
GO

------------------------------------------------------------------------------------------------
-- PASO 2. Crear y arrancar la traza para esa sesión. Poner el session_id del paso 1.
------------------------------------------------------------------------------------------------
DECLARE @SesionNestoViejo int = 0;               -- <-- session_id de Nesto viejo

IF @SesionNestoViejo <= 0
BEGIN
    RAISERROR('Falta el session_id de Nesto viejo (paso 1).', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue567_ImpresionNestoViejo')
    DROP EVENT SESSION [Issue567_ImpresionNestoViejo] ON SERVER;

DECLARE @filtro nvarchar(200) = N'WHERE sqlserver.session_id = ' + CAST(@SesionNestoViejo AS nvarchar(10));
DECLARE @acciones nvarchar(400) = N'ACTION (sqlserver.client_app_name, sqlserver.server_principal_name, sqlserver.database_name, sqlserver.session_id)';
DECLARE @sql nvarchar(max) = N'
CREATE EVENT SESSION [Issue567_ImpresionNestoViejo] ON SERVER
    -- Lotes de SQL tal como los manda el informe (si lee las tablas directamente, aparecen aquí)
    ADD EVENT sqlserver.sql_batch_completed (' + @acciones + N' ' + @filtro + N'),
    -- Llamadas a procedimientos y consultas parametrizadas (sp_executesql, sp_prepexec, cursores de servidor)
    ADD EVENT sqlserver.rpc_completed (' + @acciones + N' ' + @filtro + N'),
    -- Las sentencias de DENTRO de cada procedimiento o función: qué tablas acaba leyendo
    ADD EVENT sqlserver.sp_statement_completed (SET collect_object_name = (1), collect_statement = (1) ' + @acciones + N' ' + @filtro + N')
    ADD TARGET package0.event_file (SET filename = N''Issue567_ImpresionNestoViejo.xel'', max_file_size = 20, max_rollover_files = 2)
    WITH (MAX_MEMORY = 4096 KB, EVENT_RETENTION_MODE = ALLOW_SINGLE_EVENT_LOSS, MAX_DISPATCH_LATENCY = 3 SECONDS, STARTUP_STATE = OFF);';
EXEC (@sql);

ALTER EVENT SESSION [Issue567_ImpresionNestoViejo] ON SERVER STATE = START;
PRINT 'Traza en marcha. Ahora: imprimir desde Nesto viejo UNA factura creada en Nesto (apuntar su número).';
GO

------------------------------------------------------------------------------------------------
-- PASO 3. En Nesto viejo: imprimir (o previsualizar) UNA factura creada en Nesto. Nada más.
------------------------------------------------------------------------------------------------

------------------------------------------------------------------------------------------------
-- PASO 4. Parar la traza.
------------------------------------------------------------------------------------------------
ALTER EVENT SESSION [Issue567_ImpresionNestoViejo] ON SERVER STATE = STOP;
GO

------------------------------------------------------------------------------------------------
-- PASO 5. Leer lo capturado, en el orden en que pasó. Guardar el resultado (copiar con cabeceras
--         o «Guardar resultados como») y pasárselo a Claude.
------------------------------------------------------------------------------------------------
;WITH eventos AS (
    SELECT CAST(f.event_data AS xml) AS x
    FROM sys.fn_xe_file_target_read_file(N'Issue567_ImpresionNestoViejo*.xel', NULL, NULL, NULL) f
)
SELECT x.value('(event/@timestamp)[1]', 'datetime2(3)')                                        AS momento_utc,
       x.value('(event/@name)[1]', 'nvarchar(60)')                                            AS evento,
       x.value('(event/action[@name="client_app_name"]/value)[1]', 'nvarchar(200)')           AS programa,
       x.value('(event/data[@name="object_name"]/value)[1]', 'nvarchar(200)')                 AS objeto,
       x.value('(event/data[@name="row_count"]/value)[1]', 'bigint')                          AS filas,
       x.value('(event/data[@name="duration"]/value)[1]', 'bigint') / 1000                    AS milisegundos,
       COALESCE(x.value('(event/data[@name="batch_text"]/value)[1]', 'nvarchar(max)'),
                x.value('(event/data[@name="statement"]/value)[1]', 'nvarchar(max)'))         AS texto
FROM eventos
ORDER BY momento_utc;

-- Resumen: qué procedimientos y funciones han intervenido (si no sale ninguno, el informe lee las tablas a pelo)
-- (Los valores del XML se sacan primero: SQL Server no admite métodos XML en el GROUP BY, error 4148.)
;WITH eventos AS (
    SELECT CAST(f.event_data AS xml) AS x
    FROM sys.fn_xe_file_target_read_file(N'Issue567_ImpresionNestoViejo*.xel', NULL, NULL, NULL) f
), valores AS (
    SELECT x.value('(event/@name)[1]', 'nvarchar(60)')                            AS evento,
           x.value('(event/data[@name="object_name"]/value)[1]', 'nvarchar(200)') AS objeto
    FROM eventos
)
SELECT evento, objeto, COUNT(*) AS veces
FROM valores
GROUP BY evento, objeto
ORDER BY veces DESC;
GO

------------------------------------------------------------------------------------------------
-- PASO 6. Borrar la traza. Los ficheros .xel se quedan en la carpeta LOG de SQL Server
--         (unos pocos KB); se pueden borrar a mano.
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue567_ImpresionNestoViejo')
    DROP EVENT SESSION [Issue567_ImpresionNestoViejo] ON SERVER;
GO
