-- VUELTA ATRÁS de Scripts/Issue547_DeshacerFacturaCmp_FechaModificacion.sql (NestoAPI#547, corte b).
-- Quita el «, [Fecha Modificación] = GETDATE()» del UPDATE de LinPedidoCmp de prdDeshacerFacturaCmp y deja el SP como
-- estaba. Mismo mecanismo: lee la definición, cambia una frase (exactamente una vez) y ALTER con QUOTED_IDENTIFIER OFF.
-- Si ya está como antes, no hace nada.
USE NV;
GO

SET QUOTED_IDENTIFIER OFF;
SET ANSI_NULLS ON;
GO

DECLARE @definicion nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.prdDeshacerFacturaCmp'));
DECLARE @antes  nvarchar(200) = N'FechaFactura = null, estado = 2, [Fecha Modificaci' + NCHAR(243) + N'n] = GETDATE() where';
DECLARE @despues nvarchar(200) = N'FechaFactura = null, estado = 2 where';
DECLARE @veces int;

IF @definicion IS NULL
BEGIN
    RAISERROR('No existe dbo.prdDeshacerFacturaCmp. No se cambia nada.', 16, 1);
    RETURN;
END

SET @veces = (LEN(@definicion) - LEN(REPLACE(@definicion, @antes, N''))) / LEN(@antes);
IF @veces = 0
BEGIN
    PRINT 'prdDeshacerFacturaCmp ya está como antes. No se cambia nada.';
    RETURN;
END
IF @veces <> 1 OR LEFT(@definicion, LEN(N'CREATE PROCEDURE')) <> N'CREATE PROCEDURE'
BEGIN
    RAISERROR('La definición no es la esperada (la frase aparece %d veces). No se cambia nada.', 16, 1, @veces);
    RETURN;
END

DECLARE @nueva nvarchar(max) = N'ALTER PROCEDURE' + SUBSTRING(REPLACE(@definicion, @antes, @despues), LEN(N'CREATE PROCEDURE') + 1, 2147483647);
EXEC sp_executesql @nueva;
PRINT 'prdDeshacerFacturaCmp devuelto a su versión anterior.';
GO

SELECT uses_quoted_identifier, uses_ansi_nulls FROM sys.sql_modules WHERE object_id = OBJECT_ID('dbo.prdDeshacerFacturaCmp');
GO
