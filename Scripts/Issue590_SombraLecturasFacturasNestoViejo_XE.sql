/*
    NestoAPI#590 (Carlos 09/11/26): MEDIR antes de decidir cómo bloquear la impresión de facturas desde Nesto viejo.
    Crystal (facturas.rpt) lee las tablas con el mismo APP_NAME «Nesto» (#567), así que la única guarda en la BD sería un RLS en
    CabFacturaVta, y eso afectaría a TODO lo que Nesto viejo lee de facturas. Esta sesión cuenta, durante una semana, qué lecturas
    de facturas hace Nesto viejo y quién: impresiones de Crystal, vistas (vstLibroIVA, vstRepetirRuta, vstComisionesEspeciales,
    vstInformeComisionesResumen), procedimientos que leen CabFacturaVta y consultas directas. Informe:
    Issue590_SombraLecturasFacturasNestoViejo_Informe.sql.

    Como sa. Sesión aparte de la de escrituras (Issue590_SombraEscriturasNestoViejo_XE.sql) para que esa siga siendo ligera.
    No cambia ningún dato ni objeto de NV. Hasta el lunes 16/11/26.

    INTERRUPTOR (una línea):
        ALTER EVENT SESSION [Issue590_SombraLecturasFacturasNestoViejo] ON SERVER STATE = STOP;

    Qué captura (programa Nesto y BD NV primero en cada predicado; el resto del tráfico se descarta enseguida):
      - sql_statement_completed y rpc_completed cuyo texto nombra CabFacturaVta o una de las 4 vistas que la leen.
      - rpc_completed de procedimientos prd%: el informe se queda con los que leen CabFacturaVta (sys.sql_expression_dependencies,
        46 a 09/11/26). Las escrituras de esos procedimientos ya las cuenta la otra sesión; aquí interesa la llamada.
    Volumen: 50 MB × 10 como máximo, ALLOW_MULTIPLE_EVENT_LOSS.
    Sintaxis igual que la sesión de escrituras; si un predicado diera error al crearla, quitar esa línea del LIKE y avisar.
*/

IF NOT EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue590_SombraLecturasFacturasNestoViejo')
BEGIN
    EXEC (N'
CREATE EVENT SESSION [Issue590_SombraLecturasFacturasNestoViejo] ON SERVER
ADD EVENT sqlserver.sql_statement_completed (
    SET collect_statement = (1)
    ACTION (sqlserver.client_app_name, sqlserver.server_principal_name, sqlserver.client_hostname, sqlserver.session_id)
    WHERE [sqlserver].[client_app_name] = N''Nesto''
      AND [sqlserver].[database_name] = N''NV''
      AND (    [sqlserver].[like_i_sql_unicode_string]([statement], N''%CabFacturaVta%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%vstLibroIVA%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%vstRepetirRuta%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%vstComisionesEspeciales%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%vstInformeComisionesResumen%''))
),
ADD EVENT sqlserver.rpc_completed (
    SET collect_statement = (1)
    ACTION (sqlserver.client_app_name, sqlserver.server_principal_name, sqlserver.client_hostname, sqlserver.session_id)
    WHERE [sqlserver].[client_app_name] = N''Nesto''
      AND [sqlserver].[database_name] = N''NV''
      AND (    [sqlserver].[like_i_sql_unicode_string]([object_name], N''prd%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%CabFacturaVta%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%vstLibroIVA%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%vstRepetirRuta%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%vstComisionesEspeciales%'')
            OR [sqlserver].[like_i_sql_unicode_string]([statement], N''%vstInformeComisionesResumen%''))
)
ADD TARGET package0.event_file (SET filename = N''Issue590_SombraLecturasFacturasNestoViejo.xel'', max_file_size = (50), max_rollover_files = (10))
WITH (MAX_MEMORY = 8192 KB, EVENT_RETENTION_MODE = ALLOW_MULTIPLE_EVENT_LOSS, MAX_DISPATCH_LATENCY = 30 SECONDS,
      TRACK_CAUSALITY = OFF, STARTUP_STATE = ON);');
    PRINT 'Sesión Issue590_SombraLecturasFacturasNestoViejo creada.';
END
ELSE
    PRINT 'La sesión ya existía: no se cambia.';

IF NOT EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE name = N'Issue590_SombraLecturasFacturasNestoViejo')
BEGIN
    ALTER EVENT SESSION [Issue590_SombraLecturasFacturasNestoViejo] ON SERVER STATE = START;
    PRINT 'Sesión arrancada. Hasta el lunes 16/11/26.';
END
ELSE
    PRINT 'La sesión ya estaba en marcha.';
GO

-- Comprobación: una fila con su fichero
SELECT s.name Sesion, s.create_time Arrancada, CAST(t.target_data AS xml).value('(EventFileTarget/File/@name)[1]', 'nvarchar(400)') Fichero
FROM sys.dm_xe_sessions s JOIN sys.dm_xe_session_targets t ON t.event_session_address = s.address
WHERE s.name = N'Issue590_SombraLecturasFacturasNestoViejo';
GO

-- Parar (16/11/26):  ALTER EVENT SESSION [Issue590_SombraLecturasFacturasNestoViejo] ON SERVER STATE = STOP;
-- Borrar (tras el informe; los .xel de la carpeta LOG, a mano):
-- IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'Issue590_SombraLecturasFacturasNestoViejo')
--     DROP EVENT SESSION [Issue590_SombraLecturasFacturasNestoViejo] ON SERVER;
