# Guía Rápida: Agregar Nueva Tabla a Sincronización

## 🎯 Objetivo

Esta guía te muestra cómo agregar soporte para sincronizar una nueva tabla (ej: Productos, Proveedores, Pedidos, etc.)

---

## 📝 Pasos (Solo 2 pasos!)

### Paso 1: Crear el Handler

Crea un archivo `TuTablaSyncHandler.cs` en `Infraestructure/Sincronizacion/`:

```csharp
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Handler de sincronización para la tabla Productos
    /// </summary>
    public class ProductosSyncHandler : ISyncTableHandler
    {
        // 1. Definir nombre de tabla (debe coincidir con mensaje.Tabla)
        public string TableName => "Productos";

        // 2. Implementar lógica de sincronización
        public async Task<bool> HandleAsync(ExternalSyncMessageDTO message)
        {
            try
            {
                // Validaciones básicas
                if (message?.Datos?.Parent == null)
                {
                    Console.WriteLine("⚠️ Datos nulos");
                    return false;
                }

                var productoExterno = message.Datos.Parent;

                // Extraer identificador
                var codigoProducto = productoExterno.CodigoProducto?.Trim();
                if (string.IsNullOrEmpty(codigoProducto))
                {
                    Console.WriteLine("⚠️ CodigoProducto vacío");
                    return false;
                }

                Console.WriteLine($"🔍 Procesando Producto: {codigoProducto}");

                using (var db = new NVEntities())
                {
                    // Buscar en BD
                    var producto = await db.Productos
                        .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                                && p.Número.Trim() == codigoProducto)
                        .FirstOrDefaultAsync();

                    if (producto == null)
                    {
                        Console.WriteLine($"⚠️ Producto {codigoProducto} no existe");
                        return false;
                    }

                    // Actualizar campos
                    if (!string.IsNullOrWhiteSpace(productoExterno.Name))
                        producto.Nombre = productoExterno.Name;

                    if (productoExterno.Price.HasValue)
                        producto.Precio = productoExterno.Price.Value;

                    if (!string.IsNullOrWhiteSpace(productoExterno.Description))
                        producto.Descripción = productoExterno.Description;

                    // Guardar
                    await db.SaveChangesAsync();

                    Console.WriteLine($"✅ Producto {codigoProducto} actualizado");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                return false;
            }
        }
    }
}
```

### Paso 2: Registrar en Startup.cs

Abre `Startup.cs` y agrega una línea en `ConfigureServices()`:

```csharp
// En el método ConfigureServices(), donde están los otros handlers:

// Servicios de sincronización bidireccional
_ = services.AddSingleton<ISyncTableHandler, ClientesSyncHandler>();
_ = services.AddSingleton<ISyncTableHandler, ProductosSyncHandler>();  // ← AGREGAR ESTA LÍNEA
_ = services.AddSingleton<SyncTableRouter>(sp =>
{
    var handlers = sp.GetServices<ISyncTableHandler>();
    return new SyncTableRouter(handlers);
});
```

---

## ✅ ¡Listo!

El sistema detectará automáticamente el nuevo handler. No necesitas:
- ❌ Modificar el controlador
- ❌ Modificar el router
- ❌ Agregar rutas
- ❌ Reiniciar servicios

Solo necesitas **reiniciar la aplicación** para que cargue el nuevo handler.

---

## 🧪 Verificar que Funciona

### 1. Health Check

```bash
curl https://tu-dominio.com/api/sync/health
```

Deberías ver tu nueva tabla en la lista:
```json
{
  "status": "healthy",
  "supportedTables": ["Clientes", "Productos"],
  ...
}
```

### 2. Prueba Manual

Publica un mensaje de prueba desde Odoo/Prestashop:

```json
{
  "tabla": "Productos",
  "accion": "actualizar",
  "datos": {
    "parent": {
      "codigo_producto": "PROD001",
      "name": "Producto Test",
      "price": 19.99,
      "description": "Descripción del producto"
    }
  }
}
```

### 3. Verificar Logs

Deberías ver en la consola de NestoAPI:
```
📨 Webhook recibido: MessageId=...
📥 Mensaje recibido: Tabla=Productos, Acción=actualizar
🔍 Procesando Producto: PROD001
✅ Producto PROD001 actualizado
```

---

## 💡 Tips y Best Practices

### 1. Detección de Cambios (Anti-bucle)

Para evitar bucles infinitos, crea un detector de cambios:

```csharp
public class ProductoChangeDetector
{
    public List<string> DetectarCambios(Producto prodNesto, ExternalProductoDTO prodExterno)
    {
        var cambios = new List<string>();

        if (!SonIguales(prodNesto.Nombre, prodExterno.Name))
            cambios.Add("Nombre");

        if (prodNesto.Precio != prodExterno.Price)
            cambios.Add("Precio");

        return cambios;
    }

    private bool SonIguales(string a, string b)
    {
        return (a?.Trim().ToUpper() ?? "") == (b?.Trim().ToUpper() ?? "");
    }
}
```

