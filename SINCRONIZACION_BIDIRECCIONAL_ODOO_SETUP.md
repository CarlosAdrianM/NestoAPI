# Sincronización Bidireccional Odoo ↔ Nesto - Guía de Configuración

## 📋 Resumen

Esta implementación permite la sincronización **bidireccional** de clientes entre Odoo y Nesto usando Google Pub/Sub.

### Flujos Implementados

1. **Nesto → Odoo** (Ya existente)
   - Cambios en `GestorClientes` publican a topic `sincronizacion-tablas`
   - Odoo escucha y actualiza `res.partner`

2. **Odoo → Nesto** (NUEVO)
   - Cambios en Odoo publican a topic `sincronizacion-tablas`
   - NestoAPI escucha con `GooglePubSubEventSubscriber`
   - Actualiza tabla `Clientes` y `PersonasContactoClientes`

### Sistema Anti-Bucle

Ambos lados implementan **detección de cambios**:
- Si no hay cambios reales → NO actualiza → NO publica
- Esto previene bucles infinitos automáticamente

---

## 🛠️ Configuración Requerida

### 1. Configurar Web.config

Agregar las siguientes claves en `<appSettings>`:

```xml
<appSettings>
  <!-- Configuración existente de Google Cloud -->
  <add key="GoogleCloudPubSubProjectId" value="tu-proyecto-id" />

  <!-- NUEVO: Configuración del Subscriber -->
  <add key="GoogleCloudPubSubSubscriptionId" value="nesto-subscription" />

  <!-- OPCIONAL: Deshabilitar sincronización Odoo -> Nesto -->
  <!-- <add key="OdooSyncEnabled" value="false" /> -->
</appSettings>
```

### 2. Crear Suscripción en Google Cloud

El subscriber necesita una **suscripción** (subscription) al topic `sincronizacion-tablas`.

#### Opción A: Usando gcloud CLI

```bash
# Crear la suscripción
gcloud pubsub subscriptions create nesto-subscription \
  --topic=sincronizacion-tablas \
  --ack-deadline=60 \
  --message-retention-duration=7d \
  --project=tu-proyecto-id

# Verificar que se creó
gcloud pubsub subscriptions list --project=tu-proyecto-id
```

#### Opción B: Usando Google Cloud Console

1. Ir a Pub/Sub → Subscriptions
2. Hacer clic en "CREATE SUBSCRIPTION"
3. Configurar:
   - **Subscription ID**: `nesto-subscription`
   - **Topic**: `sincronizacion-tablas`
   - **Delivery Type**: Pull
   - **Acknowledgement deadline**: 60 seconds
   - **Message retention duration**: 7 days
   - **Expiration period**: Never expire
4. Guardar

### 3. Configurar Credenciales de Google Cloud

El servicio necesita autenticación con Google Cloud. Hay dos opciones:

#### Opción A: Credenciales por Defecto (Recomendado para producción)

Si estás desplegando en Google Cloud (App Engine, Cloud Run, GCE):
- Las credenciales se cargan automáticamente
- No necesitas configuración adicional

#### Opción B: Service Account (Desarrollo local)

1. Crear un Service Account en Google Cloud Console:
   - IAM & Admin → Service Accounts → Create Service Account
   - Rol: `Pub/Sub Editor` o `Pub/Sub Subscriber`

2. Descargar el JSON de credenciales

3. Configurar variable de entorno:
   ```bash
   # Windows
   set GOOGLE_APPLICATION_CREDENTIALS=C:\path\to\credentials.json

   # Linux/Mac
   export GOOGLE_APPLICATION_CREDENTIALS=/path/to/credentials.json
   ```

### 4. Verificar Permisos IAM

El Service Account necesita estos permisos:
- `pubsub.subscriptions.consume`
- `pubsub.subscriptions.get`
- `pubsub.topics.publish` (para el publisher existente)

---

## 🚀 Inicio Automático

El subscriber se inicia **automáticamente** cuando la aplicación arranca:

1. `Startup.cs` → `Configuration()` llama a `IniciarSincronizacionOdoo()`
2. Se resuelve `OdooSyncBackgroundService` del contenedor DI
3. Se llama a `Start()` que ejecuta el subscriber en background
4. El subscriber escucha mensajes 24/7 hasta que la app se detenga

### Logs de Inicio

