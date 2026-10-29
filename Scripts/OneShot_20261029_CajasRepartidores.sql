-- 29/10/26 (NestoAPI#739): cajas de los repartidores del reparto propio. El efectivo que cobran al entregar se apunta en su
-- CajaDefecto (parámetro de usuario, el de Cajas de Nesto) y «Mis cobros» de Ariadna lee el extracto de esa cuenta.
--   57000031 «Caja de Andre»  y  57000032 «Caja de Miguel», en las empresas 1 y 3.
-- Son los primeros números de la 570 sin ficha y sin apuntes: 57000003 y 57000006 no tienen ficha pero sí apuntes antiguos
-- (11 y 115), así que no se reutilizan. Las fichas se copian de 57000029 «Caja de almacén Algete» (IVA, DebeHaber, Estado...).
-- También deja su CajaDefecto en las empresas 1 y 3 (Andre hoy tiene 57000000, la Caja Menor; Miguel no tiene fila).
-- Miguel (Miguel Sotillos) no tiene usuario de Windows, como Pedro: entra solo en Ariadna con el usuario «Miguel» de Identity
-- (Ariadna_CrearUsuarios.ps1). El parámetro va con ese nombre, que es el que llega a la API.
-- Idempotente: si la cuenta ya existe no la toca, y si el número está ocupado por otra cosa aborta sin cambiar nada.
-- Lanzar como sa en NV.
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @UsuarioAndre varchar(30) = 'Andre';
DECLARE @UsuarioMiguel varchar(30) = 'Miguel';

DECLARE @cajas TABLE (Cuenta char(10), Concepto char(75), Usuario varchar(30));
INSERT INTO @cajas VALUES ('57000031', 'Caja de Andre', @UsuarioAndre), ('57000032', 'Caja de Miguel', @UsuarioMiguel);
DECLARE @empresas TABLE (Empresa char(3));
INSERT INTO @empresas VALUES ('1'), ('3');

-- El número tiene que estar libre o ser ya esta misma caja; y sin apuntes de otra época.
IF EXISTS (SELECT 1 FROM PlanCuentas p JOIN @cajas c ON c.Cuenta = p.[Nº Cuenta]
           WHERE RTRIM(p.Concepto) <> RTRIM(c.Concepto))
BEGIN RAISERROR('Alguna de las cuentas ya existe con otro concepto: no se cambia nada.', 16, 1); RETURN; END
IF EXISTS (SELECT 1 FROM Contabilidad k JOIN @cajas c ON c.Cuenta = k.[Nº Cuenta]
           WHERE NOT EXISTS (SELECT 1 FROM PlanCuentas p WHERE p.[Nº Cuenta] = c.Cuenta))
BEGIN RAISERROR('Alguna de las cuentas tiene apuntes antiguos: no se cambia nada.', 16, 1); RETURN; END

BEGIN TRAN;

-- De una en una: el trigger trgPlanCuentasUpd hace «(SELECT estado FROM inserted) = -1» y revienta con más de una fila
-- (Mens. 512, 29/10/26 18:09).
DECLARE @pendientes TABLE (Empresa char(3), Cuenta char(10), Concepto char(75));
INSERT INTO @pendientes
SELECT e.Empresa, c.Cuenta, c.Concepto
FROM @cajas c CROSS JOIN @empresas e
WHERE NOT EXISTS (SELECT 1 FROM PlanCuentas p WHERE p.Empresa = e.Empresa AND p.[Nº Cuenta] = c.Cuenta);

DECLARE @creadas int = 0, @emp char(3), @cta char(10), @con char(75);
WHILE EXISTS (SELECT 1 FROM @pendientes)
BEGIN
    SELECT TOP 1 @emp = Empresa, @cta = Cuenta, @con = Concepto FROM @pendientes ORDER BY Cuenta, Empresa;
    INSERT INTO PlanCuentas (Empresa, [Nº Cuenta], Concepto, IVA, DebeHaber, SóloAuto, Bloqueada, Estado, Usuario, [Fecha Modificación])
    SELECT @emp, @cta, @con, m.IVA, m.DebeHaber, m.SóloAuto, m.Bloqueada, m.Estado, SUSER_SNAME(), GETDATE()
    FROM PlanCuentas m WHERE m.Empresa = @emp AND m.[Nº Cuenta] = '57000029';
    SET @creadas += @@ROWCOUNT;
    DELETE FROM @pendientes WHERE Empresa = @emp AND Cuenta = @cta;
END
PRINT CONCAT('Cuentas creadas: ', @creadas);

UPDATE pu SET Valor = c.Cuenta, Usuario2 = SUSER_SNAME(), [Fecha Modificación] = GETDATE()
FROM ParámetrosUsuario pu
JOIN @cajas c ON c.Usuario = pu.Usuario
JOIN @empresas e ON e.Empresa = pu.Empresa
WHERE pu.Clave = 'CajaDefecto' AND RTRIM(pu.Valor) <> RTRIM(c.Cuenta);
PRINT CONCAT('CajaDefecto cambiadas: ', @@ROWCOUNT);

INSERT INTO ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
SELECT e.Empresa, c.Usuario, 'CajaDefecto', c.Cuenta, SUSER_SNAME(), GETDATE()
FROM @cajas c
CROSS JOIN @empresas e
WHERE NOT EXISTS (SELECT 1 FROM ParámetrosUsuario pu WHERE pu.Empresa = e.Empresa AND pu.Usuario = c.Usuario AND pu.Clave = 'CajaDefecto');
PRINT CONCAT('CajaDefecto creadas: ', @@ROWCOUNT);

COMMIT;

SELECT p.Empresa, RTRIM(p.[Nº Cuenta]) Cuenta, RTRIM(p.Concepto) Concepto FROM PlanCuentas p
WHERE p.[Nº Cuenta] IN ('57000031', '57000032') ORDER BY p.[Nº Cuenta], p.Empresa;
SELECT Empresa, RTRIM(Usuario) Usuario, RTRIM(Valor) Caja FROM ParámetrosUsuario
WHERE Clave = 'CajaDefecto' AND Usuario IN (@UsuarioAndre, @UsuarioMiguel) ORDER BY Usuario, Empresa;
