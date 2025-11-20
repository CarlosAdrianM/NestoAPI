# Sesión 2025-01-19: Implementación Completa de UI para Poison Pills

## 📋 Resumen Ejecutivo

Se implementó una **interfaz de usuario completa** para la gestión de "Poison Pills" (mensajes de sincronización que fallan repetidamente) en el módulo **Canales Externos** de Nesto. Incluye visualización, filtrado y gestión de estados de mensajes problemáticos de Google Pub/Sub.

**Fecha**: 2025-01-19
**Estado**: ✅ Completado y funcional
**Módulos afectados**:
- Backend (NestoAPI)
- Frontend (Nesto - CanalesExternos)

---

## 🎯 Objetivos Cumplidos

### Backend
- ✅ Modelos DTO para poison pills
- ✅ Integración con Entity Framework Database-First (EDMX)
- ✅ Endpoints REST para consultar y gestionar poison pills

### Frontend
- ✅ Modelos de datos
- ✅ Servicio de API
- ✅ ViewModel con lógica de negocio
- ✅ Vista XAML con DataGrid y controles
- ✅ Botón en menú Herramientas con icono vectorial personalizado
- ✅ Control de acceso por grupo de seguridad (DIRECCION)

---

## 📦 Archivos Creados

### Backend (NestoAPI)

#### 1. Modelos y DTOs
**`Models/Sincronizacion/RetryStatus.cs`**
```csharp
public enum RetryStatus
{
    Retrying,           // Aún reintentando (< 5 intentos)
    PoisonPill,         // Límite alcanzado, requiere revisión
    Reprocess,          // Marcado para reprocesar
    Resolved,           // Resuelto manualmente
    PermanentFailure    // Fallo permanente
}
```

**`Models/Sincronizacion/PoisonPillDTO.cs`**
- DTO completo con 13 propiedades
- Incluye tiempos calculados (TimeSinceFirstAttempt, TimeSinceLastAttempt)
- Usado para serialización en respuestas HTTP

**`Models/Sincronizacion/ChangeStatusRequest.cs`**
- DTO para cambiar estado de poison pills
- Campos: MessageId, NewStatus

**`Models/SyncMessageRetry.cs`** (Generado por EDMX)
- Clase principal generada automáticamente desde base de datos
- 10 propiedades mapeadas a tabla SQL

**`Models/SyncMessageRetry.Partial.cs`** ⭐ IMPORTANTE
- Extensión partial para agregar funcionalidad custom
- Propiedad `StatusEnum` para convertir string → enum
- No se pierde al regenerar EDMX

#### 2. Script SQL
**`SCRIPT_SQL_SYNC_MESSAGE_RETRIES.sql`**
```sql
CREATE TABLE [dbo].[SyncMessageRetries](
    [MessageId] [nvarchar](255) PRIMARY KEY,
    [Tabla] [nvarchar](50) NOT NULL,
    [EntityId] [nvarchar](100) NULL,
    [Source] [nvarchar](50) NULL,
    [AttemptCount] [int] NOT NULL DEFAULT 0,
    [FirstAttemptDate] [datetime] NOT NULL,
    [LastAttemptDate] [datetime] NOT NULL,
    [LastError] [nvarchar](max) NULL,
    [Status] [nvarchar](20) NOT NULL,
    [MessageData] [nvarchar](max) NULL
)
```
- 3 índices para optimización
- Script idempotente (puede ejecutarse múltiples veces)

#### 3. Endpoints (Ya existían, se verificaron)
**GET `/api/sync/poisonpills`**
- Filtros: status, tabla, limit
- Retorna: { total, filters, poisonPills[], timestamp }

**POST `/api/sync/poisonpills/changestatus`**
- Body: { messageId, newStatus }
- Retorna: { success, messageId, newStatus, timestamp }

### Frontend (Nesto - CanalesExternos)

