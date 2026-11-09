-- NestoAPI#590 (slice «Quitar picking», Carlos 09/11/26): quitar el picking de un pedido solo por la API (Nesto nuevo, botón
-- «Quitar picking del pedido» de la ventana de Picking). Hasta hoy Nesto viejo llamaba a prdDeshacerUbicacionPicking con el
-- login de Windows de cada usuario (EXECUTE concedido a public) y ponía Picking = 0, sin rastro y sin avisar a la PDA que
-- recogía el picking (picking 100033 / pedido 930110). Se deja EXECUTE solo a NUEVAVISION\RDS2016$ (la API).
--
-- ORDEN: 1) publicar la API y Nesto con «Quitar picking»; 2) comprobar que funciona; 3) ESTE script, como sa en NV. NO
-- antes: la API hoy tiene el EXECUTE por public, y por eso el script se lo concede explícitamente ANTES de quitarlo a public.
-- Idempotente.
--
-- Qué NO se rompe:
--  - prdCrearAlbaránVta (y cualquier otro SP de dbo) sigue llamando a prdDeshacerUbicacionPicking: mismo dueño (dbo), así
--    que el encadenamiento de propiedad no mira el permiso de EXECUTE del usuario sobre el procedimiento de dentro.
--  - db_owner (grupo «Administradores», Carlos) no se ve afectado.
-- Qué verá Nesto viejo: al quitar el picking (o al borrar una línea o un pedido con picking, que también llama a este SP), un
-- error de SQL Server 229 «Se denegó el permiso EXECUTE en el objeto 'prdDeshacerUbicacionPicking'». La línea se queda como
-- estaba (con su picking y su reserva). Hay que hacerlo desde Nesto: Picking → «Quitar picking del pedido».
USE NV;
GO
SET NOCOUNT ON;

-- Antes
SELECT 'ANTES' Momento, pr.name Principal, pe.permission_name Permiso, pe.state_desc Estado
FROM sys.database_permissions pe
JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.major_id = OBJECT_ID('dbo.prdDeshacerUbicacionPicking')
ORDER BY pr.name, pe.permission_name;

-- 1) La API, primero (hoy lo tiene por public)
GRANT EXECUTE ON dbo.prdDeshacerUbicacionPicking TO [NUEVAVISION\RDS2016$];

-- 2) Todos los demás que lo tengan concedido de forma explícita (el 09/11/26 solo public)
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'REVOKE EXECUTE ON dbo.prdDeshacerUbicacionPicking FROM ' + QUOTENAME(pr.name) + N';' + CHAR(10)
FROM sys.database_permissions pe
JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.major_id = OBJECT_ID('dbo.prdDeshacerUbicacionPicking')
  AND pe.permission_name = 'EXECUTE'
  AND pr.name <> N'NUEVAVISION\RDS2016$';
PRINT @sql;
EXEC sys.sp_executesql @sql;

-- Después: solo debe quedar EXECUTE para NUEVAVISION\RDS2016$
SELECT 'DESPUES' Momento, pr.name Principal, pe.permission_name Permiso, pe.state_desc Estado
FROM sys.database_permissions pe
JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.major_id = OBJECT_ID('dbo.prdDeshacerUbicacionPicking')
ORDER BY pr.name, pe.permission_name;

-- Comprobación del encadenamiento: los dos SP tienen que ser de dbo (si no, prdCrearAlbaránVta fallaría para los usuarios)
SELECT o.name, USER_NAME(OBJECTPROPERTY(o.object_id, 'OwnerId')) Dueno
FROM sys.objects o
WHERE o.name IN (N'prdDeshacerUbicacionPicking', N'prdCrearAlbaránVta');
GO

-- Vuelta atrás (si hiciera falta):
-- GRANT EXECUTE ON dbo.prdDeshacerUbicacionPicking TO [public];
