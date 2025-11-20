# Refactorización: Sistema de Mensajes Tipados para Sincronización

**Fecha:** 2025-01-20
**Autor:** Claude Code
**Objetivo:** Eliminar la clase plana `ExternalSyncMessageDTO` que mezclaba campos de diferentes entidades

## Problema Identificado

La clase `ExternalSyncMessageDTO` era una estructura plana que contenía **todos** los campos de **todas** las entidades sincronizadas (Clientes, Productos, etc.), lo que generaba:

### JSON con campos nulos innecesarios en poison pills:
```json
{
  "Tabla": "Productos",
  "Source": "Nesto viejo",
  "Usuario": "NUEVAVISION\\Administrador",
  "Nif": null,                    // ❌ Campo de Cliente
  "Cliente": null,                // ❌ Campo de Cliente
  "Contacto": null,               // ❌ Campo de Cliente
  "ClientePrincipal": false,      // ❌ Campo de Cliente
  "Direccion": null,              // ❌ Campo de Cliente
  "Producto": "42117",            // ✅ Campo de Producto
  "Nombre": "SUN PROTECTION...",  // ✅ Campo de Producto
  "PrecioProfesional": 17.38,     // ✅ Campo de Producto
  ...
}
```

## Solución Implementada

### Nueva Jerarquía de Tipos

```
SyncMessageBase (abstracta)
├── Tabla: string
├── Source: string
└── Usuario: string
    │
    ├─── ClienteSyncMessage
    │    ├── Nif, Cliente, Contacto
    │    ├── Nombre, Direccion, Poblacion
    │    └── PersonasContacto[]
    │
    └─── ProductoSyncMessage
         ├── Producto, Nombre
         ├── PrecioProfesional, Estado
         ├── ProductosKit[]
         └── Stocks[]
```

### Archivos Creados (3 nuevos)

1. **`Models/Sincronizacion/SyncMessageBase.cs`**
   - Clase base abstracta con campos comunes
   - Propiedades: `Tabla`, `Source`, `Usuario`

2. **`Models/Sincronizacion/ClienteSyncMessage.cs`**
   - Mensaje específico para clientes
   - 14 propiedades específicas de clientes
   - Lista de `PersonasContacto`

3. **`Models/Sincronizacion/ProductoSyncMessage.cs`**
   - Mensaje específico para productos
   - 18 propiedades específicas de productos
   - Conversión de `ProductosKit` a `List<string>`
   - Conversión de `Stocks` a `List<ProductoDTO.StockProducto>`

### Interfaces Refactorizadas

**`Infraestructure/Sincronizacion/ISyncTableHandler.cs`**

Doble interfaz para permitir polimorfismo y tipado fuerte:

```csharp
// Interfaz base no genérica (para colecciones)
public interface ISyncTableHandlerBase
{
    string TableName { get; }
    Task<bool> HandleAsync(SyncMessageBase message);
    string GetMessageKey(SyncMessageBase message);
    string GetLogInfo(SyncMessageBase message);
}

// Interfaz genérica (para implementaciones tipadas)
public interface ISyncTableHandler<TMessage> : ISyncTableHandlerBase
    where TMessage : SyncMessageBase
{
    Task<bool> HandleAsync(TMessage message);
    string GetMessageKey(TMessage message);
    string GetLogInfo(TMessage message);
}
```

### Handlers Actualizados (2 archivos)

**`ClientesSyncHandler.cs`** y **`ProductosSyncHandler.cs`**:
- Implementan `ISyncTableHandler<ClienteSyncMessage>` / `ISyncTableHandler<ProductoSyncMessage>`
- Implementación explícita de métodos base (polimórficos)
- Implementación pública de métodos tipados

```csharp
public class ClientesSyncHandler : ISyncTableHandler<ClienteSyncMessage>
{
    // Implementación base polimórfica
    Task<bool> ISyncTableHandlerBase.HandleAsync(SyncMessageBase message)
    {
        return HandleAsync(message as ClienteSyncMessage);
    }

    // Implementación tipada
    public async Task<bool> HandleAsync(ClienteSyncMessage message)
    {
        // ... lógica específica
    }
}
```

### Gestores Actualizados (2 archivos)

