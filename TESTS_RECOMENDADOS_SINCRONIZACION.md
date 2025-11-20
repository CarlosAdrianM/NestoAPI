# Tests Recomendados - Refactorización Sincronización

## Tests Existentes

✅ **ClienteChangeDetectorTests.cs** - Ya actualizado con `ClienteSyncMessage`

## Tests Faltantes Recomendados

### 1. ProductoChangeDetectorTests.cs (ALTA PRIORIDAD)

Similar a `ClienteChangeDetectorTests`, debería probar:

```csharp
[TestClass]
public class ProductoChangeDetectorTests
{
    private ProductoChangeDetector _detector;

    [TestInitialize]
    public void Setup()
    {
        _detector = new ProductoChangeDetector();
    }

    [TestMethod]
    public void DetectarCambios_ProductoNulo_RetornaProductoNuevo()
    {
        // Arrange
        Producto productoNesto = null;
        var productoExterno = new ProductoSyncMessage
        {
            Nombre = "Nuevo Producto"
        };

        // Act
        var cambios = _detector.DetectarCambios(productoNesto, productoExterno);

        // Assert
        Assert.IsTrue(cambios.Contains("Producto no existe en Nesto"));
    }

    [TestMethod]
    public void DetectarCambios_SinCambios_RetornaListaVacia()
    {
        // Arrange
        var productoNesto = new Producto
        {
            Nombre = "Test Producto",
            PVP = 10.50m,
            Estado = 0
        };

        var productoExterno = new ProductoSyncMessage
        {
            Nombre = "Test Producto",
            PrecioProfesional = 10.50m,
            Estado = 0
        };

        // Act
        var cambios = _detector.DetectarCambios(productoNesto, productoExterno);

        // Assert
        Assert.AreEqual(0, cambios.Count);
    }

    [TestMethod]
    public void DetectarCambios_PrecioModificado_DetectaCambio()
    {
        // Arrange
        var productoNesto = new Producto
        {
            Nombre = "Test Producto",
            PVP = 10.50m
        };

        var productoExterno = new ProductoSyncMessage
        {
            Nombre = "Test Producto",
            PrecioProfesional = 12.00m
        };

        // Act
        var cambios = _detector.DetectarCambios(productoNesto, productoExterno);

        // Assert
        Assert.AreEqual(1, cambios.Count);
        Assert.IsTrue(cambios.First().Contains("PVP"));
    }
}
```

### 2. SyncWebhookControllerTests.cs (ALTA PRIORIDAD)

Probar la deserialización correcta según el campo "Tabla":

```csharp
[TestClass]
public class SyncWebhookControllerTests
{
    [TestMethod]
    public void DeserializeSyncMessage_TablaClientes_RetornaClienteSyncMessage()
    {
        // Arrange
        var json = @"{
            ""Tabla"": ""Clientes"",
            ""Source"": ""Odoo"",
            ""Usuario"": ""test"",
            ""Cliente"": ""12345"",
            ""Contacto"": ""0"",
            ""Nombre"": ""Test Cliente""
        }";

        // Act
        var controller = new SyncWebhookController(null, null);
        var result = controller.DeserializeSyncMessage(json); // Necesitarías hacer público el método o usar reflection

        // Assert
        Assert.IsInstanceOfType(result, typeof(ClienteSyncMessage));
        var clienteMsg = result as ClienteSyncMessage;
        Assert.AreEqual("Clientes", clienteMsg.Tabla);
        Assert.AreEqual("12345", clienteMsg.Cliente);
    }

    [TestMethod]
    public void DeserializeSyncMessage_TablaProductos_RetornaProductoSyncMessage()
    {
        // Arrange
        var json = @"{
            ""Tabla"": ""Productos"",
            ""Source"": ""Odoo"",
            ""Usuario"": ""test"",
            ""Producto"": ""42117"",
            ""Nombre"": ""Test Producto"",
            ""PrecioProfesional"": 17.38
        }";

        // Act
        var result = DeserializeSyncMessage(json);

        // Assert
        Assert.IsInstanceOfType(result, typeof(ProductoSyncMessage));
        var productoMsg = result as ProductoSyncMessage;
        Assert.AreEqual("Productos", productoMsg.Tabla);
        Assert.AreEqual("42117", productoMsg.Producto);
    }

    [TestMethod]
    public void DeserializeSyncMessage_TablaDesconocida_RetornaNulo()
    {
        // Arrange
        var json = @"{
            ""Tabla"": ""Proveedores"",
            ""Source"": ""Odoo""
        }";

        // Act
        var result = DeserializeSyncMessage(json);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void DeserializeSyncMessage_SinTabla_RetornaNulo()
    {
        // Arrange
        var json = @"{
            ""Source"": ""Odoo"",
            ""Usuario"": ""test""
        }";

        // Act
        var result = DeserializeSyncMessage(json);

        // Assert
        Assert.IsNull(result);
    }
}
```

### 3. MessageRetryManagerTests.cs (MEDIA PRIORIDAD)

Probar el pattern matching en `GetEntityId()`:

