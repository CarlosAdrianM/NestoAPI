# Resumen de Cambios - Sistema de Sincronización
**Fecha:** 2025-11-12
**Versión:** 1.0

## 🎯 Objetivo

Mejorar el sistema de logs y detección de cambios en la sincronización bidireccional entre Nesto y sistemas externos (Odoo, Prestashop) para facilitar el diagnóstico de problemas y reducir falsos positivos.

---

## 📝 Cambios Implementados

### 1. **Logs Enriquecidos con Información Completa**

**Archivos modificados:**
- `NestoAPI/Controllers/SyncWebhookController.cs` (líneas 90-144)
- `NestoAPI/Infraestructure/Sincronizacion/ClientesSyncHandler.cs` (líneas 45-103, 160-212)
- `NestoAPI/Infraestructure/GestorClientes.cs` (líneas 1412-1416)

**Qué se agregó:**
- Cliente, Contacto y PersonaContacto en todos los logs
- Formato consistente: `Cliente 24971-1-2` (Cliente-Contacto-PersonaContacto)
- Source del mensaje para identificar origen
- Logs jerárquicos con indentación para PersonasContacto

**Antes:**
```
MessageId=16385923098460642
```

**Después:**
```
MessageId=16385923098460642 - Cliente 39598, Contacto 0, Source=Nesto viejo, PersonasContacto=[Id=1 (Ainhoa)]
```

---

### 2. **Detección Automática de Mensajes Duplicados**

**Archivos modificados:**
- `NestoAPI/Controllers/SyncWebhookController.cs` (líneas 21, 24, 118-142)

**Qué se agregó:**
- Sistema de tracking de mensajes recientes (ventana de 60 segundos)
- Detección automática basada en Cliente+Contacto+Source
- Log con tiempo transcurrido desde último mensaje

**Resultado:**
```
📄 MessageId=123 - Cliente 24971, Contacto 0, Source=Nesto viejo ⚠️ POSIBLE DUPLICADO (último mensaje hace 0.5s)
```

---

### 3. **Normalización de Comentarios**

**Archivos modificados:**
- `NestoAPI/Infraestructure/Sincronizacion/ClienteChangeDetector.cs` (líneas 5, 67-69, 134-176)

**Qué se agregó:**
- Método `NormalizeComentarios()` que:
  - Elimina etiquetas HTML (`<p>`, `</p>`, etc.)
  - Normaliza saltos de línea (`\r\n` → `\n`)
  - Ordena líneas alfabéticamente
  - Trim y mayúsculas

**Problema resuelto:**
```
// Antes: Detectaba como diferentes
"<p>[Teléfonos extra] 649172403\nA/A Mª JOSÉ: 660101678</p>"
"A/A Mª JOSÉ: 660101678\n[Teléfonos extra] 649172403"

// Después: Detecta como iguales ✅
```

---

### 4. **Source Dinámico**

**Archivos modificados:**
- `NestoAPI/Infraestructure/GestorClientes.cs` (línea 1405, 1430)
- `NestoAPI/Infraestructure/IGestorClientes.cs` (línea 26)
- `NestoAPI/Controllers/ClientesController.cs` (líneas 638, 694)

**Qué se agregó:**
- Parámetro `source` en `PublicarClienteSincronizar()`
- Valor por defecto: `"Nesto"`
- Sincronización desde `/api/Clientes/Sync`: `"Nesto viejo"`

**Utilidad:**
Permite distinguir mensajes de sincronización manual/batch de operaciones normales.

---

### 5. **Logs de No Actualización**

**Archivos modificados:**
- `NestoAPI/Infraestructure/Sincronizacion/ClientesSyncHandler.cs` (líneas 64-76, 188-190)

**Qué se agregó:**
- Log explícito cuando no hay cambios: `⚪ NO SE ACTUALIZA`
- Diferencia visual con actualizaciones exitosas: `✅`
- Continúa procesando PersonasContacto aunque el cliente no cambie

**Resultado:**
```
⚪ Cliente 24971-1: Sin cambios en datos principales, NO SE ACTUALIZA
   ℹ️ Procesando 2 PersonasContacto...
```

---

## 🧪 Tests Agregados

**Archivo:** `NestoAPI.Tests/Infrastructure/ClienteChangeDetectorTests.cs`

### Nuevos Tests (9 tests de normalización de comentarios):

1. ✅ `DetectarCambios_ComentariosConHTMLYOrdenDiferente_NoDetectaCambio`
2. ✅ `DetectarCambios_ComentariosConDiferentesSaltosLinea_NoDetectaCambio`
3. ✅ `DetectarCambios_ComentariosHTMLVsTextoPlano_NoDetectaCambio`
4. ✅ `DetectarCambios_ComentariosConLineasEnOrdenInverso_NoDetectaCambio`
5. ✅ `DetectarCambios_ComentariosConContenidoDiferente_DetectaCambio`
6. ✅ `DetectarCambios_ComentariosConHTMLComplejoDiferente_DetectaCambio`
7. ✅ `DetectarCambios_ComentariosConEspaciosYHTMLExtra_NoDetectaCambio`
8. ✅ `DetectarCambios_ComentarioNullVsHTMLVacio_NoDetectaCambio`
9. ✅ `DetectarCambios_ComentariosRealCasoUsuario_NoDetectaCambio`

### Ejecución de Tests:
```bash
cd NestoAPI.Tests
dotnet test --filter "FullyQualifiedName~ClienteChangeDetectorTests"
```

---

## 📚 Documentación Creada

