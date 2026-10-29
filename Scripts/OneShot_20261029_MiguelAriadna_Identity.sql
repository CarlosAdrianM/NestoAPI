/*
    29/10/26 (NestoAPI#739): Miguel Sotillos, repartidor del reparto propio, entra solo en Ariadna (sin usuario de Windows, como Pedro).
    Lanzar DESPUÉS de crear el usuario con Ariadna_CrearUsuarios.ps1 (crea «Miguel» con miguelsotillos@nuevavision.es y le manda la
    contraseña; a los que ya existen no los toca).
    Hace lo mismo que se hizo a mano con Andre y Pedro:
      - EmailConfirmed = 1: api/accounts/create lo deja a 0 y el login de Ariadna lo rechaza («User did not confirm email»).
      - Rol «Almacén»: sin él la API no le deja escribir en api/Almacen ni en el reparto.
    Lanzar como sa en DC2016 (BD AspNetIdentity). Idempotente: si Miguel no existe todavía, avisa y no cambia nada.
*/
SET NOCOUNT ON;
USE AspNetIdentity;

DECLARE @Usuario nvarchar(256) = N'Miguel';
DECLARE @Rol nvarchar(256) = N'Almacén';

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE UserName = @Usuario)
BEGIN
    RAISERROR('Miguel todavía no existe en Identity: lance antes Ariadna_CrearUsuarios.ps1. No se cambia nada.', 16, 1);
    RETURN;
END

BEGIN TRAN;

UPDATE dbo.AspNetUsers
SET EmailConfirmed = 1, AccessFailedCount = 0, LockoutEndDateUtc = NULL
WHERE UserName = @Usuario AND (EmailConfirmed = 0 OR AccessFailedCount > 0 OR LockoutEndDateUtc IS NOT NULL);

INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT a.Id, r.Id
FROM dbo.AspNetUsers a
CROSS JOIN dbo.AspNetRoles r
WHERE a.UserName = @Usuario AND r.Name = @Rol
  AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = a.Id AND ur.RoleId = r.Id);

COMMIT;

SELECT a.UserName, a.Email, a.EmailConfirmed, r.Name AS Rol
FROM dbo.AspNetUsers a
LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = a.Id
LEFT JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
WHERE a.UserName = @Usuario;
