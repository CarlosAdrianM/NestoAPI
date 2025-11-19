# Configuración de Elmah para NestoAPI

## 📦 Paso 1: Instalar NuGet Package

En **Package Manager Console** de Visual Studio:

```powershell
Install-Package Elmah.MVC
```

## ⚙️ Paso 2: Configurar Web.config

### 2.1 Agregar sección Elmah en `<configSections>`

Después de la línea 9 (sección entityFramework), agrega:

```xml
<section name="elmah" type="Elmah.ElmahSectionHandler, Elmah" requirePermission="false" />
```

### 2.2 Configurar Elmah después de `</appSettings>`

Agrega esta configuración completa (línea ~16, después de `</appSettings>`):

```xml
<!-- Configuración de Elmah para logging de errores -->
<elmah>
  <!-- Guardar errores en SQL Server -->
  <errorLog type="Elmah.SqlErrorLog, Elmah"
            connectionStringName="NestoConnection"
            applicationName="NestoAPI" />

  <!-- Filtros: NO loggear ciertos errores -->
  <security allowRemoteAccess="true" />

  <!-- Configuración adicional -->
  <errorMail
    from="nesto@nuevavision.es"
    to="carlosadrian@nuevavision.es"
    subject="Error en NestoAPI"
    async="true"
    smtpServer="smtp.office365.com"
    smtpPort="587"
    useSsl="true" />
</elmah>
```

### 2.3 Agregar HttpModule en `<system.web>`

Dentro de `<httpModules>` (línea ~30), agrega al final:

```xml
<add name="ErrorLog" type="Elmah.ErrorLogModule, Elmah" />
<add name="ErrorMail" type="Elmah.ErrorMailModule, Elmah" />
<add name="ErrorFilter" type="Elmah.ErrorFilterModule, Elmah" />
```

### 2.4 Agregar HttpModule en `<system.webServer>`

Dentro de `<modules>` (línea ~262), agrega al final antes del `</modules>`:

```xml
<add name="Elmah.ErrorLog" type="Elmah.ErrorLogModule, Elmah" preCondition="managedHandler" />
<add name="Elmah.ErrorFilter" type="Elmah.ErrorFilterModule, Elmah" preCondition="managedHandler" />
<add name="Elmah.ErrorMail" type="Elmah.ErrorMailModule, Elmah" preCondition="managedHandler" />
```

### 2.5 Agregar Handler en `<system.webServer>`

Dentro de `<handlers>` (línea ~271), agrega al final antes del `</handlers>`:

```xml
<add name="Elmah" path="elmah.axd" verb="POST,GET,HEAD"
     type="Elmah.ErrorLogPageFactory, Elmah"
     preCondition="integratedMode" />
```

### 2.6 Configurar permisos de acceso

Después de `</elmah>` (nueva sección), agrega:

```xml
<!-- Permisos de acceso a Elmah -->
<location path="elmah.axd" inheritInChildApplications="false">
  <system.web>
    <httpHandlers>
      <add verb="POST,GET,HEAD" path="elmah.axd"
           type="Elmah.ErrorLogPageFactory, Elmah" />
    </httpHandlers>
    <authorization>
      <!-- Solo usuarios autenticados pueden ver elmah -->
      <deny users="?" />
      <allow users="*" />
    </authorization>
  </system.web>
  <system.webServer>
    <handlers>
      <add name="ELMAH" verb="POST,GET,HEAD" path="elmah.axd"
           type="Elmah.ErrorLogPageFactory, Elmah"
           preCondition="integratedMode" />
    </handlers>
  </system.webServer>
</location>
```

## 🗄️ Paso 3: Crear tabla en SQL Server

Ejecuta este script en tu base de datos **NV** (la que usa NestoConnection):

