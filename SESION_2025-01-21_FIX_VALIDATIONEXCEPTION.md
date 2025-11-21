# Sesión 2025-01-21: Fix ValidationException en Flujo de Validación de Pedidos

## 📋 Resumen Ejecutivo

Esta sesión resolvió un problema crítico donde las `ValidationException` lanzadas en el backend no llegaban correctamente al frontend, rompiendo el flujo de "Crear sin pasar validación" y mostrando mensajes de error inútiles al usuario.

## 🐛 Problema Original

### Síntoma
Cuando un pedido no pasaba las validaciones de precios/ofertas/descuentos:
- El usuario veía el mensaje genérico: `"Exception of type System.Exception was thrown"`
- El flujo de "¿Desea crear el pedido sin pasar validación?" NO se activaba
- Los ViewModels NO podían capturar la `ValidationException`

### Causa Raíz

**Backend (NestoAPI):**
- `PedidosVentaController.cs:1073` lanzaba `throw new ValidationException(...)`
- `GlobalExceptionFilter` lo capturaba pero como NO era `NestoBusinessException`, entraba por el bloque `else`:
  ```csharp
  if (exception is NestoBusinessException businessException)
  {
      // Manejo especial con código de error
  }
  else
  {
      // Excepciones genéricas - aquí caía ValidationException ❌
      statusCode = HttpStatusCode.InternalServerError;
      responseContent = CreateGenericErrorResponse(exception);
  }
  ```
- Devolvía respuesta JSON genérica sin código de error identificable

**Frontend (Nesto):**
- `PlantillaVentaService.vb` y `PedidoVentaService.vb` intentaban detectar `ValidationException` por el campo `ExceptionType` (formato antiguo)
- Como el `GlobalExceptionFilter` devolvía formato nuevo (`error.code`), NO lo detectaba
- Lanzaba `Exception` genérica en lugar de `ValidationException`
- Los ViewModels esperaban `ValidationException` para activar el flujo especial:
  ```vb
  Catch ex As ValidationException
      crearModificarEx = ex
      ' Preguntar: "¿Crear sin pasar validación?"
  ```

## ✅ Solución Implementada

### 1. Backend: Crear `PedidoValidacionException`

**Archivo creado:** `NestoAPI/Infraestructure/Exceptions/PedidoValidacionException.cs`

```csharp
/// <summary>
/// Excepción para errores de validación de pedidos que hereda de NestoBusinessException.
/// Código de error: "PEDIDO_VALIDACION_FALLO"
/// StatusCode: 400 (BadRequest)
/// </summary>
public class PedidoValidacionException : NestoBusinessException
{
    public RespuestaValidacion RespuestaValidacion { get; }

    public PedidoValidacionException(
        string mensaje,
        RespuestaValidacion respuestaValidacion,
        string empresa = null,
        int? pedido = null,
        string cliente = null,
        string usuario = null)
        : base(mensaje, new ErrorContext
        {
            ErrorCode = "PEDIDO_VALIDACION_FALLO",
            Empresa = empresa,
            Pedido = pedido,
            Cliente = cliente,
            Usuario = usuario
        })
    {
        RespuestaValidacion = respuestaValidacion;
        StatusCode = HttpStatusCode.BadRequest;
        // Agregar detalles de validación al contexto...
    }
}
```

**Características:**
- ✅ Hereda de `NestoBusinessException` → `GlobalExceptionFilter` lo maneja correctamente
- ✅ Código de error específico: `"PEDIDO_VALIDACION_FALLO"`
- ✅ Incluye `RespuestaValidacion` completa con todos los motivos y errores
- ✅ StatusCode 400 (BadRequest) en lugar de 500 (InternalServerError)
- ✅ Contexto rico con empresa, pedido, cliente, usuario

### 2. Backend: Actualizar `PedidosVentaController`

**Archivo modificado:** `NestoAPI/Controllers/PedidosVentaController.cs`

**Antes:**
```csharp
if (!respuestaValidacion.ValidacionSuperada)
{
    throw new ValidationException(respuestaValidacion.Motivo);
}
```

