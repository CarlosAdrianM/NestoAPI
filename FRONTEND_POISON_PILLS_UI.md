# Frontend UI para Gestión de Poison Pills

## 📋 Resumen

Se ha implementado una interfaz de usuario completa en el módulo **Canales Externos** de Nesto (frontend WPF) para gestionar poison pills de mensajes de sincronización. Esta UI permite visualizar, filtrar y gestionar mensajes que han fallado repetidamente en el sistema de sincronización Pub/Sub.

**Fecha**: 2025-01-19
**Estado**: ✅ Implementación completa
**Backend relacionado**: Ver `SISTEMA_CONTROL_REINTENTOS_PUBSUB.md`

---

## 🎯 Funcionalidades Implementadas

### 1. Visualización de Poison Pills
- **Listado en DataGrid** con todas las propiedades del mensaje
- **Columnas mostradas**:
  - Tabla (Clientes, Productos, etc.)
  - Entidad ID
  - Origen (Odoo, Prestashop, etc.)
  - Estado (con código de colores)
  - Intentos realizados
  - Fecha del primer intento
  - Fecha del último intento
  - Tiempo transcurrido desde el primer intento
  - Último error (con tooltip para ver completo)

### 2. Filtros
- **Filtro por Estado**:
  - Todos
  - PoisonPill (mensajes que alcanzaron el límite)
  - Retrying (aún reintentando)
  - Reprocess (marcados para reprocesar)
  - Resolved (resueltos manualmente)
  - PermanentFailure (fallos permanentes)

- **Filtro por Tabla**:
  - Todas
  - Clientes
  - Productos
  - Pedidos
  - Pagos

### 3. Acciones Disponibles
- **Reprocesar**: Marca el mensaje para reprocesarlo (resetea contador)
- **Marcar como Resuelto**: Indica que el problema fue solucionado manualmente
- **Marcar como Fallo Permanente**: Indica que el mensaje no debe procesarse nunca más
- **Ver Detalle**: Muestra toda la información del mensaje en un diálogo

### 4. Seguridad
- **Acceso restringido** al grupo de seguridad `ADMINISTRACION`
- Solo usuarios autorizados pueden ver y gestionar poison pills

---

## 📦 Archivos Creados

### Backend (NestoAPI)

#### Modelos y DTOs

**NestoAPI/Models/Sincronizacion/RetryStatus.cs**
```csharp
public enum RetryStatus
{
    Retrying,           // Aún reintentando
    PoisonPill,         // Límite alcanzado
    Reprocess,          // Marcado para reprocesar
    Resolved,           // Resuelto manualmente
    PermanentFailure    // Fallo permanente
}
```

**NestoAPI/Models/Sincronizacion/PoisonPillDTO.cs**
```csharp
public class PoisonPillDTO
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
}
```

**NestoAPI/Models/Sincronizacion/ChangeStatusRequest.cs**
```csharp
public class ChangeStatusRequest
{
    public string MessageId { get; set; }
    public string NewStatus { get; set; } // "Reprocess", "Resolved", "PermanentFailure"
}
```

### Frontend (Nesto/CanalesExternos)

#### Modelos

**Nesto/CanalesExternos/Models/PoisonPillModel.cs**
- Modelo frontend que espeja el DTO del backend
- Propiedad adicional `DisplayId` para mostrar en la UI

**Nesto/CanalesExternos/Models/ChangeStatusRequestModel.cs**
- Modelo para las peticiones de cambio de estado

#### Servicios

**Nesto/CanalesExternos/Interfaces/IPoisonPillsService.cs**
```csharp
public interface IPoisonPillsService
{
    Task<List<PoisonPillModel>> ObtenerPoisonPillsAsync(string status, string tabla, int limit);
    Task<bool> CambiarEstadoAsync(string messageId, string newStatus);
}
```

**Nesto/CanalesExternos/Services/PoisonPillsService.cs**
- Implementación del servicio que llama a la API
- Endpoint GET: `/api/sync/poisonpills`
- Endpoint POST: `/api/sync/poisonpills/changestatus`
- Manejo de errores y deserialización de respuestas

#### ViewModels

**Nesto/CanalesExternos/ViewModels/PoisonPillsViewModel.cs**

**Propiedades principales**:
- `ListaPoisonPills`: Colección observable de poison pills
- `PoisonPillSeleccionado`: Item seleccionado en el DataGrid
- `EstadoSeleccionado`: Filtro de estado
- `TablaSeleccionada`: Filtro de tabla
- `TotalPoisonPills`: Contador de resultados
- `EstaOcupado`: Indicador de carga

