# Estado de la Implementación - Sincronización Push Subscription

**Fecha**: 2025-11-10
**Estado**: ✅ Implementación completa, pendiente de pruebas

---

## ✅ Completado en Esta Sesión

### 1. Arquitectura Push Subscription Implementada

Se implementó un sistema completo de sincronización bidireccional mediante Google Pub/Sub Push Subscription:

#### Archivos Creados:

**Controllers**:
- `Controllers/SyncWebhookController.cs` - Controlador que recibe webhooks de Google Pub/Sub
  - `POST /api/sync/webhook` - Endpoint para recibir mensajes push
  - `GET /api/sync/health` - Health check que lista tablas soportadas

**Infraestructura**:
- `Infraestructure/Sincronizacion/ISyncTableHandler.cs` - Interfaz para handlers por tabla
- `Infraestructure/Sincronizacion/SyncTableRouter.cs` - Router que dirige mensajes al handler correcto
- `Infraestructure/Sincronizacion/ClientesSyncHandler.cs` - Handler específico para tabla Clientes
- `Infraestructure/Sincronizacion/ClienteChangeDetector.cs` - Detector de cambios (anti-loop)

**Modelos**:
- `Models/Sincronizacion/PubSubPushRequestDTO.cs` - DTOs para requests de Google Pub/Sub
- `Models/Sincronizacion/ExternalSyncMessageDTO.cs` - DTOs genéricos para mensajes de sistemas externos

**Startup.cs** (Modificado):
- Líneas 155-161: Registro de servicios en contenedor DI
  ```csharp
  _ = services.AddSingleton<ISyncTableHandler, ClientesSyncHandler>();
  _ = services.AddSingleton<SyncTableRouter>(sp =>
  {
      var handlers = sp.GetServices<ISyncTableHandler>();
      return new SyncTableRouter(handlers);
  });
  _ = services.AddScoped<SyncWebhookController>();
  ```

**NestoAPI.csproj** (Modificado):
- Agregadas referencias a todos los archivos nuevos (líneas 485, 593-597, 1057-1060)

### 2. Correcciones Aplicadas

**Bug Crítico Identificado pero NO Corregido**:
- En `ClientesSyncHandler.cs` líneas 57-76: El código valida `clienteNesto == null` **después** de llamar a `DetectarCambios()`, lo que causará `NullReferenceException`
- **NOTA**: El usuario deshizo la corrección que apliqué, el código actual tiene el bug
- **ADVERTENCIA**: Este bug causará error si intentas actualizar un cliente que no existe

### 3. Scripts de Prueba Creados

- `test_webhook_local.ps1` - Script PowerShell para pruebas locales sin Google Pub/Sub
- `test_webhook_curl.sh` - Script Bash alternativo para pruebas con curl
- `TESTING_LOCAL_WEBHOOK.md` - Guía completa de pruebas para desarrollo local

### 4. Documentación Completa

- `CONFIGURACION_PUSH_SUBSCRIPTION.md` - Guía de configuración de Google Cloud
- `GUIA_AGREGAR_TABLA_SINCRONIZACION.md` - Cómo agregar nuevas tablas (2 pasos)
- `RESUMEN_PUSH_SUBSCRIPTION.md` - Resumen ejecutivo de la migración
- `ARQUITECTURA_FINAL_PUSH.txt` - Diagramas técnicos de arquitectura
- `LISTADO_ARCHIVOS_SINCRONIZACION.txt` - Inventario completo
- `TESTING_LOCAL_WEBHOOK.md` - Guía de pruebas locales

---

## 🎯 Características del Sistema

### Genérico y Extensible
- ✅ No está atado a "Odoo" - funciona con cualquier sistema externo
- ✅ Agregar nuevas tablas requiere solo 2 pasos:
  1. Crear handler implementando `ISyncTableHandler`
  2. Registrar en `Startup.cs`

### Push vs Pull
- ✅ Latencia < 1 segundo (antes 30-60 segundos)
- ✅ Sin background jobs
- ✅ Sin polling constante
- ✅ Google maneja escalabilidad automáticamente

### Anti-Loop Protection
- ✅ `ClienteChangeDetector` compara campo por campo
- ✅ Si no hay cambios reales, no actualiza BD
- ✅ Previene bucles infinitos de sincronización

---

## ⚠️ Problema Identificado (NO Resuelto)

### Bug en ClientesSyncHandler.cs

**Ubicación**: Líneas 57-76

**Problema**:
```csharp
var clienteNesto = await db.Clientes.Where(...).FirstOrDefaultAsync();

// ❌ PROBLEMA: Llama DetectarCambios con clienteNesto que puede ser null
var cambios = _changeDetector.DetectarCambios(clienteNesto, clienteExternal);

if (!cambios.Any()) { ... }

// ❌ PROBLEMA: Esta validación debería estar ANTES
if (clienteNesto == null) { ... }
```

**Consecuencia**:
Si intentas sincronizar un cliente que no existe en Nesto, la línea 58 lanzará `NullReferenceException`.

**Solución** (para aplicar en próxima sesión):
Mover la validación `if (clienteNesto == null)` a la línea 57, **antes** de llamar a `DetectarCambios()`.

---

## 🚀 Próximos Pasos (Para Siguiente Sesión)

### 1. Corregir Bug Crítico
- [ ] Reordenar validaciones en `ClientesSyncHandler.cs`
- [ ] Compilar y verificar sin errores