Cuando la app arranca, deberías ver en la consola:

```
🚀 Iniciando OdooSyncBackgroundService...
📡 Subscription ID: nesto-subscription
✅ OdooSyncBackgroundService iniciado correctamente
✅ Sincronización bidireccional Odoo <-> Nesto iniciada
```

### Deshabilitar el Subscriber (Temporal)

Para deshabilitar sin eliminar código, agregar en `Web.config`:

```xml
<add key="OdooSyncEnabled" value="false" />
```

---

## 📊 Estructura de Mensajes

### Mensaje de Odoo → Nesto

```json
{
  "accion": "actualizar",
  "tabla": "Clientes",
  "datos": {
    "parent": {
      "cliente_externo": "12345",
      "contacto_externo": "001",
      "persona_contacto_externa": null,
      "name": "Cliente Test S.L.",
      "mobile": "666111222",
      "street": "Calle Test 123",
      "city": "Madrid",
      "zip": "28001",
      "state": "Madrid",
      "country": "ES",
      "vat": "B12345678",
      "email": "cliente@test.com",
      "comment": "Comentarios del cliente",
      "is_company": true,
      "type": "invoice"
    },
    "children": [
      {
        "cliente_externo": "12345",
        "contacto_externo": "001",
        "persona_contacto_externa": "001",
        "name": "Juan Pérez",
        "mobile": "666333444",
        "email": "juan@cliente.com",
        "comment": "Responsable de compras",
        "type": "contact"
      }
    ]
  }
}
```

### Campos Mapeados

| Odoo (res.partner) | Nesto (Cliente) | Nesto (PersonaContacto) |
|-------------------|-----------------|------------------------|
| `cliente_externo` | `Nº_Cliente` | `NºCliente` |
| `contacto_externo` | `Contacto` | `Contacto` |
| `persona_contacto_externa` | - | `Número` |
| `name` | `Nombre` | `Nombre` |
| `mobile` | `Teléfono` | `Teléfono` |
| `street` | `Dirección` | - |
| `city` | `Población` | - |
| `zip` | `CodPostal` | - |
| `state` | `Provincia` | - |
| `vat` | `CIF_NIF` | - |
| `email` | - | `CorreoElectrónico` |
| `comment` | `Comentarios` | `Comentarios` |

---

## 🔄 Flujo de Procesamiento

### 1. Recepción del Mensaje

```
Google Pub/Sub → GooglePubSubEventSubscriber → OdooToNestoSyncService
```

### 2. Validación

- ✅ Verificar que `tabla == "Clientes"`
- ✅ Verificar que `cliente_externo` y `contacto_externo` no sean vacíos

### 3. Detección de Cambios (Anti-Bucle)

```csharp
var clienteNesto = db.Clientes.Find(empresa, cliente, contacto);
var cambios = _changeDetector.DetectarCambios(clienteNesto, clienteOdoo);

if (!cambios.Any()) {
    Console.WriteLine("✅ Sin cambios, omitiendo actualización");
    return; // NO actualizar, NO publicar
}
```

### 4. Actualización

- Actualizar `Cliente` en Nesto
- Actualizar `PersonasContactoClientes` (children)
- Guardar cambios en BD
- **NO** publicar a Pub/Sub (para evitar bucle)

### 5. Logs

Cada mensaje procesado genera logs detallados:

```
📥 Mensaje recibido: Tabla=Clientes, Acción=actualizar
🔍 Procesando Cliente: 12345, Contacto: 001, Nombre: Cliente Test
🔄 Cambios detectados en Cliente 12345-001:
   - Teléfono: '666111111' → '666111222'
   - Dirección: 'CALLE VIEJA 1' → 'CALLE TEST 123'
✅ Cliente 12345-001 actualizado exitosamente
```

---

## 🧪 Testing

### Prueba Manual 1: Cambio en Odoo

1. Editar un cliente en Odoo UI
2. Cambiar el teléfono móvil
3. Guardar
4. Verificar en logs de NestoAPI:
   ```
   📥 Mensaje recibido...
   🔄 Cambios detectados...
   ✅ Cliente actualizado exitosamente
   ```
5. Verificar en BD de Nesto que el teléfono se actualizó

### Prueba Manual 2: Cambio en Nesto

