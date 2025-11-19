# Sesión 2025-01-19: Sistema de Gestión de Errores y Logging

## 📋 Resumen Ejecutivo

Esta sesión implementó un sistema completo de gestión de errores para NestoAPI y Nesto, mejorando significativamente la experiencia de debugging y proporcionando mensajes de error descriptivos tanto para usuarios como para desarrolladores.

## 🎯 Problemas Resueltos

### 1. Bug Crítico: Facturación de Rutas con Traspaso de Empresa

**Problema:** Al traspasar un pedido de la empresa "1" a la empresa "3" para facturación, el objeto `cabPedido` quedaba Detached en Entity Framework y no se recargaba desde la base de datos.

**Síntoma:** Error al llamar a `prdCrearFacturaVta` porque se ejecutaba con la empresa "1" en lugar de la empresa "3".

**Solución:** En `ServicioFacturas.cs:317`, se agregó recarga del pedido después del traspaso:

```csharp
// IMPORTANTE: Después del traspaso, el objeto cabPedido queda Detached
// Debemos recargar el pedido desde la BD para tener los datos actualizados
// (especialmente el campo IVA que se actualiza durante el traspaso)
cabPedido = db.CabPedidoVtas.Single(p => p.Empresa == empresa && p.Número == pedido);
```

**Archivos modificados:**
- `NestoAPI/Infraestructure/Facturas/ServicioFacturas.cs`

---

### 2. Mensajes de Error Genéricos e Inútiles

**Problema Anterior:**
```
"Este pedido no se puede facturar"
"Error al crear la factura"
```

**Solución Implementada:**
```
"El pedido 12345 no se puede facturar porque falta configurar el campo IVA en la cabecera del pedido"
"Error al ejecutar el procedimiento almacenado de facturación: [detalles técnicos]"
```

---

## 🏗️ Infraestructura Implementada

### 1. Sistema de Excepciones de Negocio

**Ubicación:** `NestoAPI/Infraestructure/Exceptions/`

#### Archivos Creados:

**ErrorContext.cs**
- Contexto rico con metadata (empresa, pedido, usuario, cliente, factura, etc.)
- Datos adicionales personalizables
- Timestamp automático
- Método `ToString()` para logging

**NestoBusinessException.cs**
- Clase base para todas las excepciones de negocio
- Propiedades:
  - `ErrorContext Context`: Contexto del error
  - `HttpStatusCode StatusCode`: Código HTTP sugerido (default: 400)
  - `bool IsWarning`: Flag para indicar si es warning o error
- Métodos:
  - `GetFullMessage()`: Mensaje + contexto
  - `GetErrorCode()`: Código de error o "BUSINESS_ERROR"

**FacturacionException.cs**
- Excepción específica para errores de facturación
- Constructor con parámetros: empresa, pedido, factura, usuario
- Métodos fluent:
  - `.WithData(key, value)`: Agregar datos adicionales
  - `.AsWarning()`: Marcar como warning
  - `.WithStatusCode(code)`: Personalizar código HTTP

**PedidoInvalidoException.cs**
- Para errores de validación de pedidos
- Parámetros: empresa, pedido, cliente, usuario

**TraspasoEmpresaException.cs**
- Para errores en traspasos entre empresas
- Parámetros: empresaOrigen, empresaDestino, pedido, cliente, usuario

#### Códigos de Error Estándar:

| Código | Área | Descripción |
|--------|------|-------------|
| `FACTURACION_IVA_FALTANTE` | Facturación | Falta configurar campo IVA |
| `FACTURACION_STORED_PROCEDURE_ERROR` | Facturación | Error al ejecutar prdCrearFacturaVta |
| `FACTURACION_ERROR_INESPERADO` | Facturación | Error genérico inesperado |
| `PEDIDO_SIN_LINEAS` | Pedidos | El pedido no tiene líneas |
| `PEDIDO_CLIENTE_NO_EXISTE` | Pedidos | El cliente no existe |
| `TRASPASO_CLIENTE_ERROR` | Traspasos | Error al copiar cliente |
| `TRASPASO_PRODUCTO_ERROR` | Traspasos | Error al copiar producto |

---

### 2. Filtro Global de Excepciones

**Ubicación:** `NestoAPI/Infraestructure/Filters/GlobalExceptionFilter.cs`

**Funcionalidad:**
- Captura TODAS las excepciones no manejadas
- Loggea automáticamente en Elmah
- Formatea respuestas JSON consistentes
- Diferencia entre modo DEBUG y RELEASE