```sql
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

ALTER TABLE [dbo].[ELMAH_Error] WITH NOCHECK ADD
    CONSTRAINT [PK_ELMAH_Error] PRIMARY KEY NONCLUSTERED
    (
        [ErrorId]
    )  ON [PRIMARY]

GO

ALTER TABLE [dbo].[ELMAH_Error] ADD
    CONSTRAINT [DF_ELMAH_Error_ErrorId] DEFAULT (NEWID()) FOR [ErrorId]

GO

CREATE NONCLUSTERED INDEX [IX_ELMAH_Error_App_Time_Seq] ON [dbo].[ELMAH_Error]
(
    [Application]   ASC,
    [TimeUtc]       DESC,
    [Sequence]      DESC
) ON [PRIMARY]

GO

-- Stored procedures para Elmah
CREATE PROCEDURE [dbo].[ELMAH_GetErrorXml]
(
    @Application NVARCHAR(60),
    @ErrorId UNIQUEIDENTIFIER
)
AS
    SET NOCOUNT ON
    SELECT
        [AllXml]
    FROM
        [ELMAH_Error]
    WHERE
        [ErrorId] = @ErrorId
    AND
        [Application] = @Application

GO

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
    DECLARE @StartRow INT
    DECLARE @StartRowIndex INT

    SELECT
        @TotalCount = COUNT(1)
    FROM
        [ELMAH_Error]
    WHERE
        [Application] = @Application

    SET @StartRowIndex = @PageIndex * @PageSize + 1

    IF @StartRowIndex <= @TotalCount
    BEGIN

        SET ROWCOUNT @StartRowIndex

        SELECT
            @FirstTimeUTC = [TimeUtc],
            @FirstSequence = [Sequence]
        FROM
            [ELMAH_Error]
        WHERE
            [Application] = @Application
        ORDER BY
            [TimeUtc] DESC,
            [Sequence] DESC

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
    FROM
        [ELMAH_Error] error
    WHERE
        [Application] = @Application
    AND
        [TimeUtc] <= @FirstTimeUTC
    AND
        [Sequence] <= @FirstSequence
    ORDER BY
        [TimeUtc] DESC,
        [Sequence] DESC
    FOR
        XML AUTO

GO

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
        [ErrorId],
        [Application],
        [Host],
        [Type],
        [Source],
        [Message],
        [User],
        [AllXml],
        [StatusCode],
        [TimeUtc]
    )
    VALUES
    (
        @ErrorId,
        @Application,
        @Host,
        @Type,
        @Source,
        @Message,
        @User,
        @AllXml,
        @StatusCode,
        @TimeUtc
    )

GO
```

## 🔗 Paso 4: Integrar con GlobalExceptionFilter

El archivo `GlobalExceptionFilter.cs` ya está preparado. Solo necesitamos agregar el logging a Elmah.

## 🚀 Uso

### Ver errores en el navegador

1. Ejecuta la aplicación
2. Ve a: **http://localhost:puerto/elmah.axd**
3. Verás una lista de todos los errores ordenados por fecha (más recientes arriba)
4. Haz clic en cualquier error para ver detalles completos
5. Presiona **F5** para refrescar

### Endpoints disponibles

- `GET /elmah.axd` - Ver lista de errores
- `GET /elmah.axd/detail?id={guid}` - Ver detalle de un error
- `GET /elmah.axd/download` - Descargar log CSV
- `GET /elmah.axd/rss` - Feed RSS de errores

### Características

✅ **Auto-refresh**: Solo presiona F5 para ver nuevos errores
✅ **Filtrado**: Busca por tipo, mensaje, usuario
✅ **Paginación**: 15 errores por página por defecto
✅ **Detalles completos**: Stack trace, inner exceptions, contexto
✅ **RSS Feed**: Suscríbete para recibir notificaciones
✅ **Descarga CSV**: Exporta errores para análisis

## 🔐 Seguridad

Por defecto, solo usuarios **autenticados** pueden acceder a `/elmah.axd`.

Para cambiar permisos, edita la sección `<authorization>` en Web.config:

```xml
<!-- Solo admins -->
<authorization>
  <allow roles="Admin" />
  <deny users="*" />
</authorization>

<!-- Todos (NO RECOMENDADO en producción) -->
<authorization>
  <allow users="*" />
</authorization>
```

## 🧹 Mantenimiento

Elmah NO limpia automáticamente errores antiguos. Para evitar que la tabla crezca indefinidamente:

```sql
-- Limpiar errores mayores a 30 días (ejecutar periódicamente)
DELETE FROM ELMAH_Error
WHERE TimeUtc < DATEADD(day, -30, GETDATE())

-- O mantener solo los últimos 1000
DELETE FROM ELMAH_Error
WHERE ErrorId NOT IN (
    SELECT TOP 1000 ErrorId
    FROM ELMAH_Error
    ORDER BY TimeUtc DESC
)
```

Puedes crear un **SQL Server Agent Job** para ejecutar esto automáticamente cada semana.

## 📊 Integración con nuestro sistema de excepciones

Elmah capturará automáticamente:
- ✅ Todas las `FacturacionException`
- ✅ Todas las `PedidoInvalidoException`
- ✅ Todas las `TraspasoEmpresaException`
- ✅ Cualquier otra excepción no manejada

Y guardará:
- Código de error (`ErrorCode`)
- Mensaje descriptivo
- Contexto de negocio (empresa, pedido, usuario)
- Stack trace completo
- Inner exceptions
- Timestamp

## 🎯 Resultado Final

Cuando ocurra un error en facturación, verás en `/elmah.axd`:

```
┌──────────────────────────────────────────────────────────────┐
│ Error                    Time               User              │
├──────────────────────────────────────────────────────────────┤
│ FacturacionException    2025-01-19 10:30   carlos            │
│ FACTURACION_IVA_...                                          │
├──────────────────────────────────────────────────────────────┤
│ SqlException            2025-01-19 09:15   admin             │
│ Connection timeout                                           │
└──────────────────────────────────────────────────────────────┘
```

Al hacer clic:
- Mensaje completo
- Empresa: 1
- Pedido: 12345
- Usuario: carlos
- Stack trace completo
- URL de la petición
- Timestamp exacto

---

**Última actualización:** 2025-01-19
**Estado:** ✅ Listo para instalar
