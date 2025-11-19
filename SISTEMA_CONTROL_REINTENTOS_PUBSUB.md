# Sistema de Control de Reintentos para Pub/Sub

## 📋 Resumen

Se ha implementado un **sistema centralizado de control de reintentos** para prevenir bucles infinitos en mensajes de Google Pub/Sub que fallan repetidamente. El sistema registra cada intento, limita el número máximo de reintentos, y proporciona endpoints de gestión para resolver poison pills manualmente.

**Fecha**: 2025-01-19
**Estado**: ✅ Implementación completa

---

## 🎯 Problema Resuelto

### Antes (Sin Control de Reintentos)
- ❌ Mensajes que fallan retornan 500 → Pub/Sub reintenta indefinidamente
- ❌ Bucle infinito de reintentos ♾️
- ❌ Sin visibilidad de mensajes problemáticos
- ❌ Necesidad de vaciar la cola manualmente desde GCP Console

### Después (Con Control de Reintentos)
- ✅ Máximo 5 intentos por mensaje
- ✅ Poison pills detectados automáticamente
- ✅ Retorno de 200 después del límite (Pub/Sub deja de reintentar)
- ✅ Endpoint de gestión para revisar y resolver poison pills
- ✅ Sistema de estados configurable
- ✅ Auditoría completa en base de datos

---

## 🏗️ Arquitectura Implementada

```
┌─────────────────────────────────────────────────────────┐
│  Google Pub/Sub Push → POST /api/sync/webhook          │
└──────────────────┬──────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────┐
│  SyncWebhookController                                  │
│  1. Validar mensaje                                     │
│  2. Deserializar                                        │
└──────────────────┬──────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────┐
│  MessageRetryManager.ShouldProcessMessage()             │
│  • Si attempts < 5 → Continuar                          │
│  • Si attempts >= 5 → Retornar 200 (poison pill)        │
│  • Si status = Resolved/PermanentFailure → No procesar  │
│  • Si status = Reprocess → Resetear y continuar         │
└──────────────────┬──────────────────────────────────────┘
                   │
                   ▼ (si debe procesarse)
┌─────────────────────────────────────────────────────────┐
│  MessageRetryManager.RecordAttempt()                    │
│  • Registrar intento en BD                              │
│  • Incrementar contador                                 │
│  • Si attempts >= 5 → Cambiar status a "PoisonPill"     │
└──────────────────┬──────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────┐
│  SyncTableRouter → Handler específico                   │
│  (ClientesSyncHandler, ProductosSyncHandler, etc.)      │
└──────────────────┬──────────────────────────────────────┘
                   │
        ┌──────────┴──────────┐
        │                     │
        ▼ ÉXITO               ▼ FALLO
┌──────────────────┐   ┌──────────────────────────┐
│ RecordSuccess()  │   │ RecordFailure()          │
│ • Eliminar       │   │ • Guardar error          │
│   registro       │   │ • Incrementar attempts   │
│                  │   │ • Si >= 5 → PoisonPill   │
└──────────────────┘   └──────────────────────────┘
                                │
                                ▼
                        ┌──────────────────┐
                        │ Retornar 200 o   │
                        │ 500 según límite │
                        └──────────────────┘
```

---

## 📦 Componentes Implementados

### 1. Tabla SQL: `SyncMessageRetries`
**Archivo**: `SCRIPT_SQL_SYNC_MESSAGE_RETRIES.sql`

**Estructura**:
```sql
CREATE TABLE SyncMessageRetries (
    MessageId NVARCHAR(255) PRIMARY KEY,
    Tabla NVARCHAR(50) NOT NULL,
    EntityId NVARCHAR(100),
    Source NVARCHAR(50),
    AttemptCount INT NOT NULL DEFAULT 0,
    FirstAttemptDate DATETIME NOT NULL,
    LastAttemptDate DATETIME NOT NULL,
    LastError NVARCHAR(MAX),
    Status NVARCHAR(20) NOT NULL,
    MessageData NVARCHAR(MAX)
)
```

**Índices creados**:
- `IX_SyncMessageRetries_Status`: Para filtrar por estado
- `IX_SyncMessageRetries_Tabla_Status`: Para filtrar por tabla y estado
- `IX_SyncMessageRetries_LastAttemptDate`: Para ordenar por fecha

