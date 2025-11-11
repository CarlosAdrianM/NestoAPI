# Google Pub/Sub: Pull vs Push Subscriptions

## Resumen

Este proyecto usa **Pull Subscription**, que NO requiere un controlador HTTP público.

---

## ¿Qué es una Subscription (Suscripción)?

Una **subscription** es el mecanismo que permite a una aplicación recibir mensajes de un **topic** en Google Pub/Sub.

**Analogía**:
- El topic es como un canal de TV
- La subscription es como tu televisor sintonizado a ese canal

---

## Dos Tipos de Subscriptions

### 1. Pull Subscription (⭐ Lo que usamos)

**Cómo funciona:**
- Tu aplicación (NestoAPI) **hace polling activo** a Google Pub/Sub
- Pregunta: "¿Hay mensajes nuevos para mí?"
- Google responde con los mensajes disponibles
- Tu app procesa los mensajes y envía ACK (confirmación)

**Ventajas:**
- ✅ No necesitas endpoint HTTP público
- ✅ Control total sobre cuándo y cómo procesar mensajes
- ✅ Más seguro (no expones endpoint público)
- ✅ Puedes procesar mensajes en batch
- ✅ Ideal para servicios internos

**Implementación en NestoAPI:**
```csharp
// GooglePubSubEventSubscriber.cs
await _subscriberClient.StartListeningAsync(subscriptionName);
```

**Configuración en Google Cloud Console:**
```
Delivery Type: Pull  ← Importante: NO Push
```

**NO necesitas:**
- ❌ Controlador HTTP (como `SyncWebhookController`)
- ❌ Endpoint público accesible desde Internet
- ❌ Configurar "Push endpoint" en Google Cloud

---

### 2. Push Subscription (alternativa no usada)

**Cómo funciona:**
- Google Pub/Sub **hace HTTP POST** a un endpoint que especificas
- Tu app expone un endpoint público (ej: `https://tudominio.com/api/sync/webhook`)
- Google envía mensajes automáticamente a ese endpoint
- Tu controlador procesa el POST y responde con HTTP 200

**Ventajas:**
- ✅ Más "reactivo" (mensajes llegan inmediatamente)
- ✅ No necesitas código de polling
- ✅ Escalado automático por Google

**Desventajas:**
- ❌ Necesitas endpoint HTTP público accesible desde Internet
- ❌ Debes configurar autenticación (verificar que el request viene de Google)
- ❌ Menos control sobre rate limiting

**Implementación (si lo usáramos):**
```csharp
// Ejemplo: SyncWebhookController.cs
[HttpPost]
[Route("api/sync/webhook")]
public async Task<IHttpActionResult> ReceiveMessage([FromBody] PubSubMessage message)
{
    // Verificar que viene de Google (autenticación)
    // Procesar mensaje
    // Responder HTTP 200
}
```

**Configuración en Google Cloud Console:**
```
Delivery Type: Push
Push endpoint: https://tudominio.com/api/sync/webhook
```

---

## ¿Por qué usamos Pull en lugar de Push?

1. **Seguridad**: No necesitamos exponer endpoint público
2. **Simplicidad**: No necesitamos configurar autenticación de Google
3. **Control**: Decidimos cuándo y cómo procesar mensajes
4. **Infraestructura**: NestoAPI corre en IIS interno, no tiene dominio público configurado

---

## Creando la Subscription (Pull)

### Usando gcloud CLI:

```bash
gcloud pubsub subscriptions create nesto-subscription \
  --topic=sincronizacion-tablas \
  --ack-deadline=60 \
  --message-retention-duration=7d
```

### Usando Google Cloud Console:

1. Ir a **Pub/Sub → Subscriptions**
2. Click en **"CREATE SUBSCRIPTION"**
3. Configurar:
   - **Subscription ID**: `nesto-subscription`
   - **Topic**: `sincronizacion-tablas`
   - **Delivery Type**: **Pull** ⭐ (NO Push)
   - **Acknowledgement deadline**: 60 seconds
   - **Message retention**: 7 days
