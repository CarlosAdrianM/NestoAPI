-- VUELTA ATRÁS de Scripts/Issue547_DeshacerAlbaranCmp_FechaModificacion.sql (NestoAPI#547).
-- Quita el «, [Fecha Modificación] = GETDATE()» del UPDATE de LinPedidoCmp de prdDeshacerAlbaránCmp y deja el SP como
-- estaba. Mismo mecanismo: lee la definición, cambia una frase (exactamente una vez) y ALTER con QUOTED_IDENTIFIER ON
-- (el que tiene este SP). Si ya está como antes, no hace nada.
USE NV;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

DECLARE @nombre nvarchar(200) = N'dbo.prdDeshacerAlbar' + NCHAR(225) + N'nCmp';
DECLARE @definicion nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(@nombre));
DECLARE @antes  nvarchar(200) = N'fechaalbar' + NCHAR(225) + N'n = null, estado = 1, [Fecha Modificaci' + NCHAR(243) + N'n] = GETDATE() where';
DECLARE @despues nvarchar(200) = N'fechaalbar' + NCHAR(225) + N'n = null, estado = 1 where';
DECLARE @veces int;

IF @definicion IS NULL
BEGIN
    RAISERROR('No existe dbo.prdDeshacerAlbaránCmp. No se cambia nada.', 16, 1);
    RETURN;
END

SET @veces = (LEN(@definicion) - LEN(REPLACE(@definicion, @antes, N''))) / LEN(@antes);
IF @veces = 0
BEGIN
    PRINT 'prdDeshacerAlbaránCmp ya está como antes. No se cambia nada.';
    RETURN;
END
DECLARE @inicio int = CHARINDEX(N'CREATE PROCEDURE', @definicion);
IF @veces <> 1 OR @inicio = 0 OR @inicio > 2
BEGIN
    RAISERROR('La definición no es la esperada (la frase aparece %d veces). No se cambia nada.', 16, 1, @veces);
    RETURN;
END

DECLARE @nueva nvarchar(max) = N'ALTER PROCEDURE' + SUBSTRING(REPLACE(@definicion, @antes, @despues), @inicio + LEN(N'CREATE PROCEDURE'), 2147483647);
EXEC sp_executesql @nueva;
PRINT 'prdDeshacerAlbaránCmp devuelto a su versión anterior.';
GO

SELECT uses_quoted_identifier, uses_ansi_nulls FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerAlbar' + NCHAR(225) + N'nCmp');
GO
