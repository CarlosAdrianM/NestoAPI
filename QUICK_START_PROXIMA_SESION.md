# Quick Start - Próxima Sesión

## 🎯 Lo que Hicimos Hoy

✅ Implementado sistema completo de sincronización Push Subscription
✅ 7 archivos nuevos creados
✅ 3 archivos modificados (Startup.cs, NestoAPI.csproj)
✅ Documentación completa
✅ Scripts de prueba listos

## ⚠️ Bug Conocido - CORREGIR PRIMERO

**Archivo**: `ClientesSyncHandler.cs` líneas 57-76

**Problema**: El código valida si `clienteNesto == null` DESPUÉS de usarlo en `DetectarCambios()`.

**Solución**: Mover la validación ANTES de la detección de cambios.

```csharp
// CORRECTO:
var clienteNesto = await db.Clientes.Where(...).FirstOrDefaultAsync();

if (clienteNesto == null)  // ✅ Validar primero
{
    Console.WriteLine($"⚠️ Cliente no existe");
    return false;
}

var cambios = _changeDetector.DetectarCambios(clienteNesto, ...);  // ✅ Seguro
```

## 🚀 Pasos para Probar (15 minutos)

### 1. Corregir el Bug (2 min)
- Abrir `ClientesSyncHandler.cs`
- Mover validación `if (clienteNesto == null)` a línea 57
- Guardar

### 2. Compilar (1 min)
```bash
# En Visual Studio: Build → Build Solution
# O desde terminal:
msbuild NestoAPI.sln /t:Build /p:Configuration=Debug
```

### 3. Preparar Datos de Prueba (2 min)
```sql
-- Buscar un cliente real en tu BD
SELECT TOP 1 Nº_Cliente, Contacto, Nombre, Teléfono
FROM Clientes
WHERE Empresa = '1'
```

### 4. Actualizar Script de Prueba (2 min)
Editar `test_webhook_local.ps1` líneas 7-16 con datos del cliente real.

### 5. Ejecutar Prueba (5 min)
1. **F5** en Visual Studio para ejecutar API
2. **Verificar health check**:
   ```powershell
   Invoke-RestMethod -Uri "http://localhost:53364/api/sync/health"
   ```
   Debe mostrar: `"status": "healthy"` y `"supportedTables": ["Clientes"]`

3. **Ejecutar script**:
   ```powershell
   .\test_webhook_local.ps1
   ```

4. **Observar consola de Visual Studio** - deberías ver:
   ```
   📨 Webhook recibido: MessageId=...
   📥 Mensaje recibido: Tabla=Clientes, Acción=actualizar
   🔍 Procesando Cliente: ...
   ✅ Cliente actualizado exitosamente
   ```

### 6. Verificar en BD (3 min)
```sql
SELECT Nº_Cliente, Contacto, Nombre, Teléfono, Usuario, Fecha_Modificación
FROM Clientes
WHERE Nº_Cliente = 'TU_CLIENTE' AND Contacto = 'TU_CONTACTO'
```

Debe mostrar:
- `Usuario = 'EXTERNAL_SYNC'`
- `Fecha_Modificación` = fecha/hora reciente

## 📋 Checklist de Verificación

- [ ] Bug corregido en `ClientesSyncHandler.cs`
- [ ] Compilación exitosa sin errores
- [ ] Health check responde correctamente
- [ ] Script de prueba actualizado con cliente real
- [ ] Prueba local ejecutada exitosamente
- [ ] Logs en Visual Studio muestran procesamiento correcto
- [ ] BD actualizada con `Usuario = 'EXTERNAL_SYNC'`

## 🎯 Si Todo Funciona → Siguiente Nivel

### Opción A: Probar con ngrok (integración real)
1. Descargar ngrok: https://ngrok.com/download
2. Extraer a `C:\Tools\ngrok\`
3. Ejecutar: `.\ngrok.exe http 53364`
4. Crear Push Subscription con URL de ngrok
5. Publicar mensaje desde Odoo/Prestashop

### Opción B: Agregar más tablas
Ejemplo: Productos, Proveedores, etc.
Ver guía: `GUIA_AGREGAR_TABLA_SINCRONIZACION.md`

## 📂 Archivos Importantes

| Archivo | Descripción |
|---------|-------------|
| `ESTADO_SESION_SINCRONIZACION.md` | 📝 Documento completo de estado |
| `TESTING_LOCAL_WEBHOOK.md` | 🧪 Guía detallada de pruebas |
| `CONFIGURACION_PUSH_SUBSCRIPTION.md` | ⚙️ Setup de Google Cloud |
| `test_webhook_local.ps1` | 🔧 Script de prueba PowerShell |

## 🆘 Problemas Comunes

### "No se encontró el endpoint"
→ ✅ Verifica que la API está corriendo (F5 en VS)
→ ✅ Verifica puerto 53364

### "Cliente no existe"
→ ✅ Usa datos de cliente real de tu BD
→ ✅ Verifica que `Empresa = '1'`

### "NullReferenceException"
→ ⚠️ El bug no está corregido
→ ✅ Mover validación de null antes de DetectarCambios

### No veo logs
→ ✅ Ventana Output en VS → Seleccionar "Debug"

## 🎉 Objetivo de Próxima Sesión

**Meta mínima**: Prueba local funcionando correctamente
**Meta ideal**: Integración completa con Google Pub/Sub mediante ngrok
**Meta extendida**: Agregar soporte para otra tabla (Productos/Proveedores)

---

**Tiempo estimado total**: 15-30 minutos
**Dificultad**: Baja (solo corregir bug y probar)
**Riesgo**: Muy bajo (solo desarrollo local)
