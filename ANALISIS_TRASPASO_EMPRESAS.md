# Análisis: ¿Verificar antes de llamar a prdCopiar*?

## Pregunta
¿Es mejor verificar si existe en la empresa destino antes de llamar a los procedimientos `prdCopiarCliente`, `prdCopiarProducto` y `prdCopiarCuentaContable`, o simplemente llamarlos directamente?

## Respuesta Recomendada: **NO verificar antes**

### Razones

#### 1. **Los procedimientos legacy suelen ser idempotentes**
Los procedimientos almacenados legacy en SQL Server típicamente incluyen lógica `IF NOT EXISTS`:

```sql
-- Patrón típico en prdCopiarCliente
IF NOT EXISTS (SELECT 1 FROM Clientes WHERE Empresa = @EmpresaDestino AND Nº_Cliente = @NumCliente)
BEGIN
    INSERT INTO Clientes (...)
    SELECT ... FROM Clientes WHERE Empresa = @EmpresaOrigen AND Nº_Cliente = @NumCliente
END
```

#### 2. **Evidencia en el código existente**
En `ContabilidadService.cs` (línea 96), se llama a `prdCopiarCliente` **directamente sin verificar antes**:

```csharp
_ = await db.Database.ExecuteSqlCommandAsync(
    "EXEC prdCopiarCliente @EmpresaOrigen, @EmpresaDestino, @NumCliente",
    empresaOrigenParam, empresaDestinoParam, numClienteParam);
```

Esto sugiere que el procedimiento ya maneja la lógica de existencia internamente.

#### 3. **Más roundtrips = peor rendimiento**
Verificar antes añade un roundtrip adicional a la base de datos por cada elemento:

```
❌ CON VERIFICACIÓN (2 roundtrips por elemento):
1. SELECT EXISTS(...) -> retorna true/false
2. IF false THEN EXEC prdCopiar*

✅ SIN VERIFICACIÓN (1 roundtrip por elemento):
1. EXEC prdCopiar* (con IF NOT EXISTS interno)
```

Para un pedido con 10 productos únicos:
- **Con verificación**: 20 roundtrips (10 SELECT + 10 EXEC)
- **Sin verificación**: 10 roundtrips (10 EXEC)

#### 4. **Race conditions**
Verificar desde C# introduce una condición de carrera:

```
Thread A: SELECT EXISTS() -> false
Thread B: EXEC prdCopiar* -> INSERT exitoso
Thread A: EXEC prdCopiar* -> FALLA (duplicate key) ❌
```

Los procedimientos con `IF NOT EXISTS` interno son atómicos y evitan esto.

#### 5. **Menos código, menos complejidad**
Código más simple = menos bugs:

```csharp
// ❌ Más complejo
foreach (var producto in productos)
{
    bool existe = await db.Productos
        .AnyAsync(p => p.Empresa == empresaDestino && p.Número == producto);

    if (!existe)
    {
        await db.Database.ExecuteSqlCommandAsync("EXEC prdCopiarProducto...");
    }
}

// ✅ Más simple
foreach (var producto in productos)
{
    await db.Database.ExecuteSqlCommandAsync("EXEC prdCopiarProducto...");
}
```

### Excepciones (cuando SÍ verificar)

Solo tiene sentido verificar antes si:

1. **Los procedimientos NO son idempotentes** (lanzan error si el elemento ya existe)
   - En ese caso, habría que envolver en try-catch y filtrar errores de duplicados
2. **Hay lógica de negocio adicional** (ej: "no copiar productos descatalogados")
3. **Auditoría**: necesitas saber cuántos elementos fueron realmente copiados vs saltados

### Recomendación Final

**NO verificar antes.** Confiar en que los procedimientos `prdCopiar*` son idempotentes.

Si los procedimientos lanzan error cuando el elemento ya existe (poco probable), el `catch` en la línea 190 del `ServicioTraspasoEmpresa.cs` lo capturará y hará rollback de toda la transacción.

Si quieres estar 100% seguro, puedes:
1. Probar con un pedido que ya fue traspasado antes
2. O revisar el código fuente de los procedimientos en SQL Server Management Studio

```sql
-- Para ver el código de los procedimientos
EXEC sp_helptext 'prdCopiarCliente'
EXEC sp_helptext 'prdCopiarProducto'
EXEC sp_helptext 'prdCopiarCuentaContable'
```

### Optimización Adicional (si fuera necesario)

Si el rendimiento fuera crítico en el futuro, podrías:

1. **Batch de procedimientos**: Crear un nuevo procedimiento que copie todo en una sola llamada
   ```sql
   CREATE PROCEDURE prdCopiarPedidoCompleto
       @EmpresaOrigen VARCHAR(10),
       @EmpresaDestino VARCHAR(10),
       @NumeroPedido INT
   AS
   BEGIN
       -- Copia cliente, productos, cuentas en una sola transacción
   END
   ```

2. **Llamadas paralelas**: Ejecutar las copias de productos/cuentas en paralelo
   ```csharp
   var tasks = productosUnicos.Select(p =>
       db.Database.ExecuteSqlCommandAsync("EXEC prdCopiarProducto...")
   );
   await Task.WhenAll(tasks);
   ```

Pero esto solo si se demuestra que el traspaso es un cuello de botella.
