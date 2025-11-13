# Hangfire - Sincronización Automática de Productos

## 📋 Resumen

Se ha implementado **Hangfire** para ejecutar automáticamente la sincronización de productos cada 5 minutos, reemplazando la necesidad de Task Scheduler de Windows.

**Fecha**: 2025-11-13
**Estado**: ✅ Implementación completa - Listo para probar
**Job configurado**: Productos (cada 5 minutos)
**Jobs pendientes**: Clientes (aún usa Task Scheduler)

---

## 🎯 ¿Qué es Hangfire?

Hangfire es una librería que permite ejecutar tareas programadas (jobs) directamente desde tu aplicación ASP.NET, sin necesidad de Task Scheduler u otras herramientas externas.

**Ventajas vs Task Scheduler**:
- ✅ Configuración en código (versionada en Git)
- ✅ Dashboard web visual (`/hangfire`)
- ✅ Historial completo de ejecuciones
- ✅ Reintentos automáticos si falla
- ✅ Ver logs en tiempo real
- ✅ Pausar/reanudar jobs desde el navegador
- ✅ No requiere acceso al servidor para configurar

---

## 📦 Componentes Instalados

### 1. Packages NuGet
- `Hangfire.Core 1.8.17`
- `Hangfire.SqlServer 1.8.17`

### 2. Archivos Nuevos/Modificados

**Nuevos**:
- `Infraestructure/SincronizacionJobsService.cs`: Métodos para jobs
- `HANGFIRE_SETUP.md`: Esta documentación

**Modificados**:
- `Startup.cs`: Configuración de Hangfire
- `packages.config`: Packages agregados
- `NestoAPI.csproj`: Referencias agregadas

### 3. Base de Datos

Hangfire crea automáticamente sus propias tablas en tu base de datos:
- `HangFire.AggregatedCounter`
- `HangFire.Counter`
- `HangFire.Hash`
- `HangFire.Job`
- `HangFire.JobParameter`
- `HangFire.JobQueue`
- `HangFire.List`
- `HangFire.Schema`
- `HangFire.Server`
- `HangFire.Set`
- `HangFire.State`

**Estas tablas NO afectan tus datos existentes**. Son solo para Hangfire.

---

## 🚀 Pasos para Activar

### Paso 1: Restaurar Packages NuGet

```bash
# En Visual Studio
# Botón derecho en la solución → Restore NuGet Packages

# O en la consola del Package Manager
Update-Package -reinstall
```

### Paso 2: Compilar el Proyecto

```bash
# En Visual Studio: Build → Build Solution
# O presiona Ctrl+Shift+B
```

### Paso 3: Ejecutar la Aplicación

```bash
# En Visual Studio: F5 o Debug → Start Debugging
```

**Logs esperados en la consola**:
```
✅ Hangfire configurado correctamente
✅ Job recurrente 'sincronizar-productos' configurado (cada 5 minutos)
```

**Event Log de Windows**:
Verás un mensaje: "Hangfire configurado correctamente en NestoAPI. Dashboard disponible en /hangfire"

### Paso 4: Acceder al Dashboard

Abre tu navegador y ve a:
```
http://localhost:53364/hangfire
```

Deberías ver el dashboard de Hangfire con:
- **Jobs**: Lista de todos los jobs
- **Recurring Jobs**: Job "sincronizar-productos" configurado
- **Servers**: Servidor Hangfire activo
- **Succeeded/Failed**: Estadísticas de ejecuciones

---

## 📊 Dashboard de Hangfire

### Vista Principal

El dashboard muestra:

1. **Recurring Jobs** (Jobs Recurrentes)
   - `sincronizar-productos`: Cada 5 minutos
   - Estado: Activo ✅ o Pausado ⏸️
   - Próxima ejecución: Countdown timer
   - Última ejecución: Timestamp

2. **Jobs en Ejecución**
   - Productos sincronizándose en tiempo real
   - Tiempo de ejecución

3. **Historial**
   - **Succeeded** (✅): Jobs completados exitosamente
   - **Failed** (❌): Jobs que fallaron
   - **Retries** (🔄): Jobs reintentándose

### Acciones Disponibles

**En Recurring Jobs**:
- ✅ **Trigger now**: Ejecutar inmediatamente
- ⏸️ **Pause**: Pausar ejecución automática
- ▶️ **Resume**: Reanudar ejecución
- 🗑️ **Delete**: Eliminar job (no recomendado)

**En un Job específico**:
- 📋 **Ver detalles**: Stack trace, parámetros, logs
- 🔄 **Retry**: Reintentar manualmente
- 🗑️ **Delete**: Eliminar del historial

---

## 🔄 Flujo de Ejecución