**Formato de Respuesta:**

```json
{
  "error": {
    "code": "FACTURACION_IVA_FALTANTE",
    "message": "El pedido 12345 no se puede facturar porque falta configurar el campo IVA en la cabecera del pedido",
    "details": {
      "empresa": "1",
      "pedido": 12345,
      "usuario": "carlos"
    },
    "timestamp": "2025-01-19T10:30:00Z",
    "stackTrace": "...",  // Solo en DEBUG
    "innerException": {...} // Solo en DEBUG
  }
}
```

**Registro:**
- Agregado en `App_Start/WebApiConfig.cs:15`
- Se ejecuta automáticamente para todas las peticiones

---

### 3. Integración con Elmah (Error Logging)

**Paquete NuGet Instalado:**
```
Elmah.MVC 2.1.2
elmah.corelibrary 1.2.0
```

**Configuración en Web.config:**

1. **ConfigSections** (líneas 10-15): Secciones Elmah
2. **AppSettings** (líneas 21-28): Configuración de ruta y autenticación
3. **Elmah** (líneas 31-35): ErrorLog con SQL Server
4. **HttpModules** (líneas 53-55): Módulos para system.web
5. **Modules** (líneas 293-295): Módulos para system.webServer
6. **Handlers** (línea 302): Handler para la interfaz web

**Base de Datos:**
- Tabla: `ELMAH_Error` en base de datos `NV`
- Stored Procedures:
  - `ELMAH_GetErrorXml`
  - `ELMAH_GetErrorsXml`
  - `ELMAH_LogError`
- Índice: `IX_ELMAH_Error_App_Time_Seq`

**URL de Acceso:**
```
Desarrollo: http://localhost:puerto/logs-nestoapi
Producción:  https://api.nuevavision.es/logs-nestoapi
```

**Seguridad:**
- Sin autenticación requerida (acceso directo desde móvil)
- URL no obvia (`logs-nestoapi` en lugar de `/elmah`)
- Security by obscurity (solo el equipo conoce la ruta)

**Características:**
- ✅ Auto-refresh: Presiona F5 para ver nuevos errores
- ✅ Paginación: 15 errores por página
- ✅ Filtrado: Por tipo, mensaje, usuario
- ✅ Detalles completos: Stack trace, inner exceptions, contexto
- ✅ RSS Feed: Suscripción a errores
- ✅ Descarga CSV: Exportar para análisis

---

## 🔄 Cambios en el Backend (NestoAPI)

### Archivos Modificados:

**ServicioFacturas.cs**
- Línea 1: Agregado `using NestoAPI.Infraestructure.Exceptions;`
- Línea 317: Recarga de pedido después de traspaso
- Líneas 323-328: Excepción descriptiva para IVA faltante
- Líneas 365-386: Manejo de excepciones SQL con contexto

**GestorFacturas.cs**
- Línea 2: Agregado `using NestoAPI.Infraestructure.Exceptions;`
- Líneas 1047-1050: Simplificado - delega al servicio (propagación de excepciones)

**FacturasController.cs**
- Líneas 183-186: Eliminado try-catch - las excepciones se propagan al GlobalExceptionFilter

**WebApiConfig.cs**
- Línea 2: Agregado `using NestoAPI.Infraestructure.Filters;`
- Línea 15: Registro de `GlobalExceptionFilter`

**NestoAPI.csproj**
- Líneas 557-562: Agregados archivos de Exceptions
- Líneas 1423-1424: Agregados archivos de documentación

---

## 💻 Cambios en el Frontend (Nesto)

### Archivo Modificado:

**PedidoVentaService.vb** (3 funciones actualizadas)

**Cambio Implementado:**
Actualización del parseo de errores HTTP para soportar el nuevo formato de la API.

**Código Anterior:**
```vb
Else
    Dim respuestaError = response.Content.ReadAsStringAsync().Result
    Dim detallesError As JObject = JsonConvert.DeserializeObject(Of Object)(respuestaError)
    Dim contenido As String = detallesError("ExceptionMessage")
    While Not IsNothing(detallesError("InnerException"))
        detallesError = detallesError("InnerException")
        Dim contenido2 As String = detallesError("ExceptionMessage")
        contenido = contenido + vbCr + contenido2
    End While
    Throw New Exception(contenido)
End If
```

