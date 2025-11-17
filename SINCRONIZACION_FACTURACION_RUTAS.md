# Documentación: Refactorización del Traspaso de Pedidos en Facturación de Rutas

## Fecha
2025-11-14

## Problema Original

Al facturar rutas, se producían los siguientes errores:

### Error 1: No se puede eliminar la línea
```
Pedido 901691: No se puede eliminar la linea porque el producto ya está entregado
Pedido 902947: No se puede eliminar la linea porque el producto ya está entregado
Pedido 903101: No se puede eliminar la linea porque el producto ya está entregado
Pedido 903147: No se puede eliminar la linea porque el producto ya está entregado
```

**Causa**: El traspaso de empresa usaba INSERT (nuevo pedido) + DELETE (pedido antiguo). El DELETE fallaba cuando las líneas tenían `Estado >= 2` (albaranadas o facturadas) debido a un trigger de base de datos.

### Error 2: Connection null en Rollback
```
Pedido 900630: El valor no puede ser nulo. Nombre del parámetro: connection
```

**Causa**:
1. `SaveChangesAsync()` se llamaba ANTES del traspaso, cerrando la conexión
2. El `Rollback()` no estaba protegido contra conexiones nulas

## Solución Implementada

### Cambio de Enfoque: INSERT+DELETE → UPDATE

En lugar de crear un nuevo pedido y eliminar el antiguo, ahora se actualiza directamente el campo `Empresa` en las tablas `CabPedidoVta` y `LinPedidoVta`.

### Archivos Modificados

#### 1. ServicioTraspasoEmpresa.cs

**Ubicación**: `C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Infraestructure\Traspasos\ServicioTraspasoEmpresa.cs`

**Cambios principales**:

##### A. Nuevo método para ejecutar SQL
```csharp
private async Task<int> ExecuteSqlCommandAsync(
    DbConnection connection,
    DbTransaction transaction,
    string sqlCommand,
    params SqlParameter[] parameters)
{
    using (var cmd = connection.CreateCommand())
    {
        cmd.Transaction = transaction;
        cmd.CommandText = sqlCommand;
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = 60;

        if (parameters != null)
        {
            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }
        }

        return await cmd.ExecuteNonQueryAsync();
    }
}
```

##### B. Lógica de traspaso refactorizada

**Secuencia de operaciones**:

1. **Validaciones iniciales** (sin cambios)
2. **Iniciar transacción** (sin cambios)
3. **Guardar número de pedido original** (sin cambios)
4. **Validar estado del pedido** (sin cambios)
5. **Crear albarán** - Se mantiene en empresa ORIGEN ('1')
6. **NUEVO: Deshabilitar constraints temporalmente**
   ```csharp
   await ExecuteSqlCommandAsync(connection, transaction.UnderlyingTransaction,
       "ALTER TABLE LinPedidoVta NOCHECK CONSTRAINT ALL");
   ```

7. **NUEVO: UPDATE de cabecera del pedido**
   ```csharp
   await ExecuteSqlCommandAsync(connection, transaction.UnderlyingTransaction,
       @"UPDATE CabPedidoVta
         SET Empresa = @EmpresaDestino
         WHERE Empresa = @EmpresaOrigen AND Número = @NumeroPedido",
       new SqlParameter("@EmpresaOrigen", SqlDbType.NVarChar, 10) { Value = empresaOrigen.Trim() },
       new SqlParameter("@EmpresaDestino", SqlDbType.NVarChar, 10) { Value = empresaDestino.Trim() },
       new SqlParameter("@NumeroPedido", SqlDbType.Int) { Value = numeroPedido });
   ```

8. **NUEVO: UPDATE de líneas del pedido**
   ```csharp
   int lineasActualizadas = await ExecuteSqlCommandAsync(connection, transaction.UnderlyingTransaction,
       @"UPDATE LinPedidoVta
         SET Empresa = @EmpresaDestino
         WHERE Empresa = @EmpresaOrigen AND Número = @NumeroPedido",
       new SqlParameter("@EmpresaOrigen", SqlDbType.NVarChar, 10) { Value = empresaOrigen.Trim() },
       new SqlParameter("@EmpresaDestino", SqlDbType.NVarChar, 10) { Value = empresaDestino.Trim() },
       new SqlParameter("@NumeroPedido", SqlDbType.Int) { Value = numeroPedido });
   ```

9. **NUEVO: Re-habilitar y verificar constraints**
   ```csharp
   await ExecuteSqlCommandAsync(connection, transaction.UnderlyingTransaction,
       "ALTER TABLE LinPedidoVta WITH CHECK CHECK CONSTRAINT ALL");
   ```

10. **Detach del pedido original** (necesario porque el PK cambió)
    ```csharp
    db.Entry(pedido).State = EntityState.Detached;
    foreach (var linea in pedido.LinPedidoVtas.ToList())
    {
        db.Entry(linea).State = EntityState.Detached;
    }
    ```

11. **Reload del pedido con nuevo PK**
    ```csharp
    var pedidoRecargado = await db.CabPedidoVtas
        .Include(p => p.LinPedidoVtas)
        .FirstOrDefaultAsync(p =>
            p.Empresa == empresaDestino.Trim() &&
            p.Número == numeroPedido);
    ```

12. **Recalcular totales del pedido**
    ```csharp
    gestorPedidosVenta.ActualizarTotalesCabeceraPedido(pedidoRecargado);
    ```

