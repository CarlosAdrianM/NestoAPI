-- OneShot 05/11/26 (Carlos, épica NestoAPI#590 «capar Nesto viejo»): los códigos postales solo se crean y se
-- modifican por la API (Nesto nuevo). Hasta hoy `public` tenía INSERT sobre CódigosPostales y varios grupos de
-- Windows tenían UPDATE, así que desde Nesto viejo cualquiera podía inventarse un CP (caso: 48430 ORTUELLA el
-- 05/11/26, el real es 48530). Se deja SELECT a todos y INSERT/UPDATE/DELETE solo a NUEVAVISION\RDS2016$ (la API).
-- Ejecutar como sa en NV. Idempotente.
--
-- OJO: los miembros de los roles db_owner y db_datawriter (grupo «Administradores», Carlos) no se ven afectados:
-- esos roles dan INSERT/UPDATE sobre todas las tablas por encima de estos permisos.
USE NV;
GO
SET NOCOUNT ON;

-- Antes
SELECT 'ANTES' Momento, pr.name Principal, pe.permission_name Permiso, pe.state_desc Estado
FROM sys.database_permissions pe
JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.major_id = OBJECT_ID('dbo.CódigosPostales')
ORDER BY pr.name, pe.permission_name;

-- INSERT: lo tenían public, Cursos y «Eva Visnú»
REVOKE INSERT ON dbo.[CódigosPostales] FROM [public];
REVOKE INSERT ON dbo.[CódigosPostales] FROM [Cursos];
REVOKE INSERT ON dbo.[CódigosPostales] FROM [Eva Visnú];

-- UPDATE: lo tenían Administración, Almacén, Cursos, «Eva Visnú», Informática y NUEVAVISION\Almacén
REVOKE UPDATE ON dbo.[CódigosPostales] FROM [Administración];
REVOKE UPDATE ON dbo.[CódigosPostales] FROM [Almacén];
REVOKE UPDATE ON dbo.[CódigosPostales] FROM [Cursos];
REVOKE UPDATE ON dbo.[CódigosPostales] FROM [Eva Visnú];
REVOKE UPDATE ON dbo.[CódigosPostales] FROM [Informática];
REVOKE UPDATE ON dbo.[CódigosPostales] FROM [NUEVAVISION\Almacén];

-- DELETE (mismo criterio: solo la API). Comentar este bloque si se prefiere dejarlo como estaba.
REVOKE DELETE ON dbo.[CódigosPostales] FROM [Administración];
REVOKE DELETE ON dbo.[CódigosPostales] FROM [Eva Visnú];
REVOKE DELETE ON dbo.[CódigosPostales] FROM [Informática];
REVOKE DELETE ON dbo.[CódigosPostales] FROM [NUEVAVISION\Almacén];

-- La API conserva todo (por si acaso, se vuelve a conceder: es idempotente)
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.[CódigosPostales] TO [NUEVAVISION\RDS2016$];

-- Después: debe quedar SELECT para public y «Eva Visnú», y todo para RDS2016$
SELECT 'DESPUES' Momento, pr.name Principal, pe.permission_name Permiso, pe.state_desc Estado
FROM sys.database_permissions pe
JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.major_id = OBJECT_ID('dbo.CódigosPostales')
ORDER BY pr.name, pe.permission_name;
GO
