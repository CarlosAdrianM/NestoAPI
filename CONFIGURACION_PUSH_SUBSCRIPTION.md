# Configuración de Push Subscription para Sincronización

## ✅ Arquitectura Implementada

NestoAPI usa **Push Subscription** de Google Pub/Sub, eliminando la necesidad de background jobs y proporcionando **sincronización instantánea**.

---

## 🎯 Ventajas de Push sobre Pull

| Característica | Push (✅ Implementado) | Pull (❌ NO usado) |
|----------------|----------------------|-------------------|
| **Latencia** | Inmediata (< 1 seg) | Polling (30-60 seg) |
| **Complejidad** | Simple (1 controlador) | Compleja (background service) |
| **Recursos** | Solo cuando hay mensajes | Polling constante |
| **Escalabilidad** | Automática por Google | Manual |

---

## 📋 Componentes Implementados

### 1. SyncWebhookController (Entry Point)

**Ubicación**: `NestoAPI/Controllers/SyncWebhookController.cs`

**Endpoints**:
- `POST /api/sync/webhook` - Recibe mensajes push de Google Pub/Sub
- `GET /api/sync/health` - Health check y lista de tablas soportadas

**Funcionamiento**:
```
Google Pub/Sub → POST /api/sync/webhook → Decodifica base64 → Parsea JSON → Router
```

### 2. SyncTableRouter (Orchestrator)

**Ubicación**: `NestoAPI/Infraestructure/Sincronizacion/SyncTableRouter.cs`

**Responsabilidad**: Rutear mensajes al handler correcto según la tabla

**Ejemplo**:
```csharp
mensaje.Tabla = "Clientes" → ClientesSyncHandler
mensaje.Tabla = "Productos" → ProductosSyncHandler (futuro)
```

### 3. ISyncTableHandler (Interface)

**Ubicación**: `NestoAPI/Infraestructure/Sincronizacion/ISyncTableHandler.cs`

**Contrato**:
```csharp
public interface ISyncTableHandler
{
    string TableName { get; }  // "Clientes", "Productos", etc.
    Task<bool> HandleAsync(ExternalSyncMessageDTO message);
}
```

### 4. ClientesSyncHandler (Implementation)

**Ubicación**: `NestoAPI/Infraestructure/Sincronizacion/ClientesSyncHandler.cs`

**Responsabilidad**: Procesar actualizaciones de tabla Clientes

**Features**:
- ✅ Detección de cambios (anti-bucle)
- ✅ Actualización de Cliente
- ✅ Actualización de PersonasContacto (children)
- ✅ Logs detallados

---

## 🔧 Configuración en Google Cloud

### Paso 1: Crear Push Subscription

#### Usando gcloud CLI:

```bash
gcloud pubsub subscriptions create nesto-push-subscription \
  --topic=sincronizacion-tablas \
  --push-endpoint=https://TU-DOMINIO.com/api/sync/webhook \
  --ack-deadline=60 \
  --message-retention-duration=7d \
  --project=tu-proyecto-id
```

#### Usando Google Cloud Console:

1. Ir a **Pub/Sub → Subscriptions**
2. Click **"CREATE SUBSCRIPTION"**
3. Configurar:
   - **Subscription ID**: `nesto-push-subscription`
   - **Topic**: `sincronizacion-tablas`
   - **Delivery Type**: **Push** ⭐
   - **Push endpoint**: `https://TU-DOMINIO.com/api/sync/webhook`
   - **Acknowledgement deadline**: 60 seconds
   - **Message retention**: 7 days
4. Guardar

### Paso 2: Configurar Autenticación (Opcional pero Recomendado)

Para producción, configura autenticación para que solo Google pueda enviar mensajes:

#### Opción A: Service Account Token

```bash
gcloud pubsub subscriptions update nesto-push-subscription \
  --push-auth-service-account=pubsub-invoker@tu-proyecto.iam.gserviceaccount.com
```

Luego en el controlador, verificar el token JWT.

#### Opción B: IP Allowlist (Más Simple)

En IIS o firewall, permitir solo IPs de Google Pub/Sub:
- Rangos de IP: https://cloud.google.com/pubsub/docs/push#ip_addresses

---

## 🚀 Despliegue y Testing

### 1. Verificar que el Endpoint es Accesible

```bash
# Desde fuera de tu red
curl https://TU-DOMINIO.com/api/sync/health

# Respuesta esperada:
{
  "status": "healthy",
  "service": "SyncWebhook",
  "supportedTables": ["Clientes"],
  "timestamp": "2025-..."
}
```

### 2. Probar con Mensaje Manual

Publica un mensaje de prueba:

```bash
gcloud pubsub topics publish sincronizacion-tablas \
  --message='{"tabla":"Clientes","accion":"actualizar","datos":{"parent":{"cliente_externo":"12345","contacto_externo":"001","name":"Test"}}}' \
  --project=tu-proyecto-id
```

### 3. Verificar Logs

En NestoAPI deberías ver:
```
📨 Webhook recibido: MessageId=..., Subscription=...
📄 Mensaje decodificado: {...}
📥 Mensaje recibido: Tabla=Clientes, Acción=actualizar
🔍 Procesando Cliente: 12345, Contacto: 001
...
✅ Cliente actualizado exitosamente
✅ Mensaje procesado exitosamente: ...
```