**Después:**
```csharp
if (!respuestaValidacion.ValidacionSuperada)
{
    // Carlos 21/11/24: Usar PedidoValidacionException para que
    // GlobalExceptionFilter lo maneje correctamente
    throw new PedidoValidacionException(
        respuestaValidacion.Motivo,
        respuestaValidacion,
        empresa: pedido.empresa,
        pedido: pedido.numero,
        cliente: pedido.cliente,
        usuario: pedido.Usuario);
}
```

**Agregado:**
- `using NestoAPI.Infraestructure.Exceptions;`

### 3. Frontend: Helper Centralizado

**Archivo creado:** `Nesto/Infrastructure/Shared/HttpErrorHelper.cs`

```csharp
/// <summary>
/// Helper para parsear errores HTTP del API
/// Soporta formato nuevo (GlobalExceptionFilter) y antiguo (fallback)
/// </summary>
public static class HttpErrorHelper
{
    public static string ParsearErrorHttp(JObject detallesError)
    {
        // Intentar formato NUEVO: { "error": { "code": "...", "message": "..." } }
        if (detallesError["error"] != null)
        {
            var errorObj = detallesError["error"] as JObject;
            var contenido = errorObj["message"]?.ToString() ?? "";
            var errorCode = errorObj["code"]?.ToString();

            if (!string.IsNullOrEmpty(errorCode) && errorCode != "INTERNAL_ERROR")
            {
                contenido = $"[{errorCode}] {contenido}";
            }
            return contenido;
        }

        // Fallback al formato ANTIGUO: { "ExceptionMessage": "..." }
        // ... (código de compatibilidad)
    }
}
```

**Ventajas:**
- ✅ Código centralizado y reutilizable
- ✅ Soporta ambos formatos (nuevo y antiguo)
- ✅ Incluye código de error en el mensaje

### 4. Frontend: Actualizar `PlantillaVentaService.vb`

**Archivo modificado:** `Nesto/Modulos/PlantillaVenta/PlantillaVentaService.vb`

**Antes:**
```vb
Dim contenido As String = detallesError("ExceptionMessage")
While Not IsNothing(detallesError("InnerException"))
    ' Recorrer inner exceptions manualmente...
End While

Dim tipoEx As String = CStr(detallesError("ExceptionType"))
If Not String.IsNullOrEmpty(tipoEx) AndAlso tipoEx.Contains("ValidationException") Then
    Throw New ValidationException(contenido)
End If
```

**Después:**
```vb
' Carlos 21/11/24: Detectar si es un error de validación de pedido
Dim errorCode As String = Nothing
If Not IsNothing(detallesError("error")) Then
    Dim errorObj As JObject = detallesError("error")
    errorCode = errorObj("code")?.ToString()
End If

' Parsear el mensaje usando HttpErrorHelper
Dim contenido As String = HttpErrorHelper.ParsearErrorHttp(detallesError)

' Si es error de validación de pedido, lanzar ValidationException
If errorCode = "PEDIDO_VALIDACION_FALLO" Then
    Throw New System.ComponentModel.DataAnnotations.ValidationException(contenido)
Else
    Throw New Exception(contenido)
End If
```

**Agregado:**
- `Imports Nesto.Infrastructure.Shared`

### 5. Frontend: Actualizar `PedidoVentaService.vb`

**Archivo modificado:** `Nesto/Modulos/PedidoVenta/PedidoVenta/PedidoVentaService.vb`

- Cambios idénticos a `PlantillaVentaService.vb`
- Detecta código `"PEDIDO_VALIDACION_FALLO"` y lanza `ValidationException`
- Usa `HttpErrorHelper` para parsear errores

**Agregado:**
- `Imports Nesto.Infrastructure.Shared`

### 6. Documentación Actualizada

**Archivo modificado:** `NestoAPI/Infraestructure/Exceptions/README.md`

Agregado nuevo código de error a la tabla:

| Código | Descripción |
|--------|-------------|
| `PEDIDO_VALIDACION_FALLO` | El pedido no pasó las validaciones de precios/ofertas/descuentos |

---

