-- =============================================
-- Triggers para sincronización de Productos
-- =============================================
-- Estos triggers registran en la tabla Nesto_sync
-- los productos que han sido insertados o modificados
-- para su posterior sincronización con sistemas externos
-- =============================================

USE [bthnesto_NestoPROD]
GO

-- =============================================
-- Trigger: INSERT en Productos
-- =============================================
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'trg_Productos_Insert_Sincronizacion')
    DROP TRIGGER trg_Productos_Insert_Sincronizacion
GO

CREATE TRIGGER trg_Productos_Insert_Sincronizacion
ON Productos
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    -- Insertar registro en Nesto_sync para cada producto insertado
    -- NOTA: Ajustar la captura de Usuario según el método usado en Nesto viejo
    -- Opciones comunes:
    --   1. SYSTEM_USER - Usuario de Windows/SQL Server
    --   2. CONVERT(VARCHAR(25), CONTEXT_INFO()) - Si usan CONTEXT_INFO para guardar el usuario
    --   3. i.Usuario - Si la tabla Productos tiene un campo Usuario
    INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, Sincronizado)
    SELECT
        'Productos' AS Tabla,
        LTRIM(RTRIM(i.Número)) AS ModificadoId,
        COALESCE(i.Usuario, SYSTEM_USER) AS Usuario, -- Usar campo Usuario del producto o SYSTEM_USER como fallback
        NULL AS Sincronizado
    FROM inserted i
    WHERE i.Empresa = '1' -- Solo sincronizar empresa por defecto
        AND i.Número IS NOT NULL
        AND LTRIM(RTRIM(i.Número)) <> '' -- Evitar IDs vacíos

    -- Si el producto ya existe en Nesto_sync y está sincronizado,
    -- actualizamos para marcarlo como pendiente
    UPDATE ns
    SET ns.Sincronizado = NULL
    FROM Nesto_sync ns
    INNER JOIN inserted i ON ns.ModificadoId = LTRIM(RTRIM(i.Número))
    WHERE ns.Tabla = 'Productos'
        AND ns.Sincronizado IS NOT NULL
        AND i.Empresa = '1'
END
GO

-- =============================================
-- Trigger: UPDATE en Productos
-- =============================================
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'trg_Productos_Update_Sincronizacion')
    DROP TRIGGER trg_Productos_Update_Sincronizacion
GO

CREATE TRIGGER trg_Productos_Update_Sincronizacion
ON Productos
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Solo procesar si hay cambios reales (no actualizar si no cambió nada)
    IF NOT EXISTS (
        SELECT * FROM inserted i
        INNER JOIN deleted d ON i.Empresa = d.Empresa AND i.Número = d.Número
        WHERE
            ISNULL(i.Nombre, '') <> ISNULL(d.Nombre, '') OR
            ISNULL(i.Tamaño, 0) <> ISNULL(d.Tamaño, 0) OR
            ISNULL(i.UnidadMedida, '') <> ISNULL(d.UnidadMedida, '') OR
            ISNULL(i.Familia, '') <> ISNULL(d.Familia, '') OR
            ISNULL(i.PVP, 0) <> ISNULL(d.PVP, 0) OR
            ISNULL(i.Estado, 0) <> ISNULL(d.Estado, 0) OR
            ISNULL(i.Grupo, '') <> ISNULL(d.Grupo, '') OR
            ISNULL(i.SubGrupo, '') <> ISNULL(d.SubGrupo, '') OR
            ISNULL(i.RoturaStockProveedor, 0) <> ISNULL(d.RoturaStockProveedor, 0) OR
            ISNULL(i.CodBarras, '') <> ISNULL(d.CodBarras, '')
    )
    BEGIN
        -- No hay cambios relevantes, no hacer nada
        RETURN;
    END

    -- Insertar o actualizar registro en Nesto_sync
    -- NOTA: Ajustar la captura de Usuario según el método usado en Nesto viejo (ver trigger INSERT)
    MERGE INTO Nesto_sync AS target
    USING (
        SELECT DISTINCT
            LTRIM(RTRIM(i.Número)) AS ModificadoId,
            COALESCE(i.Usuario, SYSTEM_USER) AS Usuario -- Usar campo Usuario del producto o SYSTEM_USER como fallback
        FROM inserted i
        WHERE i.Empresa = '1' -- Solo sincronizar empresa por defecto
            AND i.Número IS NOT NULL
            AND LTRIM(RTRIM(i.Número)) <> '' -- Evitar IDs vacíos
    ) AS source
    ON target.Tabla = 'Productos' AND target.ModificadoId = source.ModificadoId
    WHEN MATCHED THEN
        UPDATE SET
            target.Sincronizado = NULL, -- Marcar como pendiente de sincronización
            target.Usuario = source.Usuario -- Actualizar el usuario
    WHEN NOT MATCHED THEN
        INSERT (Tabla, ModificadoId, Usuario, Sincronizado)
        VALUES ('Productos', source.ModificadoId, source.Usuario, NULL);
END
GO

-- =============================================
-- Verificar que los triggers se crearon correctamente
-- =============================================
SELECT
    t.name AS TriggerName,
    OBJECT_NAME(t.parent_id) AS TableName,
    t.is_disabled AS IsDisabled,
    t.create_date AS CreatedDate,
    t.modify_date AS ModifiedDate
FROM sys.triggers t
WHERE t.name IN ('trg_Productos_Insert_Sincronizacion', 'trg_Productos_Update_Sincronizacion')
ORDER BY t.name;

PRINT '✅ Triggers de sincronización de Productos creados correctamente';
GO

-- =============================================
-- Script de prueba (comentado)
-- =============================================
/*
-- Para probar los triggers, ejecuta:

-- 1. Insertar un producto de prueba
INSERT INTO Productos (Empresa, Número, Nombre, Estado)
VALUES ('1', 'TEST001', 'Producto de Prueba', 0);

-- 2. Verificar que se creó el registro en Nesto_sync
SELECT * FROM Nesto_sync WHERE Tabla = 'Productos' AND ModificadoId = 'TEST001';

-- 3. Actualizar el producto
UPDATE Productos
SET Nombre = 'Producto Modificado'
WHERE Empresa = '1' AND Número = 'TEST001';

-- 4. Verificar que el registro en Nesto_sync sigue pendiente (Sincronizado = NULL)
SELECT * FROM Nesto_sync WHERE Tabla = 'Productos' AND ModificadoId = 'TEST001';

-- 5. Limpiar datos de prueba
DELETE FROM Nesto_sync WHERE Tabla = 'Productos' AND ModificadoId = 'TEST001';
DELETE FROM Productos WHERE Empresa = '1' AND Número = 'TEST001';
*/
