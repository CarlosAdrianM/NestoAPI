-- ===========================================================================
-- Script: Issue356_355_Clientes_CIF_Pais.sql   (BLINDADO)
-- Fecha:  23/07/2026
-- Issues: NestoAPI#356 (ampliar Clientes.[CIF/NIF] a varchar(20)) + #355 (Pais)
--
-- Parte PESADA (off-hours). La columna Pais ya se puede anadir aparte y sin bloqueo con
-- Issue355_Clientes_Pais_rapido.sql; este script hace el ensanchado del CIF (dropea/recrea
-- los indices que lo referencian) y, por idempotencia, tambien deja Pais si falta.
--
-- BLINDAJE (comprueba TODO antes de tocar nada; si algo no cuadra, aborta sin cambios):
--   * Idempotencia: si el CIF ya es varchar, no rehace el ensanchado.
--   * Espacio: informa recovery model + log + DISCO LIBRE de la unidad del log, y ABORTA si
--     no hay hueco para el log estimado (evita llenar el disco a media operacion).
--   * Lock: agarra Clientes en EXCLUSIVA al principio con LOCK_TIMEOUT; si esta ocupada FALLA
--     AL INSTANTE sin haber tocado nada (nunca se queda colgado a mitad). Una vez agarrada,
--     corre entera y atomica: cualquier fallo hace rollback COMPLETO (no deja indices caidos).
-- ===========================================================================

-- QUOTED_IDENTIFIER/ANSI_NULLS deben ir ON: Clientes tiene indice(s) filtrado(s) o sobre
-- columna calculada, y sqlcmd conecta con QUOTED_IDENTIFIER OFF por defecto (a diferencia de
-- SSMS). Sin esto, el UPDATE/recreacion de indices falla con msg 1934.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @t0 datetime2(0) = SYSDATETIME();
DECLARE @LogEstimadoMB int = 400;   -- ~13 indices (subconjunto de los 235 MB DTA) + ALTER + RTRIM

-- ---------- 0. IDEMPOTENCIA ----------
DECLARE @tipoCIF sysname = (SELECT t.name FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id
                            WHERE c.object_id=OBJECT_ID('dbo.Clientes') AND c.name='CIF/NIF');
DECLARE @cifYaAncho bit = CASE WHEN @tipoCIF = 'varchar' THEN 1 ELSE 0 END;
IF @cifYaAncho = 1
    PRINT 'IDEMPOTENCIA: Clientes.[CIF/NIF] ya es varchar; solo se asegurara la columna Pais.';

-- ---------- 1. PRE-FLIGHT: ESPACIO ----------
PRINT '== PRE-FLIGHT ==';
DECLARE @recovery sysname = (SELECT recovery_model_desc FROM sys.databases WHERE database_id=DB_ID());
PRINT 'Recovery model: ' + @recovery
    + CASE WHEN @recovery='FULL' THEN '  -> si el log va justo, haz un BACKUP LOG JUSTO ANTES de este script.' ELSE '' END;

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

IF @cifYaAncho = 0
BEGIN
    IF @discoLibreMB IS NULL
        PRINT 'Disco libre: no se pudo medir (permisos). Log estimado ~' + CAST(@LogEstimadoMB AS varchar(10)) + ' MB: asegurate de tener hueco.';
    ELSE
    BEGIN
        PRINT CONCAT('Disco libre (unidad del log): ', @discoLibreMB, ' MB.  Log estimado: ~', @LogEstimadoMB, ' MB.');
        IF @discoLibreMB < @LogEstimadoMB * 1.5
        BEGIN
            -- RAISERROR no admite decimal/float como parametro de sustitucion (msg 2748);
            -- se compone el mensaje con CONCAT y se lanza sin sustituciones.
            DECLARE @msgAbort nvarchar(400) = CONCAT(
                'ABORTADO: disco libre (', CAST(@discoLibreMB AS int),
                ' MB) insuficiente para el log estimado (~', @LogEstimadoMB,
                ' MB, x1,5 de margen). Libera espacio o haz backup de log y reintenta.');
            RAISERROR(@msgAbort, 16, 1);
            RETURN;
        END
    END
    PRINT 'Espacio OK.';
END