```csharp
[TestClass]
public class MessageRetryManagerTests
{
    [TestMethod]
    public async Task RecordAttempt_ClienteSyncMessage_GeneraEntityIdCorrectamente()
    {
        // Arrange
        var db = new NVEntities();
        var manager = new MessageRetryManager(db);
        var messageId = "test-msg-123";
        var message = new ClienteSyncMessage
        {
            Tabla = "Clientes",
            Cliente = "12345",
            Contacto = "0"
        };

        // Act
        await manager.RecordAttempt(messageId, message);

        // Assert
        var record = await db.SyncMessageRetries.FirstOrDefaultAsync(r => r.MessageId == messageId);
        Assert.IsNotNull(record);
        Assert.AreEqual("12345-0", record.EntityId);
    }

    [TestMethod]
    public async Task RecordAttempt_ProductoSyncMessage_GeneraEntityIdCorrectamente()
    {
        // Arrange
        var db = new NVEntities();
        var manager = new MessageRetryManager(db);
        var messageId = "test-msg-456";
        var message = new ProductoSyncMessage
        {
            Tabla = "Productos",
            Producto = "42117"
        };

        // Act
        await manager.RecordAttempt(messageId, message);

        // Assert
        var record = await db.SyncMessageRetries.FirstOrDefaultAsync(r => r.MessageId == messageId);
        Assert.IsNotNull(record);
        Assert.AreEqual("42117", record.EntityId);
    }
}
```

### 4. SyncTableRouterTests.cs (MEDIA PRIORIDAD)

Probar el routing correcto:

```csharp
[TestClass]
public class SyncTableRouterTests
{
    [TestMethod]
    public async Task RouteAsync_ClienteMessage_UsaClientesSyncHandler()
    {
        // Arrange
        var handlers = new List<ISyncTableHandlerBase>
        {
            new ClientesSyncHandler(),
            new ProductosSyncHandler()
        };
        var router = new SyncTableRouter(handlers);
        var message = new ClienteSyncMessage
        {
            Tabla = "Clientes",
            Cliente = "12345",
            Contacto = "0"
        };

        // Act
        var handler = router.GetHandler(message);

        // Assert
        Assert.IsNotNull(handler);
        Assert.AreEqual("Clientes", handler.TableName);
    }

    [TestMethod]
    public async Task RouteAsync_ProductoMessage_UsaProductosSyncHandler()
    {
        // Arrange
        var handlers = new List<ISyncTableHandlerBase>
        {
            new ClientesSyncHandler(),
            new ProductosSyncHandler()
        };
        var router = new SyncTableRouter(handlers);
        var message = new ProductoSyncMessage
        {
            Tabla = "Productos",
            Producto = "42117"
        };

        // Act
        var handler = router.GetHandler(message);

        // Assert
        Assert.IsNotNull(handler);
        Assert.AreEqual("Productos", handler.TableName);
    }
}
```

### 5. GestorProductosTests.cs (BAJA PRIORIDAD)

Probar la conversión de ProductosKit y Stocks:

```csharp
[TestClass]
public class GestorProductosTests
{
    [TestMethod]
    public async Task PublicarProductoSincronizar_ConProductosKit_ConvierteAListaStrings()
    {
        // Arrange
        var productoDTO = new ProductoDTO
        {
            Producto = "42117",
            Nombre = "Test",
            ProductosKit = new List<ProductoKit>
            {
                new ProductoKit { ProductoId = "KIT001", Cantidad = 1 },
                new ProductoKit { ProductoId = "KIT002", Cantidad = 2 }
            }
        };

        // Act
        // Necesitarías interceptar el mensaje publicado o hacer el método testable

        // Assert
        // Verificar que ProductosKit = ["KIT001", "KIT002"]
    }
}
```

## Resumen de Prioridades

### Alta Prioridad
1. ✅ **ClienteChangeDetectorTests.cs** - Ya existe y está actualizado
2. ❌ **ProductoChangeDetectorTests.cs** - Falta, similar a ClienteChangeDetectorTests
3. ❌ **SyncWebhookControllerTests.cs** - Crítico: probar deserialización por tipo

### Media Prioridad
4. ❌ **MessageRetryManagerTests.cs** - Probar pattern matching en GetEntityId
5. ❌ **SyncTableRouterTests.cs** - Probar routing correcto por tabla

### Baja Prioridad
6. ❌ **GestorProductosTests.cs** - Probar conversión de ProductosKit/Stocks

## Notas de Implementación

- Algunos métodos privados (como `DeserializeSyncMessage`) necesitarían hacerse `internal` y usar `[InternalsVisibleTo]`
- Alternativa: usar reflection para testear métodos privados
- Para testear publicación de mensajes, considerar usar FakeItEasy para mockear `SincronizacionEventWrapper`

## Comando para ejecutar tests

```bash
# Ejecutar todos los tests
dotnet test NestoAPI.Tests/NestoAPI.Tests.csproj

# Ejecutar solo tests de sincronización
dotnet test --filter "FullyQualifiedName~Sincronizacion"

# Ejecutar tests específicos
dotnet test --filter "FullyQualifiedName~ProductoChangeDetectorTests"
```