### 2. Enumeración: `RetryStatus`
**Archivo**: `Models/Sincronizacion/RetryStatus.cs`

**Estados**:
```csharp
public enum RetryStatus
{
    Retrying,           // Aún reintentando (< 5 intentos)
    PoisonPill,         // Límite alcanzado, pendiente de revisión
    Reprocess,          // Marcado para reprocesar (resetea contador)
    Resolved,           // Marcado como solucionado manualmente
    PermanentFailure    // Marcado como fallo permanente
}
```

### 3. Modelo Entity Framework: `SyncMessageRetry`
**Archivo**: `Models/Sincronizacion/SyncMessageRetry.cs`

**Características**:
- Mapeado a tabla `SyncMessageRetries`
- Propiedad computada `StatusEnum` para conversión automática
- DbSet agregado a `NVEntities.Partial.cs`

### 4. Gestor: `MessageRetryManager`
**Archivo**: `Infraestructure/Sincronizacion/MessageRetryManager.cs`

**Métodos principales**:

#### `ShouldProcessMessage(messageId)`
Verifica si un mensaje debe procesarse:
- ✅ `Retrying` con attempts < 5 → Procesar
- 🚫 `PoisonPill` → NO procesar (retornar 200)
- 🔄 `Reprocess` → Procesar (reseteará contador)
- ✅ `Resolved` → NO procesar
- ❌ `PermanentFailure` → NO procesar

#### `RecordAttempt(messageId, message)`
Registra un intento de procesamiento:
- Primer intento → Crear registro con `Status = Retrying`
- Intentos subsecuentes → Incrementar `AttemptCount`
- Si `AttemptCount >= 5` → Cambiar a `Status = PoisonPill`
- Si status era `Reprocess` → Resetear contador a 1

#### `RecordSuccess(messageId)`
Registra procesamiento exitoso:
- Elimina el registro de la tabla (no necesita auditoría de éxitos)

#### `RecordFailure(messageId, error)`
Registra fallo:
- Guarda error en campo `LastError`
- Actualiza `LastAttemptDate`

#### `ChangeStatus(messageId, newStatus)`
Cambia estado manualmente (usado por endpoint de gestión):
- Valida que el mensaje existe
- Actualiza `Status`
- Si es `Reprocess`, limpia `LastError`

### 5. DTOs para Gestión

#### `PoisonPillDTO`
**Archivo**: `Models/Sincronizacion/PoisonPillDTO.cs`

Para visualizar poison pills en el endpoint:
```csharp
public class PoisonPillDTO
{
    public string MessageId { get; set; }
    public string Tabla { get; set; }
    public string EntityId { get; set; }
    public int AttemptCount { get; set; }
    public string LastError { get; set; }
    public string Status { get; set; }
    public string TimeSinceFirstAttempt { get; set; }
    public string TimeSinceLastAttempt { get; set; }
    // ... otros campos
}
```

#### `ChangeStatusRequest`
**Archivo**: `Models/Sincronizacion/ChangeStatusRequest.cs`

Para cambiar estado de un mensaje:
```csharp
public class ChangeStatusRequest
{
    public string MessageId { get; set; }
    public string NewStatus { get; set; } // "Reprocess", "Resolved", "PermanentFailure"
}
```

### 6. Integración en `SyncWebhookController`

**Cambios realizados**:

1. **Constructor actualizado**:
```csharp
public SyncWebhookController(SyncTableRouter router, MessageRetryManager retryManager = null)
{
    _router = router;
    _retryManager = retryManager ?? new MessageRetryManager(new Models.NVEntities());
}
```

2. **Flujo en `ReceiveWebhook()`**:
```csharp
// 1. Verificar si debe procesarse
bool shouldProcess = await _retryManager.ShouldProcessMessage(messageId);
if (!shouldProcess)
{
    return Ok(new { success = false, poisonPill = true });
}

// 2. Registrar intento
await _retryManager.RecordAttempt(messageId, syncMessage);

// 3. Procesar mensaje
bool success = await _router.RouteAsync(syncMessage);

// 4. Registrar resultado
if (success)
{
    await _retryManager.RecordSuccess(messageId);
}
else
{
    await _retryManager.RecordFailure(messageId, "Error...");
}
```

