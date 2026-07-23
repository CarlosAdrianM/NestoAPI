-- ===========================================================================
-- Script: Issue356_355_Clientes_CIF_Pais.sql
-- Fecha:  23/07/2026
-- Issues: NestoAPI#356 (ampliar [CIF/NIF] a varchar(20) para NIF extranjeros)
--         NestoAPI#355 (columna Pais en Clientes para el alta de cliente)
--
-- QUE HACE (tabla Clientes, ~64K filas, rapido):
--   1. Captura y elimina TODOS los indices _dta_ que referencian [CIF/NIF] (como
--      clave O como columna incluida) y TODAS las estadisticas _dta_stat_ sobre la
--      columna: cualquiera de esos objetos bloquea el ALTER COLUMN. Los indices se
--      capturan por metadatos vivos y se RECREAN identicos al final (se conserva el
--      rendimiento). Las estadisticas _dta_stat_ NO se recrean (son cruft del Tuning
--      Advisor; SQL Server auto-genera las que hagan falta).
--   2. Amplia Clientes.[CIF/NIF] de char(9) a varchar(20) y quita el padding heredado.
--   3. Anade Clientes.Pais varchar(2) DEFAULT 'ES' y rellena 'ES' en el historico.
--
-- Idempotente: si ya esta hecho (CIF ya es varchar) no captura indices y solo asegura Pais.
-- Todo en UNA transaccion con XACT_ABORT: si algo falla, deshace TODO (no deja indices caidos).
--
-- EJECUTAR ANTES DE DESPLEGAR LA API (el EDMX ya espera varchar(20) y la columna Pais).
--
-- PENDIENTE APARTE (no en este script):
--   * Issue356_ExtractoCliente_CIF.sql -> ampliar ExtractoCliente.[CIF/NIF] (2,7M filas,
--     pesado: ventana de mantenimiento).
--   * SPs con @cif char(9) local (truncarian un NIF extranjero al facturar/remesar). Cambiar
--     en SSMS ANTES del primer cliente extranjero real:
--        - prdCrearFacturaVta      : "declare @cif as char(9)"            -> varchar(20)
--        - prdContabilizar         : parametro "@CIF char(9)"             -> varchar(20)
--        - prdCrearRemesaIso20022  : columna tabla-var "cif varchar(9)"   -> varchar(20)
-- ===========================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRAN;

-- 1a. Capturar el CREATE de TODOS los indices nonclustered (no PK/unique) que referencian
--     [CIF/NIF], sea como clave o como columna incluida.
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
  AND i.type_desc = 'NONCLUSTERED'
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
  AND EXISTS (SELECT 1 FROM sys.index_columns ic2
              JOIN sys.columns c2 ON ic2.object_id = c2.object_id AND ic2.column_id = c2.column_id
              WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id
                AND c2.name = 'CIF/NIF');   -- clave O incluida

DECLARE @capturados INT = (SELECT COUNT(*) FROM @indices);
PRINT CONCAT('Indices con [CIF/NIF] capturados (clave o incluida): ', @capturados);

-- 1b. Eliminar esos indices.
IF @capturados > 0
BEGIN
    DECLARE @sql NVARCHAR(MAX) = N'';
    SELECT @sql = @sql + 'DROP INDEX ' + QUOTENAME(nombre) + ' ON dbo.Clientes;' + CHAR(10) FROM @indices;
    EXEC sys.sp_executesql @sql;
    PRINT 'Indices eliminados.';
END

-- 1c. Eliminar las estadisticas independientes (_dta_stat_ / _WA_Sys_) que referencian
--     [CIF/NIF] y que tambien bloquean el ALTER. Las de indice se fueron con su indice.
DECLARE @sqlStats NVARCHAR(MAX) = N'';
SELECT @sqlStats = @sqlStats + 'DROP STATISTICS dbo.Clientes.' + QUOTENAME(s.name) + ';' + CHAR(10)
FROM sys.stats s
WHERE s.object_id = OBJECT_ID('dbo.Clientes')
  AND NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id = s.object_id AND i.name = s.name)
  AND EXISTS (SELECT 1 FROM sys.stats_columns sc
              JOIN sys.columns c ON sc.object_id = c.object_id AND sc.column_id = c.column_id
              WHERE sc.object_id = s.object_id AND sc.stats_id = s.stats_id AND c.name = 'CIF/NIF');
IF @sqlStats <> N''
BEGIN
    EXEC sys.sp_executesql @sqlStats;
    PRINT 'Estadisticas sobre [CIF/NIF] eliminadas.';
END

-- 2. Ampliar [CIF/NIF] a varchar(20) y quitar el padding del antiguo char(9).
ALTER TABLE dbo.Clientes ALTER COLUMN [CIF/NIF] varchar(20) NULL;
UPDATE dbo.Clientes SET [CIF/NIF] = RTRIM([CIF/NIF]) WHERE [CIF/NIF] IS NOT NULL AND [CIF/NIF] <> RTRIM([CIF/NIF]);
PRINT 'Columna [CIF/NIF] ampliada a varchar(20) y sin padding.';

-- 3. Columna Pais (ISO-2) con 'ES' por defecto. El backfill va por SQL dinamico A PROPOSITO:
--    SQL Server valida los nombres de columna al COMPILAR el lote (antes de ejecutar el ADD),
--    asi que un 'UPDATE ... SET Pais' estatico en el mismo batch daria "columna no valida".
IF COL_LENGTH('dbo.Clientes', 'Pais') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD Pais varchar(2) NULL
        CONSTRAINT DF_Clientes_Pais DEFAULT 'ES';
    PRINT 'Columna Pais anadida (DEFAULT ES).';
END
EXEC ('UPDATE dbo.Clientes SET Pais = ''ES'' WHERE Pais IS NULL;');
PRINT 'Pais rellenado a ES en el historico.';

-- 4. Recrear los indices identicos (ahora sobre la columna ya ampliada).
DECLARE @i INT = 1, @n INT = (SELECT MAX(id) FROM @indices), @crear NVARCHAR(MAX);
WHILE @i <= ISNULL(@n, 0)
BEGIN
    SELECT @crear = crear FROM @indices WHERE id = @i;
    EXEC sys.sp_executesql @crear;
    SET @i += 1;
END
PRINT CONCAT('Indices recreados: ', ISNULL(@n, 0));

COMMIT TRAN;
PRINT 'OK: Clientes.[CIF/NIF] varchar(20) + Pais listo.';
GO
