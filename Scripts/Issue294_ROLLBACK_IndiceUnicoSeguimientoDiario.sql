-- Issue #294 ROLLBACK URGENTE (15/07/26): el índice filtrado + columna calculada indexada
-- de SeguimientoCliente rompen la facturación (error 1934) porque prdCrearFacturaVta (y otros
-- SPs legacy que escriben en SeguimientoCliente) están creados con QUOTED_IDENTIFIER OFF.
-- SQL Server exige QI ON (estampado al crear el SP) para hacer DML sobre tablas con índices
-- filtrados o columnas calculadas persistidas/indexadas.
--
-- Este script revierte la BD al estado del 14/07 por la mañana. Es rápido (el DROP COLUMN de
-- una computed column es solo metadatos) e idempotente.
--
-- ⚠️ Ejecutar con login ADMIN (nuevavision no tiene ALTER). En SSMS directamente, o con
-- sqlcmd añadiendo el flag -I.
--
-- PLAN POSTERIOR (cuando haya calma):
--   1. Recrear con SET QUOTED_IDENTIFIER ON los SPs que ESCRIBEN en SeguimientoCliente:
--      prdCrearFacturaVta, prdTransferirRapport, prdActualizarEstadosCliente,
--      prdModificarEfectoCliente, prdComprobarRetenidosFacturaVta, prdImpresoRapport,
--      prdCrearClavesEmpresa (revisar cuáles hacen INSERT/UPDATE/DELETE; los prdInforme* de
--      solo lectura no hace falta). Ojo: si algún cuerpo usa comillas dobles como delimitador
--      de cadena, cambiarlas a simples.
--   2. Volver a ejecutar Issue294_IndiceUnicoSeguimientoDiario.sql AJUSTANDO la fecha del
--      filtro al día siguiente de la nueva ejecución.
--   3. Verificar: SELECT uses_quoted_identifier FROM sys.sql_modules
--      WHERE object_id = OBJECT_ID('dbo.prdCrearFacturaVta');  -- debe ser 1

-- Parte 1: quitar el índice único filtrado
SET LOCK_TIMEOUT 15000;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente') AND name = 'UQ_SeguimientoCliente_UnoPorClienteUsuarioDia')
BEGIN
    DROP INDEX UQ_SeguimientoCliente_UnoPorClienteUsuarioDia ON dbo.SeguimientoCliente;
END
SET LOCK_TIMEOUT -1;
GO

-- Parte 2: quitar la columna calculada persistida
SET LOCK_TIMEOUT 15000;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente') AND name = 'FechaDia')
BEGIN
    ALTER TABLE dbo.SeguimientoCliente DROP COLUMN FechaDia;
END
SET LOCK_TIMEOUT -1;
GO

-- VERIFICACIÓN (ambas consultas deben devolver 0 filas):
SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente') AND name = 'UQ_SeguimientoCliente_UnoPorClienteUsuarioDia';
SELECT name FROM sys.computed_columns WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente');
GO