#### 1. Modelos
**`Models/PoisonPillModel.cs`**
```csharp
public class PoisonPillModel
{
    public string MessageId { get; set; }
    public string Tabla { get; set; }
    public string EntityId { get; set; }
    public string Source { get; set; }
    public int AttemptCount { get; set; }
    public DateTime FirstAttemptDate { get; set; }
    public DateTime LastAttemptDate { get; set; }
    public string LastError { get; set; }
    public string Status { get; set; }
    public string MessageData { get; set; }
    public string TimeSinceFirstAttempt { get; set; }
    public string TimeSinceLastAttempt { get; set; }

    // Propiedad calculada para UI
    public string DisplayId => !string.IsNullOrEmpty(EntityId)
        ? $"{Tabla} - {EntityId}"
        : MessageId;
}
```

**`Models/ChangeStatusRequestModel.cs`**
- Equivalente frontend del DTO backend

#### 2. Servicios
**`Interfaces/IPoisonPillsService.cs`**
```csharp
public interface IPoisonPillsService
{
    Task<List<PoisonPillModel>> ObtenerPoisonPillsAsync(
        string status = null,
        string tabla = null,
        int limit = 100);

    Task<bool> CambiarEstadoAsync(
        string messageId,
        string newStatus);
}
```

**`Services/PoisonPillsService.cs`**
- Implementación con HttpClient
- Consume endpoints de NestoAPI
- Manejo de errores con detalles
- Deserialización de respuestas

#### 3. ViewModels
**`ViewModels/PoisonPillsViewModel.cs`** (340 líneas)

**Propiedades principales:**
- `ListaPoisonPills`: ObservableCollection<PoisonPillModel>
- `PoisonPillSeleccionado`: PoisonPillModel
- `EstadosDisponibles`: ["Todos", "PoisonPill", "Retrying", "Reprocess", "Resolved", "PermanentFailure"]
- `TablasDisponibles`: ["Todas", "Clientes", "Productos", "Pedidos", "Pagos"]
- `EstadoSeleccionado`: string (default: "PoisonPill")
- `TablaSeleccionada`: string (default: "Todas")
- `EstaOcupado`: bool (busy indicator)

**Comandos:**
- `CargarPoisonPillsCommand`: Carga lista con filtros
- `ReprocesarCommand`: Marca para reprocesar (resetea contador)
- `MarcarComoResueltoCommand`: Marca como resuelto
- `MarcarComoFalloPermanenteCommand`: Marca como fallo permanente
- `VerDetalleCommand`: Muestra diálogo con todos los detalles

**Características:**
- Confirmaciones con `ShowConfirmationAnswer()`
- Notificaciones con `ShowNotification()`
- Errores con `ShowError()`
- Recarga automática tras cambios
- Validación de selección

#### 4. Vistas
**`Views/PoisonPillsView.xaml`** (130 líneas)

**Estructura:**
1. **Panel de filtros** (Border con StackPanel):
   - ComboBox Estado
   - ComboBox Tabla
   - Botón Buscar
   - TextBlock con contador total

2. **DataGrid principal** (con BusyIndicator):
   - 9 columnas con información completa
   - Colores por estado:
     - **Rojo/Bold**: PoisonPill (requiere atención)
     - **Verde**: Resolved
     - **Naranja/Bold**: Reprocess
     - **Rojo oscuro**: PermanentFailure
   - Tooltips en columna de error
   - Selección única
   - AutoGenerateColumns="False"

3. **Panel de botones** (inferior derecha):
   - Ver Detalle
   - Reprocesar (botón naranja)
   - Marcar como Resuelto (botón verde)
   - Marcar como Fallo Permanente (botón rojo)

**`Views/PoisonPillsView.xaml.cs`**
- Code-behind mínimo (solo InitializeComponent)

#### 5. Menú
**`CanalesExternosMenuBar.xaml`** (modificado)
```xml
<RibbonButton Label="Poison Pills" Command="{Binding AbrirModuloPoisonPillsCommand}">
    <RibbonButton.LargeImageSource>
        <DrawingImage>
            <DrawingImage.Drawing>
                <DrawingGroup>
                    <!-- Octágono de alerta rojo -->
                    <GeometryDrawing Brush="#D32F2F">
                        <GeometryDrawing.Geometry>
                            <PathGeometry Figures="M12,2L4.2,4.2L2,12L4.2,19.8L12,22L19.8,19.8L22,12L19.8,4.2L12,2Z"/>
                        </GeometryDrawing.Geometry>
                    </GeometryDrawing>
                    <!-- Signo de exclamación blanco -->
                    <GeometryDrawing Brush="White">
                        <GeometryDrawing.Geometry>
                            <PathGeometry Figures="M11,7H13V13H11V7M11,15H13V17H11V15Z"/>
                        </GeometryDrawing.Geometry>
                    </GeometryDrawing>
                </DrawingGroup>
            </DrawingImage.Drawing>
        </DrawingImage>
    </RibbonButton.LargeImageSource>
</RibbonButton>
```
**Icono vectorial personalizado**: Octágono rojo con exclamación blanca (completamente vectorial, escala perfectamente)

