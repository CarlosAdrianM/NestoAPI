/*
    NestoAPI#590 (Carlos 09/11/26): SOMBRA de lo que Nesto viejo ESCRIBE en NV, para decidir los siguientes slices de la épica con
    datos (qué tablas y procedimientos toca, quién y cuántas veces) en vez de ir caso a caso cuando algo sale mal.
    Sesión de Extended Events filtrada por programa = 'Nesto' (Nesto viejo; también lo es Crystal, que solo lee) y 'DataProj'
    (una sesión de Andre desde RDS2016: para saber de una vez qué es, probablemente Ariadna vieja). Solo la BD NV.
    El informe está en Issue590_SombraEscriturasNestoViejo_Informe.sql.

    Como sa (crear una sesión de XE pide ALTER ANY EVENT SESSION). Se lanza el PASO 1 entero; los demás, cuando toque.
    No cambia ningún dato ni ningún objeto de NV. Duración prevista: una semana (hasta el lunes 16/11/26).

    INTERRUPTOR (una línea, para al momento):
        ALTER EVENT SESSION [Issue590_SombraEscriturasNestoViejo] ON SERVER STATE = STOP;

    Qué captura (solo si el programa es Nesto/DataProj y la BD es NV; esas dos condiciones van PRIMERO en cada predicado, así que
    para el resto del tráfico, la API, el coste es comparar el nombre del programa y descartar):
      - rpc_completed: llamadas a procedimientos (EXEC prd… por RPC) y sentencias parametrizadas (sp_executesql / sp_prepexec, que
        es como ADO manda los UPDATE «… WHERE valor = @P4»), si el procedimiento empieza por prd o el texto lleva INSERT/UPDATE/
        DELETE/MERGE. También sp_cursor (escritura posicionada por un cursor de servidor: la sesión «DataProj» los usa); se quitan
        sp_reset_connection y el resto de llamadas de cursores (sp_cursoropen, sp_cursorfetch…: solo leen).
      - sql_statement_completed: sentencias sueltas de los lotes (SQL a pelo) con INSERT/UPDATE/DELETE/MERGE que hayan tocado alguna
        fila, sin tablas temporales (#).
      - sp_statement_completed: lo mismo DENTRO de procedimientos y de sentencias preparadas (sp_execute: ADO reutiliza la sentencia
        preparada y en rpc_completed ya no viene el texto). Así se ve qué tablas acaba escribiendo cada procedimiento.
      Acciones: programa, login (server_principal_name), equipo, sesión. Texto de la sentencia y nombre del objeto.
    Volumen y disco: ficheros .xel en la carpeta LOG de SQL Server, 100 MB × 10 como máximo (1 GB; al llenarse se van pisando los
    más antiguos). MAX_MEMORY 8 MB, ALLOW_MULTIPLE_EVENT_LOSS (si hay mucha carga se pierden eventos antes que frenar a nadie).
    STARTUP_STATE = ON: si se reinicia SQL Server durante la semana, vuelve a arrancar sola (el PASO 4 la borra).

    Comprobado: sintaxis con SET PARSEONLY (09/11/26). No se ha podido crear desde mi login (sin permiso de servidor): si algún
    predicado diera error al crearla, quitar la línea del LIKE de ese evento y avisar.
*/

