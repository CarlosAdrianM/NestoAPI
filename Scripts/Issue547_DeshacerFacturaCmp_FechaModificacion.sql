-- NestoAPI#547, incremental de precios medios, corte (b): DESHACER una factura de compra tiene que recalcular la
-- media de sus productos.
--
-- Problema: prdDeshacerFacturaCmp (no lo llama NestoAPI ni Nesto: se ejecuta desde fuera, Access/SSMS) devuelve las
-- líneas a estado 2 con NºFactura y FechaFactura nulos y BORRA CabFacturaCmp. No toca LinPedidoCmp.[Fecha
-- Modificación] (no hay trigger que lo haga), así que la selección nocturna del incremental («compras modificadas
-- desde la última pasada», por LinPedidoCmp.[Fecha Modificación] o CabFacturaCmp.[Fecha Modificación]) NO lo ve: la
-- media de esos productos seguiría contando la compra deshecha hasta el SP del domingo.
--
-- Arreglo: en el UPDATE de las líneas que hace el propio SP, poner también [Fecha Modificación] = GETDATE(). Así, la
-- siguiente pasada nocturna encola esos productos como cualquier otra compra modificada (y, al volver a facturar por
-- NestoAPI, también se encolan al momento). Es el ÚNICO cambio: el resto del SP queda byte a byte igual.
--
-- CÓMO: no se reescribe el SP a mano (300 líneas con tildes y comentarios de 2002). Se lee su definición, se cambia
-- UNA frase (se comprueba que aparece exactamente una vez) y se ejecuta como ALTER con QUOTED_IDENTIFIER OFF y
-- ANSI_NULLS ON, que son los que tiene hoy (sys.sql_modules: uses_quoted_identifier = 0). Si algo no cuadra, no
-- toca nada. Idempotente: si ya está cambiado, no hace nada.
--
-- Vuelta atrás: Scripts/Issue547_DeshacerFacturaCmp_FechaModificacion_ROLLBACK.sql
-- Ejecutar en NV como sa, fuera de horario de facturación de compras (es un ALTER de un SP: un momento).
USE NV;
GO

-- OJO: los SET tienen que estar ANTES del sp_executesql (el ALTER toma los de la sesión). NO cambiar.
SET QUOTED_IDENTIFIER OFF;
SET ANSI_NULLS ON;
GO

DECLARE @definicion nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.prdDeshacerFacturaCmp'));
DECLARE @antes  nvarchar(200) = N'FechaFactura = null, estado = 2 where';
DECLARE @despues nvarchar(200) = N'FechaFactura = null, estado = 2, [Fecha Modificaci' + NCHAR(243) + N'n] = GETDATE() where';
DECLARE @veces int;

IF @definicion IS NULL
BEGIN
    RAISERROR('No existe dbo.prdDeshacerFacturaCmp (o no hay permiso para ver su definición). No se cambia nada.', 16, 1);
    RETURN;
END

IF CHARINDEX(@despues, @definicion) > 0
BEGIN
    PRINT 'prdDeshacerFacturaCmp ya pone [Fecha Modificación]. No se cambia nada.';
    RETURN;
END

SET @veces = (LEN(@definicion) - LEN(REPLACE(@definicion, @antes, N''))) / LEN(@antes);
IF @veces <> 1
BEGIN
    RAISERROR('La frase a cambiar aparece %d veces en prdDeshacerFacturaCmp (se esperaba 1). No se cambia nada.', 16, 1, @veces);
    RETURN;
END

IF LEFT(@definicion, LEN(N'CREATE PROCEDURE')) <> N'CREATE PROCEDURE'
BEGIN
    RAISERROR('La definición de prdDeshacerFacturaCmp no empieza por CREATE PROCEDURE. No se cambia nada.', 16, 1);
    RETURN;
END

DECLARE @nueva nvarchar(max) = N'ALTER PROCEDURE' + SUBSTRING(REPLACE(@definicion, @antes, @despues), LEN(N'CREATE PROCEDURE') + 1, 2147483647);
EXEC sp_executesql @nueva;
PRINT 'prdDeshacerFacturaCmp cambiado: el UPDATE de LinPedidoCmp pone ya [Fecha Modificación] = GETDATE().';
GO

-- Comprobación: debe salir la línea nueva y uses_quoted_identifier = 0, uses_ansi_nulls = 1 (como antes).
SELECT uses_quoted_identifier, uses_ansi_nulls, modify_date
FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id
WHERE m.object_id = OBJECT_ID('dbo.prdDeshacerFacturaCmp');
SELECT SUBSTRING(d, CHARINDEX(N'update LinPedidoCmp set', d), 200) AS UpdateLineas
FROM (SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.prdDeshacerFacturaCmp')) AS d) x;
GO