3. **Manejo de excepciones**:
```csharp
catch (Exception ex)
{
    await _retryManager.RecordFailure(messageId, ex.Message);

    bool shouldRetry = await _retryManager.ShouldProcessMessage(messageId);

    if (!shouldRetry)
    {
        return Ok(new { success = false, poisonPill = true });
    }

    return InternalServerError(ex); // 500 para reintento
}
```

### 7. Endpoints de Gestión

#### `GET /api/sync/poisonpills`
Lista poison pills con filtros opcionales.

**Parámetros**:
- `status` (opcional): Filtrar por estado ("PoisonPill", "Retrying", etc.)
- `tabla` (opcional): Filtrar por tabla ("Clientes", "Productos", etc.)
- `limit` (opcional): Máximo de registros (default: 100)

**Ejemplo**:
```bash
GET /api/sync/poisonpills?status=PoisonPill&limit=50
```

**Respuesta**:
```json
{
  "total": 3,
  "filters": { "status": "PoisonPill", "tabla": null, "limit": 50 },
  "poisonPills": [
    {
      "messageId": "1234567890",
      "tabla": "Clientes",
      "entityId": "12345-0",
      "source": "Odoo",
      "attemptCount": 5,
      "firstAttemptDate": "2025-01-19T10:00:00Z",
      "lastAttemptDate": "2025-01-19T10:05:00Z",
      "lastError": "Error al actualizar cliente...",
      "status": "PoisonPill",
      "timeSinceFirstAttempt": "2h 30m",
      "timeSinceLastAttempt": "15m"
    }
  ],
  "timestamp": "2025-01-19T12:30:00Z"
}
```

#### `POST /api/sync/poisonpills/changestatus`
Cambia el estado de un mensaje.

**Body**:
```json
{
  "messageId": "1234567890",
  "newStatus": "Reprocess"
}
```

**Estados permitidos**:
- `Reprocess`: Marca para reprocesar (Pub/Sub lo enviará de nuevo y se reseteará el contador)
- `Resolved`: Marca como solucionado manualmente
- `PermanentFailure`: Marca como fallo permanente (no reprocesar)

**Respuesta**:
```json
{
  "success": true,
  "messageId": "1234567890",
  "newStatus": "Reprocess",
  "timestamp": "2025-01-19T12:35:00Z"
}
```

### 8. Registro en Dependency Injection
**Archivo**: `Startup.cs:167-171`

```csharp
_ = services.AddScoped<MessageRetryManager>(sp =>
{
    var db = new NVEntities();
    return new MessageRetryManager(db);
});
```

---

## 🚀 Cómo Usar

### 1. Ejecutar Script SQL
```sql
-- En SQL Server Management Studio
USE [bthnesto_NestoPROD]
GO
-- Ejecutar todo el contenido de SCRIPT_SQL_SYNC_MESSAGE_RETRIES.sql
```

### 2. Workflow de Poison Pills

#### Escenario 1: Mensaje Falla Repetidamente
```
1. Mensaje llega de Pub/Sub
2. Falla en handler (excepción)
3. Attempt 1/5 → Retorna 500 → Pub/Sub reenvía
4. Attempt 2/5 → Retorna 500 → Pub/Sub reenvía
5. Attempt 3/5 → Retorna 500 → Pub/Sub reenvía
6. Attempt 4/5 → Retorna 500 → Pub/Sub reenvía
7. Attempt 5/5 → Cambia a "PoisonPill" → Retorna 200
8. Pub/Sub ya no reenvía (recibió 200) ✅
```

#### Escenario 2: Revisar Poison Pills
```bash
# Listar poison pills pendientes
GET /api/sync/poisonpills?status=PoisonPill

# Ver detalles del error en campo "lastError"
# Ver datos del mensaje en campo "messageData"
```

#### Escenario 3: Resolver Poison Pill

**Opción A: Reprocesar** (ej: error temporal, ya solucionado)
```bash
POST /api/sync/poisonpills/changestatus
{
  "messageId": "1234567890",
  "newStatus": "Reprocess"
}

# El mensaje se reprocesará en el próximo envío de Pub/Sub
# El contador se reseteará a 1
```

**Opción B: Marcar como Resuelto** (ej: solucionado manualmente en BD)
```bash
POST /api/sync/poisonpills/changestatus
{
  "messageId": "1234567890",
  "newStatus": "Resolved"
}

# El mensaje ya no se procesará
# Queda registrado en BD como resuelto
```

