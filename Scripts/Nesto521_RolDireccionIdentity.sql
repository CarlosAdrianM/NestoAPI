-- Nesto#521 / NestoApp#219: rol «Dirección» en Identity para Carlos y Manuel.
-- En Nesto, Dirección es el grupo de dominio; en NestoApp (token de /oauth/token) los permisos salen de los roles de
-- Identity, y ese rol NO existía (09/10/26: Carlos = Almacén, SuperAdmin, Admin; Manuel = VendedorTelefono). Sin él,
-- desde NestoApp no podrían ver los clientes para contactar de otros vendedores (403).
-- Efecto: en NestoApp y Ariadna tienen los mismos permisos de Dirección que ya tienen en Nesto (Novedades, Verifactu,
-- almacén, reposiciones, notificaciones…). Nada más.
-- Se puede repetir sin miedo. Lanzar en SSMS como sa, en DC2016, BD AspNetIdentity.
-- El rol va dentro del token: hay que volver a entrar en NestoApp (o esperar al refresco del token) para que cuente.
USE AspNetIdentity;
GO

DECLARE @Rol nvarchar(256) = N'Dirección';
DECLARE @Usuarios TABLE (UserName nvarchar(256) PRIMARY KEY);
INSERT INTO @Usuarios (UserName) VALUES (N'Carlos'), (N'Manuel');

-- 0) Qué hay
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
  AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = a.Id AND ur.RoleId = r.Id);

-- Comprobación: deben salir Carlos y Manuel
SELECT a.UserName, r.Name AS Rol
FROM dbo.AspNetUserRoles ur
JOIN dbo.AspNetUsers a ON a.Id = ur.UserId
JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
WHERE r.Name = @Rol;

COMMIT;