**`GestorClientes.cs:1427`**
```csharp
var message = new ClienteSyncMessage
{
    Tabla = "Clientes",
    Source = source,
    Usuario = usuario,
    Nif = cliente.CIF_NIF?.Trim(),
    Cliente = cliente.Nº_Cliente?.Trim(),
    // ... solo campos de cliente
};
```

**`GestorProductos.cs:41`**
```csharp
var message = new ProductoSyncMessage
{
    Tabla = "Productos",
    Source = source,
    Usuario = usuario,
    Producto = productoDTO.Producto?.Trim(),
    // ... solo campos de producto
    ProductosKit = productoDTO.ProductosKit?.Select(k => k.ProductoId).ToList(),
    Stocks = productoDTO.Stocks?.ToList()
};
```

### Router y Controller Actualizados

**`SyncTableRouter.cs:15`**
- Usa `ISyncTableHandlerBase` para almacenar handlers de diferentes tipos
- Método `RouteAsync` acepta `SyncMessageBase`

**`SyncWebhookController.cs:55`**
- Nuevo método `DeserializeSyncMessage()` que deserializa al tipo correcto según el campo "Tabla":

```csharp
private SyncMessageBase DeserializeSyncMessage(string messageJson)
{
    using (var document = JsonDocument.Parse(messageJson))
    {
        string tabla = document.RootElement.GetProperty("Tabla").GetString();

        switch (tabla?.ToUpperInvariant())
        {
            case "CLIENTES":
                return JsonSerializer.Deserialize<ClienteSyncMessage>(messageJson);
            case "PRODUCTOS":
                return JsonSerializer.Deserialize<ProductoSyncMessage>(messageJson);
            default:
                return null;
        }
    }
}
```

### Change Detectors Actualizados (2 archivos)

**`ClienteChangeDetector.cs:21`** y **`ProductoChangeDetector.cs:15`**:
- Métodos `DetectarCambios()` ahora usan tipos específicos
- `ClienteSyncMessage` y `ProductoSyncMessage` respectivamente

### MessageRetryManager Actualizado

**`MessageRetryManager.cs`**:
- `RecordAttempt()` usa `SyncMessageBase`
- `GetEntityId()` usa pattern matching:

```csharp
private string GetEntityId(SyncMessageBase message)
{
    switch (message)
    {
        case ClienteSyncMessage clienteMsg:
            return $"{clienteMsg.Cliente}-{clienteMsg.Contacto}";
        case ProductoSyncMessage productoMsg:
            return productoMsg.Producto;
        default:
            return "Unknown";
    }
}
```

### Startup.cs (Inyección de dependencias)

**Líneas 161-166**:
```csharp
// Usar ISyncTableHandlerBase (no genérica) para DI
services.AddSingleton<ISyncTableHandlerBase, ClientesSyncHandler>();
services.AddSingleton<ISyncTableHandlerBase, ProductosSyncHandler>();
services.AddSingleton<SyncTableRouter>(sp =>
{
    var handlers = sp.GetServices<ISyncTableHandlerBase>();
    return new SyncTableRouter(handlers);
});
```

### Tests Actualizados

**`ClienteChangeDetectorTests.cs`**:
- Todas las instancias de `ExternalSyncMessageDTO` reemplazadas por `ClienteSyncMessage`

### Clase Obsoleta Marcada

**`ExternalSyncMessageDTO.cs:10`**:
```csharp
[Obsolete("Usar ClienteSyncMessage o ProductoSyncMessage en su lugar. " +
          "Esta clase mezclaba campos de diferentes entidades.")]
public class ExternalSyncMessageDTO
```

## Resultado Final

### Antes (ExternalSyncMessageDTO)
```json
{
  "Tabla": "Productos",
  "Source": "Nesto viejo",
  "Usuario": "NUEVAVISION\\Administrador",
  "Nif": null,
  "Cliente": null,
  "Contacto": null,
  "ClientePrincipal": false,
  "Nombre": "SUN PROTECTION MAKE-UP SPF-50 (SP-03) ARENA",
  "Direccion": null,
  "CodigoPostal": null,
  "Poblacion": null,
  "Provincia": null,
  "Telefono": null,
  "Comentarios": null,
  "Vendedor": null,
  "Estado": -1,
  "PersonasContacto": null,
  "Producto": "42117",
  "PrecioProfesional": 17.3800,
  "PrecioPublicoFinal": 28.75,
  "CodigoBarras": "8435167516781",
  ...
}
```