### 1. **MEJORAS_LOGS_SINCRONIZACION.md**
Documentación completa con:
- Resumen ejecutivo
- Problema original
- Soluciones implementadas
- Arquitectura de logs
- Ejemplos de logs
- Testing
- Troubleshooting
- Referencias

### 2. **RESUMEN_CAMBIOS_SINCRONIZACION_2025-11-12.md** (este archivo)
Resumen ejecutivo de los cambios realizados.

---

## 🔍 Ejemplo de Flujo Completo

### Escenario: Cliente con PersonaContacto actualizada

```
📤 Publicando mensaje: Cliente 39598-0, Source=Nesto viejo, PersonasContacto=[Id=1 (Ainhoa), Id=2 (Carlos)]
    ↓
📨 Webhook recibido: MessageId=16386696225451217
    ↓
📄 MessageId=16386696225451217 - Cliente 39598, Contacto 0, Source=Nesto viejo, PersonasContacto=[Id=1 (Ainhoa), Id=2 (Carlos)]
    ↓
🔍 Procesando Cliente 39598-0, PersonasContacto=[1, 2] (Source=Nesto viejo)
    ↓
⚪ Cliente 39598-0: Sin cambios en datos principales, NO SE ACTUALIZA
   ℹ️ Procesando 2 PersonasContacto...
      🔍 PersonaContacto 39598-0-1 (Ainhoa)
      ⚪ 39598-0-1: Sin cambios, NO SE ACTUALIZA
      🔍 PersonaContacto 39598-0-2 (Carlos)
      🔄 39598-0-2: Cambios detectados:
         - Teléfono: '600111222' → '600333444'
      ✅ 39598-0-2: Actualizada exitosamente
    ↓
✅ Mensaje procesado exitosamente: 16386696225451217
```

---

## 🎨 Guía de Emojis en Logs

| Emoji | Significado | Ubicación |
|-------|-------------|-----------|
| 📤 | Mensaje emitido desde Nesto | GestorClientes |
| 📨 | Webhook recibido | SyncWebhookController |
| 📄 | Mensaje procesado | SyncWebhookController |
| 🔍 | Procesando | ClientesSyncHandler |
| ⚪ | Sin cambios, NO actualizado | ClientesSyncHandler |
| 🔄 | Cambios detectados | ClientesSyncHandler |
| ✅ | Éxito | Varios |
| ⚠️ | Advertencia | Varios |
| ❌ | Error | Varios |
| ℹ️ | Información | Varios |

---

## 🚀 Cómo Usar los Nuevos Logs

### 1. Ver logs en tiempo real
```
GET /api/sync/logs
```

### 2. Identificar duplicados
Buscar en los logs: `⚠️ POSIBLE DUPLICADO`

### 3. Ver qué se actualiza y qué no
- `⚪` = No se actualizó (sin cambios)
- `✅` = Se actualizó exitosamente
- `🔄` = Cambios detectados

### 4. Rastrear un mensaje específico
Buscar por MessageId o por Cliente-Contacto-PersonaContacto:
```
MessageId=16386696225451217
Cliente 39598-0-1
```

---

## 📊 Estadísticas

### Archivos Modificados: 7
1. `SyncWebhookController.cs`
2. `ClientesSyncHandler.cs`
3. `ClienteChangeDetector.cs`
4. `GestorClientes.cs`
5. `IGestorClientes.cs`
6. `ClientesController.cs`
7. `ClienteChangeDetectorTests.cs` (tests)

### Documentación Creada: 2
1. `MEJORAS_LOGS_SINCRONIZACION.md` (completa)
2. `RESUMEN_CAMBIOS_SINCRONIZACION_2025-11-12.md` (este archivo)

### Tests Agregados: 9
Todos enfocados en normalización de comentarios

---

## ✅ Checklist de Verificación

- [x] Logs muestran Cliente-Contacto-PersonaContacto
- [x] Logs muestran Source del mensaje
- [x] Detección automática de duplicados
- [x] Normalización de comentarios HTML
- [x] Normalización de orden de líneas
- [x] Source dinámico (Nesto vs Nesto viejo)
- [x] Logs de no actualización
- [x] Tests unitarios creados
- [x] Documentación completa
- [x] Resumen ejecutivo

---

## 🔧 Troubleshooting Rápido

### Problema: Veo duplicados
**Buscar:** `⚠️ POSIBLE DUPLICADO`
**Verificar:** Tiempo entre mensajes (si < 1s, probablemente es un bug)

### Problema: Falsos positivos en comentarios
**Verificar:** Que `ClienteChangeDetector` usa `SonIgualesComentarios()`
**Tests:** Ejecutar `ClienteChangeDetectorTests`

### Problema: No puedo rastrear un mensaje
**Solución:** Buscar por cualquiera de estos identificadores:
- MessageId
- Cliente + Contacto
- Cliente + Contacto + PersonaContacto

---

## 📞 Referencias

- **Documentación completa:** [MEJORAS_LOGS_SINCRONIZACION.md](./MEJORAS_LOGS_SINCRONIZACION.md)
- **Setup sincronización:** [SINCRONIZACION_BIDIRECCIONAL_ODOO_SETUP.md](./SINCRONIZACION_BIDIRECCIONAL_ODOO_SETUP.md)
- **Agregar tablas:** [GUIA_AGREGAR_TABLA_SINCRONIZACION.md](./GUIA_AGREGAR_TABLA_SINCRONIZACION.md)

---

**Cambios realizados por:** Claude Code
**Fecha:** 2025-11-12
**Estado:** ✅ Completo y documentado