Luego en el handler:
```csharp
var cambios = _changeDetector.DetectarCambios(producto, productoExterno);

if (!cambios.Any())
{
    Console.WriteLine("✅ Sin cambios, omitiendo");
    return true; // No error, solo no hay cambios
}
```

### 2. Transacciones

Para operaciones complejas, usa transacciones:

```csharp
using (var transaction = db.Database.BeginTransaction())
{
    try
    {
        // Actualizar producto
        producto.Nombre = productoExterno.Name;
        await db.SaveChangesAsync();

        // Actualizar stock
        var stock = await db.Stocks.FindAsync(producto.Número);
        stock.Cantidad = productoExterno.Stock;
        await db.SaveChangesAsync();

        transaction.Commit();
        return true;
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

### 3. Validaciones

Siempre valida datos antes de actualizar:

```csharp
// Validar precio
if (productoExterno.Price.HasValue && productoExterno.Price.Value < 0)
{
    Console.WriteLine("⚠️ Precio negativo no permitido");
    return false;
}

// Validar nombre
if (string.IsNullOrWhiteSpace(productoExterno.Name))
{
    Console.WriteLine("⚠️ Nombre vacío no permitido");
    return false;
}
```

### 4. Logging Detallado

Ayuda para debugging:

```csharp
Console.WriteLine($"🔍 Procesando Producto: {codigoProducto}");
Console.WriteLine($"   Nombre: {productoExterno.Name}");
Console.WriteLine($"   Precio: {productoExterno.Price}");
Console.WriteLine($"   Stock: {productoExterno.Stock}");

// Después de actualizar
Console.WriteLine($"✅ Producto actualizado:");
Console.WriteLine($"   Cambios: {string.Join(", ", cambios)}");
```

### 5. Manejo de Relaciones (Children)

Si tu entidad tiene hijos (como Clientes tiene PersonasContacto):

```csharp
// En el handler principal
if (message.Datos.Children != null && message.Datos.Children.Any())
{
    await ProcesarVariantesProducto(codigoProducto, message.Datos.Children);
}

private async Task ProcesarVariantesProducto(string codigoProducto, List<ExternalProductoDTO> variantes)
{
    foreach (var variante in variantes)
    {
        // Procesar cada variante
    }
}
```

---

## 📋 Checklist de Nuevo Handler

Antes de poner en producción, verifica:

- [ ] Handler creado en `Infraestructure/Sincronizacion/`
- [ ] Implementa interfaz `ISyncTableHandler`
- [ ] `TableName` definido correctamente
- [ ] Validaciones de datos implementadas
- [ ] Detección de cambios (anti-bucle) si aplica
- [ ] Manejo de excepciones
- [ ] Logs informativos
- [ ] Registrado en `Startup.cs`
- [ ] Health check muestra la nueva tabla
- [ ] Probado con mensaje de prueba
- [ ] Tests unitarios creados

---

## 🔧 Ejemplo Completo: Proveedores

```csharp
public class ProveedoresSyncHandler : ISyncTableHandler
{
    private readonly ProveedorChangeDetector _changeDetector;

    public string TableName => "Proveedores";

    public ProveedoresSyncHandler()
    {
        _changeDetector = new ProveedorChangeDetector();
    }

    public async Task<bool> HandleAsync(ExternalSyncMessageDTO message)
    {
        try
        {
            if (message?.Datos?.Parent == null)
                return false;

            var proveedorExterno = message.Datos.Parent;
            var codigoProveedor = proveedorExterno.CodigoProveedor?.Trim();

            if (string.IsNullOrEmpty(codigoProveedor))
                return false;

            using (var db = new NVEntities())
            {
                var proveedor = await db.Proveedores
                    .Where(p => p.Empresa == "1" && p.Número.Trim() == codigoProveedor)
                    .FirstOrDefaultAsync();

                if (proveedor == null)
                    return false;

                var cambios = _changeDetector.DetectarCambios(proveedor, proveedorExterno);

                if (!cambios.Any())
                {
                    Console.WriteLine($"✅ Sin cambios en Proveedor {codigoProveedor}");
                    return true;
                }

                // Actualizar
                proveedor.Nombre = proveedorExterno.Name;
                proveedor.Teléfono = proveedorExterno.Phone;
                proveedor.Email = proveedorExterno.Email;

                await db.SaveChangesAsync();

                Console.WriteLine($"✅ Proveedor {codigoProveedor} actualizado");
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            return false;
        }
    }
}
```

Registrar:
```csharp
_ = services.AddSingleton<ISyncTableHandler, ProveedoresSyncHandler>();
```

---

## 🎓 Resumen

Para agregar una nueva tabla:

1. **Crear handler** que implemente `ISyncTableHandler`
2. **Registrar** en `Startup.cs` con `AddSingleton`

**Eso es todo.** El sistema se encarga del resto automáticamente.

---

**¿Dudas?** Consulta:
- `ClientesSyncHandler.cs` como ejemplo de referencia
- `ISyncTableHandler.cs` para ver el contrato
- `CONFIGURACION_PUSH_SUBSCRIPTION.md` para arquitectura general
