-- Ariadna: rol «Almacén» en Identity para los mozos (escribir en api/Almacen: lecturas, terminar salidas, ubicar,
-- recibir). Lanzar DESPUÉS de Ariadna_CrearUsuarios.ps1: solo vale para usuarios que YA existan en Identity; los
-- que no existan se listan y no se tocan. Se puede repetir sin miedo.
-- RevisionGoogle (revisores de Google Play) NUNCA en este rol.
--
-- Lanzar en SSMS como sa, en el servidor de la BD AspNetIdentity.
USE AspNetIdentity;
GO

DECLARE @Rol nvarchar(256) = N'Almacén';
DECLARE @Usuarios TABLE (UserName nvarchar(256) PRIMARY KEY);
INSERT INTO @Usuarios (UserName) VALUES (N'Santiago'), (N'Alfredo'), (N'Pedro'), (N'Andre');   -- mozos a 03/10/26

-- 0) Qué hay
SELECT Id, Name FROM dbo.AspNetRoles ORDER BY Name;
SELECT u.UserName, CASE WHEN a.Id IS NULL THEN 'NO EXISTE en Identity' ELSE 'existe' END AS Situacion
FROM @Usuarios u LEFT JOIN dbo.AspNetUsers a ON a.UserName = u.UserName;

BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Name = @Rol)
    INSERT INTO dbo.AspNetRoles (Id, Name) VALUES (CONVERT(nvarchar(128), NEWID()), @Rol);

INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT a.Id, r.Id
FROM @Usuarios u
JOIN dbo.AspNetUsers a ON a.UserName = u.UserName
CROSS JOIN dbo.AspNetRoles r
WHERE r.Name = @Rol
  AND a.UserName <> N'RevisionGoogle'
  AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = a.Id AND ur.RoleId = r.Id);

-- Comprobación: quién tiene el rol (deben salir los cuatro y Carlos)
SELECT a.UserName, r.Name AS Rol
FROM dbo.AspNetUserRoles ur
JOIN dbo.AspNetUsers a ON a.Id = ur.UserId
JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
WHERE r.Name = @Rol;

COMMIT;
-- El rol va dentro del token: hay que volver a entrar en Ariadna para que cuente.
