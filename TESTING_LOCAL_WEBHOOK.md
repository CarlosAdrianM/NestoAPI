# Guía de Pruebas del Webhook en Desarrollo Local

## 🎯 Problema

Google Pub/Sub Push Subscriptions **requieren HTTPS obligatoriamente** y **dominio público**.
No acepta `http://localhost` ni IPs privadas.

## ✅ Soluciones para Desarrollo

---

## Opción 1: ngrok (RECOMENDADO - Más Fácil)

### Instalación

1. Descarga ngrok: https://ngrok.com/download
2. Descomprime el ejecutable

### Uso

1. **Inicia tu API** en Visual Studio (F5) - debe estar en `http://localhost:53364`

2. **Abre una terminal y ejecuta**:
   ```bash
   ngrok http 53364
   ```

3. **Copia la URL HTTPS** que te muestra (ejemplo: `https://abc123.ngrok.io`)

4. **Crea la Push Subscription** en Google Cloud:
   ```bash
   gcloud pubsub subscriptions create nesto-push-dev \
     --topic=sincronizacion-tablas \
     --push-endpoint=https://abc123.ngrok.io/api/sync/webhook \
     --ack-deadline=60
   ```

5. **Publica un mensaje de prueba**:
   ```bash
   gcloud pubsub topics publish sincronizacion-tablas \
     --message='{"tabla":"Clientes","accion":"actualizar","datos":{"parent":{"cliente_externo":"12345","contacto_externo":"001","name":"Test"}}}'
   ```

6. **Observa la consola de Visual Studio** - verás los logs en tiempo real

### Ver tráfico HTTP

Abre en tu navegador: http://127.0.0.1:4040 para ver todos los requests que recibe ngrok.

---

## Opción 2: Pruebas Manuales Locales (Sin Google Pub/Sub)

Para verificar que tu lógica funciona sin depender de Google.

### Con PowerShell

```powershell
# En la raíz del proyecto
.\test_webhook_local.ps1
```

Este script:
1. Crea un mensaje JSON de prueba
2. Lo codifica en base64 (como Google)
3. Lo envía a `http://localhost:53364/api/sync/webhook`
4. Muestra la respuesta

### Con Bash/curl

```bash
# En Git Bash o WSL
bash test_webhook_curl.sh
```

### Con Postman

1. **URL**: `POST http://localhost:53364/api/sync/webhook`
2. **Headers**: `Content-Type: application/json`
3. **Body** (raw JSON):

```json
{
  "message": {
    "data": "eyJ0YWJsYSI6IkNsaWVudGVzIiwiYWNjaW9uIjoiYWN0dWFsaXphciIsImRhdG9zIjp7InBhcmVudCI6eyJjbGllbnRlX2V4dGVybm8iOiIxMjM0NSIsImNvbnRhY3RvX2V4dGVybm8iOiIwMDEiLCJuYW1lIjoiVGVzdCBDbGllbnRlIiwibW9iaWxlIjoiNjY2MTIzNDU2In19fQ==",
    "messageId": "test-123",
    "publishTime": "2025-01-10T12:00:00.000Z"
  },
  "subscription": "projects/test/subscriptions/test"
}
```

**¿Qué es ese `data` tan largo?** Es el mensaje JSON codificado en base64. Puedes decodificarlo:

```json
{
  "tabla": "Clientes",
  "accion": "actualizar",
  "datos": {
    "parent": {
      "cliente_externo": "12345",
      "contacto_externo": "001",
      "name": "Test Cliente",
      "mobile": "666123456"
    }
  }
}
```

---

## Opción 3: Generar tu Propio Base64

Si quieres probar con otros datos:

### Con PowerShell

```powershell
# Tu mensaje
$mensaje = @{
    tabla = "Clientes"
    accion = "actualizar"
    datos = @{
        parent = @{
            cliente_externo = "99999"
            contacto_externo = "002"
            name = "Mi Cliente de Prueba"
        }
    }
} | ConvertTo-Json -Depth 5

# Convertir a base64
$bytes = [System.Text.Encoding]::UTF8.GetBytes($mensaje)
$base64 = [Convert]::ToBase64String($bytes)

Write-Host $base64

# Copiar al portapapeles
$base64 | Set-Clipboard
```

### Con Bash

```bash
echo '{"tabla":"Clientes","accion":"actualizar","datos":{"parent":{"cliente_externo":"99999","contacto_externo":"002","name":"Test"}}}' | base64 -w 0
```