## 🔄 Flujo Completo (DESPUÉS del Fix)

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Usuario en Nesto intenta crear pedido con oferta        │
│    no autorizada                                            │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│ 2. Frontend: PlantillaVentaService.CrearPedido()            │
│    - POST api/PedidosVenta                                  │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│ 3. Backend: PedidosVentaController.PostCabPedidoVta()       │
│    - Valida pedido con GestorPrecios                        │
│    - respuestaValidacion.ValidacionSuperada = false         │
│    - throw new PedidoValidacionException(...)  ✅ NUEVO     │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│ 4. GlobalExceptionFilter captura la excepción               │
│    - Detecta: exception is NestoBusinessException ✅        │
│    - Crea respuesta JSON estructurada:                      │
│      {                                                      │
│        "error": {                                           │
│          "code": "PEDIDO_VALIDACION_FALLO",                 │
│          "message": "La oferta X no está autorizada...",    │
│          "details": {...},                                  │
│          "timestamp": "2025-01-21T..."                      │
│        }                                                    │
│      }                                                      │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│ 5. HTTP Response: 400 Bad Request con JSON                  │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│ 6. Frontend: PlantillaVentaService recibe error             │
│    - Parsea JSON con HttpErrorHelper                        │
│    - Detecta errorCode = "PEDIDO_VALIDACION_FALLO" ✅       │
│    - throw new ValidationException(contenido) ✅            │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│ 7. PlantillaVentaViewModel captura ValidationException ✅   │
│    Catch ex As ValidationException                          │
│        crearEx = ex                                         │
│        ' Verificar si puede crear sin pasar validación      │
└───────────────────┬─────────────────────────────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────┐
│ 8. Usuario ve diálogo de confirmación:                      │
│    ❓ "La oferta X no está autorizada para este cliente.   │
│        ¿Desea crear el pedido sin pasar validación?"        │
│                                                             │
│    [SÍ] → Crea pedido con CreadoSinPasarValidacion = true  │
│    [NO] → Cancela operación                                 │
└─────────────────────────────────────────────────────────────┘
```

---

## 📊 Archivos Modificados

### Backend (NestoAPI) - 3 archivos

1. **`Infraestructure/Exceptions/PedidoValidacionException.cs`** (NUEVO)
   - Nueva excepción que hereda de `NestoBusinessException`
   - Código de error: `"PEDIDO_VALIDACION_FALLO"`
   - Incluye `RespuestaValidacion` completa

2. **`Controllers/PedidosVentaController.cs`**
   - Agregado: `using NestoAPI.Infraestructure.Exceptions;`
   - Línea 1077: Reemplazado `ValidationException` por `PedidoValidacionException`

3. **`Infraestructure/Exceptions/README.md`**
   - Agregado código de error `PEDIDO_VALIDACION_FALLO` a la tabla de códigos estándar

### Frontend (Nesto) - 3 archivos

4. **`Infrastructure/Shared/HttpErrorHelper.cs`** (NUEVO)
   - Helper centralizado para parsear errores HTTP
   - Soporta formato nuevo (GlobalExceptionFilter) y antiguo (fallback)

5. **`Modulos/PlantillaVenta/PlantillaVentaService.vb`**
   - Agregado: `Imports Nesto.Infrastructure.Shared`
   - Línea 221-238: Detecta código `PEDIDO_VALIDACION_FALLO` y lanza `ValidationException`
   - Usa `HttpErrorHelper` para parsear errores

6. **`Modulos/PedidoVenta/PedidoVenta/PedidoVentaService.vb`**
   - Agregado: `Imports Nesto.Infrastructure.Shared`
   - Línea 678-695: Detecta código `PEDIDO_VALIDACION_FALLO` y lanza `ValidationException`
   - Usa `HttpErrorHelper` para parsear errores

---

## ✅ Beneficios Conseguidos

### Para Usuarios
- ✅ **Mensajes claros**: Ahora ven exactamente qué oferta/descuento falló
- ✅ **Flujo funcional**: El diálogo "¿Crear sin pasar validación?" vuelve a funcionar
- ✅ **Sin mensajes crípticos**: Se acabó el `"Exception of type System.Exception was thrown"`

### Para Desarrolladores
- ✅ **Arquitectura consistente**: Todas las excepciones de negocio heredan de `NestoBusinessException`
- ✅ **Código reutilizable**: `HttpErrorHelper` centraliza el parseo de errores
- ✅ **Debugging mejorado**: Logs en Elmah con contexto completo (empresa, pedido, usuario)
- ✅ **Código más limpio**: Eliminado parseo manual de errores repetido en múltiples servicios

### Para el Sistema
- ✅ **StatusCode correcto**: 400 (BadRequest) en lugar de 500 (InternalServerError)
- ✅ **Formato estándar**: Respuestas JSON consistentes en toda la API
- ✅ **Extensible**: Fácil agregar nuevos códigos de error

---

## 🧪 Cómo Probar

### Escenario de Prueba: Oferta No Autorizada

1. **Abrir PlantillaVenta o DetallePedidoVenta** en Nesto

2. **Crear pedido con una oferta no autorizada:**
   - Cliente: Cualquier cliente que NO sea "El Edén"
   - Producto: Algún producto con oferta activa que requiere autorización
   - Descuento/Oferta: Aplicar oferta no autorizada

3. **Hacer clic en "Crear Pedido"**

4. **Verificar comportamiento:**
   - ✅ Aparece diálogo de confirmación
   - ✅ Mensaje descriptivo explica qué oferta/descuento falló
   - ✅ Opciones: "SÍ" (crear sin validación) o "NO" (cancelar)

5. **Si se hace clic en "SÍ":**
   - ✅ Pedido se crea con `CreadoSinPasarValidacion = true`
   - ✅ No se bloquea la operación

6. **Si se hace clic en "NO":**
   - ✅ Operación se cancela
   - ✅ Usuario puede corregir el pedido

### Verificar Logs en Elmah

- URL: `https://api.nuevavision.es/logs-nestoapi`
- Buscar errores con código: `PEDIDO_VALIDACION_FALLO`
- Verificar que incluyen:
  - ✅ Mensaje descriptivo
  - ✅ Empresa, Pedido, Cliente, Usuario
  - ✅ Detalles de validación (motivos, errores)
  - ✅ StatusCode: 400 (BadRequest)