**`CanalesExternosMenuBarViewModel.cs`** (modificado)
```csharp
public ICommand AbrirModuloPoisonPillsCommand { get; private set; }

private bool CanAbrirModuloPoisonPills()
{
    return Configuracion.UsuarioEnGrupo(Constantes.GruposSeguridad.DIRECCION);
}

private void OnAbrirModuloPoisonPills()
{
    RegionManager.RequestNavigate("MainRegion", "PoisonPillsView");
}
```

#### 6. Registro de módulo
**`CanalesExternos.cs`** (modificado)
```csharp
public void RegisterTypes(IContainerRegistry containerRegistry)
{
    // ... otros registros ...

    // Vista de poison pills
    containerRegistry.Register<object, PoisonPillsView>("PoisonPillsView");

    // Servicio de poison pills
    containerRegistry.Register<IPoisonPillsService, PoisonPillsService>();
}
```

---

## 🔧 Problemas Resueltos Durante la Sesión

### 1. Error: Compilación - Missing Usings
**Problema**: Errores CS0246 (tipos no encontrados)

**Solución**: Agregados usings necesarios:
- `System.Data.Entity` en SyncWebhookController (para ToListAsync)
- `NestoAPI.Models` en Startup.cs (para NVEntities)
- `ControlesUsuario.Dialogs` en PoisonPillsViewModel (para ShowError/ShowConfirmation)

### 2. Error: Conversión de tipos CrearFacturaResponseDTO
**Problema**: Código antiguo esperaba `string` pero ahora retorna DTO

**Archivos corregidos**:
- `GestorFacturacionRutas.cs`: Cambio de `string numeroFactura` a `var resultadoFactura`
- `AgenciasViewModel.vb`: Cambio de `Dim factura As String` a `Dim resultadoFactura = ...; Dim factura = resultadoFactura.NumeroFactura`

**Patrón aplicado**:
```csharp
// Antes
string numeroFactura = await servicioFacturas.CrearFactura(...);

// Después
var resultadoFactura = await servicioFacturas.CrearFactura(...);
string numeroFactura = resultadoFactura.NumeroFactura;
```

### 3. Error: XAML Padding en StackPanel
**Problema**: `StackPanel` no soporta `Padding` en WPF

**Solución**: Envolver StackPanel en Border:
```xml
<!-- Antes -->
<StackPanel Padding="10">
    ...
</StackPanel>

<!-- Después -->
<Border Padding="10">
    <StackPanel>
        ...
    </StackPanel>
</Border>
```

### 4. Error: ShowConfirmation firma incorrecta
**Problema**: Método `ShowConfirmation` no existe con 1 parámetro

**Solución**: Usar `ShowConfirmationAnswer(titulo, mensaje)`:
```csharp
// Antes
var resultado = _dialogService.ShowConfirmation(mensaje);
if (resultado != ButtonResult.OK) return;

// Después
bool continuar = _dialogService.ShowConfirmationAnswer(titulo, mensaje);
if (!continuar) return;
```

### 5. Error: Database-First EDMX
**Problema**: Tabla creada en SQL pero no en modelo EF

**Solución aplicada**:
1. ✅ Ejecutar script SQL para crear tabla
2. ✅ En Visual Studio: Abrir `NestoEntities.edmx`
3. ✅ Click derecho → "Update Model from Database..."
4. ✅ Agregar tabla `SyncMessageRetries`
5. ✅ Eliminar clase manual `Models/Sincronizacion/SyncMessageRetry.cs`
6. ✅ Crear clase partial `Models/SyncMessageRetry.Partial.cs` para StatusEnum
7. ✅ Eliminar DbSet manual de `NVEntities.Partial.cs`