------------------------------------------------------------------------------------------------------------------------------
-- PASO 1. Crear y arrancar (idempotente: si ya existe, la deja como está y solo comprueba que está en marcha).
------------------------------------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue590_SombraEscriturasNestoViejo')
BEGIN
    EXEC (N'
CREATE EVENT SESSION [Issue590_SombraEscriturasNestoViejo] ON SERVER
ADD EVENT sqlserver.rpc_completed (
    SET collect_statement = (1)
    ACTION (sqlserver.client_app_name, sqlserver.server_principal_name, sqlserver.client_hostname, sqlserver.session_id)
    WHERE ([sqlserver].[client_app_name] = N''Nesto'' OR [sqlserver].[client_app_name] = N''DataProj'')
      AND [sqlserver].[database_name] = N''NV''
      AND [object_name] <> N''sp_reset_connection''
      AND (    [object_name] = N''sp_cursor''
            OR (    NOT [sqlserver].[like_i_sql_unicode_string]([object_name], N''sp_cursor%'')
                AND (    [sqlserver].[like_i_sql_unicode_string]([object_name], N''prd%'')
                      OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%insert%'')
                      OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%update%'')
                      OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%delete%'')
                      OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%merge%''))))
),
ADD EVENT sqlserver.sql_statement_completed (
    SET collect_statement = (1)
    ACTION (sqlserver.client_app_name, sqlserver.server_principal_name, sqlserver.client_hostname, sqlserver.session_id)
    WHERE ([sqlserver].[client_app_name] = N''Nesto'' OR [sqlserver].[client_app_name] = N''DataProj'')
      AND [sqlserver].[database_name] = N''NV''
      AND [row_count] > (0)
      AND NOT [sqlserver].[like_i_sql_unicode_string]([statement], N''%#%'')
      AND (    [sqlserver].[like_i_sql_unicode_string]([statement], N''%insert%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%update%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%delete%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%merge%''))
),
ADD EVENT sqlserver.sp_statement_completed (
    SET collect_object_name = (1), collect_statement = (1)
    ACTION (sqlserver.client_app_name, sqlserver.server_principal_name, sqlserver.client_hostname, sqlserver.session_id)
    WHERE ([sqlserver].[client_app_name] = N''Nesto'' OR [sqlserver].[client_app_name] = N''DataProj'')
      AND [sqlserver].[database_name] = N''NV''
      AND [row_count] > (0)
      AND NOT [sqlserver].[like_i_sql_unicode_string]([statement], N''%#%'')
      AND (    [sqlserver].[like_i_sql_unicode_string]([statement], N''%insert%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%update%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%delete%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%merge%''))
)
ADD TARGET package0.event_file (SET filename = N''Issue590_SombraEscriturasNestoViejo.xel'', max_file_size = (100), max_rollover_files = (10))
WITH (MAX_MEMORY = 8192 KB, EVENT_RETENTION_MODE = ALLOW_MULTIPLE_EVENT_LOSS, MAX_DISPATCH_LATENCY = 30 SECONDS,
      TRACK_CAUSALITY = OFF, STARTUP_STATE = ON);');
    PRINT 'Sesión Issue590_SombraEscriturasNestoViejo creada.';
END
ELSE
    PRINT 'La sesión ya existía: no se cambia.';

IF NOT EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE name = N'Issue590_SombraEscriturasNestoViejo')
BEGIN
    ALTER EVENT SESSION [Issue590_SombraEscriturasNestoViejo] ON SERVER STATE = START;
    PRINT 'Sesión arrancada. Hasta el lunes 16/11/26; el informe: Issue590_SombraEscriturasNestoViejo_Informe.sql.';
END
ELSE
    PRINT 'La sesión ya estaba en marcha.';
GO

-- Comprobación: tiene que salir una fila, con su fichero en la carpeta LOG y eventos (tras unos minutos con Nesto viejo abierto)
SELECT s.name Sesion, s.create_time Arrancada, CAST(t.target_data AS xml).value('(EventFileTarget/File/@name)[1]', 'nvarchar(400)') Fichero
FROM sys.dm_xe_sessions s
JOIN sys.dm_xe_session_targets t ON t.event_session_address = s.address
WHERE s.name = N'Issue590_SombraEscriturasNestoViejo';
GO

------------------------------------------------------------------------------------------------------------------------------
-- PASO 2 (cuando se quiera, sin pararla): el informe -> Issue590_SombraEscriturasNestoViejo_Informe.sql
------------------------------------------------------------------------------------------------------------------------------

------------------------------------------------------------------------------------------------------------------------------
-- PASO 3. Parar (el lunes 16/11/26, o antes si molesta). Los ficheros se quedan para el informe.
------------------------------------------------------------------------------------------------------------------------------
-- ALTER EVENT SESSION [Issue590_SombraEscriturasNestoViejo] ON SERVER STATE = STOP;

------------------------------------------------------------------------------------------------------------------------------
-- PASO 4. Borrar la sesión (después de sacar el informe). Los .xel de la carpeta LOG (hasta 1 GB) se borran a mano:
--         Issue590_SombraEscriturasNestoViejo_*.xel
------------------------------------------------------------------------------------------------------------------------------
-- IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue590_SombraEscriturasNestoViejo')
--     DROP EVENT SESSION [Issue590_SombraEscriturasNestoViejo] ON SERVER;