---

## 🔮 Mejoras Futuras Propuestas

1. **Crear más excepciones específicas:**
   - `StockInsuficienteException` para errores de stock
   - `ClienteInactivoException` para clientes bloqueados
   - `ProductoDescatalogoException` para productos no disponibles

2. **HttpErrorHelper en todos los servicios:**
   - Actualizar los 9 servicios VB que aún usan parseo manual
   - Eliminar código duplicado de parseo de errores

3. **Códigos de error más granulares:**
   - `PEDIDO_VALIDACION_OFERTA_NO_AUTORIZADA`
   - `PEDIDO_VALIDACION_DESCUENTO_EXCESIVO`
   - `PEDIDO_VALIDACION_PRECIO_INCORRECTO`

4. **Testing automatizado:**
   - Tests unitarios para `PedidoValidacionException`
   - Tests de integración para el flujo completo
   - Tests de regresión para evitar que se rompa nuevamente

---

## 📞 Contacto y Soporte

Para dudas o problemas:

1. Revisar documentación:
   - Este archivo (`SESION_2025-01-21_FIX_VALIDATIONEXCEPTION.md`)
   - `SESION_2025-01-19_GESTION_ERRORES.md` (documentación base del sistema)
   - `Infraestructure/Exceptions/README.md` (guía de uso de excepciones)

2. Consultar logs: `https://api.nuevavision.es/logs-nestoapi`

3. Revisar código:
   - Backend: `PedidoValidacionException.cs`, `PedidosVentaController.cs`
   - Frontend: `HttpErrorHelper.cs`, `PlantillaVentaService.vb`, `PedidoVentaService.vb`

---

**Autor:** Claude Code (Anthropic)
**Fecha:** 21 de Enero de 2025
**Estado:** ✅ Implementado y listo para probar
**Sesión relacionada:** `SESION_2025-01-19_GESTION_ERRORES.md`