1. Editar un cliente en Nesto
2. Cambiar la dirección
3. Guardar
4. Verificar que se publicó a Pub/Sub
5. Verificar en Odoo que la dirección se actualizó
6. Verificar que Nesto NO recibió su propio cambio de vuelta (anti-bucle)

### Prueba de Bucle Infinito

1. Cambiar un campo en Odoo
2. Esperar a que sincronice a Nesto
3. Verificar logs: debe mostrar "Sin cambios" en el segundo round
4. **NO** debe haber publicación infinita

---

## 📁 Archivos Creados

### Nuevos Archivos

```
NestoAPI/
├── Models/
│   └── Sincronizacion/
│       ├── ISincronizacionEventSubscriber.cs (NUEVO)
│       ├── GooglePubSubEventSubscriber.cs (NUEVO)
│       └── OdooSyncMessageDTO.cs (NUEVO)
│
└── Infraestructure/
    └── Sincronizacion/
        ├── ClienteChangeDetector.cs (NUEVO)
        ├── OdooToNestoSyncService.cs (NUEVO)
        └── OdooSyncBackgroundService.cs (NUEVO)
```

### Archivos Modificados

- `Startup.cs`: Registrar servicios e iniciar subscriber

---

## 🐛 Troubleshooting

### El subscriber no se inicia

**Síntoma**: No ves logs de inicio

**Soluciones**:
1. Verificar que `GoogleCloudPubSubSubscriptionId` esté en Web.config
2. Verificar que no esté `OdooSyncEnabled=false`
3. Revisar Event Log de Windows para errores

### Error: "Subscription not found"

**Solución**: Crear la suscripción en Google Cloud (ver paso 2)

### Error: "Permission denied"

**Solución**: Verificar permisos IAM del Service Account

### Los cambios no se sincronizan

**Síntomas**: No hay logs de mensajes recibidos

**Soluciones**:
1. Verificar que Odoo está publicando mensajes al topic
2. Verificar que el mensaje tiene `"tabla": "Clientes"`
3. Revisar logs de Google Cloud Pub/Sub para ver si hay mensajes encolados

### Bucle infinito detectado

**Síntoma**: Muchos mensajes del mismo cliente

**Solución**: El sistema debería prevenirlo automáticamente. Si ocurre:
1. Verificar que `ClienteChangeDetector` está comparando correctamente
2. Agregar más logging en `DetectarCambios()`
3. Deshabilitar temporalmente con `OdooSyncEnabled=false`

---

## 🔧 Mantenimiento

### Agregar Nuevos Campos a Sincronizar

1. Modificar `OdooClienteDTO` con el nuevo campo
2. Actualizar `ClienteChangeDetector.DetectarCambios()` para comparar el campo
3. Actualizar `OdooToNestoSyncService.ActualizarClienteDesdeOdoo()` para mapear el campo
4. Actualizar `GestorClientes.PublicarClienteSincronizar()` en el publisher

### Agregar Nuevas Tablas (ej: Productos)

1. Crear DTOs: `OdooProductoDTO`
2. Crear detector: `ProductoChangeDetector`
3. Modificar `OdooToNestoSyncService.ProcesarMensajeAsync()` para manejar `tabla == "Productos"`
4. Implementar lógica de actualización

---

## 📚 Referencias

- [Google Cloud Pub/Sub Documentation](https://cloud.google.com/pubsub/docs)
- [Google Cloud .NET Client Libraries](https://googleapis.github.io/google-cloud-dotnet/)
- Código de Odoo: `nesto_sync` module (Python)
- Código existente: `GooglePubSubEventPublisher.cs` (Publisher Nesto → Odoo)

---

## ✅ Checklist de Implementación

- [x] DTOs para mensajes de Odoo
- [x] Interfaz `ISincronizacionEventSubscriber`
- [x] Implementación `GooglePubSubEventSubscriber`
- [x] `ClienteChangeDetector` (anti-bucle)
- [x] `OdooToNestoSyncService` (procesamiento de mensajes)
- [x] `OdooSyncBackgroundService` (ejecución en background)
- [x] Registro en `Startup.cs`
- [x] Inicio automático en `Configuration()`
- [x] Documentación completa
- [ ] Tests unitarios
- [ ] Pruebas de integración
- [ ] Deploy a producción

---

**Estado**: ✅ Implementación completa, pendiente de testing

**Autor**: Claude Code
**Fecha**: 2025
**Versión**: 1.0