```
Cada 5 minutos:
  ↓
Hangfire ejecuta SincronizacionJobsService.SincronizarProductos()
  ↓
Lee registros pendientes de nesto_sync (WHERE Tabla='Productos')
  ↓
Procesa en lotes de 50 con delays de 5 segundos
  ↓
Por cada producto:
  - Construye ProductoDTO completo (foto, precio, stocks, kits)
  - Publica a Google Pub/Sub
  - Marca como sincronizado en nesto_sync
  ↓
Registra resultado en Hangfire
  ↓
Si falla: Hangfire reintenta automáticamente
```

---

## 📋 Logs y Monitoreo

### Logs en Consola

```
🚀 [Hangfire] Iniciando sincronización de productos...
🔄 Procesando 150 registros de la tabla Productos en lotes de 50
📦 Procesando lote 1/3 (50 registros)
📤 Publicando mensaje: Producto 17404, Source=Nesto viejo, Usuario=CARLOS, Kits=[ninguno], Stocks=[3 almacenes]
✅ Productos 17404 sincronizado correctamente (Usuario: CARLOS)
...
✅ [Hangfire] Sincronización de productos completada exitosamente
```

### Logs en Hangfire Dashboard

1. Ve a **Jobs** → **Succeeded**
2. Clic en el job "sincronizar-productos"
3. Verás:
   - Duración de la ejecución
   - Exception (si falló)
   - Stack trace completo
   - Logs de consola capturados

### Event Log de Windows

```
Source: Application
Event ID: Información
Mensaje: Hangfire configurado correctamente en NestoAPI. Dashboard disponible en /hangfire
```

Si hay errores:
```
Source: Application
Event ID: Error
Mensaje: Error al configurar Hangfire: [mensaje de error]
```

---

## ⚙️ Configuración Actual

### Job: sincronizar-productos

- **Frecuencia**: Cada 5 minutos
- **Cron expression**: `*/5 * * * *`
- **TimeZone**: Local (hora del servidor)
- **Worker Count**: 1 (para evitar procesamiento duplicado)
- **Método ejecutado**: `SincronizacionJobsService.SincronizarProductos()`

### Explicación del Cron

```
*/5 * * * *
│  │ │ │ │
│  │ │ │ └─── Día de la semana (0-6, 0=Domingo)
│  │ │ └───── Mes (1-12)
│  │ └─────── Día del mes (1-31)
│  └───────── Hora (0-23)
└──────────── Minuto (*/5 = cada 5 minutos)
```

**Otros ejemplos**:
- `0 * * * *`: Cada hora (minuto 0)
- `0 9 * * *`: Todos los días a las 9:00 AM
- `*/10 * * * *`: Cada 10 minutos
- `0 0 * * 1`: Cada lunes a medianoche

---

## 🔧 Cambiar la Frecuencia

Si quieres cambiar la frecuencia, edita `Startup.cs`:

```csharp
// En el método ConfigurarJobsRecurrentes()
RecurringJob.AddOrUpdate(
    "sincronizar-productos",
    () => SincronizacionJobsService.SincronizarProductos(),
    "*/10 * * * *", // ⬅️ Cambiar aquí (ejemplo: cada 10 minutos)
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.Local
    }
);
```

**No necesitas reiniciar IIS** - Hangfire detecta el cambio automáticamente.

---

## 🚨 Migrar Clientes desde Task Scheduler

Cuando estés listo para migrar Clientes a Hangfire:

### Paso 1: Desactivar Task Scheduler

En el servidor donde corre Task Scheduler:
1. Abre "Task Scheduler" (Programador de Tareas)
2. Busca la tarea que llama a `/api/Clientes/Sync`
3. Botón derecho → **Disable** (Deshabilitar)
4. **NO la borres** aún, por si necesitas volver atrás

### Paso 2: Habilitar Job en Hangfire

En `Startup.cs`, cambia `#if false` por `#if true` (alrededor de la línea 260):

```csharp
// NOTA: El job de clientes está deshabilitado porque aún se usa Task Scheduler
// Para habilitarlo en el futuro, cambia '#if false' por '#if true':
#if true  // ⬅️ Cambiar de 'false' a 'true'
            RecurringJob.AddOrUpdate(
                "sincronizar-clientes",
                () => SincronizacionJobsService.SincronizarClientes(),
                "*/5 * * * *", // Cron: cada 5 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'sincronizar-clientes' configurado (cada 5 minutos)");
#endif
```

### Paso 3: Recompilar y Desplegar

```bash
# En Visual Studio
Build → Publish
```

### Paso 4: Verificar en Dashboard

1. Ve a `http://tu-servidor/hangfire`
2. En **Recurring Jobs** deberías ver:
   - `sincronizar-productos` ✅
   - `sincronizar-clientes` ✅ (nuevo)

### Paso 5: Monitorear 24 horas

Monitorea que ambos jobs se ejecuten correctamente durante al menos un día antes de eliminar la tarea de Task Scheduler.