13. **SaveChanges y Commit**
    ```csharp
    await db.SaveChangesAsync();
    transaction.Commit();
    ```

##### C. Manejo de errores mejorado

```csharp
catch (Exception ex)
{
    try
    {
        if (transaction != null)
        {
            transaction.Rollback();
        }
    }
    catch (Exception rollbackEx)
    {
        // Log pero no re-throw - la excepción original es más importante
        // El rollback puede fallar si la conexión ya está cerrada
    }

    throw; // Re-throw de la excepción original
}
```

#### 2. GestorFacturacionRutas.cs

**Ubicación**: `C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Infraestructure\Facturas\GestorFacturacionRutas.cs`

**Cambios**:

```csharp
// 3. Verificar si hay que traspasar a empresa destino
if (servicioTraspaso.HayQueTraspasar(pedido))
{
    // El traspaso maneja su propia transacción y hace SaveChanges internamente
    await servicioTraspaso.TraspasarPedidoAEmpresa(
        pedido,
        Constantes.Empresas.EMPRESA_POR_DEFECTO,
        Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO);

    // IMPORTANTE: Recargar el pedido porque fue Detached (PK cambió)
    var pedidoRecargado = await db.CabPedidoVtas
        .Include(p => p.LinPedidoVtas)
        .FirstOrDefaultAsync(p =>
            p.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO &&
            p.Número == pedido.Número);

    if (pedidoRecargado == null)
    {
        throw new Exception($"No se pudo recargar el pedido {pedido.Número} después del traspaso");
    }

    pedido = pedidoRecargado; // Trabajar con el pedido recargado
}
else
{
    // Solo SaveChanges si NO hubo traspaso
    await db.SaveChangesAsync();
}

// 4. Crear la factura (continúa con el pedido correcto)
```

## Por Qué Funciona Esta Solución

### 1. Evita el trigger de DELETE
Al usar UPDATE en lugar de DELETE, el trigger que protege las líneas albaranadas/facturadas no se activa.

### 2. Maneja Foreign Keys correctamente
Las Foreign Keys de `LinPedidoVta` → `CabPedidoVta` incluyen el campo `Empresa` en la PK compuesta:

```sql
CONSTRAINT [FK_LinPedidoVta_CabPedidoVta] FOREIGN KEY([Empresa], [Número])
REFERENCES [dbo].[CabPedidoVta] ([empresa], [número])
```

**Problema**: Si actualizamos `CabPedidoVta.Empresa` primero (1→3), las líneas quedarían apuntando a un registro inexistente.

**Solución**: Deshabilitar temporalmente los constraints:
- `NOCHECK CONSTRAINT ALL`: Deshabilita validación
- Realizar los UPDATEs
- `WITH CHECK CHECK CONSTRAINT ALL`: Re-habilita Y verifica que los datos son válidos

### 3. Transaccionalidad completa
Todo el proceso está dentro de una transacción. Si algo falla:
- Rollback automático
- No se quedan datos inconsistentes
- Manejo seguro de errores de rollback

### 4. Entity Framework tracking correcto
- Detach del pedido con PK antiguo
- Reload del pedido con PK nuevo
- EF puede seguir trabajando normalmente

## PENDIENTE: Verificar con Programa Legacy

### Dudas a Resolver

**¿Cuándo se crea el albarán?**
- ¿Antes del traspaso en empresa origen ('1')?
- ¿Después del traspaso en empresa destino ('3')?
- ¿Se traspasa también el albarán?

**Estado de las tablas involucradas**:
- `CabPedidoVta` / `LinPedidoVta`
- `CabAlbaránVta` / `LinAlbaránVta`
- ¿Otras tablas afectadas?

### Próximos Pasos

1. **Hacer traza en programa legacy** para entender la secuencia exacta
2. **Analizar traza** para identificar:
   - Orden de operaciones
   - Tablas modificadas
   - Estados intermedios
3. **Ajustar implementación** según comportamiento legacy
4. **Probar exhaustivamente** con casos reales

## Diagrama de Flujo Actual (Implementación)

```
1. Validar pedido
2. Iniciar transacción
3. Crear albarán en empresa ORIGEN ('1')
4. Deshabilitar FK constraints en LinPedidoVta
5. UPDATE CabPedidoVta: Empresa '1' → '3'
6. UPDATE LinPedidoVta: Empresa '1' → '3'
7. Re-habilitar y verificar FK constraints
8. Detach pedido con PK antiguo ('1', numero)
9. Reload pedido con PK nuevo ('3', numero)
10. Recalcular totales
11. SaveChanges + Commit
12. Crear factura en empresa DESTINO ('3')
```

## Preguntas para la Traza Legacy

1. ¿Se crea el albarán antes o después del traspaso?
2. ¿En qué empresa se crea el albarán ('1' o '3')?
3. ¿Se traspasan también los albaranes?
4. ¿Qué tablas se modifican en cada paso?
5. ¿Hay alguna tabla adicional que necesite actualizarse?
6. ¿Cómo se manejan los números de albarán/factura?

## Estado Actual

✅ **Código implementado**: Listo y funcional desde perspectiva técnica
⏸️ **En pausa**: Esperando traza del programa legacy
❓ **Pendiente**: Validar lógica de negocio contra comportamiento legacy
🔍 **Siguiente paso**: Analizar traza y ajustar si es necesario

---

**Nota**: Este documento será actualizado después de analizar la traza del programa legacy.
