-- NestoAPI#547, incremental de precios medios: DESHACER un albarán de compra tiene que recalcular la media de sus
-- productos (gemelo de Issue547_DeshacerFacturaCmp_FechaModificacion.sql).
--
-- Problema: prdDeshacerAlbaránCmp devuelve las líneas a estado 1 con NºAlbarán y FechaAlbarán nulos, pero no toca
-- LinPedidoCmp.[Fecha Modificación] (no hay trigger que lo haga), así que la selección nocturna del incremental
-- («compras modificadas desde la última pasada») NO lo ve: la media de esos productos seguiría contando la entrada
-- deshecha hasta el SP del domingo.
--
-- Arreglo: en el UPDATE de las líneas que hace el propio SP, poner también [Fecha Modificación] = GETDATE(). Es el
-- ÚNICO cambio: el resto del SP queda byte a byte igual.
--
-- CÓMO: igual que el de facturas. Se lee la definición, se cambia UNA frase (se comprueba que aparece exactamente una
-- vez) y se ejecuta como ALTER. OJO, este SP NO tiene los mismos SET que prdDeshacerFacturaCmp: hoy tiene
-- uses_quoted_identifier = 1 y uses_ansi_nulls = 1 (comprobado 01/10/26), así que el ALTER va con
-- QUOTED_IDENTIFIER ON. Si algo no cuadra, no toca nada. Idempotente: si ya está cambiado, no hace nada.
--
-- Vuelta atrás: Scripts/Issue547_DeshacerAlbaranCmp_FechaModificacion_ROLLBACK.sql
-- Ejecutar en NV como sa, fuera de horario de recepción de compras (es un ALTER de un SP: un momento).
USE NV;
GO

-- OJO: los SET tienen que estar ANTES del sp_executesql (el ALTER toma los de la sesión). NO cambiar.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Las tildes van con NCHAR para no depender de la codificación con la que se abra el fichero (225 = á, 243 = ó).
DECLARE @nombre nvarchar(200) = N'dbo.prdDeshacerAlbar' + NCHAR(225) + N'nCmp';
DECLARE @definicion nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(@nombre));
DECLARE @antes  nvarchar(200) = N'fechaalbar' + NCHAR(225) + N'n = null, estado = 1 where';
DECLARE @despues nvarchar(200) = N'fechaalbar' + NCHAR(225) + N'n = null, estado = 1, [Fecha Modificaci' + NCHAR(243) + N'n] = GETDATE() where';
DECLARE @veces int;

IF @definicion IS NULL
BEGIN
    RAISERROR('No existe dbo.prdDeshacerAlbaránCmp (o no hay permiso para ver su definición). No se cambia nada.', 16, 1);
    RETURN;
END

IF CHARINDEX(@despues, @definicion) > 0
BEGIN
    PRINT 'prdDeshacerAlbaránCmp ya pone [Fecha Modificación]. No se cambia nada.';
    RETURN;
END

SET @veces = (LEN(@definicion) - LEN(REPLACE(@definicion, @antes, N''))) / LEN(@antes);
IF @veces <> 1
BEGIN
    RAISERROR('La frase a cambiar aparece %d veces en prdDeshacerAlbaránCmp (se esperaba 1). No se cambia nada.', 16, 1, @veces);
    RETURN;
END

-- Por si la definición trajera un BOM delante del CREATE (hoy no lo trae), se busca el CREATE PROCEDURE en las 2 primeras posiciones.
DECLARE @inicio int = CHARINDEX(N'CREATE PROCEDURE', @definicion);
IF @inicio = 0 OR @inicio > 2
BEGIN
    RAISERROR('La definición de prdDeshacerAlbaránCmp no empieza por CREATE PROCEDURE. No se cambia nada.', 16, 1);
    RETURN;
END

DECLARE @nueva nvarchar(max) = N'ALTER PROCEDURE' + SUBSTRING(REPLACE(@definicion, @antes, @despues), @inicio + LEN(N'CREATE PROCEDURE'), 2147483647);
EXEC sp_executesql @nueva;
PRINT 'prdDeshacerAlbaránCmp cambiado: el UPDATE de LinPedidoCmp pone ya [Fecha Modificación] = GETDATE().';
GO

-- Comprobación: debe salir la línea nueva y uses_quoted_identifier = 1, uses_ansi_nulls = 1 (como antes).
SELECT uses_quoted_identifier, uses_ansi_nulls, modify_date
FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id
WHERE m.object_id = OBJECT_ID(N'dbo.prdDeshacerAlbar' + NCHAR(225) + N'nCmp');
SELECT SUBSTRING(d, CHARINDEX(N'update linpedidocmp set N', d), 200) AS UpdateLineas
FROM (SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.prdDeshacerAlbar' + NCHAR(225) + N'nCmp')) AS d) x;
GO
