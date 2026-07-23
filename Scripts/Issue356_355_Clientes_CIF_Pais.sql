-- ===========================================================================
-- Script: Issue356_355_Clientes_CIF_Pais.sql
-- Fecha:  23/07/2026
-- Issues: NestoAPI#356 (ampliar [CIF/NIF] a varchar(20) para NIF extranjeros)
--         NestoAPI#355 (columna Pais en Clientes para el alta de cliente)
--
-- QUE HACE (tabla Clientes, ~64K filas, rapido):
--   1. Captura y elimina los 11 indices _dta_ que tienen [CIF/NIF] como CLAVE
--      (bloquean el ALTER). Se capturan por metadatos vivos (nombres correctos)
--      y se RECREAN identicos al final -> se conserva el rendimiento actual.
--   2. Amplia Clientes.[CIF/NIF] de char(9) a varchar(20) y quita el padding
--      heredado del char (RTRIM).
--   3. Anade Clientes.Pais varchar(2) y rellena 'ES' en todo el historico.
--
-- EJECUTAR ESTE SCRIPT ANTES DE DESPLEGAR LA API (el EDMX ya espera varchar(20)
-- y la columna Pais). No necesita GRANT (columna nueva hereda permisos de tabla).
--
-- PENDIENTE APARTE (no en este script):
--   * Issue356_ExtractoCliente_CIF.sql  -> ampliar ExtractoCliente.[CIF/NIF]
--     (2,7M filas + 62 indices con CIF incluido: pesado, ventana de mantenimiento).
--   * SPs con @cif char(9) local (truncarian un NIF extranjero al facturar/remesar).
--     Cambiar en SSMS (ahi la codificacion es correcta), ANTES del primer cliente
--     extranjero real:
--        - prdCrearFacturaVta      : "declare @cif as char(9)"   -> varchar(20)
--        - prdContabilizar         : parametro "@CIF char(9)"     -> varchar(20)
--        - prdCrearRemesaIso20022  : columna tabla-var "cif varchar(9)" -> varchar(20)
-- ===========================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRAN;

-- 1a. Capturar la definicion CREATE de los 11 indices DTA que keyean [CIF/NIF].
DECLARE @indices TABLE (id INT IDENTITY(1,1), nombre SYSNAME, crear NVARCHAR(MAX));

INSERT INTO @indices (nombre, crear)
SELECT i.name,
    'CREATE NONCLUSTERED INDEX ' + QUOTENAME(i.name) + ' ON dbo.Clientes ('
    + STUFF((SELECT ', ' + QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE '' END
             FROM sys.index_columns ic
             JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
             ORDER BY ic.key_ordinal
             FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '')
    + ')'
    + ISNULL(' INCLUDE (' + STUFF((SELECT ', ' + QUOTENAME(c.name)
             FROM sys.index_columns ic
             JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
             ORDER BY ic.index_column_id
             FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '') + ')', '')
    + ';'
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('dbo.Clientes')
  AND i.name LIKE '\_dta\_%' ESCAPE '\'
  AND EXISTS (SELECT 1 FROM sys.index_columns ic2
              JOIN sys.columns c2 ON ic2.object_id = c2.object_id AND ic2.column_id = c2.column_id
              WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id
                AND ic2.is_included_column = 0 AND c2.name = 'CIF/NIF');

DECLARE @capturados INT = (SELECT COUNT(*) FROM @indices);
PRINT CONCAT('Indices DTA con [CIF/NIF] como clave capturados: ', @capturados);
IF @capturados <> 11
BEGIN
    -- Si el numero no cuadra, algo cambio en la tabla: abortar antes de tocar nada.
    ROLLBACK TRAN;
    RAISERROR('Se esperaban 11 indices DTA con CIF/NIF como clave y se han encontrado %d. Revisar antes de continuar.', 16, 1, @capturados);
    RETURN;
END

-- 1b. Eliminar esos 11 indices (imprescindible para poder cambiar el tipo de la columna).
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql = @sql + 'DROP INDEX ' + QUOTENAME(nombre) + ' ON dbo.Clientes;' + CHAR(10) FROM @indices;
EXEC sys.sp_executesql @sql;
PRINT 'Indices eliminados.';

-- 2. Ampliar [CIF/NIF] a varchar(20) y quitar el padding del antiguo char(9).
ALTER TABLE dbo.Clientes ALTER COLUMN [CIF/NIF] varchar(20) NULL;
UPDATE dbo.Clientes SET [CIF/NIF] = RTRIM([CIF/NIF]) WHERE [CIF/NIF] IS NOT NULL AND [CIF/NIF] <> RTRIM([CIF/NIF]);
PRINT 'Columna [CIF/NIF] ampliada a varchar(20) y sin padding.';

-- 3. Columna Pais (ISO-2) con 'ES' por defecto (historico y futuros INSERT que no lo indiquen,
--    p.ej. el codigo VIEJO durante la ventana entre el script y el deploy: encaja con la regla
--    "todos ES por defecto"). El DEFAULT se crea junto con la columna para no duplicarlo al re-ejecutar.
IF COL_LENGTH('dbo.Clientes', 'Pais') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD Pais varchar(2) NULL
        CONSTRAINT DF_Clientes_Pais DEFAULT 'ES';
    PRINT 'Columna Pais anadida (DEFAULT ES).';
END
-- El backfill va por SQL dinamico A PROPOSITO: SQL Server valida los nombres de columna al
-- COMPILAR el lote (antes de ejecutar el ADD de arriba), asi que un 'UPDATE ... SET Pais'
-- estatico en el mismo batch daria "columna no valida". El EXEC difiere esa validacion a
-- ejecucion, cuando la columna ya existe.
EXEC ('UPDATE dbo.Clientes SET Pais = ''ES'' WHERE Pais IS NULL;');
PRINT 'Pais rellenado a ES en el historico.';

-- 4. Recrear los 11 indices identicos (ahora keyean sobre la columna ya ampliada).
DECLARE @i INT = 1, @n INT = (SELECT MAX(id) FROM @indices), @crear NVARCHAR(MAX);
WHILE @i <= @n
BEGIN
    SELECT @crear = crear FROM @indices WHERE id = @i;
    EXEC sys.sp_executesql @crear;
    SET @i += 1;
END
PRINT CONCAT('Indices recreados: ', @n);

COMMIT TRAN;
PRINT 'OK: Clientes.[CIF/NIF] varchar(20) + Pais listo.';
GO
