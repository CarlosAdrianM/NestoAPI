-- =============================================================================
-- Nesto#340 (Agencias, slice A2) — GRANT de EnviosHistoria para la cuenta del API
-- =============================================================================
-- EJECUTAR ANTES DE DESPLEGAR el A2: DeleteEnviosAgencia borra ahora la historia
-- de seguimiento por SQL crudo y corre como [NUEVAVISION\RDS2016$], que hoy no
-- tiene NINGÚN permiso sobre EnviosHistoria (lección LinBalance #350: comprobar
-- el GRANT de la cuenta del API antes del deploy).
-- SELECT/INSERT/UPDATE se incluyen ya para los slices siguientes (A3/A4:
-- CargarListaHistoriaEnvio y las historias de modificarEnvio irán por la API).
-- Idempotente (GRANT repetido no falla).
-- =============================================================================

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EnviosHistoria TO [NUEVAVISION\RDS2016$];

SELECT pr.name Principal, p.permission_name Permiso
FROM sys.database_permissions p
JOIN sys.objects o ON p.major_id = o.object_id
JOIN sys.database_principals pr ON p.grantee_principal_id = pr.principal_id
WHERE o.name = 'EnviosHistoria' AND pr.name = 'NUEVAVISION\RDS2016$';