**Opción C: Marcar como Fallo Permanente** (ej: mensaje inválido, no se puede procesar)
```bash
POST /api/sync/poisonpills/changestatus
{
  "messageId": "1234567890",
  "newStatus": "PermanentFailure"
}

# El mensaje ya no se procesará
# Queda registrado como fallo permanente
```

### 3. Monitoreo y Alertas

#### Query SQL: Poison Pills Pendientes
```sql
SELECT
    MessageId,
    Tabla,
    EntityId,
    AttemptCount,
    LastAttemptDate,
    LastError
FROM SyncMessageRetries
WHERE Status = 'PoisonPill'
ORDER BY LastAttemptDate DESC
```

#### Query SQL: Estadísticas por Tabla
```sql
SELECT
    Tabla,
    Status,
    COUNT(*) as Total,
    AVG(AttemptCount) as PromedioIntentos,
    MAX(LastAttemptDate) as UltimoIntento
FROM SyncMessageRetries
GROUP BY Tabla, Status
ORDER BY Tabla, Status
```

#### Query SQL: Mensajes con Más Reintentos
```sql
SELECT TOP 10
    MessageId,
    Tabla,
    EntityId,
    AttemptCount,
    LastError,
    DATEDIFF(MINUTE, FirstAttemptDate, LastAttemptDate) as MinutosReintentando
FROM SyncMessageRetries
WHERE Status = 'Retrying'
ORDER BY AttemptCount DESC
```

---

## 📊 Configuración

### Límite de Reintentos
**Archivo**: `MessageRetryManager.cs:18`
```csharp
private const int MaxAttempts = 5;
```

Para cambiar el límite, modificar esta constante y recompilar.

### Políticas de Retención
Actualmente, los registros exitosos se eliminan automáticamente. Si quieres mantener histórico de éxitos:

**En `MessageRetryManager.RecordSuccess()`**:
```csharp
// OPCIÓN 1: Eliminar (actual)
_db.SyncMessageRetries.Remove(retryRecord);

// OPCIÓN 2: Marcar como resuelto (mantener histórico)
retryRecord.Status = RetryStatus.Resolved.ToString();
```

---

## 🧪 Testing

### Test Manual 1: Simular Mensaje que Falla
```bash
# 1. Enviar mensaje inválido a Pub/Sub que cause error
# 2. Verificar que se registra en BD:
SELECT * FROM SyncMessageRetries WHERE MessageId = 'test-message-id'

# 3. Reenviar 5 veces (Pub/Sub lo hará automáticamente)
# 4. Verificar que en el intento 5 cambia a PoisonPill:
SELECT Status, AttemptCount FROM SyncMessageRetries WHERE MessageId = 'test-message-id'
-- Esperado: Status='PoisonPill', AttemptCount=5
```

### Test Manual 2: Reprocesar Poison Pill
```bash
# 1. Crear poison pill (ver Test 1)
# 2. Cambiar a Reprocess:
POST /api/sync/poisonpills/changestatus
{ "messageId": "test-message-id", "newStatus": "Reprocess" }

# 3. Verificar que status cambió:
SELECT Status, AttemptCount FROM SyncMessageRetries WHERE MessageId = 'test-message-id'
-- Esperado: Status='Reprocess', AttemptCount=5 (aún)

# 4. Reenviar mensaje (simulando Pub/Sub)
# 5. Verificar que contador se reseteó:
SELECT Status, AttemptCount FROM SyncMessageRetries WHERE MessageId = 'test-message-id'
-- Esperado: Status='Retrying', AttemptCount=1
```

### Test Manual 3: Endpoint de Listado
```bash
# Listar todos los poison pills
GET /api/sync/poisonpills?status=PoisonPill

# Filtrar por tabla
GET /api/sync/poisonpills?tabla=Clientes

# Combinar filtros
GET /api/sync/poisonpills?status=PoisonPill&tabla=Productos&limit=10
```

---

## ⚠️ Consideraciones Importantes

### 1. Pub/Sub Retry Policy
Google Pub/Sub tiene su **propia política de reintentos** independiente de nuestro sistema:
- Reintentos con backoff exponencial
- Máximo 7 días de reintentos

**Recomendación**: Configurar en GCP Console:
```
Minimum backoff: 10 segundos
Maximum backoff: 600 segundos (10 minutos)
```