-- ---------- 2. OPERACION (agarra la tabla arriba; fail-fast si esta ocupada) ----------
SET LOCK_TIMEOUT 30000;   -- 30 s: off-hours deberia ser instantaneo
BEGIN TRAN;
BEGIN TRY

    DECLARE @kk int;
    SELECT TOP 1 @kk = 1 FROM dbo.Clientes WITH (TABLOCKX, HOLDLOCK);   -- <-- si no lo pilla en 30s, error 1222
    PRINT CONCAT('Tabla Clientes bloqueada en exclusiva (', CONVERT(varchar(19), SYSDATETIME(), 121), ').');

    IF @cifYaAncho = 0
    BEGIN
        -- 2a. Capturar el CREATE de TODOS los indices nonclustered (no PK/unique) que referencian
        --     [CIF/NIF] (clave O incluida). Cualquiera bloquea el ALTER.
        DECLARE @indices TABLE (id INT IDENTITY(1,1), nombre SYSNAME, crear NVARCHAR(MAX));
        INSERT INTO @indices (nombre, crear)
        SELECT i.name,
            'CREATE NONCLUSTERED INDEX ' + QUOTENAME(i.name) + ' ON dbo.Clientes ('
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
        WHERE i.object_id=OBJECT_ID('dbo.Clientes') AND i.type_desc='NONCLUSTERED'
          AND i.is_primary_key=0 AND i.is_unique_constraint=0
          AND EXISTS (SELECT 1 FROM sys.index_columns ic2 JOIN sys.columns c2 ON ic2.object_id=c2.object_id AND ic2.column_id=c2.column_id
                      WHERE ic2.object_id=i.object_id AND ic2.index_id=i.index_id AND c2.name='CIF/NIF');
        DECLARE @nIdx int = (SELECT COUNT(*) FROM @indices);
        PRINT CONCAT('Indices con [CIF/NIF] capturados: ', @nIdx, ' (', DATEDIFF(SECOND,@t0,SYSDATETIME()), 's)');

        -- 2b. Drop indices
        IF @nIdx > 0
        BEGIN
            DECLARE @sql NVARCHAR(MAX)=N'';
            SELECT @sql = @sql + 'DROP INDEX ' + QUOTENAME(nombre) + ' ON dbo.Clientes;' + CHAR(10) FROM @indices;
            EXEC sys.sp_executesql @sql;
            PRINT CONCAT('Indices eliminados (', DATEDIFF(SECOND,@t0,SYSDATETIME()), 's)');
        END

        -- 2c. Drop estadisticas independientes sobre [CIF/NIF]
        DECLARE @sqlStats NVARCHAR(MAX)=N'';
        SELECT @sqlStats = @sqlStats + 'DROP STATISTICS dbo.Clientes.' + QUOTENAME(s.name) + ';' + CHAR(10)
        FROM sys.stats s
        WHERE s.object_id=OBJECT_ID('dbo.Clientes')
          AND NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=s.object_id AND i.name=s.name)
          AND EXISTS (SELECT 1 FROM sys.stats_columns sc JOIN sys.columns c ON sc.object_id=c.object_id AND sc.column_id=c.column_id
                      WHERE sc.object_id=s.object_id AND sc.stats_id=s.stats_id AND c.name='CIF/NIF');
        IF @sqlStats <> N'' BEGIN EXEC sys.sp_executesql @sqlStats; PRINT 'Estadisticas sobre [CIF/NIF] eliminadas.'; END

        -- 2d. Ampliar + quitar padding
        ALTER TABLE dbo.Clientes ALTER COLUMN [CIF/NIF] varchar(20) NULL;
        UPDATE dbo.Clientes SET [CIF/NIF]=RTRIM([CIF/NIF]) WHERE [CIF/NIF] IS NOT NULL AND [CIF/NIF] <> RTRIM([CIF/NIF]);
        PRINT CONCAT('[CIF/NIF] ampliado a varchar(20) y sin padding (', DATEDIFF(SECOND,@t0,SYSDATETIME()), 's)');

        -- 2e. Recrear indices
        DECLARE @i int=1, @crear NVARCHAR(MAX);
        WHILE @i <= ISNULL(@nIdx,0)
        BEGIN
            SELECT @crear = crear FROM @indices WHERE id=@i;
            EXEC sys.sp_executesql @crear;
            SET @i += 1;
        END
        PRINT CONCAT('Indices recreados: ', ISNULL(@nIdx,0), ' (', DATEDIFF(SECOND,@t0,SYSDATETIME()), 's)');
    END

    -- 2f. Columna Pais (idempotente; ya la puso el script rapido si se ejecuto antes).
    IF COL_LENGTH('dbo.Clientes','Pais') IS NULL
    BEGIN
        ALTER TABLE dbo.Clientes ADD Pais varchar(2) NULL CONSTRAINT DF_Clientes_Pais DEFAULT 'ES';
        PRINT 'Columna Pais anadida (DEFAULT ES).';
    END
    EXEC ('UPDATE dbo.Clientes SET Pais = ''ES'' WHERE Pais IS NULL;');

    COMMIT TRAN;
    PRINT CONCAT('OK: Clientes listo. Duracion total: ', DATEDIFF(SECOND,@t0,SYSDATETIME()), ' s.');

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    IF ERROR_NUMBER() = 1222
        RAISERROR('ABORTADO: no se pudo bloquear Clientes en 30 s (tabla ocupada). NO se ha tocado nada. Reintenta con menos actividad.', 16, 1);
    ELSE
    BEGIN
        DECLARE @e nvarchar(2000) = ERROR_MESSAGE();
        RAISERROR('ERROR (rollback hecho, NADA aplicado): %s', 16, 1, @e);
    END
END CATCH
GO