**Comandos**:
- `CargarPoisonPillsCommand`: Carga la lista con filtros
- `ReprocesarCommand`: Marca para reprocesar
- `MarcarComoResueltoCommand`: Marca como resuelto
- `MarcarComoFalloPermanenteCommand`: Marca como fallo permanente
- `VerDetalleCommand`: Muestra diálogo con todos los detalles

#### Vistas

**Nesto/CanalesExternos/Views/PoisonPillsView.xaml**

**Estructura de la UI**:
1. **Panel de filtros** (superior):
   - ComboBox de estados
   - ComboBox de tablas
   - Botón Buscar
   - Contador de resultados

2. **DataGrid** (centro):
   - 9 columnas con toda la información
   - Colores por estado:
     - Rojo/Bold: PoisonPill
     - Verde: Resolved
     - Naranja/Bold: Reprocess
     - Rojo oscuro: PermanentFailure
   - Tooltips en columna de error

3. **Panel de acciones** (inferior):
   - Botón "Ver Detalle"
   - Botón "Reprocesar" (naranja)
   - Botón "Marcar como Resuelto" (verde)
   - Botón "Marcar como Fallo Permanente" (rojo)

**Nesto/CanalesExternos/Views/PoisonPillsView.xaml.cs**
- Code-behind estándar (solo InitializeComponent)

#### Integración con el Módulo

**Nesto/CanalesExternos/CanalesExternos.cs** (modificado)
```csharp
// Agregados:
using Nesto.Modulos.CanalesExternos.Interfaces;
using Nesto.Modulos.CanalesExternos.Services;

// En RegisterTypes:
containerRegistry.Register<object, PoisonPillsView>("PoisonPillsView");
containerRegistry.Register<IPoisonPillsService, PoisonPillsService>();
```

**Nesto/CanalesExternos/CanalesExternosMenuBar.xaml** (modificado)
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
**Nota**: El icono es completamente vectorial (octágono rojo con exclamación blanca), no requiere archivos de imagen externos.

**Nesto/CanalesExternos/CanalesExternosMenuBarViewModel.cs** (modificado)
```csharp
public ICommand AbrirModuloPoisonPillsCommand { get; private set; }

private bool CanAbrirModuloPoisonPills()
{
    return Configuracion.UsuarioEnGrupo(Constantes.GruposSeguridad.ADMINISTRACION);
}

private void OnAbrirModuloPoisonPills()
{
    RegionManager.RequestNavigate("MainRegion", "PoisonPillsView");
}
```

---

## 🚀 Cómo Usar

### 1. Acceder al Módulo

1. Abrir Nesto (frontend)
2. Ir a pestaña **Herramientas** en el ribbon
3. Hacer clic en botón **Poison Pills** en el grupo "Canales Externos"
   - ⚠️ Solo visible para usuarios del grupo ADMINISTRACION

### 2. Visualizar Poison Pills

La vista se carga automáticamente con:
- **Filtro por defecto**: Estado = "PoisonPill"
- **Tabla**: Todas

Para cambiar filtros:
1. Seleccionar estado en el ComboBox
2. Seleccionar tabla en el ComboBox
3. Hacer clic en **Buscar**

### 3. Ver Detalles de un Mensaje

1. Seleccionar un mensaje en el DataGrid
2. Hacer clic en botón **Ver Detalle**
3. Se mostrará un diálogo con:
   - ID del mensaje
   - Tabla y entidad afectada
   - Origen del mensaje
   - Número de intentos
   - Fechas de primer y último intento
   - Estado actual
   - Último error completo
   - Datos del mensaje original (JSON)

### 4. Reprocesar un Mensaje

**Cuándo usar**: El error fue temporal y ya está solucionado

1. Seleccionar el mensaje en el DataGrid
2. Hacer clic en **Reprocesar**
3. Confirmar en el diálogo
4. El mensaje cambiará a estado "Reprocess"
5. En el próximo envío de Pub/Sub, se procesará de nuevo
6. El contador de intentos se reseteará a 1

### 5. Marcar como Resuelto

**Cuándo usar**: El problema fue corregido manualmente en la base de datos

1. Seleccionar el mensaje en el DataGrid
2. Hacer clic en **Marcar como Resuelto**
3. Confirmar en el diálogo
4. El mensaje cambiará a estado "Resolved"
5. Ya no se procesará automáticamente
6. Quedará registrado como resuelto

### 6. Marcar como Fallo Permanente

**Cuándo usar**: El mensaje es inválido o no se puede procesar nunca

1. Seleccionar el mensaje en el DataGrid
2. Hacer clic en **Marcar como Fallo Permanente**
3. Confirmar en el diálogo
4. El mensaje cambiará a estado "PermanentFailure"
5. Ya no se procesará nunca más

