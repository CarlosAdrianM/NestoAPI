-- =============================================================================
-- NestoAPI#490: restaurar los 676 nombres de Productos que el eco del bus reescribió
-- en formato oración la madrugada del 17/09/2026 (02:11-02:16, usuario 'Sync stocks nocturno').
--
-- ORDEN OBLIGATORIO:
--   1. Publicar la NestoAPI con el fix de #490 (SyncTableRouter ignora los mensajes propios).
--   2. Ejecutar este script en SSMS (login con permisos sobre NV y sobre la copia del backup).
--   El trigger trgProductosUpd reencola las fichas tocadas en Nesto_sync y el job las republica:
--   con el fix es inofensivo (la tienda recibe el mismo nombre en formato oración que ya tiene);
--   SIN el fix, el eco volvería a pasarlas a minúsculas.
--
-- FUENTE: el backup completo de NV del 16/09/2026 20:30 (msdb.dbo.backupset), anterior a la
-- reescritura. Restaurarlo como copia con el nombre [NV_20260916] (o cambiar el nombre abajo).
-- UPPER(Nombre) NO vale como atajo: ~9 % de las fichas activas tenían mayúsculas mixtas antes
-- (630 de 6674) y UPPER las estropearía. La sección 3 (comentada) es el plan B si no hay backup.
-- =============================================================================

USE NV;
GO

-- 1. Vista previa: las fichas que se van a restaurar (deben ser 676) -----------------------------
SELECT p.Número, p.Nombre AS NombreActual, b.Nombre AS NombreBackup, p.Usuario, p.[Fecha Modificación]
FROM dbo.Productos p
INNER JOIN NV_20260916.dbo.Productos b
    ON b.Empresa = p.Empresa AND b.Número = p.Número
WHERE p.Empresa = '1'
  AND RTRIM(p.Usuario) = 'Sync stocks nocturno'
  AND p.[Fecha Modificación] >= '2026-09-17 02:00'
  AND p.[Fecha Modificación] <  '2026-09-17 03:00'
  AND p.Nombre COLLATE Latin1_General_CS_AS <> b.Nombre COLLATE Latin1_General_CS_AS
ORDER BY p.Número;

-- 2. Restaurar nombre y auditoría desde el backup ------------------------------------------------
BEGIN TRANSACTION;

UPDATE p
SET p.Nombre = b.Nombre,
    p.Usuario = b.Usuario,
    p.[Fecha Modificación] = b.[Fecha Modificación]
FROM dbo.Productos p
INNER JOIN NV_20260916.dbo.Productos b
    ON b.Empresa = p.Empresa AND b.Número = p.Número
WHERE p.Empresa = '1'
  AND RTRIM(p.Usuario) = 'Sync stocks nocturno'
  AND p.[Fecha Modificación] >= '2026-09-17 02:00'
  AND p.[Fecha Modificación] <  '2026-09-17 03:00'
  AND p.Nombre COLLATE Latin1_General_CS_AS <> b.Nombre COLLATE Latin1_General_CS_AS;

-- Debe decir 676 filas afectadas. Si no, ROLLBACK y revisar.
-- COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;

-- 3. PLAN B sin backup (solo si no se puede restaurar la copia): todo a mayúsculas ---------------
-- Acepta que las fichas que tenían mayúsculas mixtas antes del 17/09 queden en mayúsculas.
/*
UPDATE dbo.Productos
SET Nombre = UPPER(Nombre)
WHERE Empresa = '1'
  AND RTRIM(Usuario) = 'Sync stocks nocturno'
  AND [Fecha Modificación] >= '2026-09-17 02:00'
  AND [Fecha Modificación] <  '2026-09-17 03:00';
*/