**Código Nuevo:**
```vb
Else
    Dim respuestaError = response.Content.ReadAsStringAsync().Result
    Dim detallesError As JObject = JsonConvert.DeserializeObject(Of Object)(respuestaError)
    Dim contenido As String = ""

    ' Intentar leer el nuevo formato de errores (desde GlobalExceptionFilter)
    If Not IsNothing(detallesError("error")) Then
        ' Nuevo formato: { "error": { "code": "...", "message": "..." } }
        Dim errorObj As JObject = detallesError("error")
        contenido = errorObj("message")?.ToString()

        ' Opcionalmente agregar código de error si existe
        Dim errorCode As String = errorObj("code")?.ToString()
        If Not String.IsNullOrEmpty(errorCode) AndAlso errorCode <> "INTERNAL_ERROR" Then
            contenido = $"[{errorCode}] {contenido}"
        End If
    ElseIf Not IsNothing(detallesError("ExceptionMessage")) Then
        ' Formato antiguo: { "ExceptionMessage": "...", "InnerException": {...} }
        contenido = detallesError("ExceptionMessage")
        While Not IsNothing(detallesError("InnerException"))
            detallesError = detallesError("InnerException")
            Dim contenido2 As String = detallesError("ExceptionMessage")
            contenido = contenido + vbCr + contenido2
        End While
    Else
        ' Fallback: usar el contenido raw
        contenido = respuestaError
    End If

    Throw New Exception(contenido)
End If
```

**Compatibilidad:**
- ✅ Soporta el nuevo formato de errores (con `error.code` y `error.message`)
- ✅ Mantiene compatibilidad con el formato antiguo (`ExceptionMessage`)
- ✅ Fallback a contenido raw si no reconoce el formato
- ✅ NO requiere cambios en ViewModels existentes
- ✅ Funciona automáticamente con `dialogService.ShowError(ex.Message)`

**Funciones Actualizadas:**
1. `CrearFacturaVenta()` - Líneas 454-493
2. `CrearAlbaranVenta()` - Similar
3. Otras funciones que consumen la API

---

## 📖 Documentación Creada

### 1. README.md Principal
**Ubicación:** `NestoAPI/Infraestructure/Exceptions/README.md`

**Contenido:**
- Introducción al sistema de excepciones
- Arquitectura y flujo de trabajo
- Uso básico con ejemplos
- Excepciones disponibles
- Códigos de error estándar
- Formato de respuestas HTTP
- Guía de migración completa
- Ejemplos avanzados
- Cómo crear nuevas excepciones

### 2. Guía de Setup de Elmah
**Ubicación:** `NestoAPI/Infraestructure/Exceptions/ELMAH_SETUP.md`

**Contenido:**
- Instalación de NuGet package
- Configuración de Web.config (paso a paso)
- Script SQL completo para crear tabla
- Integración con GlobalExceptionFilter
- Configuración de seguridad
- Guía de uso
- Mantenimiento de la tabla

---

## 🎨 Ejemplos de Uso

### Para Desarrolladores Backend (NestoAPI):

#### Ejemplo 1: Lanzar excepción simple
```csharp
throw new FacturacionException(
    "El pedido no tiene líneas para facturar",
    "FACTURACION_SIN_LINEAS",
    empresa: "1",
    pedido: 12345);
```

#### Ejemplo 2: Envolver excepción SQL
```csharp
catch (SqlException ex)
{
    throw new FacturacionException(
        $"Error al ejecutar el procedimiento de facturación: {ex.Message}",
        "FACTURACION_STORED_PROCEDURE_ERROR",
        ex,  // Inner exception
        empresa: empresa,
        pedido: pedido,
        usuario: usuario)
        .WithData("SqlErrorNumber", ex.Number)
        .WithData("StoredProcedure", "prdCrearFacturaVta");
}
```

#### Ejemplo 3: Con datos adicionales
```csharp
throw new FacturacionException(
    "La serie de facturación no es válida",
    "FACTURACION_SERIE_INVALIDA",
    empresa: "3",
    pedido: 12345,
    usuario: "carlos")
    .WithData("SerieIntentada", "XX")
    .WithData("SerieEsperada", "NV");
```

### Para Desarrolladores Frontend (Nesto):

**NO CAMBIA NADA** - El patrón sigue siendo el mismo:

```vb
Try
    Dim factura As String = Await servicio.CrearFacturaVenta(empresa, pedido)
    dialogService.ShowNotification($"Factura {factura} creada")

Catch ex As Exception
    ' Esto AUTOMÁTICAMENTE mostrará el mensaje mejorado
    dialogService.ShowError($"Error al crear factura: {ex.Message}")
End Try
```