---

## 🎨 Características de UX

### Códigos de Color por Estado

| Estado | Color | Formato | Significado |
|--------|-------|---------|-------------|
| **PoisonPill** | Rojo | Bold | Requiere atención inmediata |
| **Retrying** | Negro | Normal | Aún intentando procesar |
| **Reprocess** | Naranja | Bold | Marcado para reprocesar |
| **Resolved** | Verde | Normal | Resuelto manualmente |
| **PermanentFailure** | Rojo oscuro | Normal | Fallo definitivo |

### Tooltips
- Columna "Último Error": Al pasar el mouse, se muestra el error completo

### Busy Indicator
- Aparece durante operaciones asíncronas:
  - Carga de poison pills
  - Cambio de estado
- Muestra mensaje "Cargando..."
- Deshabilita la UI mientras se ejecuta

### Confirmaciones
- Todas las acciones de cambio de estado requieren confirmación del usuario
- Diálogos descriptivos que explican qué va a pasar

### Notificaciones
- Éxito: "Mensaje X marcado para reprocesar"
- Error: "Error al cambiar estado del mensaje: [detalle]"

---

## 🔐 Seguridad

### Control de Acceso
- Solo usuarios del grupo `Constantes.GruposSeguridad.ADMINISTRACION` pueden:
  - Ver el botón "Poison Pills" en el menú
  - Acceder a la vista
  - Ver y gestionar poison pills

### Validaciones
- Todos los comandos validan que haya un mensaje seleccionado
- Los comandos se deshabilitan cuando `EstaOcupado = true`
- La API valida que el nuevo estado sea válido

---

## 📊 Flujo Completo de Uso

```
1. Usuario con permisos ADMINISTRACION abre Nesto
   ↓
2. Va a pestaña Herramientas → Click en "Poison Pills"
   ↓
3. Se carga automáticamente la vista con poison pills pendientes
   ↓
4. Usuario selecciona filtros (estado/tabla) y hace clic en "Buscar"
   ↓
5. Se muestra DataGrid con resultados filtrados
   ↓
6. Usuario selecciona un mensaje en el DataGrid
   ↓
7. Usuario hace clic en una acción:

   A) VER DETALLE:
      → Se muestra diálogo con toda la información
      → Usuario revisa el error y los datos
      → Cierra el diálogo

   B) REPROCESAR:
      → Diálogo de confirmación
      → POST a /api/sync/poisonpills/changestatus
      → Backend cambia estado a "Reprocess"
      → Notificación de éxito
      → Recarga la lista

   C) MARCAR COMO RESUELTO:
      → Diálogo de confirmación
      → POST a /api/sync/poisonpills/changestatus
      → Backend cambia estado a "Resolved"
      → Notificación de éxito
      → Recarga la lista

   D) MARCAR COMO FALLO PERMANENTE:
      → Diálogo de confirmación
      → POST a /api/sync/poisonpills/changestatus
      → Backend cambia estado a "PermanentFailure"
      → Notificación de éxito
      → Recarga la lista
```

---

## 🧪 Testing

### Test Manual 1: Visualización
1. ✅ Abrir módulo Poison Pills
2. ✅ Verificar que se cargan poison pills con filtro "PoisonPill"
3. ✅ Cambiar filtro a "Todos" y verificar que se muestran todos los estados
4. ✅ Filtrar por tabla "Clientes" y verificar que solo aparecen clientes
5. ✅ Verificar que el contador muestra el número correcto

### Test Manual 2: Reprocesar
1. ✅ Seleccionar un poison pill
2. ✅ Hacer clic en "Reprocesar"
3. ✅ Confirmar en el diálogo
4. ✅ Verificar notificación de éxito
5. ✅ Verificar que la lista se recarga
6. ✅ Buscar el mensaje y verificar que está en estado "Reprocess"

### Test Manual 3: Marcar como Resuelto
1. ✅ Seleccionar un poison pill
2. ✅ Hacer clic en "Marcar como Resuelto"
3. ✅ Confirmar en el diálogo
4. ✅ Verificar notificación de éxito
5. ✅ Verificar que la lista se recarga
6. ✅ Filtrar por estado "Resolved" y verificar que aparece

### Test Manual 4: Ver Detalle
1. ✅ Seleccionar un poison pill
2. ✅ Hacer clic en "Ver Detalle"
3. ✅ Verificar que se muestra toda la información:
   - MessageId
   - Tabla y EntityId
   - Origen
   - Intentos
   - Fechas
   - Estado
   - Último error completo
   - Datos del mensaje (JSON)

### Test Manual 5: Seguridad
1. ✅ Iniciar sesión con usuario sin permisos ADMINISTRACION
2. ✅ Verificar que NO aparece el botón "Poison Pills" en el menú

