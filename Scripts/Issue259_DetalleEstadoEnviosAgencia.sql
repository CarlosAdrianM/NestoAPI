-- NestoAPI#259 — Etiqueta del estado de seguimiento en EnviosAgencia
--
-- El poll de seguimiento (SeguimientoEnviosJobsService) traduce el estado de cada agencia al
-- enum comun (Tramitado/Entregado/Incidentado/Devuelto) y TIRA el texto original: hasta ahora
-- solo se persistian Estado y FechaEntrega. Por eso la pestana de Incidentados no puede decir
-- POR QUE esta incidentado un envio.
--
-- DetalleEstado guarda ese texto tal cual lo da la agencia ("DISPONIBLE PARA RECOGER" de
-- Innovatrans, el texto de incidencia de GLS...) para mostrarlo como etiqueta en el grid, sin
-- crear una pestana por estado y agencia.
--
-- Columna NULLABLE sin default: en SQL Server el ALTER es metadata-only (instantaneo), no
-- reescribe las 186.000 filas de la tabla.
--
-- Ejecutar en la BD NV ANTES de desplegar el API (si no, EF fallara al mapear la propiedad).

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.EnviosAgencia') AND name = 'DetalleEstado')
BEGIN
    ALTER TABLE dbo.EnviosAgencia ADD DetalleEstado varchar(100) NULL;
    PRINT 'EnviosAgencia.DetalleEstado creada.';
END
ELSE
BEGIN
    PRINT 'EnviosAgencia.DetalleEstado ya existia: nada que hacer.';
END
GO

-- La tabla ya tiene los permisos concedidos a nivel de objeto, que alcanzan a las columnas
-- nuevas: no hace falta reponer GRANTs (ver reference_scripts_sql_grants_por_bd).
