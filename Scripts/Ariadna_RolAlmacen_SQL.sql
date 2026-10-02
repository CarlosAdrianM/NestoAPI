-- Ariadna: alternativa a Ariadna_RolAlmacen.ps1 cuando no hay a mano un usuario con rol Admin en la API.
-- Crea el rol «Almacén» en Identity (si no existe) y mete en él al usuario indicado.
-- Lanzar en SSMS como sa, en el servidor de la BD AspNetIdentity. Se puede repetir sin miedo.
-- RevisionGoogle (revisores de Google Play) NO debe estar en este rol: solo lectura.
USE AspNetIdentity;
GO

DECLARE @Usuario nvarchar(256) = N'Carlos';   -- el UserName con el que se entra en NestoApp / Ariadna
DECLARE @Rol nvarchar(256) = N'Almacén';

-- 0) Antes de nada, mirar qué hay (columnas de AspNetRoles y si el usuario existe)
SELECT Id, Name FROM dbo.AspNetRoles ORDER BY Name;
SELECT Id, UserName FROM dbo.AspNetUsers WHERE UserName = @Usuario;

IF @Usuario = N'RevisionGoogle'
    THROW 50000, 'RevisionGoogle no puede tener el rol Almacén (solo lectura).', 1;

BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Name = @Rol)
    INSERT INTO dbo.AspNetRoles (Id, Name) VALUES (CONVERT(nvarchar(128), NEWID()), @Rol);

INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id
FROM dbo.AspNetUsers u
CROSS JOIN dbo.AspNetRoles r
WHERE u.UserName = @Usuario AND r.Name = @Rol
  AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = r.Id);

-- Comprobación: debe salir una fila con el usuario y el rol
SELECT u.UserName, r.Name AS Rol
FROM dbo.AspNetUserRoles ur
JOIN dbo.AspNetUsers u ON u.Id = ur.UserId
JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
WHERE u.UserName = @Usuario;

COMMIT;
-- El rol va dentro del token: hay que volver a entrar en Ariadna para que cuente.