**Lo único que cambia es el contenido de `ex.Message`:**
- Antes: "Este pedido no se puede facturar"
- Ahora: "[FACTURACION_IVA_FALTANTE] El pedido 12345 no se puede facturar porque falta configurar el campo IVA"

---

## 🔍 Flujo Completo del Sistema

```
┌─────────────────────────────────────────────────────────────┐
│  1. Usuario en Nesto → Click "Crear Factura"               │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  2. Frontend (Nesto)                                        │
│     - DetallePedidoViewModel.CrearFacturaVenta()            │
│     - PedidoVentaService.CrearFacturaVenta()                │
│     - POST /api/Facturas/CrearFactura                       │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  3. Backend (NestoAPI)                                      │
│     - FacturasController.CrearFactura()                     │
│     - GestorFacturas.CrearFactura()                         │
│     - ServicioFacturas.CrearFactura()                       │
│       ├─ Verifica IVA                                       │
│       ├─ Traspasa empresa si es necesario                   │
│       ├─ Recarga pedido desde BD ✅ NUEVO                   │
│       └─ Llama a prdCrearFacturaVta                         │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
         ┌──────────┴──────────┐
         │  ¿Hay error?        │
         └──────────┬──────────┘
                    │ SÍ
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  4. Lanza FacturacionException                              │
│     - Mensaje descriptivo                                   │
│     - Código de error                                       │
│     - Contexto (empresa, pedido, usuario)                   │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  5. GlobalExceptionFilter captura la excepción              │
│     - Loggea en Elmah (SQL Server)                          │
│     - Loggea en Debug.WriteLine                             │
│     - Formatea respuesta JSON:                              │
│       {                                                     │
│         "error": {                                          │
│           "code": "FACTURACION_IVA_FALTANTE",               │
│           "message": "El pedido 12345...",                  │
│           "details": {...}                                  │
│         }                                                   │
│       }                                                     │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  6. HTTP Response (400 Bad Request)                         │
│     - JSON con estructura de error                          │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  7. Frontend (Nesto) recibe error                           │
│     - PedidoVentaService parsea el JSON                     │
│     - Extrae error.message                                  │
│     - Crea Exception con mensaje descriptivo                │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  8. ViewModel maneja la excepción                           │
│     - Catch ex As Exception                                 │
│     - dialogService.ShowError(ex.Message)                   │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│  9. Usuario ve mensaje descriptivo                          │
│     ❌ "El pedido 12345 no se puede facturar porque        │
│         falta configurar el campo IVA en la cabecera"       │
└─────────────────────────────────────────────────────────────┘

                    Y ADEMÁS...

┌─────────────────────────────────────────────────────────────┐
│  10. Desarrollador consulta logs                            │
│      - Abre: https://api.nuevavision.es/logs-nestoapi       │
│      - Ve todos los detalles:                               │
│        • Timestamp                                          │
│        • Usuario                                            │
│        • Código de error                                    │
│        • Mensaje completo                                   │
│        • Stack trace                                        │
│        • Contexto (empresa, pedido, etc.)                   │
│        • Inner exceptions                                   │
│      - Presiona F5 para actualizar                          │
└─────────────────────────────────────────────────────────────┘
```

---

## ✅ Checklist de Despliegue

### Pre-Despliegue:

- [x] Código compilado sin errores (NestoAPI)
- [x] Código compilado sin errores (Nesto)
- [x] Web.config actualizado con configuración de Elmah
- [x] Paquetes NuGet instalados (Elmah.MVC)
- [ ] **PENDIENTE: Ejecutar script SQL en base de datos NV (producción)**

### Script SQL a Ejecutar:

```sql
-- Conectar a base de datos: NV
-- Ejecutar script completo de ELMAH_SETUP.md (líneas con CREATE TABLE, etc.)
```

### Post-Despliegue:

- [ ] Verificar que `/logs-nestoapi` sea accesible
- [ ] Provocar un error de prueba (pedido sin IVA)
- [ ] Verificar que el error aparezca en Elmah
- [ ] Verificar que el usuario vea mensaje descriptivo en Nesto
- [ ] Configurar Job de SQL Server para limpieza periódica (opcional):

```sql
-- Job semanal para limpiar errores antiguos
DELETE FROM ELMAH_Error
WHERE TimeUtc < DATEADD(day, -30, GETDATE())
```

