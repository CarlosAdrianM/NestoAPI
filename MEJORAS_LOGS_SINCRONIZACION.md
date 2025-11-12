# Mejoras en Logs y Detección de Cambios del Sistema de Sincronización

**Fecha:** 2025-11-12
**Autor:** Claude Code
**Versión:** 1.0

## Índice

1. [Resumen Ejecutivo](#resumen-ejecutivo)
2. [Problema Original](#problema-original)
3. [Soluciones Implementadas](#soluciones-implementadas)
4. [Arquitectura de Logs](#arquitectura-de-logs)
5. [Ejemplos de Logs](#ejemplos-de-logs)
6. [Testing](#testing)
7. [Troubleshooting](#troubleshooting)

---

## Resumen Ejecutivo

Se implementaron mejoras significativas en el sistema de sincronización bidireccional entre Nesto y sistemas externos (Odoo, Prestashop) para mejorar la trazabilidad, reducir falsos positivos en detección de cambios, y facilitar el diagnóstico de problemas.

### Cambios Principales

1. **Logs enriquecidos** con identificadores completos (Cliente-Contacto-PersonaContacto)
2. **Normalización de comentarios** para evitar falsos positivos por diferencias en formato HTML
3. **Detección automática de duplicados** en mensajes recibidos
4. **Source dinámico** para diferenciar origen de mensajes ("Nesto" vs "Nesto viejo")
5. **Logs de emisión y recepción** para trazabilidad completa

---

## Problema Original

### 1. Logs Insuficientes

Los logs anteriores no mostraban información completa de los mensajes procesados:

```
[07:33:38.876] MessageId=16385923098460642 - Cliente 24971, Contacto 1
[07:33:38.892] MessageId=16386313881124273 - Cliente 24971, Contacto 1 (DUPLICADO)
```

**Problemas:**
- ❌ No se veía qué PersonasContacto estaban incluidas
- ❌ No se distinguía el origen del mensaje (Source)
- ❌ No quedaba claro si los "duplicados" eran reales o tenían PersonasContacto diferentes
- ❌ No se logueaba cuando un cliente NO se actualizaba por no tener cambios

### 2. Falsos Positivos en Comentarios

El sistema detectaba como diferentes comentarios que eran idénticos pero con diferente formato:

```
// Base de datos de Nesto (texto plano)
"A/A Mª JOSÉ: 660101678\n[Teléfonos extra] 649172403"

// Sistema externo (HTML)
"<p>[Teléfonos extra] 649172403\nA/A Mª JOSÉ: 660101678</p>"
```

Estos se marcaban como cambio cuando en realidad son idénticos.

### 3. Source Único

Todos los mensajes usaban `Source = "Nesto"`, sin poder distinguir entre:
- Sincronización manual/batch desde `api/Clientes/Sync`
- Operaciones normales de creación/modificación

---

## Soluciones Implementadas

### 1. Logs Enriquecidos (SyncWebhookController.cs)

#### Archivo: `NestoAPI/Controllers/SyncWebhookController.cs`
**Líneas:** 90-144

```csharp
// Loguear información detallada del mensaje
string logInfo = $"MessageId={request.Message.MessageId}";

if (!string.IsNullOrEmpty(syncMessage?.Cliente))
{
    logInfo += $" - Cliente {syncMessage.Cliente}";
}

if (!string.IsNullOrEmpty(syncMessage?.Contacto))
{
    logInfo += $", Contacto {syncMessage.Contacto}";
}

if (!string.IsNullOrEmpty(syncMessage?.Source))
{
    logInfo += $", Source={syncMessage.Source}";
}

if (syncMessage?.PersonasContacto != null && syncMessage.PersonasContacto.Count > 0)
{
    var personasInfo = string.Join(", ", syncMessage.PersonasContacto.Select(p =>
        $"Id={p.Id} ({p.Nombre})"
    ));
    logInfo += $", PersonasContacto=[{personasInfo}]";
}

Log($"📄 {logInfo}");
```

**Resultado:**
```
📄 MessageId=16386696225451217 - Cliente 39598, Contacto 0, Source=Nesto viejo, PersonasContacto=[Id=1 (Ainhoa)]
```

### 2. Detección Automática de Duplicados

#### Archivo: `NestoAPI/Controllers/SyncWebhookController.cs`
**Líneas:** 21, 24, 118-142

```csharp
// Diccionario para rastrear mensajes recientes
private static readonly Dictionary<string, DateTime> _recentMessages = new Dictionary<string, DateTime>();
private const int DuplicateDetectionWindowSeconds = 60;

// Detectar duplicados
string messageKey = $"{syncMessage?.Cliente}|{syncMessage?.Contacto}|{syncMessage?.Source}";

lock (_lockObj)
{
    // Limpiar mensajes antiguos (fuera de la ventana de detección)
    var cutoffTime = DateTime.UtcNow.AddSeconds(-DuplicateDetectionWindowSeconds);
    var keysToRemove = _recentMessages.Where(kvp => kvp.Value < cutoffTime).Select(kvp => kvp.Key).ToList();
    foreach (var key in keysToRemove)
    {
        _recentMessages.Remove(key);
    }

    // Verificar si es duplicado
    if (_recentMessages.ContainsKey(messageKey))
    {
        isDuplicate = true;
        var timeSinceLastMessage = DateTime.UtcNow - _recentMessages[messageKey];
        logInfo += $" ⚠️ POSIBLE DUPLICADO (último mensaje hace {timeSinceLastMessage.TotalSeconds:F1}s)";
    }

    // Registrar este mensaje
    _recentMessages[messageKey] = DateTime.UtcNow;
}
```

**Resultado:**
```
📄 MessageId=16386333144279214 - Cliente 24971, Contacto 0, Source=Nesto viejo ⚠️ POSIBLE DUPLICADO (último mensaje hace 0.5s)
```

### 3. Normalización de Comentarios

#### Archivo: `NestoAPI/Infraestructure/Sincronizacion/ClienteChangeDetector.cs`
**Líneas:** 67-69, 134-176

```csharp
/// <summary>
/// Normaliza comentarios para comparación:
/// - Quita etiquetas HTML (<p>, </p>, etc.)
/// - Normaliza saltos de línea (\r\n → \n)
/// - Ordena las líneas alfabéticamente para evitar falsos positivos por diferente orden
/// - Trim y mayúsculas
/// </summary>
private string NormalizeComentarios(string comentario)
{
    if (string.IsNullOrWhiteSpace(comentario))
    {
        return string.Empty;
    }

    // Quitar etiquetas HTML
    string sinHtml = Regex.Replace(comentario, @"<[^>]+>", string.Empty);

    // Normalizar saltos de línea
    sinHtml = sinHtml.Replace("\r\n", "\n").Replace("\r", "\n");

    // Dividir en líneas, ordenar alfabéticamente, y volver a unir
    var lineas = sinHtml.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
        .Select(linea => linea.Trim())
        .Where(linea => !string.IsNullOrWhiteSpace(linea))
        .OrderBy(linea => linea)
        .ToList();

    // Unir líneas ordenadas
    string resultado = string.Join("\n", lineas);

    return resultado.Trim().ToUpperInvariant();
}
```

**Comparación:**

| Comentario 1 | Comentario 2 | ¿Iguales? |
|--------------|--------------|-----------|
| `<p>[Teléfonos extra] 649172403\nA/A Mª JOSÉ: 660101678</p>` | `A/A Mª JOSÉ: 660101678\n[Teléfonos extra] 649172403` | ✅ SÍ |
| `[Tel] 123\n[Email] a@b.com` | `[Email] a@b.com\n[Tel] 123` | ✅ SÍ |
| `Cliente VIP` | `Cliente NORMAL` | ❌ NO |

### 4. Source Dinámico

#### Archivo: `NestoAPI/Infraestructure/GestorClientes.cs`
**Línea:** 1405

```csharp
public async Task PublicarClienteSincronizar(Cliente cliente, string source = "Nesto")
{
    // ...
    var message = new
    {
        // ... otros campos
        Source = source
    };
}
```

#### Archivo: `NestoAPI/Controllers/ClientesController.cs`
**Líneas:** 638, 694

```csharp
// Sincronización manual/batch (api/Clientes/Sync)
await _gestorClientes.PublicarClienteSincronizar(cliente, "Nesto viejo");

// Operaciones normales (ModificarCliente, CrearCliente)
await _gestorClientes.PublicarClienteSincronizar(cliente); // usa "Nesto" por defecto
```

### 5. Logs de No Actualización

#### Archivo: `NestoAPI/Infraestructure/Sincronizacion/ClientesSyncHandler.cs`
**Líneas:** 64-76, 188-190

```csharp
if (!cambios.Any())
{
    Console.WriteLine($"⚪ Cliente {clienteExterno}-{contactoExterno}: Sin cambios en datos principales, NO SE ACTUALIZA");

    // Continuar procesando PersonasContacto aunque el cliente no haya cambiado
    if (message.PersonasContacto != null && message.PersonasContacto.Any())
    {
        Console.WriteLine($"   ℹ️ Procesando {message.PersonasContacto.Count} PersonasContacto...");
        await ProcesarPersonasContacto(clienteExterno, contactoExterno, message.PersonasContacto);
    }

    return true;
}
```

### 6. Logs de Emisión de Mensajes

#### Archivo: `NestoAPI/Infraestructure/GestorClientes.cs`
**Líneas:** 1412-1416

```csharp
// Log para rastrear de dónde viene cada publicación
var personasInfo = personasContacto.Any()
    ? string.Join(", ", personasContacto.Select(p => $"Id={p.Id} ({p.Nombre})"))
    : "ninguna";
Console.WriteLine($"📤 Publicando mensaje: Cliente {cliente.Nº_Cliente?.Trim()}-{cliente.Contacto?.Trim()}, Source={source}, PersonasContacto=[{personasInfo}]");
```

---

## Arquitectura de Logs

### Flujo Completo de Logs

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. EMISIÓN (Nesto → Pub/Sub)                                   │
│    GestorClientes.PublicarClienteSincronizar()                 │
│    📤 Publicando mensaje: Cliente 24971-1, Source=Nesto viejo  │
│       PersonasContacto=[Id=1 (María), Id=2 (Juan)]             │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 2. RECEPCIÓN (Pub/Sub → Nesto)                                 │
│    SyncWebhookController.ReceiveWebhook()                      │
│    📨 Webhook recibido: MessageId=123, Subscription=...        │
│    📄 MessageId=123 - Cliente 24971, Contacto 1,               │
│       Source=Nesto viejo, PersonasContacto=[Id=1 (María)]      │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 3. PROCESAMIENTO (ClientesSyncHandler)                         │
│    🔍 Procesando Cliente 24971-1, PersonasContacto=[1, 2]      │
│       (Source=Nesto viejo)                                      │
│                                                                 │
│    ⚪ Cliente 24971-1: Sin cambios en datos principales,       │
│       NO SE ACTUALIZA                                           │
│       ℹ️ Procesando 2 PersonasContacto...                       │
│                                                                 │
│          🔍 PersonaContacto 24971-1-1 (María)                  │
│          ⚪ 24971-1-1: Sin cambios, NO SE ACTUALIZA            │
│                                                                 │
│          🔍 PersonaContacto 24971-1-2 (Juan)                   │
│          🔄 24971-1-2: Cambios detectados:                     │
│             - Teléfono: '600111222' → '600333444'              │
│          ✅ 24971-1-2: Actualizada exitosamente                │
│                                                                 │
│    ✅ Mensaje procesado exitosamente: 123                      │
└─────────────────────────────────────────────────────────────────┘
```

### Emojis en Logs

| Emoji | Significado | Ubicación |
|-------|-------------|-----------|
| 📤 | Mensaje emitido desde Nesto | GestorClientes |
| 📨 | Webhook recibido | SyncWebhookController |
| 📄 | Mensaje decodificado y deserializado | SyncWebhookController |
| 🔍 | Procesando cliente o persona de contacto | ClientesSyncHandler |
| ⚪ | Sin cambios, NO se actualiza | ClientesSyncHandler |
| 🔄 | Cambios detectados, actualizando | ClientesSyncHandler |
| ✅ | Actualización exitosa | ClientesSyncHandler |
| ⚠️ | Advertencia (duplicado, error, etc.) | Varios |
| ❌ | Error crítico | Varios |
| ℹ️ | Información adicional | Varios |

---

## Ejemplos de Logs

### Escenario 1: Cliente sin cambios, PersonaContacto actualizada

```
📤 Publicando mensaje: Cliente 39598-0, Source=Nesto viejo, PersonasContacto=[Id=1 (Ainhoa), Id=2 (Carlos)]
📨 Webhook recibido: MessageId=16386696225451217, Subscription=projects/nestomaps/subscriptions/sincronizacion-tablas-nesto
📄 MessageId=16386696225451217 - Cliente 39598, Contacto 0, Source=Nesto viejo, PersonasContacto=[Id=1 (Ainhoa), Id=2 (Carlos)]
🔍 Procesando Cliente 39598-0, PersonasContacto=[1, 2] (Source=Nesto viejo)
⚪ Cliente 39598-0: Sin cambios en datos principales, NO SE ACTUALIZA
   ℹ️ Procesando 2 PersonasContacto...
      🔍 PersonaContacto 39598-0-1 (Ainhoa)
      ⚪ 39598-0-1: Sin cambios, NO SE ACTUALIZA
      🔍 PersonaContacto 39598-0-2 (Carlos)
      🔄 39598-0-2: Cambios detectados:
         - Teléfono: '600111222' → '600333444'
      ✅ 39598-0-2: Actualizada exitosamente
✅ Mensaje procesado exitosamente: 16386696225451217
```

### Escenario 2: Cliente con cambios en comentarios (normalizados)

```
📤 Publicando mensaje: Cliente 24971-1, Source=Nesto, PersonasContacto=[Id=1 (María)]
📨 Webhook recibido: MessageId=16387001234567890, Subscription=projects/nestomaps/subscriptions/sincronizacion-tablas-nesto
📄 MessageId=16387001234567890 - Cliente 24971, Contacto 1, Source=Nesto, PersonasContacto=[Id=1 (María)]
🔍 Procesando Cliente 24971-1, PersonasContacto=[1] (Source=Nesto)
⚪ Cliente 24971-1: Sin cambios en datos principales, NO SE ACTUALIZA
   ℹ️ Procesando 1 PersonasContacto...
      🔍 PersonaContacto 24971-1-1 (María)
      ⚪ 24971-1-1: Sin cambios, NO SE ACTUALIZA
✅ Mensaje procesado exitosamente: 16387001234567890
```

**Nota:** Los comentarios `<p>Tel: 123\nEmail: a@b.com</p>` y `Email: a@b.com\nTel: 123` se detectan como iguales gracias a la normalización.

### Escenario 3: Duplicados detectados

```
📤 Publicando mensaje: Cliente 24971-0, Source=Nesto viejo, PersonasContacto=[Id=1 (Juan)]
📨 Webhook recibido: MessageId=16386333144279214, Subscription=projects/nestomaps/subscriptions/sincronizacion-tablas-nesto
📄 MessageId=16386333144279214 - Cliente 24971, Contacto 0, Source=Nesto viejo, PersonasContacto=[Id=1 (Juan)]
🔍 Procesando Cliente 24971-0, PersonasContacto=[1] (Source=Nesto viejo)
⚪ Cliente 24971-0: Sin cambios en datos principales, NO SE ACTUALIZA
   ℹ️ Procesando 1 PersonasContacto...
      🔍 PersonaContacto 24971-0-1 (Juan)
      ⚪ 24971-0-1: Sin cambios, NO SE ACTUALIZA
✅ Mensaje procesado exitosamente: 16386333144279214

📨 Webhook recibido: MessageId=16386333144279999, Subscription=projects/nestomaps/subscriptions/sincronizacion-tablas-nesto
📄 MessageId=16386333144279999 - Cliente 24971, Contacto 0, Source=Nesto viejo ⚠️ POSIBLE DUPLICADO (último mensaje hace 0.5s), PersonasContacto=[Id=1 (Juan)]
🔍 Procesando Cliente 24971-0, PersonasContacto=[1] (Source=Nesto viejo)
⚪ Cliente 24971-0: Sin cambios en datos principales, NO SE ACTUALIZA
   ℹ️ Procesando 1 PersonasContacto...
      🔍 PersonaContacto 24971-0-1 (Juan)
      ⚪ 24971-0-1: Sin cambios, NO SE ACTUALIZA
✅ Mensaje procesado exitosamente: 16386333144279999
```

---

## Testing

### Tests Unitarios

Ver archivo: `NestoAPI.Tests/Infraestructure/Sincronizacion/ClienteChangeDetectorTests.cs`

#### Tests de Normalización de Comentarios

```csharp
[TestClass]
public class ClienteChangeDetectorTests
{
    [TestMethod]
    public void NormalizeComentarios_ComentariosConHTMLYOrdenDiferente_DebenSerIguales()
    {
        // Arrange
        var detector = new ClienteChangeDetector();
        var comentario1 = "<p>[Teléfonos extra] 649172403\nA/A Mª JOSÉ: 660101678</p>";
        var comentario2 = "A/A Mª JOSÉ: 660101678\n[Teléfonos extra] 649172403";

        // Act
        var cliente = new Cliente { Comentarios = comentario1 };
        var mensaje = new ExternalSyncMessageDTO { Comentarios = comentario2 };
        var cambios = detector.DetectarCambios(cliente, mensaje);

        // Assert
        Assert.IsFalse(cambios.Any(c => c.Contains("Comentarios")));
    }

    [TestMethod]
    public void NormalizeComentarios_ComentariosDiferentes_DebenDetectarseCambios()
    {
        // Arrange
        var detector = new ClienteChangeDetector();
        var comentario1 = "Cliente VIP";
        var comentario2 = "Cliente NORMAL";

        // Act
        var cliente = new Cliente { Comentarios = comentario1 };
        var mensaje = new ExternalSyncMessageDTO { Comentarios = comentario2 };
        var cambios = detector.DetectarCambios(cliente, mensaje);

        // Assert
        Assert.IsTrue(cambios.Any(c => c.Contains("Comentarios")));
    }
}
```

### Tests de Integración

Ver archivo: `NestoAPI.Tests/Controllers/SyncWebhookControllerTests.cs`

---

## Troubleshooting

### Problema: Mensajes Duplicados

**Síntomas:**
```
📄 MessageId=123 - Cliente 24971, Contacto 0
📄 MessageId=456 - Cliente 24971, Contacto 0 ⚠️ POSIBLE DUPLICADO (último mensaje hace 0.3s)
```

**Causas Posibles:**

1. **Trigger de Base de Datos Múltiple**
   - Revisar si el trigger `trg_Clientes_Sincronizacion` se dispara múltiples veces
   - Verificar si hay múltiples `SaveChangesAsync()` en el mismo contexto

2. **Sincronización Bidireccional (Loop)**
   - Sistema externo recibe mensaje → lo procesa → vuelve a enviar a Nesto
   - Verificar que `ClientesSyncHandler` no esté publicando mensajes tras actualizar

3. **Retry de Pub/Sub**
   - Pub/Sub reenvía mensajes si no recibe ACK rápido
   - Verificar que el webhook retorna 200 OK rápidamente

**Solución:**
1. Revisar logs de emisión (📤) para ver cuántas veces se publica el mismo mensaje
2. Comparar timestamps entre emisión y recepción
3. Verificar que no haya loops de sincronización

### Problema: Falsos Positivos en Comentarios

**Síntomas:**
```
🔄 24971-1: Cambios detectados:
   - Comentarios: '<P>TEL: 123</P>' → 'TEL: 123'
```

**Solución:**
La normalización ya maneja este caso. Si aún ves falsos positivos, verificar:
1. Que `ClienteChangeDetector` usa `SonIgualesComentarios()` para el campo Comentarios
2. Que no hay caracteres especiales no manejados (emojis, etc.)

### Problema: PersonasContacto No Se Actualizan

**Síntomas:**
```
⚪ Cliente 24971-1: Sin cambios en datos principales, NO SE ACTUALIZA
   ℹ️ Procesando 1 PersonasContacto...
      ⚠️ 24971-1-1: No existe en Nesto
```

**Causas Posibles:**
1. El `Id` de la PersonaContacto no coincide con el `Número` en la base de datos
2. La PersonaContacto tiene `Estado < 0` (fue eliminada)

**Solución:**
1. Verificar en la base de datos: `SELECT * FROM PersonasContactoClientes WHERE Cliente = '24971' AND Contacto = '1'`
2. Verificar que el campo `Número` coincida con el `Id` del mensaje

---

## Configuración

### Variables de Configuración

#### SyncWebhookController.cs

```csharp
// Número máximo de logs almacenados en memoria
private const int MaxLogs = 100;

// Ventana de tiempo para detectar duplicados (en segundos)
private const int DuplicateDetectionWindowSeconds = 60;
```

Para cambiar estos valores, editar las constantes en `SyncWebhookController.cs`.

---

## Referencias

- [SINCRONIZACION_BIDIRECCIONAL_ODOO_SETUP.md](./SINCRONIZACION_BIDIRECCIONAL_ODOO_SETUP.md) - Setup completo del sistema de sincronización
- [GUIA_AGREGAR_TABLA_SINCRONIZACION.md](./GUIA_AGREGAR_TABLA_SINCRONIZACION.md) - Cómo agregar nuevas tablas al sistema
- [ESTADO_SESION_SINCRONIZACION.md](./ESTADO_SESION_SINCRONIZACION.md) - Estado actual de la sincronización

---

## Changelog

### Versión 1.0 (2025-11-12)

#### Añadido
- Logs enriquecidos con Cliente, Contacto y PersonasContacto
- Detección automática de duplicados con ventana de 60 segundos
- Normalización de comentarios (HTML, orden de líneas)
- Source dinámico ("Nesto" vs "Nesto viejo")
- Logs de emisión de mensajes
- Logs de no actualización (sin cambios)

#### Modificado
- `SyncWebhookController.cs` - Logs mejorados y detección de duplicados
- `ClientesSyncHandler.cs` - Logs jerárquicos para PersonasContacto
- `ClienteChangeDetector.cs` - Normalización de comentarios
- `GestorClientes.cs` - Source dinámico y logs de emisión
- `IGestorClientes.cs` - Firma del método `PublicarClienteSincronizar`
- `ClientesController.cs` - Uso de Source="Nesto viejo" en sincronización batch

---

**Documentación generada:** 2025-11-12
**Última actualización:** 2025-11-12
