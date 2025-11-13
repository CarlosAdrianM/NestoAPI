# Captura de Usuario en Sincronización

## 📋 Resumen

Se ha implementado la captura del campo `Usuario` de la tabla `nesto_sync` para que los mensajes de sincronización incluyan el usuario real que realizó la modificación, en lugar de usar el genérico `"EXTERNAL_SYNC"`.

**Fecha**: 2025-11-13
**Estado**: ✅ Implementación completa

---

## 🎯 Problema Resuelto

### Antes
Los registros sincronizados desde `nesto_sync` se publicaban con un usuario genérico:
```csharp
Usuario = "EXTERNAL_SYNC"
```

Esto dificultaba la trazabilidad de quién había realizado cada cambio en Nesto viejo.

### Después
Ahora se captura el usuario real del registro en `nesto_sync`:
```csharp
Usuario = registro.Usuario // Ej: "CARLOS", "ADMIN", etc.
```

---

## 🔄 Flujo de Datos

```
1. Usuario modifica producto/cliente en Nesto viejo
   ↓
2. Trigger SQL captura la modificación + Usuario
   ↓
3. Registro guardado en nesto_sync con Usuario
   ↓
4. GestorSincronizacion lee el Usuario del registro
   ↓
5. Usuario pasado a PublicarClienteSincronizar/PublicarProductoSincronizar
   ↓
6. Mensaje publicado a Pub/Sub con Usuario real
   ↓
7. Odoo recibe el mensaje con el usuario correcto
```

---

## 📦 Cambios Implementados

### 1. Nuevo DTO: `NestoSyncRecord`
**Archivo**: `Models/NestoSyncRecord.cs`

```csharp
public class NestoSyncRecord
{
    public int Id { get; set; }
    public string Tabla { get; set; }
    public string ModificadoId { get; set; }
    public string Usuario { get; set; }  // ⬅️ NUEVO
    public DateTime? Sincronizado { get; set; }
}
```

### 2. `IGestorSincronizacion` Actualizado
**Archivo**: `Infraestructure/IGestorSincronizacion.cs`

**Cambios**:
- `obtenerEntidades` ahora recibe `NestoSyncRecord` (antes: `string`)
- `publicarEntidad` ahora recibe `string usuario` como segundo parámetro

```csharp
Task<bool> ProcesarTabla<T>(
    string tabla,
    Func<NestoSyncRecord, Task<List<T>>> obtenerEntidades,     // ⬅️ Recibe registro completo
    Func<T, string, Task> publicarEntidad,                     // ⬅️ Recibe usuario
    int batchSize = 50,
    int delayMs = 5000
) where T : class;
```

### 3. `GestorSincronizacion` Actualizado
**Archivo**: `Infraestructure/GestorSincronizacion.cs`

**Cambios**:
- Query SQL ahora lee `Usuario`:
```csharp
List<NestoSyncRecord> registros = await _db.Database.SqlQuery<NestoSyncRecord>(
    "SELECT Id, Tabla, ModificadoId, Usuario, Sincronizado FROM Nesto_sync WHERE Tabla = @tabla AND Sincronizado IS NULL",
    new SqlParameter("@tabla", tabla)
).ToListAsync();
```

- Usuario extraído del registro:
```csharp
string usuario = string.IsNullOrWhiteSpace(registro.Usuario)
    ? "DESCONOCIDO"
    : registro.Usuario.Trim();
```

- Usuario pasado a `publicarEntidad`:
```csharp
await publicarEntidad(entidad, usuario);
```

- Logging mejorado:
```csharp
Console.WriteLine($"✅ {tabla} {registro.ModificadoId} sincronizado correctamente (Usuario: {usuario})");
```

### 4. `GestorClientes` Actualizado
**Archivos**:
- `Infraestructure/IGestorClientes.cs`
- `Infraestructure/GestorClientes.cs`

**Cambios**:
```csharp
// Interfaz
Task PublicarClienteSincronizar(Cliente cliente, string source = "Nesto", string usuario = null);

// Implementación
public async Task PublicarClienteSincronizar(Cliente cliente, string source = "Nesto", string usuario = null)
{
    // Logging con usuario
    string usuarioInfo = !string.IsNullOrWhiteSpace(usuario) ? $", Usuario={usuario}" : "";
    Console.WriteLine($"📤 Publicando mensaje: Cliente {cliente.Nº_Cliente?.Trim()}-{cliente.Contacto?.Trim()}, Source={source}{usuarioInfo}, PersonasContacto=[...]");

    var message = new {
        // ... otros campos
        Usuario = usuario  // ⬅️ NUEVO
    };
}
```