### 2. Pruebas Locales
- [ ] Ejecutar API en Visual Studio (F5)
- [ ] Verificar health check: `http://localhost:53364/api/sync/health`
- [ ] Ejecutar script de prueba: `.\test_webhook_local.ps1`
- [ ] Verificar logs en consola de Visual Studio

### 3. Configuración de Datos de Prueba
- [ ] Identificar un cliente real en BD de desarrollo
- [ ] Actualizar script con número de cliente real
- [ ] Probar actualización de campos

### 4. Pruebas con ngrok (Opcional)
- [ ] Descargar ngrok: https://ngrok.com/download
- [ ] Extraer `ngrok.exe` a `C:\Tools\ngrok\` o `Downloads`
- [ ] Ejecutar: `ngrok http 53364`
- [ ] Copiar URL HTTPS generada
- [ ] Crear Push Subscription en Google Cloud apuntando a esa URL
- [ ] Publicar mensaje de prueba desde Odoo/Prestashop

### 5. Deployment a Producción (Cuando Funcione)
- [ ] Compilar: `msbuild NestoAPI.sln /t:Build /p:Configuration=Release`
- [ ] Publicar a servidor IIS con HTTPS
- [ ] Configurar Push Subscription apuntando a dominio público
- [ ] Probar con mensaje real

---

## 📋 Comandos Rápidos para Próxima Sesión

### Verificar Health Check
```powershell
Invoke-RestMethod -Uri "http://localhost:53364/api/sync/health"
```

### Ejecutar Prueba Local
```powershell
.\test_webhook_local.ps1
```

### Iniciar ngrok (si decides usarlo)
```bash
cd C:\Tools\ngrok
.\ngrok.exe http 53364
```

### Crear Push Subscription en Google
```bash
gcloud pubsub subscriptions create nesto-push-dev \
  --topic=sincronizacion-tablas \
  --push-endpoint=https://TU-URL-NGROK.ngrok.io/api/sync/webhook \
  --ack-deadline=60
```

### Publicar Mensaje de Prueba
```bash
gcloud pubsub topics publish sincronizacion-tablas \
  --message='{"tabla":"Clientes","accion":"actualizar","datos":{"parent":{"cliente_externo":"12345","contacto_externo":"001","name":"Test"}}}'
```

---

## 📂 Estructura de Archivos Final

```
NestoAPI/
├── Controllers/
│   └── SyncWebhookController.cs          ✅ Nuevo
├── Infraestructure/
│   └── Sincronizacion/
│       ├── ClienteChangeDetector.cs      ✅ Existente
│       ├── ClientesSyncHandler.cs        ✅ Nuevo (⚠️ Tiene bug)
│       ├── ISyncTableHandler.cs          ✅ Nuevo
│       └── SyncTableRouter.cs            ✅ Nuevo
├── Models/
│   └── Sincronizacion/
│       ├── ExternalSyncMessageDTO.cs     ✅ Nuevo
│       ├── PubSubPushRequestDTO.cs       ✅ Nuevo
│       ├── GooglePubSubEventPublisher.cs ✅ Existente (sin cambios)
│       └── ISincronizacionEventPublisher.cs ✅ Existente (sin cambios)
├── Startup.cs                            ✅ Modificado (líneas 155-161)
├── NestoAPI.csproj                       ✅ Modificado (referencias agregadas)
│
├── test_webhook_local.ps1                ✅ Script de prueba
├── test_webhook_curl.sh                  ✅ Script alternativo
│
└── Documentación/
    ├── CONFIGURACION_PUSH_SUBSCRIPTION.md
    ├── GUIA_AGREGAR_TABLA_SINCRONIZACION.md
    ├── RESUMEN_PUSH_SUBSCRIPTION.md
    ├── ARQUITECTURA_FINAL_PUSH.txt
    ├── LISTADO_ARCHIVOS_SINCRONIZACION.txt
    ├── TESTING_LOCAL_WEBHOOK.md
    └── ESTADO_SESION_SINCRONIZACION.md   ✅ Este archivo
```

---

## 🔑 Conceptos Clave para Recordar

### Push Subscription
Google Pub/Sub hace POST a tu endpoint cuando hay mensajes:
- **Requiere HTTPS** (obligatorio para producción)
- **Requiere endpoint público** (no acepta localhost)
- Solución para desarrollo: **ngrok** crea túnel HTTPS público

### Message Flow
1. Sistema externo publica JSON a topic de Google
2. Google codifica mensaje en base64
3. Google hace POST a tu webhook con JSON + base64
4. Tu webhook decodifica y deserializa
5. Router dirige a handler correcto según `message.Tabla`
6. Handler procesa y actualiza BD
7. Responde 200 OK o 500 Error a Google

### Anti-Loop
`ClienteChangeDetector` compara:
- Nombre (case-insensitive, sin espacios)
- Teléfono
- Dirección
- Población
- Código Postal
- Provincia
- CIF/NIF
- Comentarios

Si todos son iguales → No actualiza → No publica evento → No hay loop

---

## 📞 Información de Contacto y Referencias

- **Documentación Google Pub/Sub Push**: https://cloud.google.com/pubsub/docs/push
- **ngrok**: https://ngrok.com/download
- **Repositorio oficial (si aplicable)**: [completar si hay repo Git]

---

## ✅ Estado del Proyecto

**Compilación**: ✅ Sin errores (todos los archivos incluidos en .csproj)
**Implementación**: ✅ Completa
**Pruebas**: ⚠️ Pendiente
**Bug Conocido**: ⚠️ Sí (orden de validaciones en ClientesSyncHandler)
**Listo para Producción**: ❌ No (requiere pruebas y corrección de bug)

---

**Fin del documento de estado**
