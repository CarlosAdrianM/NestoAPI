/*
    13/10/26: Andre y Pedro no pueden entrar en Ariadna con el usuario y la contraseña que les llegaron por correo.
    Causa: Ariadna_CrearUsuarios.ps1 los dio de alta con POST api/accounts/create, que crea el usuario con EmailConfirmed = 0,
    y el login (/oauth/token, CustomOAuthProvider) rechaza a quien no tiene el correo confirmado («User did not confirm email»),
    aunque la contraseña sea buena. Lo mismo le pasaría a Santiago (creado por el mismo script el 03/10).

    Lanzar como sa en DC2016 (BD AspNetIdentity). Idempotente. Enseña cómo estaban y cómo quedan.
*/
SET NOCOUNT ON;
USE AspNetIdentity;

SELECT UserName, Email, EmailConfirmed, LockoutEndDateUtc, AccessFailedCount
FROM dbo.AspNetUsers WHERE UserName IN ('Andre', 'Pedro', 'Santiago');

UPDATE dbo.AspNetUsers
SET EmailConfirmed = 1, AccessFailedCount = 0, LockoutEndDateUtc = NULL
WHERE UserName IN ('Andre', 'Pedro', 'Santiago') AND (EmailConfirmed = 0 OR AccessFailedCount > 0 OR LockoutEndDateUtc IS NOT NULL);

SELECT @@ROWCOUNT AS UsuariosArreglados;

SELECT UserName, Email, EmailConfirmed, LockoutEndDateUtc, AccessFailedCount
FROM dbo.AspNetUsers WHERE UserName IN ('Andre', 'Pedro', 'Santiago');