### Después (ProductoSyncMessage)
```json
{
  "Tabla": "Productos",
  "Source": "Nesto viejo",
  "Usuario": "NUEVAVISION\\Administrador",
  "Producto": "42117",
  "Nombre": "SUN PROTECTION MAKE-UP SPF-50 (SP-03) ARENA",
  "PrecioProfesional": 17.3800,
  "PrecioPublicoFinal": 28.75,
  "CodigoBarras": "8435167516781",
  "RoturaStockProveedor": false,
  "Estado": -1,
  "Familia": "Cazcarra - Ten Image Professional",
  "Grupo": "COS",
  "Subgrupo": "Maquillaje",
  "UrlFoto": "https://...",
  "UrlEnlace": "https://..."
}
```

## Beneficios

1. ✅ **JSON más limpios**: Solo campos relevantes para cada entidad
2. ✅ **Type safety**: Compilador detecta errores de tipos
3. ✅ **Mejor mantenibilidad**: Cada mensaje tiene su responsabilidad clara
4. ✅ **Extensibilidad**: Agregar nuevas entidades es más sencillo
5. ✅ **Poison pills más legibles**: Debugging más fácil
6. ✅ **Reducción de tráfico**: JSON más pequeños en Pub/Sub

## Archivos Modificados

### Nuevos (3)
- `Models/Sincronizacion/SyncMessageBase.cs`
- `Models/Sincronizacion/ClienteSyncMessage.cs`
- `Models/Sincronizacion/ProductoSyncMessage.cs`

### Modificados (12)
1. `Infraestructure/GestorClientes.cs`
2. `Infraestructure/GestorProductos.cs`
3. `Infraestructure/Sincronizacion/ISyncTableHandler.cs`
4. `Infraestructure/Sincronizacion/ClientesSyncHandler.cs`
5. `Infraestructure/Sincronizacion/ProductosSyncHandler.cs`
6. `Infraestructure/Sincronizacion/ClienteChangeDetector.cs`
7. `Infraestructure/Sincronizacion/ProductoChangeDetector.cs`
8. `Infraestructure/Sincronizacion/SyncTableRouter.cs`
9. `Infraestructure/Sincronizacion/MessageRetryManager.cs`
10. `Controllers/SyncWebhookController.cs`
11. `Startup.cs`
12. `NestoAPI.csproj`

### Tests Modificados (1)
- `NestoAPI.Tests/Infrastructure/ClienteChangeDetectorTests.cs`

### Marcados como Obsoletos (1)
- `Models/Sincronizacion/ExternalSyncMessageDTO.cs`

## Migración para Nuevas Entidades

Para agregar una nueva entidad (ej: "Proveedores"):

1. **Crear mensaje específico**:
```csharp
public class ProveedorSyncMessage : SyncMessageBase
{
    public string Proveedor { get; set; }
    public string Nombre { get; set; }
    // ... campos específicos
}
```

2. **Crear handler**:
```csharp
public class ProveedoresSyncHandler : ISyncTableHandler<ProveedorSyncMessage>
{
    // ... implementación
}
```

3. **Actualizar SyncWebhookController**:
```csharp
case "PROVEEDORES":
    return JsonSerializer.Deserialize<ProveedorSyncMessage>(messageJson);
```

4. **Registrar en Startup.cs**:
```csharp
services.AddSingleton<ISyncTableHandlerBase, ProveedoresSyncHandler>();
```

## Compatibilidad hacia atrás

La clase `ExternalSyncMessageDTO` se mantiene marcada como `[Obsolete]` para compatibilidad temporal, pero **no debe usarse** en código nuevo.

## Testing

Todos los tests existentes han sido actualizados para usar los nuevos tipos.
Se recomienda agregar tests para:
- Deserialización de mensajes según el campo "Tabla"
- Conversión de ProductosKit a List<string>
- Conversión de Stocks a List
- Pattern matching en GetEntityId

## Notas Importantes

- Los mensajes ahora se deserializan al tipo correcto en `SyncWebhookController.DeserializeSyncMessage()`
- La inyección de dependencias usa `ISyncTableHandlerBase` (interfaz no genérica)
- Los handlers implementan tanto la interfaz base como la genérica
- Los change detectors usan tipos específicos para mayor seguridad