### Online

Puedes usar: https://www.base64encode.org/

---

## ✅ Verificar que Funciona

### 1. Health Check

Antes de probar el webhook, verifica que el controlador está disponible:

```bash
curl http://localhost:53364/api/sync/health
```

Deberías ver:
```json
{
  "status": "healthy",
  "service": "SyncWebhook",
  "supportedTables": ["Clientes"],
  "timestamp": "2025-01-10T..."
}
```

### 2. Logs en Visual Studio

Al procesar un mensaje, deberías ver en la consola de Visual Studio:

```
📨 Webhook recibido: MessageId=test-123, Subscription=...
📄 Mensaje decodificado: {"tabla":"Clientes",...}
📥 Mensaje recibido: Tabla=Clientes, Acción=actualizar
🔍 Procesando Cliente: 12345, Contacto: 001, Nombre: Test Cliente
```

**Si hay cambios**:
```
🔄 Cambios detectados en Cliente 12345-001:
   - Nombre: 'Viejo Nombre' → 'Test Cliente'
   - Teléfono: '666111111' → '666123456'
✅ Cliente 12345-001 actualizado exitosamente
✅ Mensaje procesado exitosamente: test-123
```

**Si no hay cambios**:
```
✅ Sin cambios en Cliente 12345-001, omitiendo actualización
✅ Mensaje procesado exitosamente: test-123
```

**Si hay error**:
```
⚠️ Cliente 12345-001 no existe en Nesto. No se puede crear desde sistemas externos.
⚠️ Mensaje procesado con advertencias: test-123
```

---

## 🔍 Debugging

### Verificar que el cliente existe en BD

Antes de probar, asegúrate de que el cliente existe en tu base de datos de desarrollo:

```sql
SELECT TOP 1
    Nº_Cliente, Contacto, Nombre, Teléfono, Dirección
FROM Clientes
WHERE Empresa = '1'
  AND Nº_Cliente = '12345'
  AND Contacto = '001'
```

Si no existe, el webhook responderá OK pero no actualizará nada (por diseño, no creamos clientes desde sistemas externos).

### Breakpoints en Visual Studio

Pon un breakpoint en:
- `SyncWebhookController.cs` línea 36 (inicio del método)
- `ClientesSyncHandler.cs` línea 28 (inicio del HandleAsync)
- `ClientesSyncHandler.cs` línea 79 (antes de actualizar)

---

## 📊 Comparativa de Opciones

| Aspecto | ngrok | Pruebas Manuales |
|---------|-------|------------------|
| **Prueba integración real con Google** | ✅ Sí | ❌ No |
| **Requiere Google Cloud** | ✅ Sí | ❌ No |
| **Configuración** | Fácil (2 min) | Muy fácil (0 min) |
| **Requiere Internet** | ✅ Sí | ❌ No |
| **Simula exactamente Google** | ✅ 100% | ✅ 95% |
| **Debugging en Visual Studio** | ✅ Sí | ✅ Sí |
| **Ver logs en tiempo real** | ✅ Sí | ✅ Sí |

---

## 🚀 Recomendación

### Para desarrollo diario:
→ **Pruebas manuales con PowerShell/curl** (fast, sin configuración)

### Para probar integración completa:
→ **ngrok** (verifica que Google Pub/Sub funciona correctamente)

### Para producción:
→ **HTTPS en servidor público** (IIS con certificado SSL válido)

---

## 📞 Troubleshooting

### Error: "No se encontró el endpoint"

- ✅ Verifica que tu API está corriendo (`F5` en Visual Studio)
- ✅ Verifica el puerto correcto (53364)
- ✅ Prueba el health check primero

### Error: "Error decodificando base64"

- ✅ Asegúrate de que el JSON no tiene saltos de línea antes de codificar
- ✅ Usa los scripts proporcionados que lo hacen correctamente

### Error: "Cliente no existe"

- ✅ Verifica que el cliente existe en tu BD de desarrollo
- ✅ Verifica los campos `cliente_externo` y `contacto_externo`
- ✅ Recuerda que los campos en BD suelen tener espacios: `TRIM()`

### No veo logs en Visual Studio

- ✅ Asegúrate de que la ventana "Output" está visible
- ✅ Selecciona "Debug" en el dropdown de la ventana Output
- ✅ Los `Console.WriteLine()` aparecen ahí