De esta forma, los 5 intentos de nuestro sistema se distribuirán en ~30-60 minutos en lugar de segundos.

### 2. Dead Letter Queue (Opcional)
Como complemento, puedes configurar un **Dead Letter Queue** en Pub/Sub:
```
1. Crear topic: sync-dlq
2. Crear subscription: sync-dlq-sub
3. En subscription principal (sync-push):
   - Dead letter topic: sync-dlq
   - Max delivery attempts: 5
```

Esto proporciona doble protección:
- Nuestro sistema: Control en aplicación + BD
- Pub/Sub: DLQ para mensajes que fallan

### 3. Limpieza Periódica
Los registros se acumulan en `SyncMessageRetries`. Crear job de limpieza:

```sql
-- Borrar registros resueltos/permanentes con más de 30 días
DELETE FROM SyncMessageRetries
WHERE Status IN ('Resolved', 'PermanentFailure')
  AND LastAttemptDate < DATEADD(DAY, -30, GETDATE())
```

Programar con SQL Server Agent o Hangfire.

### 4. Mensajes Duplicados
El sistema de detección de duplicados en `SyncWebhookController` (líneas 109-128) **es independiente** del control de reintentos. Ambos funcionan en paralelo:
- Detección de duplicados: Ventana de 60 segundos en memoria
- Control de reintentos: Persistente en BD

---

## 📈 Próximos Pasos

### Mejoras Futuras
- [ ] Dashboard de visualización de poison pills
- [ ] Alertas automáticas cuando hay > X poison pills
- [ ] Reintento automático programado (ej: cada hora)
- [ ] Exportar poison pills a CSV para análisis
- [ ] Estadísticas de tasa de éxito por tabla/source
- [ ] Integración con Dead Letter Queue de Pub/Sub

### Extensiones
- [ ] Aplicar mismo patrón a otros webhooks (no solo sync)
- [ ] Rate limiting por source (ej: máximo 100 msg/min de Odoo)
- [ ] Circuit breaker pattern (detener procesamiento si tasa de error > 80%)

---

## 🔍 Troubleshooting

### Problema: Mensajes siguen llegando infinitamente
**Causa**: El sistema aún retorna 500 después del límite

**Solución**:
```csharp
// Verificar en SyncWebhookController catch block (línea 205)
if (!shouldRetry)
{
    return Ok(...); // ✅ Debe retornar 200
}
return InternalServerError(ex); // ❌ NO debe llegar aquí si shouldRetry = false
```

### Problema: Poison pills no aparecen en endpoint
**Causa**: Tabla no creada o registros no se guardan

**Solución**:
```sql
-- Verificar que tabla existe
SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SyncMessageRetries'

-- Verificar que hay registros
SELECT COUNT(*) FROM SyncMessageRetries

-- Verificar permisos
-- Usuario de la app debe tener INSERT/UPDATE/DELETE en tabla
```

### Problema: Estado no cambia con POST
**Causa**: MessageRetryManager no registrado en DI

**Solución**:
```csharp
// Verificar en Startup.cs que está registrado:
_ = services.AddScoped<MessageRetryManager>(...);

// O verificar que controller usa instancia correcta:
public SyncWebhookController(MessageRetryManager retryManager) // ✅
// NO: new MessageRetryManager(...) en cada método ❌
```

---

## 📚 Archivos Relacionados

**SQL**:
- `SCRIPT_SQL_SYNC_MESSAGE_RETRIES.sql` - Script de creación de tabla

**Modelos**:
- `Models/Sincronizacion/RetryStatus.cs` - Enumeración de estados
- `Models/Sincronizacion/SyncMessageRetry.cs` - Modelo EF
- `Models/Sincronizacion/PoisonPillDTO.cs` - DTO para listado
- `Models/Sincronizacion/ChangeStatusRequest.cs` - DTO para cambio de estado
- `Models/NVEntities.Partial.cs` - DbSet agregado

**Infraestructura**:
- `Infraestructure/Sincronizacion/MessageRetryManager.cs` - Gestor principal

**Controllers**:
- `Controllers/SyncWebhookController.cs` - Integración y endpoints

**Configuración**:
- `Startup.cs` - Registro en DI

---

**Estado Final**: ✅ **Sistema de control de reintentos implementado y listo para producción**

🎉 Los bucles infinitos de Pub/Sub ahora están controlados con un sistema robusto de gestión de poison pills.
