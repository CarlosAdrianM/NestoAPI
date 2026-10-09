-- Ariadna para tiendas (Carlos 09/10/26): rol «Tiendas» en Identity para quien prepara y recibe las reposiciones de
-- Reina y Alcobendas con el móvil.
--
-- OJO: Ariadna NO necesita este rol para funcionar. Qué ve cada uno lo decide el servidor con su almacén
-- (ParámetrosUsuario.AlmacénPedidoVta: GET api/Almacen/Perfil) y los permisos de las reposiciones ya son por almacén
-- (preparar y terminar la de SU tienda, recibir en SU tienda). El rol sirve para:
--   - Novedades (sugerencia 551): con él, en la campana y en Novedades de Ariadna solo salen las de Tiendas y las de
--     todos; sin él, salen todas.
--   - Que el rol «Tiendas» de Identity diga lo mismo que el grupo de Windows NUEVAVISION\Tiendas de Nesto.
-- NUNCA dar a estos usuarios el rol «Almacén»: con él podrían escribir en todo api/Almacen (picking, ubicar…).
--
-- Los usuarios de Identity tienen que llamarse IGUAL que su usuario de Windows sin dominio (Reina, Paloma), para que
-- la API encuentre su AlmacénPedidoVta (Reina → REI, Paloma → ALC; comprobado el 09/10/26: son las dos cuentas que
-- han hecho las reposiciones de las tiendas en los últimos 60 días). Si se da de alta a otra persona de la tienda
-- (Pilar, Patricia en REI; Eva, Almudena, Ines, Javier, Victoria en ALC ya tienen su AlmacénPedidoVta), vale igual con
-- su nombre de Windows. Lanzar DESPUÉS de crear los usuarios (Ariadna_CrearUsuarios.ps1 con ellos en la lista); los que
-- no existan se listan y no se tocan. Se puede repetir sin miedo.
--
-- Lanzar en SSMS como sa, en el servidor de la BD AspNetIdentity (DC2016).
USE AspNetIdentity;
GO

DECLARE @Rol nvarchar(256) = N'Tiendas';
DECLARE @Usuarios TABLE (UserName nvarchar(256) PRIMARY KEY);
INSERT INTO @Usuarios (UserName) VALUES (N'Reina'), (N'Paloma');   -- tiendas a 09/10/26

-- 0) Qué hay
SELECT Id, Name FROM dbo.AspNetRoles ORDER BY Name;
SELECT u.UserName, CASE WHEN a.Id IS NULL THEN 'NO EXISTE en Identity: crearlo antes' ELSE 'existe' END AS Situacion
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

-- Comprobación: quién tiene el rol y que ninguno tiene además «Almacén»
SELECT a.UserName, r.Name AS Rol
FROM dbo.AspNetUserRoles ur
JOIN dbo.AspNetUsers a ON a.Id = ur.UserId
JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
WHERE a.UserName IN (SELECT UserName FROM @Usuarios)
ORDER BY a.UserName, r.Name;

COMMIT;
-- El rol va dentro del token: hay que volver a entrar en Ariadna para que cuente.
--
-- Su almacén (BD NV, solo lectura; si falta la fila, la API usa la de «(defecto)», que es Algete, y Ariadna le enseñaría
-- el menú de Algete sin poder escribir nada):
--   SELECT RTRIM(Usuario), RTRIM(Valor) FROM NV.dbo.ParametrosUsuario
--   WHERE Empresa = '1' AND Clave = 'AlmacénPedidoVta' AND RTRIM(Usuario) IN ('Reina', 'Paloma');