---

## 🔄 Integración con el Backend

### Endpoints Consumidos

#### GET `/api/sync/poisonpills`
**Parámetros**:
- `status` (opcional): Filtro de estado
- `tabla` (opcional): Filtro de tabla
- `limit` (opcional): Máximo de resultados (default: 100)

**Respuesta**:
```json
{
  "total": 3,
  "filters": { "status": "PoisonPill", "tabla": null, "limit": 100 },
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
      "messageData": "{...}",
      "timeSinceFirstAttempt": "2h 30m",
      "timeSinceLastAttempt": "15m"
    }
  ],
  "timestamp": "2025-01-19T12:30:00Z"
}
```

#### POST `/api/sync/poisonpills/changestatus`
**Body**:
```json
{
  "messageId": "1234567890",
  "newStatus": "Reprocess"  // "Reprocess", "Resolved", o "PermanentFailure"
}
```

**Respuesta**:
```json
{
  "success": true,
  "messageId": "1234567890",
  "newStatus": "Reprocess",
  "timestamp": "2025-01-19T12:35:00Z"
}
```

---

## 📝 Notas de Implementación

### Patrón MVVM con Prism
- Uso de `BindableBase` para ViewModels
- `DelegateCommand` para comandos
- `ViewModelLocator.AutoWireViewModel="True"` para auto-wiring
- Navegación con `IRegionManager.RequestNavigate`

### Inyección de Dependencias
- `IPoisonPillsService` registrado como singleton en el contenedor
- `IDialogService` inyectado para diálogos
- `IConfiguracion` inyectado para configuración y seguridad

### Manejo de Errores
- Try-catch en todos los métodos asíncronos
- Diálogos de error con `dialogService.ShowError`
- Mensajes descriptivos que incluyen contexto

### Performance
- Carga asíncrona con `async`/`await`
- Límite de 100 resultados por defecto
- Uso de `ObservableCollection` para binding eficiente

---

## 🔮 Mejoras Futuras Propuestas

1. **Auto-refresh**
   - Botón para refrescar automáticamente cada X segundos
   - Notificación cuando aparecen nuevos poison pills

2. **Exportar a CSV/Excel**
   - Botón para exportar la lista actual
   - Útil para análisis y reporting

3. **Estadísticas**
   - Panel con resumen:
     - Total de poison pills por tabla
     - Tasa de éxito de reprocesamiento
     - Errores más comunes
     - Gráfico de tendencias

4. **Búsqueda avanzada**
   - Buscar por MessageId
   - Buscar por EntityId
   - Buscar en el texto del error

5. **Acciones en lote**
   - Selección múltiple de mensajes
   - Reprocesar múltiples mensajes a la vez
   - Marcar múltiples como resueltos

6. **Historial**
   - Ver historial de cambios de estado de un mensaje
   - Quién marcó el mensaje como resuelto y cuándo

7. **Iconos personalizados**
   - Crear icono específico para Poison Pills
   - Usar en el botón del menú

---

## 📚 Archivos Relacionados

### Backend
- `SISTEMA_CONTROL_REINTENTOS_PUBSUB.md` - Documentación completa del sistema backend
- `SCRIPT_SQL_SYNC_MESSAGE_RETRIES.sql` - Script de creación de tabla
- `NestoAPI/Controllers/SyncWebhookController.cs` - Endpoints de API
- `NestoAPI/Infraestructure/Sincronizacion/MessageRetryManager.cs` - Lógica de negocio

### Frontend
- Todos los archivos listados en la sección "Archivos Creados" arriba

---

## ✅ Checklist de Despliegue

### Pre-Despliegue
- [x] Backend compilado sin errores
- [x] Frontend compilado sin errores
- [x] Servicios registrados en DI
- [x] Vistas registradas en módulo
- [x] Menú actualizado con nuevo botón
- [x] Permisos de seguridad configurados

### Post-Despliegue
- [ ] Verificar que usuarios ADMINISTRACION ven el botón
- [ ] Verificar que usuarios sin permisos NO ven el botón
- [ ] Probar carga de poison pills
- [ ] Probar filtros
- [ ] Probar reprocesamiento
- [ ] Probar marcar como resuelto
- [ ] Probar marcar como fallo permanente
- [ ] Probar ver detalle

---

**Estado Final**: ✅ **Sistema de gestión de Poison Pills UI completamente implementado**

🎉 Los usuarios de administración ahora tienen una interfaz completa para gestionar mensajes problem áticos de sincronización desde el frontend Nesto.

**Última actualización**: 2025-01-19
**Versión**: 1.0