**Patrón Database-First correcto**:
```
1. Tabla SQL
   ↓
2. EDMX genera clase base (SyncMessageRetry.cs)
   ↓
3. Partial class agrega funcionalidad custom (SyncMessageRetry.Partial.cs)
```

### 6. Error: Grupo de seguridad incorrecto
**Problema**: Botón visible para grupo ADMINISTRACION

**Solución**: Cambiado a `Constantes.GruposSeguridad.DIRECCION`

---

## 🚀 Cómo Usar la Funcionalidad

### 1. Acceso
1. Abrir Nesto (frontend)
2. Usuario debe estar en grupo **DIRECCION**
3. Pestaña **Herramientas** → Botón **Poison Pills** (icono octágono rojo)

### 2. Visualizar Poison Pills
- Por defecto carga: Estado = "PoisonPill"
- Cambiar filtros:
  - **Estado**: Todos, PoisonPill, Retrying, Reprocess, Resolved, PermanentFailure
  - **Tabla**: Todas, Clientes, Productos, Pedidos, Pagos
- Click **Buscar**

### 3. Ver Detalles
1. Seleccionar mensaje en DataGrid
2. Click **Ver Detalle**
3. Se muestra diálogo con:
   - MessageId, Tabla, EntityId, Source
   - Intentos, fechas, estado
   - Error completo
   - Datos del mensaje (JSON)

### 4. Reprocesar
1. Seleccionar mensaje
2. Click **Reprocesar**
3. Confirmar
4. Estado cambia a "Reprocess"
5. Contador se reseteará a 1 en próximo envío Pub/Sub

### 5. Marcar como Resuelto
1. Seleccionar mensaje
2. Click **Marcar como Resuelto**
3. Confirmar
4. Estado cambia a "Resolved"
5. Ya no se procesará automáticamente

### 6. Marcar como Fallo Permanente
1. Seleccionar mensaje
2. Click **Marcar como Fallo Permanente**
3. Confirmar
4. Estado cambia a "PermanentFailure"
5. No se procesará nunca más

---

## 🎨 Características de UX

