/*
    Carlos 26/11/26: fichas de cliente DUPLICADAS (la misma persona con dos números de cliente, descubiertas por el control de NIF de
    Verifactu). El 26/11/26 se dejó por la API una sola ficha viva por persona, con el NIF bien, y la otra de baja con el motivo
    «Duplicado». Este script las ENLAZA en dbo.ClientesAnteriores (NestoAPI#876), la misma tabla del cambio de forma fiscal: la ficha
    viva apunta a la duplicada como su «cliente anterior». Así queda constancia y, cuando se conecte la pieza de clientes
    equivalentes, el histórico de las dos saldrá junto.

    La tabla admite un solo anterior por cliente; cuando hay tres fichas de la misma persona se encadenan (39570 <- 14315 <- 5868).

    Solo enlaza un par si: las dos fichas existen, la anterior está de baja en todos sus contactos, y la ficha que apunta no tiene ya
    un anterior. Lo demás lo salta y lo dice. No toca ninguna ficha. Lanzar como sa en NV. Se puede lanzar más de una vez.
*/
USE NV;
GO
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NULL
BEGIN
    RAISERROR(N'Falta dbo.ClientesAnteriores (Scripts/Issue876_ClientesAnteriores.sql). No se ha hecho nada.', 16, 1);
    SET NOEXEC ON;
END
GO
DECLARE @pares TABLE (Cliente varchar(10) NOT NULL, Anterior varchar(10) NOT NULL);
INSERT @pares (Cliente, Anterior) VALUES
    ('26815', '8244'),
    ('39570', '14315'),
    ('14315', '5868'),
    ('13302', '8055'),
    ('27390', '3901'),
    ('15579', '5957'),
    ('15878', '8089'),
    ('17438', '25321'),
    ('27314', '17980');

DECLARE @resultado TABLE (Cliente varchar(10), Anterior varchar(10), Resultado nvarchar(200));

INSERT @resultado (Cliente, Anterior, Resultado)
SELECT p.Cliente, p.Anterior,
       CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.Clientes c WHERE c.Empresa = '1' AND RTRIM(c.[Nº Cliente]) = p.Cliente) THEN N'SALTADO: no existe el cliente'
            WHEN NOT EXISTS (SELECT 1 FROM dbo.Clientes c WHERE c.Empresa = '1' AND RTRIM(c.[Nº Cliente]) = p.Anterior) THEN N'SALTADO: no existe el anterior'
            WHEN EXISTS (SELECT 1 FROM dbo.Clientes c WHERE c.Empresa = '1' AND RTRIM(c.[Nº Cliente]) = p.Anterior AND c.Estado >= 0) THEN N'SALTADO: el anterior no está de baja'
            WHEN EXISTS (SELECT 1 FROM dbo.ClientesAnteriores a WHERE a.Empresa = '1' AND RTRIM(a.Cliente) = p.Cliente AND RTRIM(a.ClienteAnterior) = p.Anterior) THEN N'Ya estaba enlazado'
            WHEN EXISTS (SELECT 1 FROM dbo.ClientesAnteriores a WHERE a.Empresa = '1' AND RTRIM(a.Cliente) = p.Cliente) THEN N'SALTADO: el cliente ya tiene otro anterior'
            ELSE N'ENLAZAR' END
FROM @pares p;

BEGIN TRANSACTION;
INSERT INTO dbo.ClientesAnteriores (Empresa, Cliente, ClienteAnterior, Fecha, Usuario)
SELECT '1', r.Cliente, r.Anterior, GETDATE(), 'Duplicado 26/11/26'
FROM @resultado r WHERE r.Resultado = N'ENLAZAR';
PRINT CONCAT('Pares enlazados: ', @@ROWCOUNT);
UPDATE @resultado SET Resultado = N'Enlazado' WHERE Resultado = N'ENLAZAR';
COMMIT TRANSACTION;

SELECT Cliente, Anterior, Resultado FROM @resultado ORDER BY Cliente;
GO
SELECT RTRIM(a.Cliente) AS Cliente, RTRIM(a.ClienteAnterior) AS ClienteAnterior, a.Fecha, RTRIM(a.Usuario) AS Usuario
FROM dbo.ClientesAnteriores a WHERE a.Empresa = '1' ORDER BY a.Fecha, a.Cliente;
GO
SET NOEXEC OFF;
GO