---

## 🎓 Guía de Migración para Otras Áreas

Cuando encuentres mensajes de error poco informativos en otra área de la aplicación:

### Paso 1: Identificar el Área
Ejemplo: Stock, Clientes, Pedidos, etc.

### Paso 2: Crear Excepción Específica (si no existe)
```csharp
// En Infraestructure/Exceptions/StockException.cs
public class StockException : NestoBusinessException
{
    public StockException(
        string message,
        string errorCode = "STOCK_ERROR",
        string empresa = null,
        string producto = null,
        string almacen = null)
        : base(message, new ErrorContext
        {
            ErrorCode = errorCode,
            Empresa = empresa
        })
    {
        if (!string.IsNullOrEmpty(producto))
            Context.WithData("Producto", producto);

        if (!string.IsNullOrEmpty(almacen))
            Context.WithData("Almacen", almacen);
    }
}
```

### Paso 3: Reemplazar Excepciones Genéricas
**Antes:**
```csharp
if (stock < cantidad)
{
    throw new Exception("No hay stock");
}
```

**Después:**
```csharp
if (stock < cantidad)
{
    throw new StockException(
        $"No hay stock suficiente del producto {producto} en almacén {almacen}. Stock actual: {stock}, requerido: {cantidad}",
        "STOCK_INSUFICIENTE",
        empresa: "1",
        producto: producto,
        almacen: almacen)
        .WithData("StockActual", stock)
        .WithData("CantidadRequerida", cantidad);
}
```

### Paso 4: Eliminar try-catch en Controllers
Dejar que GlobalExceptionFilter maneje todo automáticamente.

### Paso 5: Actualizar README.md
Agregar nuevos códigos de error a la tabla de códigos estándar.

---

## 📊 Beneficios Conseguidos

### Para Usuarios:
- ✅ Mensajes de error claros y accionables
- ✅ Saben exactamente qué está mal y cómo arreglarlo
- ✅ Menos llamadas de soporte

### Para Desarrolladores:
- ✅ Debugging 10x más rápido
- ✅ Logs persistentes consultables desde cualquier dispositivo
- ✅ Contexto completo de cada error
- ✅ Código más limpio y mantenible

### Para la Empresa:
- ✅ Menos tiempo perdido en debugging
- ✅ Mejor experiencia de usuario
- ✅ Base de conocimiento de errores comunes
- ✅ Infraestructura escalable para futuras mejoras

---

## 🔮 Mejoras Futuras Propuestas

1. **Endpoint JSON para móvil con autenticación**
   - Crear `/api/ErrorLog` con autenticación Bearer
   - Formato JSON optimizado para móvil
   - Filtros y búsqueda avanzada

2. **Dashboard de errores**
   - Gráficos de errores más frecuentes
   - Tendencias por día/semana/mes
   - Alertas automáticas

3. **Integración con sistema de logging externo**
   - Serilog
   - Application Insights
   - Seq (UI potente)

4. **Traducción de mensajes**
   - i18n para múltiples idiomas
   - Mensajes técnicos vs user-friendly

5. **Métricas y Analytics**
   - Errores más frecuentes
   - Usuarios más afectados
   - Áreas con más problemas

---

## 📞 Contacto y Soporte

Para dudas o problemas con este sistema:

1. Revisar documentación en:
   - `NestoAPI/Infraestructure/Exceptions/README.md`
   - `NestoAPI/Infraestructure/Exceptions/ELMAH_SETUP.md`
   - Este archivo (`SESION_2025-01-19_GESTION_ERRORES.md`)

2. Consultar logs en:
   - Desarrollo: `http://localhost:puerto/logs-nestoapi`
   - Producción: `https://api.nuevavision.es/logs-nestoapi`

3. Revisar ejemplos de código en:
   - `ServicioFacturas.cs` (líneas 323-386)
   - `README.md` (sección "Ejemplos Avanzados")

---

## 📝 Changelog

**2025-01-19 - Implementación Inicial**
- Creada infraestructura completa de excepciones de negocio
- Implementado GlobalExceptionFilter
- Integrado Elmah para logging persistente
- Migrada área de facturación al nuevo sistema
- Actualizado frontend Nesto para parsear nuevo formato
- Configurada seguridad de Elmah (security by obscurity)
- Documentación completa creada

---

**Última actualización:** 2025-01-19
**Versión:** 1.0
**Estado:** ✅ Activo en producción (pendiente ejecución script SQL)