### Códigos de Color por Estado
| Estado | Color | Formato | Significado |
|--------|-------|---------|-------------|
| **PoisonPill** | Rojo (#FF0000) | Bold | ⚠️ Requiere atención inmediata |
| **Retrying** | Negro | Normal | 🔄 Aún intentando procesar |
| **Reprocess** | Naranja (#FFA500) | Bold | 🔁 Marcado para reprocesar |
| **Resolved** | Verde (#228B22) | Normal | ✅ Resuelto manualmente |
| **PermanentFailure** | Rojo oscuro (#8B0000) | Normal | ❌ Fallo definitivo |

### Indicadores Visuales
- **BusyIndicator**: Muestra "Cargando..." durante operaciones async
- **Tooltips**: En columna "Último Error" para ver error completo
- **Contador total**: Muestra número de resultados
- **Botones coloreados**:
  - Reprocesar: Naranja
  - Resuelto: Verde
  - Fallo Permanente: Rojo

### Confirmaciones
Todas las acciones de cambio de estado piden confirmación:
```
Título: "Reprocesar mensaje"
Mensaje: "¿Está seguro de que desea reprocesar el mensaje Clientes - 12345?

El contador de intentos se reseteará y el mensaje se procesará
en el próximo envío de Pub/Sub."

[Aceptar] [Cancelar]
```

---

## 🔒 Seguridad

### Control de Acceso
- **Grupo requerido**: `Constantes.GruposSeguridad.DIRECCION`
- **Ubicación del control**: `CanalesExternosMenuBarViewModel.CanAbrirModuloPoisonPills()`
- **Comportamiento**: Si no tiene permisos, el botón no se muestra en el menú

### Backend
- Endpoints con `[AllowAnonymous]` (consultar con equipo si debe tener autenticación)
- Validación de estados permitidos en `ChangeStatus`

---

## 📊 Arquitectura de la Solución

### Flujo Completo

```
┌─────────────────────────────────────────┐
│  Usuario (Grupo DIRECCION)              │
│  → Click botón "Poison Pills"           │
└───────────────┬─────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────┐
│  PoisonPillsView.xaml                   │
│  → Se carga automáticamente             │
│  → Ejecuta CargarPoisonPillsCommand     │
└───────────────┬─────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────┐
│  PoisonPillsViewModel                   │
│  → OnCargarPoisonPillsAsync()           │
│  → Prepara filtros                      │
└───────────────┬─────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────┐
│  PoisonPillsService                     │
│  → ObtenerPoisonPillsAsync()            │
│  → HttpClient.GetAsync()                │
│  → URL: api/sync/poisonpills?...        │
└───────────────┬─────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────┐
│  NestoAPI                               │
│  → SyncWebhookController                │
│  → GetPoisonPills()                     │
└───────────────┬─────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────┐
│  Entity Framework                       │
│  → NVEntities.SyncMessageRetries        │
│  → Query con filtros                    │
│  → ToListAsync()                        │
└───────────────┬─────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────┐
│  SQL Server                             │
│  → Tabla: SyncMessageRetries            │
│  → SELECT con WHERE + índices           │
└───────────────┬─────────────────────────┘
                │
                ▼ (Response)
┌─────────────────────────────────────────┐
│  JSON Response                          │
│  {                                      │
│    total: 5,                            │
│    poisonPills: [...],                  │
│    timestamp: "..."                     │
│  }                                      │
└───────────────┬─────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────┐
│  PoisonPillsService                     │
│  → Deserializa JSON                     │
│  → Retorna List<PoisonPillModel>        │
└───────────────┬─────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────┐
│  PoisonPillsViewModel                   │
│  → ListaPoisonPills = new Obs...(lista) │
│  → TotalPoisonPills = lista.Count       │
└───────────────┬─────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────┐
│  PoisonPillsView.xaml                   │
│  → DataGrid actualizado con binding     │
│  → Usuario ve lista de poison pills     │
└─────────────────────────────────────────┘
```

### Patrón MVVM con Prism
- **Model**: `PoisonPillModel`, `ChangeStatusRequestModel`
- **View**: `PoisonPillsView.xaml` (UserControl con DataGrid)
- **ViewModel**: `PoisonPillsViewModel` (lógica de negocio)
- **Service**: `PoisonPillsService` (comunicación API)
- **Navigation**: `IRegionManager.RequestNavigate()`
- **DI**: Registrado en `CanalesExternos.RegisterTypes()`

---

## 📝 Checklist de Despliegue

### Pre-Despliegue
- [x] Backend compilado sin errores
- [x] Frontend compilado sin errores
- [x] Script SQL ejecutado en base de datos
- [x] Tabla `SyncMessageRetries` existe
- [x] Modelo EDMX actualizado
- [x] Partial class creada para StatusEnum
- [x] Servicios registrados en DI
- [x] Vistas registradas en módulo
- [x] Menú actualizado con botón
- [x] Permisos de seguridad configurados (DIRECCION)
- [x] Icono vectorial creado

### Post-Despliegue (Testing)
- [ ] Verificar que usuarios DIRECCION ven el botón
- [ ] Verificar que otros usuarios NO ven el botón
- [ ] Probar carga de poison pills (debe mostrar lista vacía si no hay)
- [ ] Probar filtros (Estado y Tabla)
- [ ] Probar reprocesamiento
- [ ] Probar marcar como resuelto
- [ ] Probar marcar como fallo permanente
- [ ] Probar ver detalle
- [ ] Verificar colores en DataGrid
- [ ] Verificar tooltips
- [ ] Verificar busy indicator

---

## 🧪 Tests Recomendados (Ver archivo de tests)

Ver `NestoAPI.Tests/PoisonPillsIntegrationTests.cs` para tests completos.

---

## 📚 Documentación Relacionada

- `SISTEMA_CONTROL_REINTENTOS_PUBSUB.md` - Sistema backend completo
- `FRONTEND_POISON_PILLS_UI.md` - Documentación detallada de la UI
- `FIX_RECARGA_PEDIDO_TRAS_TRASPASO.md` - Bug fix relacionado (CrearFacturaResponseDTO)
- `SESION_2025-01-19_GESTION_ERRORES.md` - Sistema de gestión de errores

---

## 💡 Lecciones Aprendidas

### 1. Database-First EDMX
**Aprendizaje**: Con Database-First, SIEMPRE actualizar EDMX después de crear tabla en SQL.

**Patrón correcto**:
```
1. CREATE TABLE en SQL
2. Update Model from Database en EDMX
3. Crear *.Partial.cs para funcionalidad custom
4. NO crear DbSet manual en NVEntities.Partial.cs
```

### 2. Métodos de Extensión en WPF/Prism
**Aprendizaje**: Los métodos como `ShowError`, `ShowNotification` están en `ControlesUsuario.Dialogs`.

**Siempre incluir**:
```csharp
using ControlesUsuario.Dialogs;
```

### 3. Iconos Vectoriales en WPF
**Aprendizaje**: Se pueden crear iconos vectoriales directamente en XAML con `DrawingImage`.

**Ventajas**:
- No requiere archivos externos
- Escala perfectamente
- Fácil de personalizar colores
- Mejor rendimiento

### 4. Confirmaciones en Prism
**Aprendizaje**: El método correcto es `ShowConfirmationAnswer(titulo, mensaje)` que retorna `bool`.

**NO usar**:
```csharp
var resultado = _dialogService.ShowConfirmation(mensaje); // ❌ No existe
```

**Usar**:
```csharp
bool continuar = _dialogService.ShowConfirmationAnswer(titulo, mensaje); // ✅ Correcto
```

### 5. Padding en WPF
**Aprendizaje**: `StackPanel` no tiene propiedad `Padding`.

**Soluciones**:
- Envolver en `Border` (tiene Padding)
- Usar `Margin` en elementos hijos
- Usar `Grid` con padding

---

## 🔮 Mejoras Futuras Propuestas

### Funcionalidades
1. **Auto-refresh**: Botón o timer para actualizar automáticamente
2. **Exportar a Excel/CSV**: Para análisis offline
3. **Estadísticas**: Dashboard con métricas y gráficos
4. **Búsqueda avanzada**: Por MessageId, EntityId, texto en error
5. **Acciones en lote**: Selección múltiple para reprocesar/resolver
6. **Historial de cambios**: Auditoría de quién cambió qué y cuándo
7. **Notificaciones**: Alert cuando aparecen nuevos poison pills

### Técnicas
1. **Paginación**: Para manejar miles de registros
2. **Ordenamiento**: Permitir ordenar por cualquier columna
3. **Filtros avanzados**: Rango de fechas, número de intentos
4. **Caché**: Para mejorar rendimiento en consultas repetidas
5. **SignalR**: Actualización en tiempo real desde backend

---

## 📞 Soporte

### En caso de problemas:

1. **Tabla no existe**:
   ```sql
   -- Verificar
   SELECT * FROM INFORMATION_SCHEMA.TABLES
   WHERE TABLE_NAME = 'SyncMessageRetries'

   -- Si no existe, ejecutar SCRIPT_SQL_SYNC_MESSAGE_RETRIES.sql
   ```

2. **EDMX desincronizado**:
   - Visual Studio → Abrir NestoEntities.edmx
   - Click derecho → Update Model from Database
   - Agregar tabla SyncMessageRetries

3. **Botón no visible**:
   - Verificar que usuario está en grupo DIRECCION
   - Verificar en DB: `SELECT * FROM AspNetUserRoles ...`

4. **Error 500 al cargar**:
   - Verificar logs en Elmah: `/logs-nestoapi`
   - Verificar conexión a base de datos
   - Verificar permisos de tabla

---

## ✅ Estado Final

**Implementación**: ✅ **Completada al 100%**
**Testing**: ⏳ Pendiente de testing manual en entorno de producción
**Documentación**: ✅ Completa
**Tests unitarios**: ✅ Creados (ver archivo de tests)

---

**Última actualización**: 2025-01-19
**Desarrolladores**: Carlos (con asistencia de Claude Code)
**Versión**: 1.0
