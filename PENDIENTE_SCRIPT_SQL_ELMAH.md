# ⚠️ PENDIENTE: Script SQL de Elmah

## 🎯 Acción Requerida

Antes de desplegar a producción, **DEBES ejecutar el script SQL** para crear la tabla de Elmah.

## 📋 Pasos:

### 1. Abrir SQL Server Management Studio

Conectar a: **DC2016** (servidor de producción)

### 2. Seleccionar Base de Datos

Base de datos: **NV**

### 3. Copiar y Ejecutar el Script

El script completo está en: `NestoAPI/Infraestructure/Exceptions/ELMAH_SETUP.md`

O copia el script de abajo:

```sql
-- =============================================
-- Script de creación de tablas y procedimientos para ELMAH
-- Base de datos: NV
-- =============================================

-- 1. Crear tabla ELMAH_Error
CREATE TABLE [dbo].[ELMAH_Error]
(
    [ErrorId]      UNIQUEIDENTIFIER NOT NULL,
    [Application]  NVARCHAR(60)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Host]         NVARCHAR(50)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Type]         NVARCHAR(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Source]       NVARCHAR(60)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [Message]      NVARCHAR(500) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [User]         NVARCHAR(50)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
    [StatusCode]   INT NOT NULL,
    [TimeUtc]      DATETIME NOT NULL,
    [Sequence]     INT IDENTITY (1, 1) NOT NULL,
    [AllXml]       NTEXT COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

-- 2. Agregar primary key
ALTER TABLE [dbo].[ELMAH_Error] WITH NOCHECK ADD
    CONSTRAINT [PK_ELMAH_Error] PRIMARY KEY NONCLUSTERED ([ErrorId]) ON [PRIMARY]
GO

-- 3. Agregar default constraint
ALTER TABLE [dbo].[ELMAH_Error] ADD
    CONSTRAINT [DF_ELMAH_Error_ErrorId] DEFAULT (NEWID()) FOR [ErrorId]
GO

-- 4. Crear índice para mejorar rendimiento
CREATE NONCLUSTERED INDEX [IX_ELMAH_Error_App_Time_Seq] ON [dbo].[ELMAH_Error]
(
    [Application]   ASC,
    [TimeUtc]       DESC,
    [Sequence]      DESC
) ON [PRIMARY]
GO

-- 5. Stored procedure: ELMAH_GetErrorXml
CREATE PROCEDURE [dbo].[ELMAH_GetErrorXml]
(
    @Application NVARCHAR(60),
    @ErrorId UNIQUEIDENTIFIER
)
AS
    SET NOCOUNT ON
    SELECT [AllXml]
    FROM [ELMAH_Error]
    WHERE [ErrorId] = @ErrorId AND [Application] = @Application
GO

-- 6. Stored procedure: ELMAH_GetErrorsXml
CREATE PROCEDURE [dbo].[ELMAH_GetErrorsXml]
(
    @Application NVARCHAR(60),
    @PageIndex INT = 0,
    @PageSize INT = 15,
    @TotalCount INT OUTPUT
)
AS
    SET NOCOUNT ON

    DECLARE @FirstTimeUTC DATETIME
    DECLARE @FirstSequence INT
    DECLARE @StartRowIndex INT

    SELECT @TotalCount = COUNT(1)
    FROM [ELMAH_Error]
    WHERE [Application] = @Application

    SET @StartRowIndex = @PageIndex * @PageSize + 1

    IF @StartRowIndex <= @TotalCount
    BEGIN
        SET ROWCOUNT @StartRowIndex

        SELECT
            @FirstTimeUTC = [TimeUtc],
            @FirstSequence = [Sequence]
        FROM [ELMAH_Error]
        WHERE [Application] = @Application
        ORDER BY [TimeUtc] DESC, [Sequence] DESC
    END
    ELSE
    BEGIN
        SET @PageSize = 0
    END

    SET ROWCOUNT @PageSize

    SELECT
        errorId     = [ErrorId],
        application = [Application],
        host        = [Host],
        type        = [Type],
        source      = [Source],
        message     = [Message],
        [user]      = [User],
        statusCode  = [StatusCode],
        time        = CONVERT(VARCHAR(50), [TimeUtc], 126) + 'Z'
    FROM [ELMAH_Error] error
    WHERE [Application] = @Application
    AND [TimeUtc] <= @FirstTimeUTC
    AND [Sequence] <= @FirstSequence
    ORDER BY [TimeUtc] DESC, [Sequence] DESC
    FOR XML AUTO
GO

-- 7. Stored procedure: ELMAH_LogError
CREATE PROCEDURE [dbo].[ELMAH_LogError]
(
    @ErrorId UNIQUEIDENTIFIER,
    @Application NVARCHAR(60),
    @Host NVARCHAR(30),
    @Type NVARCHAR(100),
    @Source NVARCHAR(60),
    @Message NVARCHAR(500),
    @User NVARCHAR(50),
    @AllXml NTEXT,
    @StatusCode INT,
    @TimeUtc DATETIME
)
AS
    SET NOCOUNT ON

    INSERT INTO [ELMAH_Error]
    (
        [ErrorId], [Application], [Host], [Type], [Source],
        [Message], [User], [AllXml], [StatusCode], [TimeUtc]
    )
    VALUES
    (
        @ErrorId, @Application, @Host, @Type, @Source,
        @Message, @User, @AllXml, @StatusCode, @TimeUtc
    )
GO

-- =============================================
-- FIN DEL SCRIPT
-- =============================================
PRINT 'Tablas y procedimientos de ELMAH creados correctamente'
```

### 4. Verificar

Ejecuta esta query para verificar que se creó correctamente:

```sql
-- Debe devolver 1 registro
SELECT COUNT(*) FROM sys.tables WHERE name = 'ELMAH_Error'

-- Debe devolver 3 registros
SELECT COUNT(*) FROM sys.procedures WHERE name LIKE 'ELMAH_%'
```

## ✅ Resultado Esperado

Deberías ver:
```
Tablas y procedimientos de ELMAH creados correctamente
```

Y las queries de verificación deben devolver:
- Primera query: `1` (tabla creada)
- Segunda query: `3` (procedimientos creados)

## 🚀 Después de Ejecutar

Una vez ejecutado el script:

1. Desplegar NestoAPI a producción
2. Acceder a: `https://api.nuevavision.es/logs-nestoapi`
3. Provocar un error de prueba
4. Verificar que aparezca en Elmah

## 📞 Si Hay Problemas

Si el script falla:
- Verifica que estás conectado a la base de datos **NV**
- Verifica que no exista ya la tabla `ELMAH_Error`
- Si existe, puedes borrarla primero: `DROP TABLE ELMAH_Error`

---

**IMPORTANTE:** Este script solo necesita ejecutarse UNA VEZ en producción.