---

## 📊 Flujo Completo

```
┌─────────────────┐
│ Odoo/Prestashop │
│ Cambia cliente  │
└────────┬────────┘
         │ 1. Publica a topic
         ▼
┌──────────────────────┐
│ Google Pub/Sub       │
│ sincronizacion-tablas│
└────────┬─────────────┘
         │ 2. Push inmediato
         ▼
┌───────────────────────────────┐
│ NestoAPI                      │
│ POST /api/sync/webhook        │
│                               │
│  SyncWebhookController        │
│         │                     │
│         ▼                     │
│  SyncTableRouter              │
│         │                     │
│    ┌────┴────────┐            │
│    ▼             ▼            │
│ Clientes    Productos         │
│ Handler      Handler          │
│    │             │            │
│    ▼             ▼            │
│  BD Nesto                     │
└───────────────────────────────┘
         │ 3. Responde HTTP 200
         ▼
  Google Pub/Sub ACK
```

---

## 🔐 Seguridad en Producción

### 1. HTTPS Obligatorio

Google Pub/Sub **solo** hace push a endpoints HTTPS. HTTP no es soportado.

### 2. Autenticación

Implementar una de estas opciones:

#### Opción A: Verificar Token de Google

```csharp
[HttpPost]
[Route("webhook")]
public async Task<IHttpActionResult> ReceiveWebhook([FromBody] PubSubPushRequestDTO request)
{
    // Verificar token JWT en header Authorization
    var authHeader = Request.Headers.Authorization;
    if (!await VerifyGoogleToken(authHeader))
    {
        return Unauthorized();
    }

    // Procesar mensaje...
}
```

#### Opción B: IP Allowlist

Configurar firewall para permitir solo IPs de Google Pub/Sub.

### 3. Rate Limiting

Implementar rate limiting en el controlador para prevenir abuse.

---

## 🆕 Agregar Soporte para Nueva Tabla

**Ejemplo: Agregar soporte para Productos**

### Paso 1: Crear Handler

```csharp
// NestoAPI/Infraestructure/Sincronizacion/ProductosSyncHandler.cs

public class ProductosSyncHandler : ISyncTableHandler
{
    public string TableName => "Productos";

    public async Task<bool> HandleAsync(ExternalSyncMessageDTO message)
    {
        // Tu lógica para actualizar productos
        var producto = message.Datos.Parent;

        using (var db = new NVEntities())
        {
            // Buscar producto
            var prod = await db.Productos.FindAsync(producto.CodigoProducto);

            // Actualizar
            prod.Nombre = producto.Name;
            prod.Precio = producto.Price;

            await db.SaveChangesAsync();
            return true;
        }
    }
}
```

### Paso 2: Registrar en Startup.cs

```csharp
// Agregar esta línea en ConfigureServices()
_ = services.AddSingleton<ISyncTableHandler, ProductosSyncHandler>();
```

**¡Eso es todo!** El router detectará automáticamente el nuevo handler.

### Paso 3: Verificar

```bash
curl https://tu-dominio.com/api/sync/health

# Deberías ver:
{
  "supportedTables": ["Clientes", "Productos"]
}
```

---

## 🐛 Troubleshooting

### Error: "404 Not Found" en webhook

**Problema**: Google no puede alcanzar el endpoint.

**Soluciones**:
1. Verificar que la URL es correcta
2. Verificar que el sitio está publicado y accesible externamente
3. Verificar certificado SSL

### Error: "Mensaje procesado con advertencias"

**Problema**: El handler retornó `false` pero sin excepción.

**Soluciones**:
1. Revisar logs para ver qué advertencia se generó
2. Verificar que el cliente/producto existe en Nesto
3. Verificar formato del mensaje

### Mensajes Duplicados

**Problema**: Google reenvía el mismo mensaje múltiples veces.

**Causa**: El endpoint respondió con error (500) o timeout.

**Soluciones**:
1. Asegurar que el endpoint siempre responde en < 60 segundos
2. Retornar HTTP 200 incluso para errores lógicos (no técnicos)
3. Implementar idempotencia en handlers

---

## 📚 Referencias

- [Google Cloud Pub/Sub - Push](https://cloud.google.com/pubsub/docs/push)
- [Push Subscription Authentication](https://cloud.google.com/pubsub/docs/push#setting_up_for_push_authentication)
- [Retry Policy](https://cloud.google.com/pubsub/docs/push#exponential_backoff)

---

## ✅ Checklist de Implementación

- [x] SyncWebhookController creado
- [x] SyncTableRouter implementado
- [x] ClientesSyncHandler implementado
- [x] Startup.cs configurado
- [x] Health check endpoint
- [ ] Push subscription creada en Google Cloud
- [ ] Endpoint público accesible (HTTPS)
- [ ] Autenticación configurada (opcional)
- [ ] Tests de integración

---

**Estado**: ✅ **Implementación completa lista para desplegar**

**Próximo Paso**: Crear push subscription en Google Cloud Console apuntando a tu endpoint.