### 5. `GestorProductos` Actualizado
**Archivos**:
- `Infraestructure/IGestorProductos.cs`
- `Infraestructure/GestorProductos.cs`

**Cambios**:
```csharp
// Interfaz
Task PublicarProductoSincronizar(ProductoDTO productoDTO, string source = "Nesto", string usuario = null);

// Implementación
public async Task PublicarProductoSincronizar(ProductoDTO productoDTO, string source = "Nesto", string usuario = null)
{
    // Logging con usuario
    string usuarioInfo = !string.IsNullOrWhiteSpace(usuario) ? $", Usuario={usuario}" : "";
    Console.WriteLine($"📤 Publicando mensaje: Producto {productoDTO.Producto?.Trim()}, Source={source}{usuarioInfo}, Kits=[...], Stocks=[...]");

    var message = new {
        // ... otros campos
        Usuario = usuario  // ⬅️ NUEVO
    };
}
```

### 6. Controllers Actualizados

#### ClientesController
**Archivo**: `Controllers/ClientesController.cs`

```csharp
public async Task<IHttpActionResult> GetClientesSync()
{
    bool resultado = await _gestorSincronizacion.ProcesarTabla(
        tabla: "Clientes",
        obtenerEntidades: async (registro) =>  // ⬅️ Recibe NestoSyncRecord
        {
            return await db.Clientes
                .Where(c => c.Nº_Cliente == registro.ModificadoId && ...)
                .ToListAsync();
        },
        publicarEntidad: async (cliente, usuario) =>  // ⬅️ Recibe usuario
        {
            await _gestorClientes.PublicarClienteSincronizar(cliente, "Nesto viejo", usuario);
        }
    );
    return Ok(resultado);
}
```

#### ProductosController
**Archivo**: `Controllers/ProductosController.cs`

```csharp
public async Task<IHttpActionResult> GetProductosSync()
{
    bool resultado = await _gestorSincronizacion.ProcesarTabla(
        tabla: "Productos",
        obtenerEntidades: async (registro) =>  // ⬅️ Recibe NestoSyncRecord
        {
            Producto producto = await db.Productos
                .SingleOrDefaultAsync(p => p.Número == registro.ModificadoId && ...);
            // ... construir ProductoDTO
        },
        publicarEntidad: async (productoDTO, usuario) =>  // ⬅️ Recibe usuario
        {
            await _gestorProductos.PublicarProductoSincronizar(productoDTO, "Nesto viejo", usuario);
        }
    );
    return Ok(resultado);
}
```

### 7. Triggers SQL Actualizados
**Archivo**: `TRIGGERS_PRODUCTOS_SINCRONIZACION.sql`

**Trigger INSERT**:
```sql
INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, Sincronizado)
SELECT
    'Productos' AS Tabla,
    LTRIM(RTRIM(i.Número)) AS ModificadoId,
    COALESCE(i.Usuario, SYSTEM_USER) AS Usuario,  -- ⬅️ NUEVO
    NULL AS Sincronizado
FROM inserted i
WHERE i.Empresa = '1'
```

**Trigger UPDATE**:
```sql
MERGE INTO Nesto_sync AS target
USING (
    SELECT DISTINCT
        LTRIM(RTRIM(i.Número)) AS ModificadoId,
        COALESCE(i.Usuario, SYSTEM_USER) AS Usuario  -- ⬅️ NUEVO
    FROM inserted i
    WHERE i.Empresa = '1'
) AS source
ON target.Tabla = 'Productos' AND target.ModificadoId = source.ModificadoId
WHEN MATCHED THEN
    UPDATE SET
        target.Sincronizado = NULL,
        target.Usuario = source.Usuario  -- ⬅️ NUEVO
WHEN NOT MATCHED THEN
    INSERT (Tabla, ModificadoId, Usuario, Sincronizado)
    VALUES ('Productos', source.ModificadoId, source.Usuario, NULL);
```

**⚠️ IMPORTANTE: Captura de Usuario en Triggers**

Los triggers usan `COALESCE(i.Usuario, SYSTEM_USER)` para capturar el usuario:
- **Si la tabla tiene campo `Usuario`**: Se usa ese valor
- **Si no**: Se usa `SYSTEM_USER` (usuario de SQL Server) como fallback

**Ajustes necesarios según tu entorno**:

Si Nesto viejo usa un método diferente para capturar el usuario, ajusta los triggers:

```sql
-- Opción 1: Campo Usuario en la tabla
COALESCE(i.Usuario, SYSTEM_USER)

-- Opción 2: CONTEXT_INFO (si guardan el usuario ahí)
COALESCE(CONVERT(VARCHAR(25), CONTEXT_INFO()), SYSTEM_USER)

-- Opción 3: Tabla de sesión
COALESCE(
    (SELECT Usuario FROM SesionesUsuario WHERE SessionId = @@SPID),
    SYSTEM_USER
)

-- Opción 4: Solo SYSTEM_USER
SYSTEM_USER
```

---

## 📊 Ejemplo de Mensaje Publicado

### Antes
```json
{
  "Cliente": "24971",
  "Nombre": "CLIENTE TEST S.L.",
  "Tabla": "Clientes",
  "Source": "Nesto viejo"
  // Usuario no incluido
}
```

### Después
```json
{
  "Cliente": "24971",
  "Nombre": "CLIENTE TEST S.L.",
  "Tabla": "Clientes",
  "Source": "Nesto viejo",
  "Usuario": "CARLOS"  // ⬅️ NUEVO
}
```

---

## 📋 Logs Mejorados

### Antes
```
📦 Procesando lote 1/3 (50 registros)
✅ Clientes 24971 sincronizado correctamente
```

### Después
```
📦 Procesando lote 1/3 (50 registros)
📤 Publicando mensaje: Cliente 24971-0, Source=Nesto viejo, Usuario=CARLOS, PersonasContacto=[...]
✅ Clientes 24971 sincronizado correctamente (Usuario: CARLOS)
```

---

## 🚀 Próximos Pasos

1. **Ejecutar los triggers SQL actualizados**:
   ```bash
   # Ejecutar TRIGGERS_PRODUCTOS_SINCRONIZACION.sql en SQL Server
   ```

2. **Verificar captura de usuario**:
   ```sql
   -- Modificar un producto en Nesto viejo
   UPDATE Productos SET Nombre = 'Test' WHERE Número = '17404';

   -- Verificar que se capturó el usuario
   SELECT * FROM Nesto_sync WHERE Tabla = 'Productos' AND ModificadoId = '17404';
   -- Debe mostrar el Usuario correcto
   ```

3. **Probar sincronización**:
   ```bash
   GET /api/Productos/Sync

   # Verificar logs:
   # ✅ Productos 17404 sincronizado correctamente (Usuario: CARLOS)
   ```

4. **Verificar en Odoo**: El mensaje debe llegar con el campo `Usuario` correcto

---

## ⚠️ Notas Importantes

1. **Compatibilidad hacia atrás**: El parámetro `usuario` es **opcional** (`null` por defecto), por lo que el código existente que no pasa usuario seguirá funcionando.

2. **Fallback a "DESCONOCIDO"**: Si el registro en `nesto_sync` no tiene usuario, se usa `"DESCONOCIDO"` en lugar de null.

3. **Triggers existentes**: Si ya tienes triggers para la tabla `Clientes`, asegúrate de actualizarlos también para capturar el campo `Usuario`.

4. **SYSTEM_USER vs campo Usuario**: Revisa si la tabla `Productos` tiene un campo `Usuario` que se actualiza cuando alguien modifica el producto. Si no, `SYSTEM_USER` capturará el usuario de la conexión SQL (que puede ser genérico como `sa` o `BUILTIN\Administrators`).

---

## ✅ Checklist de Implementación

- [x] Crear DTO `NestoSyncRecord` con campo `Usuario`
- [x] Actualizar `IGestorSincronizacion` para pasar usuario
- [x] Actualizar `GestorSincronizacion` para leer y pasar usuario
- [x] Actualizar `IGestorClientes` + `GestorClientes` para recibir usuario
- [x] Actualizar `IGestorProductos` + `GestorProductos` para recibir usuario
- [x] Actualizar `ClientesController.GetClientesSync()`
- [x] Actualizar `ProductosController.GetProductosSync()`
- [x] Actualizar triggers SQL de Productos
- [ ] Ejecutar triggers SQL actualizados en base de datos
- [ ] Actualizar triggers SQL de Clientes (si existen)
- [ ] Probar sincronización y verificar usuario en logs
- [ ] Verificar que Odoo recibe el campo `Usuario` correctamente

---

**Estado Final**: ✅ **Código actualizado, pendiente ejecutar triggers SQL**

🎉 Ahora los mensajes de sincronización incluyen el usuario real que realizó la modificación.
