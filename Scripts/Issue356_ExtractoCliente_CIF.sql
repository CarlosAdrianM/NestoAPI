-- ===========================================================================
-- Script: Issue356_ExtractoCliente_CIF.sql   (BLINDADO)
-- Fecha:  23/07/2026
-- Issue:  NestoAPI#356 (ampliar ExtractoCliente.[CIF/NIF] a varchar(20)) - PESADO/OPCIONAL
--
-- OPCIONAL y de baja prioridad: ExtractoCliente.[CIF/NIF] es una copia denormalizada del NIF
-- que Verifactu NO lee (lee de CabFacturaVta.CifNif, ya varchar(20)). Solo afecta al CIF de un
-- cliente extranjero en el informe de IRPF/modelo 347. Puede quedar pendiente indefinidamente.
--
-- PESADO: ~2,7M filas y [CIF/NIF] esta INCLUIDO en ~6 indices (~4,2 GB) que hay que soltar y
-- RECREAR (mas el clustered, ~0,58 GB). El ALTER + recrear reescribe ~5 GB -> minutos y ~5-6 GB
-- de log. EJECUTAR OFF-HOURS.
--
-- BLINDAJE identico al de Clientes: idempotencia + chequeo de espacio (ABORTA si falta disco) +
-- agarrar la tabla en exclusiva arriba con LOCK_TIMEOUT (fail-fast si esta ocupada) + atomico
-- (rollback completo si falla) + marcas de tiempo por fase.
-- ===========================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @t0 datetime2(0) = SYSDATETIME();
DECLARE @LogEstimadoMB int = 6000;   -- ~4,2 GB indices + 0,58 GB datos + margen

-- ---------- 0. IDEMPOTENCIA ----------
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id
           WHERE c.object_id=OBJECT_ID('dbo.ExtractoCliente') AND c.name='CIF/NIF' AND t.name='varchar')
BEGIN
    PRINT 'IDEMPOTENCIA: ExtractoCliente.[CIF/NIF] ya es varchar. Nada que hacer.';
    RETURN;
END

-- ---------- 1. PRE-FLIGHT: ESPACIO ----------
PRINT '== PRE-FLIGHT ==';
DECLARE @recovery sysname = (SELECT recovery_model_desc FROM sys.databases WHERE database_id=DB_ID());
PRINT 'Recovery model: ' + @recovery
    + CASE WHEN @recovery='FULL' THEN '  -> haz un BACKUP LOG JUSTO ANTES (esta operacion genera ~5-6 GB de log).' ELSE '' END;

DECLARE @logTotMB decimal(12,1), @logUsoMB decimal(12,1);
SELECT @logTotMB = total_log_size_in_bytes/1048576.0, @logUsoMB = used_log_space_in_bytes/1048576.0
FROM sys.dm_db_log_space_usage;
PRINT CONCAT('Log: ', @logTotMB, ' MB total, ', @logUsoMB, ' MB en uso.');

DECLARE @discoLibreMB decimal(15,1) = NULL;
BEGIN TRY
    SELECT TOP 1 @discoLibreMB = vs.available_bytes/1048576.0
    FROM sys.master_files mf CROSS APPLY sys.dm_os_volume_stats(mf.database_id, mf.file_id) vs
    WHERE mf.database_id=DB_ID() AND mf.type_desc='LOG';
END TRY BEGIN CATCH SET @discoLibreMB = NULL; END CATCH

IF @discoLibreMB IS NULL
    PRINT 'Disco libre: no se pudo medir (permisos). Log estimado ~' + CAST(@LogEstimadoMB AS varchar(10)) + ' MB: asegurate MUCHO de tener hueco.';
ELSE
BEGIN
    PRINT CONCAT('Disco libre (unidad del log): ', @discoLibreMB, ' MB.  Log estimado: ~', @LogEstimadoMB, ' MB.');
    IF @discoLibreMB < @LogEstimadoMB * 1.5
    BEGIN
        RAISERROR('ABORTADO: disco libre (%.0f MB) insuficiente para el log estimado (~%d MB, x1,5 de margen). Libera espacio o haz backup de log y reintenta.', 16, 1, @discoLibreMB, @LogEstimadoMB);
        RETURN;
    END
END
PRINT 'Espacio OK.';