### Paso 6: Eliminar Task Scheduler (Opcional)

Una vez que todo funciona bien, puedes eliminar la tarea de Task Scheduler.

---

## ⚠️ Seguridad: Dashboard en Producción

**⚠️ IMPORTANTE**: El dashboard actualmente está **sin autenticación** (permite acceso a todos).

### Opción A: Restringir por IP (Rápido)

En `Startup.cs`, en la clase `HangfireAuthorizationFilter`:

```csharp
public bool Authorize(Hangfire.Dashboard.DashboardContext context)
{
    // Solo permitir desde IPs internas
    var remoteIp = context.GetHttpContext().Request.RemoteIpAddress;
    return remoteIp.ToString().StartsWith("192.168.") ||
           remoteIp.ToString().StartsWith("10.") ||
           remoteIp.ToString() == "127.0.0.1";
}
```

### Opción B: Requerir Autenticación (Recomendado)

```csharp
public bool Authorize(Hangfire.Dashboard.DashboardContext context)
{
    var owinContext = new OwinContext(context.GetOwinEnvironment());

    // Verificar si el usuario está autenticado
    return owinContext.Authentication.User.Identity.IsAuthenticated &&
           owinContext.Authentication.User.IsInRole("Admin");
}
```

### Opción C: Deshabilitar Dashboard en Producción

En `Startup.cs`:

```csharp
#if DEBUG
    // Dashboard solo en desarrollo
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter() }
    });
#endif
```

---

## 🐛 Troubleshooting

### Problema: No veo el dashboard

**Solución**:
1. Verifica que la app esté corriendo
2. Accede a `http://localhost:53364/hangfire` (ajusta el puerto)
3. Revisa Event Log de Windows para ver si Hangfire se configuró

### Problema: Job no se ejecuta

**Solución**:
1. Ve al dashboard → **Servers**
2. Verifica que haya al menos 1 servidor activo
3. Ve a **Recurring Jobs**
4. Verifica que el job no esté pausado
5. Click en "Trigger now" para ejecutar manualmente

### Problema: Job falla constantemente

**Solución**:
1. Ve al dashboard → **Failed Jobs**
2. Click en el job fallido
3. Lee el stack trace completo
4. Verifica:
   - Connection string correcto
   - Permisos de la base de datos
   - Google Pub/Sub configurado
5. Hangfire reintentará automáticamente

### Problema: Tablas de Hangfire ocupan mucho espacio

**Solución**:
Hangfire limpia automáticamente jobs antiguos después de 7 días. Si quieres ajustar:

```csharp
.UseSqlServerStorage(connectionString, new SqlServerStorageOptions
{
    JobExpirationCheckInterval = TimeSpan.FromHours(1), // Revisar cada hora
    // Otros settings...
});
```

---

## 📊 Métricas y Monitoreo

### Dashboard Muestra

- **Succeeded**: Total de jobs exitosos
- **Failed**: Total de jobs fallidos
- **Processing**: Jobs en ejecución ahora
- **Scheduled**: Jobs programados para el futuro
- **Retries**: Jobs reintentándose
- **Deleted**: Jobs eliminados manualmente

### Gráficos

El dashboard incluye gráficos en tiempo real de:
- Jobs por hora (últimas 24 horas)
- Tasa de éxito/fallo
- Tiempos de ejecución promedio

---

## ✅ Checklist de Implementación

- [x] Instalar packages de Hangfire
- [x] Crear `SincronizacionJobsService`
- [x] Configurar Hangfire en `Startup.cs`
- [x] Configurar job "sincronizar-productos" (cada 5 minutos)
- [x] Documentación completa
- [ ] **Restaurar packages NuGet** (¡HACER ESTO!)
- [ ] **Compilar el proyecto** (¡HACER ESTO!)
- [ ] **Ejecutar y probar** (¡HACER ESTO!)
- [ ] Acceder al dashboard `/hangfire`
- [ ] Verificar que el job se ejecuta cada 5 minutos
- [ ] Monitorear 24 horas
- [ ] (Futuro) Migrar Clientes desde Task Scheduler
- [ ] (Producción) Restringir acceso al dashboard

---

## 🎉 ¡Listo!

Hangfire está configurado y listo para usar. Solo necesitas:

1. **Restaurar packages NuGet**
2. **Compilar**
3. **Ejecutar**
4. **Acceder a `/hangfire`** y disfrutar del dashboard

---

## 📚 Recursos Adicionales

- **Documentación oficial**: https://docs.hangfire.io/
- **Cron expression generator**: https://crontab.guru/
- **Dashboard**: https://docs.hangfire.io/en/latest/configuration/using-dashboard.html
- **Best Practices**: https://docs.hangfire.io/en/latest/best-practices.html

---

**Estado**: ✅ **Implementación completa - Listo para restaurar packages y probar**

¡Adiós Task Scheduler! 👋
