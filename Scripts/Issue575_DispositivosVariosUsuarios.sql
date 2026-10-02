-- NestoAPI#575 (fase 3, push de Ariadna): el token de una PDA lo comparten varios mozos.
-- Hoy UQ_DispositivosNotificaciones_Token obliga a un usuario por token. Se cambia por UNA FILA POR TOKEN Y USUARIO.
-- NestoApp y TNV no cambian: su código sigue buscando el token sin mirar el usuario (un móvil, una persona).
--
-- Lanzar como sa en NV ANTES de publicar la versión de Ariadna con push (la API funciona sin él: si falta, el
-- segundo mozo de una PDA se queda el token del primero y se avisa en ELMAH).
-- Se puede repetir sin miedo.
--
-- Nota: Token es nvarchar(1000) y la clave pasa de 2.000 a 2.200 bytes (más de 1.700): SQL Server crea el índice con
-- un aviso, igual que el UQ actual. Los tokens de FCM rondan los 160 caracteres (~320 bytes), muy lejos del límite.
USE NV;
GO

-- 0) Comprobación: no puede haber dos filas con el mismo token y usuario (con el UQ actual es imposible)
SELECT Token, Usuario, COUNT(*) AS Filas
FROM dbo.DispositivosNotificaciones
GROUP BY Token, Usuario
HAVING COUNT(*) > 1;
GO

BEGIN TRAN;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_DispositivosNotificaciones_Token' AND object_id = OBJECT_ID('dbo.DispositivosNotificaciones'))
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_DispositivosNotificaciones_Token')
        ALTER TABLE dbo.DispositivosNotificaciones DROP CONSTRAINT UQ_DispositivosNotificaciones_Token;
    ELSE
        DROP INDEX UQ_DispositivosNotificaciones_Token ON dbo.DispositivosNotificaciones;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_DispositivosNotificaciones_TokenUsuario' AND object_id = OBJECT_ID('dbo.DispositivosNotificaciones'))
    CREATE UNIQUE INDEX UQ_DispositivosNotificaciones_TokenUsuario
        ON dbo.DispositivosNotificaciones (Token, Usuario);

-- Comprobación: debe salir el índice nuevo y no el antiguo
SELECT i.name, i.is_unique
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('dbo.DispositivosNotificaciones') AND i.name LIKE 'UQ_%';

COMMIT;
GO