-- ---------- 2. OPERACION (agarra la tabla arriba; fail-fast si esta ocupada) ----------
SET LOCK_TIMEOUT 30000;
BEGIN TRAN;
BEGIN TRY

    DECLARE @kk int;
    SELECT TOP 1 @kk = 1 FROM dbo.ExtractoCliente WITH (TABLOCKX, HOLDLOCK);
    PRINT CONCAT('Tabla ExtractoCliente bloqueada en exclusiva (', CONVERT(varchar(19), SYSDATETIME(), 121), ').');

    -- 2a. Capturar CREATE de todos los indices nonclustered (no PK/unique) con [CIF/NIF] (clave o incluida)
    DECLARE @indices TABLE (id INT IDENTITY(1,1), nombre SYSNAME, crear NVARCHAR(MAX));
    INSERT INTO @indices (nombre, crear)
    SELECT i.name,
        'CREATE NONCLUSTERED INDEX ' + QUOTENAME(i.name) + ' ON dbo.ExtractoCliente ('
        + STUFF((SELECT ', ' + QUOTENAME(c.name) + CASE WHEN ic.is_descending_key=1 THEN ' DESC' ELSE '' END
                 FROM sys.index_columns ic JOIN sys.columns c ON ic.object_id=c.object_id AND ic.column_id=c.column_id
                 WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=0
                 ORDER BY ic.key_ordinal FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '')
        + ')'
        + ISNULL(' INCLUDE (' + STUFF((SELECT ', ' + QUOTENAME(c.name)
                 FROM sys.index_columns ic JOIN sys.columns c ON ic.object_id=c.object_id AND ic.column_id=c.column_id
                 WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=1
                 ORDER BY ic.index_column_id FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '') + ')', '')
        + ';'
    FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID('dbo.ExtractoCliente') AND i.type_desc='NONCLUSTERED'
      AND i.is_primary_key=0 AND i.is_unique_constraint=0
      AND EXISTS (SELECT 1 FROM sys.index_columns ic2 JOIN sys.columns c2 ON ic2.object_id=c2.object_id AND ic2.column_id=c2.column_id
                  WHERE ic2.object_id=i.object_id AND ic2.index_id=i.index_id AND c2.name='CIF/NIF');
    DECLARE @nIdx int = (SELECT COUNT(*) FROM @indices);
    PRINT CONCAT('Indices con [CIF/NIF] capturados: ', @nIdx, ' (', DATEDIFF(SECOND,@t0,SYSDATETIME()), 's)');

    -- 2b. Drop indices
    IF @nIdx > 0
    BEGIN
        DECLARE @sql NVARCHAR(MAX)=N'';
        SELECT @sql = @sql + 'DROP INDEX ' + QUOTENAME(nombre) + ' ON dbo.ExtractoCliente;' + CHAR(10) FROM @indices;
        EXEC sys.sp_executesql @sql;
        PRINT CONCAT('Indices eliminados (', DATEDIFF(SECOND,@t0,SYSDATETIME()), 's)');
    END

    -- 2c. Drop estadisticas independientes sobre [CIF/NIF]
    DECLARE @sqlStats NVARCHAR(MAX)=N'';
    SELECT @sqlStats = @sqlStats + 'DROP STATISTICS dbo.ExtractoCliente.' + QUOTENAME(s.name) + ';' + CHAR(10)
    FROM sys.stats s
    WHERE s.object_id=OBJECT_ID('dbo.ExtractoCliente')
      AND NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=s.object_id AND i.name=s.name)
      AND EXISTS (SELECT 1 FROM sys.stats_columns sc JOIN sys.columns c ON sc.object_id=c.object_id AND sc.column_id=c.column_id
                  WHERE sc.object_id=s.object_id AND sc.stats_id=s.stats_id AND c.name='CIF/NIF');
    IF @sqlStats <> N'' BEGIN EXEC sys.sp_executesql @sqlStats; PRINT 'Estadisticas sobre [CIF/NIF] eliminadas.'; END

    -- 2d. Ampliar (SIN RTRIM: el padding es inocuo y el codigo hace Trim; un UPDATE de 2,7M filas
    --     tocando los 6 indices seria decenas de GB de log por puro cosmetico).
    ALTER TABLE dbo.ExtractoCliente ALTER COLUMN [CIF/NIF] varchar(20) NULL;
    PRINT CONCAT('[CIF/NIF] ampliado a varchar(20) (', DATEDIFF(SECOND,@t0,SYSDATETIME()), 's)');

    -- 2e. Recrear indices (lo pesado: ~4,2 GB sobre 2,7M filas)
    DECLARE @i int=1, @crear NVARCHAR(MAX);
    WHILE @i <= ISNULL(@nIdx,0)
    BEGIN
        SELECT @crear = crear FROM @indices WHERE id=@i;
        EXEC sys.sp_executesql @crear;
        PRINT CONCAT('  indice ', @i, '/', @nIdx, ' recreado (', DATEDIFF(SECOND,@t0,SYSDATETIME()), 's)');
        SET @i += 1;
    END

    COMMIT TRAN;
    PRINT CONCAT('OK: ExtractoCliente listo. Duracion total: ', DATEDIFF(SECOND,@t0,SYSDATETIME()), ' s.');

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    IF ERROR_NUMBER() = 1222
        RAISERROR('ABORTADO: no se pudo bloquear ExtractoCliente en 30 s (tabla ocupada). NO se ha tocado nada. Reintenta con menos actividad.', 16, 1);
    ELSE
    BEGIN
        DECLARE @e nvarchar(2000) = ERROR_MESSAGE();
        RAISERROR('ERROR (rollback hecho, NADA aplicado): %s', 16, 1, @e);
    END
END CATCH
GO