4. Guardar

**⚠️ Importante**: En "Delivery Type", seleccionar **Pull**, NO Push. No necesitas especificar ningún endpoint.

---

## Verificar que funciona

### 1. Verifica que la subscription existe:

```bash
gcloud pubsub subscriptions list --project=tu-proyecto-id
```

Deberías ver:
```
projects/tu-proyecto-id/subscriptions/nesto-subscription
```

### 2. Verifica en logs de NestoAPI:

Cuando la app inicia, deberías ver:
```
🚀 Iniciando SyncSubscriberBackgroundService...
📡 Subscription ID: nesto-subscription
✅ SyncSubscriberBackgroundService iniciado correctamente
```

### 3. Prueba publicando un mensaje:

Desde otro sistema (Odoo, Prestashop, etc.), publica un mensaje al topic `sincronizacion-tablas`. NestoAPI lo recibirá automáticamente y verás logs como:
```
📥 Mensaje recibido: Tabla=Clientes, Acción=actualizar
🔍 Procesando Cliente: 12345, Contacto: 001
```

---

## Troubleshooting

### "Subscription not found"

**Problema**: La subscription no existe en Google Cloud.

**Solución**: Crear la subscription usando los comandos de arriba.

### "Permission denied"

**Problema**: El Service Account no tiene permisos.

**Solución**: Agregar rol `Pub/Sub Subscriber` al Service Account:
```bash
gcloud projects add-iam-policy-binding tu-proyecto-id \
  --member="serviceAccount:tu-sa@tu-proyecto.iam.gserviceaccount.com" \
  --role="roles/pubsub.subscriber"
```

### "No messages received"

**Problema**: El subscriber está corriendo pero no recibe mensajes.

**Soluciones**:
1. Verificar que otros sistemas (Odoo, Prestashop) están publicando al topic
2. Verificar en Google Cloud Console → Pub/Sub → Subscriptions que hay mensajes encolados
3. Verificar que el subscription ID es correcto en `Web.config`

---

## Comparación Visual

```
PULL SUBSCRIPTION (lo que usamos):
┌─────────────────┐
│  NestoAPI       │ ──── "¿Hay mensajes?" ──→ ┌──────────────┐
│  (tu servidor)  │ ←──── Mensajes ────────── │ Google       │
└─────────────────┘                            │ Pub/Sub      │
                                               └──────────────┘
       ↑                                              ↑
   Privado                                        Internet
   No expuesto                                    Accesible


PUSH SUBSCRIPTION (NO usamos):
┌─────────────────┐
│  NestoAPI       │                             ┌──────────────┐
│  Endpoint       │ ←──── HTTP POST ────────── │ Google       │
│  público        │                             │ Pub/Sub      │
│  /api/webhook   │                             └──────────────┘
└─────────────────┘
       ↑
   DEBE ser
   público en
   Internet
```

---

## Resumen Final

✅ **Usamos Pull Subscription**
✅ **NO necesitamos controlador HTTP**
✅ **NO necesitamos endpoint público**
✅ **SyncSubscriberBackgroundService hace polling automático**
✅ **Configurar "Delivery Type: Pull" en Google Cloud Console**

❌ **NO uses Push Subscription**
❌ **NO necesitas crear SyncWebhookController**
❌ **NO necesitas especificar "Push endpoint"**

---

## Referencias

- [Google Cloud Pub/Sub - Pull Documentation](https://cloud.google.com/pubsub/docs/pull)
- [Google Cloud Pub/Sub - .NET Client](https://googleapis.github.io/google-cloud-dotnet/docs/Google.Cloud.PubSub.V1/)
- Implementación: `NestoAPI/Models/Sincronizacion/GooglePubSubEventSubscriber.cs`
