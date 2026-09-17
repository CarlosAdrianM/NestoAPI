-- =============================================================================
-- NestoAPI#490: restaurar los 676 nombres de Productos que el eco del bus reescribió
-- en formato oración la madrugada del 17/09/2026 (02:11-02:16, usuario 'Sync stocks nocturno').
--
-- ✅ EJECUTADO el 17/09/2026 a las 09:50 (tras publicar la API con el fix): COMMIT de 676 filas.
--
-- Se eligió UPPER() (decisión de Carlos) en vez de restaurar el backup del 16/09 20:30 como
-- copia: son 97 GB y no había garantía de espacio en el servidor. Coste asumido: las fichas
-- que ya tenían mayúsculas mixtas antes del 17/09 (~9 % del catálogo activo) quedan en
-- mayúsculas; el que se detecte se corrige a mano en la ficha.
--
-- ORDEN: publicar primero la API con el fix de #490 (SyncTableRouter ignora los mensajes
-- propios). Sin él, cualquier republicación volvería a bajar los nombres a minúsculas.
--
-- Fechas en ISO 8601 con T: los literales 'yyyy-mm-dd hh:mm' fallan con idioma español
-- (Msg 242, se leen como día/mes).
-- =============================================================================

USE NV;
GO

BEGIN TRANSACTION;

UPDATE dbo.Productos
SET Nombre = UPPER(Nombre)
WHERE Empresa = '1'
  AND RTRIM(Usuario) = 'Sync stocks nocturno'
  AND [Fecha Modificación] >= '2026-09-17T02:00:00'
  AND [Fecha Modificación] <  '2026-09-17T03:00:00';

DECLARE @n int = @@ROWCOUNT;
IF @n = 676
BEGIN
    COMMIT TRANSACTION;
    PRINT 'COMMIT: ' + CAST(@n AS varchar);
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'ROLLBACK: filas=' + CAST(@n AS varchar);
END

-- Verificación: debe devolver 0.
SELECT COUNT(*) AS quedanEnMinusculas
FROM dbo.Productos
WHERE Empresa = '1'
  AND RTRIM(Usuario) = 'Sync stocks nocturno'
  AND [Fecha Modificación] >= '2026-09-17T00:00:00'
  AND Nombre COLLATE Latin1_General_CS_AS <> UPPER(Nombre) COLLATE Latin1_General_CS_AS;

-- Verificación del fix (mañana 18/09 y siguientes): debe devolver 0. Si no, el eco sigue vivo.
SELECT COUNT(*) AS fichasTocadasPorElSyncDespuesDelFix
FROM dbo.Productos
WHERE Empresa = '1'
  AND RTRIM(Usuario) = 'Sync stocks nocturno'
  AND [Fecha Modificación] >= '2026-09-17T12:00:00';
