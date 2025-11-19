-- =============================================
-- Script: Crear tabla SyncMessageRetries
-- Descripción: Sistema de control de reintentos para mensajes de Pub/Sub
-- Fecha: 2025-01-19
-- =============================================

USE [bthnesto_NestoPROD]
GO

-- Crear tabla si no existe
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SyncMessageRetries]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SyncMessageRetries](
        [MessageId] [nvarchar](255) NOT NULL,
        [Tabla] [nvarchar](50) NOT NULL,
        [EntityId] [nvarchar](100) NULL,
        [Source] [nvarchar](50) NULL,
        [AttemptCount] [int] NOT NULL DEFAULT 0,
        [FirstAttemptDate] [datetime] NOT NULL,
        [LastAttemptDate] [datetime] NOT NULL,
        [LastError] [nvarchar](max) NULL,
        [Status] [nvarchar](20) NOT NULL,
        [MessageData] [nvarchar](max) NULL,
        CONSTRAINT [PK_SyncMessageRetries] PRIMARY KEY CLUSTERED ([MessageId] ASC)
    )

    PRINT 'Tabla SyncMessageRetries creada exitosamente'
END
ELSE
BEGIN
    PRINT 'Tabla SyncMessageRetries ya existe'
END
GO

-- Crear índices para mejorar rendimiento
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SyncMessageRetries_Status')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SyncMessageRetries_Status]
    ON [dbo].[SyncMessageRetries] ([Status])
    INCLUDE ([Tabla], [LastAttemptDate], [AttemptCount])

    PRINT 'Índice IX_SyncMessageRetries_Status creado exitosamente'
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SyncMessageRetries_Tabla_Status')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SyncMessageRetries_Tabla_Status]
    ON [dbo].[SyncMessageRetries] ([Tabla], [Status])
    INCLUDE ([MessageId], [LastAttemptDate], [AttemptCount])

    PRINT 'Índice IX_SyncMessageRetries_Tabla_Status creado exitosamente'
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SyncMessageRetries_LastAttemptDate')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SyncMessageRetries_LastAttemptDate]
    ON [dbo].[SyncMessageRetries] ([LastAttemptDate] DESC)

    PRINT 'Índice IX_SyncMessageRetries_LastAttemptDate creado exitosamente'
END
GO

PRINT ''
PRINT 'Script ejecutado completamente'
PRINT ''
PRINT 'Estados válidos para el campo Status:'
PRINT '  - Retrying: Aún reintentando (< 5 intentos)'
PRINT '  - PoisonPill: Llegó al límite, pendiente de revisión'
PRINT '  - Reprocess: Marcado para reprocesar'
PRINT '  - Resolved: Marcado como solucionado manualmente'
PRINT '  - PermanentFailure: Marcado como fallo permanente'
GO

-- =============================================
-- Script de prueba (comentado - descomentar para testing)
-- =============================================

/*
-- Insertar registro de prueba
INSERT INTO SyncMessageRetries
    (MessageId, Tabla, EntityId, Source, AttemptCount, FirstAttemptDate, LastAttemptDate, Status, LastError, MessageData)
VALUES
    ('test-msg-001', 'Clientes', '12345', 'Odoo', 3, GETDATE(), GETDATE(), 'Retrying', 'Error de ejemplo', '{"Cliente":"12345"}')

-- Consultar registros
SELECT * FROM SyncMessageRetries ORDER BY LastAttemptDate DESC

-- Consultar poison pills pendientes
SELECT * FROM SyncMessageRetries WHERE Status = 'PoisonPill' ORDER BY LastAttemptDate DESC

-- Limpiar tabla de prueba
-- DELETE FROM SyncMessageRetries WHERE MessageId = 'test-msg-001'
*/
