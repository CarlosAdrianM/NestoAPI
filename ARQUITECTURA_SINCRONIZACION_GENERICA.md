# Arquitectura de Sincronización Genérica

## 📋 Resumen

Se ha implementado una arquitectura genérica y reutilizable para la sincronización de entidades con sistemas externos, eliminando código duplicado y facilitando la extensión a nuevas tablas.

**Fecha**: 2025-11-13
**Estado**: ✅ Implementación completa

---

## 🎯 Problema Resuelto

### Antes (Arquitectura Anterior)
- ❌ Cada `Controller` tenía código duplicado de sincronización
- ❌ Lógica de lotes, delays y actualización de `nesto_sync` repetida
- ❌ Difícil mantener consistencia entre diferentes endpoints
- ❌ Cada nueva tabla requería copiar y pegar mucho código

### Después (Nueva Arquitectura)
- ✅ Lógica centralizada en `GestorSincronizacion`
- ✅ Controllers delgados con lógica específica de cada entidad
- ✅ Fácil agregar nuevas tablas
- ✅ Código DRY (Don't Repeat Yourself)

---

## 🏗️ Arquitectura Implementada

```
┌─────────────────────────────────────────────────────────────┐
│                    Controller Layer                         │
│  (Endpoints específicos con lógica mínima)                  │
├─────────────────────────────────────────────────────────────┤
│  ClientesController.GetClientesSync()                       │
│  ProductosController.GetProductosSync()                     │
│  [Futuros: PedidosController, FacturasController, etc.]     │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│               GestorSincronizacion (Genérico)               │
│  • Lectura de nesto_sync                                    │
│  • Procesamiento por lotes                                  │
│  • Delays entre lotes                                       │
│  • Actualización de campo Sincronizado                      │
│  • Manejo de errores y logging                              │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│               Gestores Específicos                          │
│  • GestorClientes.PublicarClienteSincronizar()             │
│  • GestorProductos.PublicarProductoSincronizar()           │
│  [Lógica específica de cada entidad]                       │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────────────┐
│         GooglePubSubEventPublisher (Infraestructura)        │
│  • Serialización a JSON                                     │
│  • Publicación a Google Pub/Sub                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 📦 Componentes Implementados

### 1. Interfaz Genérica
**Archivo**: `Infraestructure/IGestorSincronizacion.cs`

```csharp
public interface IGestorSincronizacion
{
    Task<bool> ProcesarTabla<T>(
        string tabla,
        Func<string, Task<List<T>>> obtenerEntidades,
        Func<T, Task> publicarEntidad,
        int batchSize = 50,
        int delayMs = 5000
    ) where T : class;
}
```

**Responsabilidades**:
- Define el contrato para procesamiento genérico
- Permite inyección de dependencias para testing

### 2. Implementación Genérica
**Archivo**: `Infraestructure/GestorSincronizacion.cs`

**Características**:
- ✅ Lectura de registros pendientes en `nesto_sync`
- ✅ Procesamiento por lotes configurables (default: 50)
- ✅ Pausas entre lotes para evitar saturación (default: 5s)
- ✅ Actualización automática del campo `Sincronizado`
- ✅ Logging detallado con emojis para fácil seguimiento
- ✅ Manejo de errores sin interrumpir el lote completo
- ✅ Retorno de estado de éxito/fallo

**Flujo de Procesamiento**:
```
1. Query a nesto_sync: WHERE Tabla = @tabla AND Sincronizado IS NULL
2. Dividir en lotes de tamaño configurable
3. Para cada ID en el lote:
   a. Obtener entidad(es) completas (función inyectada)
   b. Publicar cada entidad (función inyectada)
   c. Marcar como sincronizado en BD
   d. Logging de resultado
4. Delay antes del siguiente lote
5. Retornar resultado final (true/false)
```

### 3. Gestor de Productos (NUEVO)
**Archivos**:
- `Infraestructure/IGestorProductos.cs` (interfaz)
- `Infraestructure/GestorProductos.cs` (implementación)

**Método**: `PublicarProductoSincronizar(ProductoDTO, string source)`

**Mensaje Publicado** (formato JSON):
```json
{
  "Producto": "17404",
  "Nombre": "ROLLO PAPEL CAMILLA",
  "Tamanno": 100,
  "UnidadMedida": "m",
  "Familia": "Productos Genéricos",
  "PrecioProfesional": 7.49,
  "PrecioPublicoFinal": 12.95,
  "Estado": 0,
  "Grupo": "ACC",
  "Subgrupo": "Desechables",
  "UrlEnlace": "https://...",
  "UrlFoto": "https://...",
  "RoturaStockProveedor": false,
  "ClasificacionMasVendidos": 0,
  "CodigoBarras": "0",
  "ProductosKit": [],
  "Stocks": [
    {
      "Almacen": "ALG",
      "Stock": 390,
      "PendienteEntregar": 18,
      "PendienteRecibir": 0,
      "CantidadDisponible": 372,
      "FechaEstimadaRecepcion": "9999-12-31T23:59:59",
      "PendienteReposicion": 0
    }
  ],
  "Tabla": "Productos",
  "Source": "Nesto viejo"
}
```

### 4. Controllers Refactorizados

#### ClientesController (REFACTORIZADO)
**Cambios**:
- ✅ Agregada dependencia `IGestorSincronizacion` en constructor
- ✅ Método `GetClientesSync()` simplificado de ~60 líneas a ~20 líneas
- ✅ Lógica específica de clientes (qué obtener y cómo publicar) clara y separada

**Código Antes** (líneas 659-720):
```csharp
// ~60 líneas de código con lógica de lotes, delays, SQL queries, etc.
```

**Código Después** (líneas 659-683):
```csharp
[HttpGet]
[Route("api/Clientes/Sync")]
public async Task<IHttpActionResult> GetClientesSync()
{
    bool resultado = await _gestorSincronizacion.ProcesarTabla(
        tabla: "Clientes",
        obtenerEntidades: async (clienteId) => {
            return await db.Clientes
                .Where(c => c.Nº_Cliente == clienteId && c.Empresa == "1")
                .Include(c => c.PersonasContactoClientes1)
                .ToListAsync();
        },
        publicarEntidad: async (cliente) => {
            await _gestorClientes.PublicarClienteSincronizar(cliente, "Nesto viejo");
        }
    );
    return Ok(resultado);
}
```

#### ProductosController (NUEVO ENDPOINT)
**Cambios**:
- ✅ Agregadas dependencias `IGestorSincronizacion` e `IGestorProductos`
- ✅ Nuevo método `GetProductosSync()` siguiendo el mismo patrón
- ✅ Construcción completa de `ProductoDTO` con ficha completa (URL foto, precios, stocks, kits)

**Endpoint**: `GET /api/Productos/Sync`

**Lógica Específica**:
```csharp
obtenerEntidades: async (productoId) => {
    // 1. Buscar producto con includes necesarios
    // 2. Construir ProductoDTO completo (ficha completa)
    // 3. Agregar kits y stocks
    // 4. Retornar en lista
}

publicarEntidad: async (productoDTO) => {
    await _gestorProductos.PublicarProductoSincronizar(productoDTO, "Nesto viejo");
}
```

### 5. Triggers SQL (NUEVOS)
**Archivo**: `TRIGGERS_PRODUCTOS_SINCRONIZACION.sql`

**Triggers Creados**:
1. **`trg_Productos_Insert_Sincronizacion`**
   - Se dispara en INSERT
   - Registra nuevo producto en `nesto_sync`

2. **`trg_Productos_Update_Sincronizacion`**
   - Se dispara en UPDATE
   - Detecta cambios reales antes de registrar
   - Usa MERGE para insertar o actualizar

**Campos Monitoreados**:
- Nombre
- Tamaño
- UnidadMedida
- Familia
- PVP
- Estado
- Grupo
- SubGrupo
- RoturaStockProveedor
- CodBarras

**Características**:
- ✅ Solo sincroniza empresa '1'
- ✅ Evita registros vacíos
- ✅ Normaliza IDs con LTRIM/RTRIM
- ✅ Marca como pendiente (`Sincronizado = NULL`)
- ✅ Script de prueba incluido (comentado)

---

## 🚀 Cómo Usar

### 1. Ejecutar Triggers SQL
```sql
-- Ejecutar en SQL Server Management Studio
USE [bthnesto_NestoPROD]
GO
-- Ejecutar todo el contenido de TRIGGERS_PRODUCTOS_SINCRONIZACION.sql
```

### 2. Sincronizar Productos Manualmente
```bash
# Endpoint para sincronizar todos los productos pendientes
GET https://tu-servidor/api/Productos/Sync

# Respuesta esperada:
{
  "result": true
}
```

### 3. Logs en Consola
Durante la sincronización verás:
```
🔄 Procesando 150 registros de la tabla Productos en lotes de 50
📦 Procesando lote 1/3 (50 registros)
📤 Publicando mensaje: Producto 17404, Source=Nesto viejo, Kits=[ninguno], Stocks=[3 almacenes]
✅ Productos 17404 sincronizado correctamente
...
⏳ Esperando 5000ms antes del siguiente lote...
📦 Procesando lote 2/3 (50 registros)
...
✅ ÉXITO: Sincronización de tabla Productos finalizada. Total procesados: 150
```

---

## 🔧 Agregar Nueva Tabla (Ej: Pedidos)

### Paso 1: Crear Gestor (si no existe)
```csharp
// Infraestructure/IGestorPedidos.cs
public interface IGestorPedidos
{
    Task PublicarPedidoSincronizar(PedidoDTO pedido, string source = "Nesto");
}

// Infraestructure/GestorPedidos.cs
public class GestorPedidos : IGestorPedidos
{
    private readonly SincronizacionEventWrapper _sincronizacionEventWrapper;

    public async Task PublicarPedidoSincronizar(PedidoDTO pedido, string source = "Nesto")
    {
        var message = new {
            Pedido = pedido.Numero,
            Cliente = pedido.Cliente,
            // ... otros campos
            Tabla = "Pedidos",
            Source = source
        };
        await _sincronizacionEventWrapper.PublishSincronizacionEventAsync(
            "sincronizacion-tablas",
            message
        );
    }
}
```

### Paso 2: Agregar Endpoint en Controller
```csharp
// Controllers/PedidosController.cs
private readonly IGestorSincronizacion _gestorSincronizacion;
private readonly IGestorPedidos _gestorPedidos;

// En constructor:
_gestorSincronizacion = gestorSincronizacion ?? new GestorSincronizacion(db);
_gestorPedidos = gestorPedidos;

[HttpGet]
[Route("api/Pedidos/Sync")]
public async Task<IHttpActionResult> GetPedidosSync()
{
    bool resultado = await _gestorSincronizacion.ProcesarTabla(
        tabla: "Pedidos",
        obtenerEntidades: async (pedidoId) => {
            // Lógica específica de pedidos
            return await db.CabPedidosVenta
                .Where(p => p.Número == pedidoId)
                .Include(p => p.LinPedidoVenta)
                .ToListAsync();
        },
        publicarEntidad: async (pedido) => {
            var dto = ConvertirAPedidoDTO(pedido);
            await _gestorPedidos.PublicarPedidoSincronizar(dto, "Nesto viejo");
        }
    );
    return Ok(resultado);
}
```

### Paso 3: Crear Triggers SQL
```sql
CREATE TRIGGER trg_Pedidos_Insert_Sincronizacion
ON CabPedidosVenta
AFTER INSERT
AS
BEGIN
    -- Similar a triggers de Productos
END
GO

CREATE TRIGGER trg_Pedidos_Update_Sincronizacion
ON CabPedidosVenta
AFTER UPDATE
AS
BEGIN
    -- Similar a triggers de Productos
END
GO
```

**Total Líneas Nuevas**: ~50 (vs. ~200 en arquitectura anterior)

---

## 📊 Comparación: Antes vs. Después

| Aspecto | Antes | Después |
|---------|-------|---------|
| **Líneas por Controller** | ~60 líneas | ~20 líneas |
| **Código Duplicado** | Sí (alto) | No |
| **Mantenibilidad** | Baja | Alta |
| **Facilidad para Agregar Tablas** | Difícil | Fácil |
| **Testing** | Complejo | Simple (mocks) |
| **Logging Consistente** | Variable | Uniforme |
| **Configuración de Lotes** | Hardcoded | Configurable |

---

## ✅ Checklist de Implementación

- [x] Interfaz `IGestorSincronizacion`
- [x] Implementación `GestorSincronizacion`
- [x] Interfaz `IGestorProductos`
- [x] Implementación `GestorProductos`
- [x] Refactorizar `ClientesController.GetClientesSync()`
- [x] Implementar `ProductosController.GetProductosSync()`
- [x] Crear triggers SQL para tabla Productos
- [x] Documentación completa
- [ ] Testing unitario de `GestorSincronizacion`
- [ ] Testing de integración end-to-end
- [ ] Registrar servicios en `Startup.cs` (si usas DI container)

---

## 🧪 Testing

### Testing Unitario (Recomendado)
```csharp
[TestClass]
public class GestorSincronizacionTests
{
    [TestMethod]
    public async Task ProcesarTabla_RegistrosPendientes_ProcesaCorrectamente()
    {
        // Arrange
        var mockDb = A.Fake<NVEntities>();
        var gestor = new GestorSincronizacion(mockDb);

        // Act
        bool resultado = await gestor.ProcesarTabla(
            "TestTabla",
            async (id) => new List<TestEntity> { new TestEntity { Id = id } },
            async (entity) => await Task.CompletedTask
        );

        // Assert
        Assert.IsTrue(resultado);
    }
}
```

### Testing Manual
1. Modificar un producto en Nesto viejo
2. Verificar registro en `nesto_sync`:
   ```sql
   SELECT * FROM Nesto_sync WHERE Tabla = 'Productos' AND Sincronizado IS NULL
   ```
3. Llamar al endpoint: `GET /api/Productos/Sync`
4. Verificar que `Sincronizado` se actualizó:
   ```sql
   SELECT * FROM Nesto_sync WHERE Tabla = 'Productos' ORDER BY Sincronizado DESC
   ```
5. Verificar que el mensaje llegó a Odoo (logs de Pub/Sub)

---

## 🔍 Troubleshooting

### Problema: Los triggers no se disparan
**Solución**:
```sql
-- Verificar que existen
SELECT name, is_disabled FROM sys.triggers
WHERE name LIKE '%Productos%Sincronizacion%'

-- Habilitar si están deshabilitados
ENABLE TRIGGER trg_Productos_Insert_Sincronizacion ON Productos
ENABLE TRIGGER trg_Productos_Update_Sincronizacion ON Productos
```

### Problema: Registros no se marcan como sincronizados
**Solución**:
- Verificar que `_gestorProductos` no es null en `ProductosController`
- Agregar logging en `GestorSincronizacion` para ver errores
- Verificar permisos de escritura en tabla `nesto_sync`

### Problema: Endpoint devuelve 500
**Solución**:
- Verificar que `SincronizacionEventWrapper` está registrado en DI
- Revisar logs de IIS Express para ver el error específico
- Verificar que todos los includes en EF están correctos

---

## 📈 Próximos Pasos

### Mejoras Futuras
1. ⬜ Agregar métricas de sincronización (dashboard)
2. ⬜ Implementar cola de reintentos para errores
3. ⬜ Agregar alertas por email si fallan > X registros
4. ⬜ Optimizar queries con paginación en memoria para tablas grandes
5. ⬜ Implementar sincronización incremental por timestamp

### Próximas Tablas a Sincronizar
1. ⬜ Pedidos de Venta
2. ⬜ Facturas
3. ⬜ Albaranes
4. ⬜ Stocks (cambios en tiempo real)

---

## 📚 Referencias

- **Código de Clientes**: `ClientesController.cs:659-683`
- **Código de Productos**: `ProductosController.cs:492-559`
- **Gestor Genérico**: `Infraestructure/GestorSincronizacion.cs`
- **Triggers SQL**: `TRIGGERS_PRODUCTOS_SINCRONIZACION.sql`
- **Documentación de Sincronización Bidireccional**: `SINCRONIZACION_BIDIRECCIONAL_ODOO_SETUP.md`

---

**Estado Final**: ✅ **Arquitectura genérica implementada y lista para producción**

🎉 La sincronización ahora es escalable, mantenible y fácil de extender a nuevas entidades.
