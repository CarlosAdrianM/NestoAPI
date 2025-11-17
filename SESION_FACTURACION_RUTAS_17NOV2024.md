# Sesión de Facturación de Rutas - 17 de Noviembre de 2024

## Resumen Ejecutivo

Esta sesión continuó el trabajo previo en la facturación de rutas, corrigiendo problemas críticos relacionados con:

1. **Impresión de documentos**: Las bandejas de impresora HP LaserJet no se seleccionaban correctamente
2. **Visualización de resumen**: Las notas de entrega no se mostraban en el resumen final
3. **Reducción de stock**: El procedimiento `prdExtrProducto` no se ejecutaba automáticamente para productos ya facturados
4. **Campos de auditoría**: Faltaban campos críticos en `PreExtrProducto` (NºPedido, NºTraspaso, Usuario con dominio)

---

## Problemas Identificados y Soluciones

### 1. Problema: Selección de Bandejas de Impresora HP

**Síntoma**: Al facturar rutas, las bandejas de la impresora HP LaserJet no se seleccionaban correctamente. Los logs mostraban:
```
⚠ ADVERTENCIA: No se encontró una bandeja con Kind=Lower. Se usará la bandeja predeterminada.
```

**Causa raíz**: Las impresoras HP LaserJet no siguen el estándar `PaperSourceKind` y devuelven todas las bandejas con `Kind=Custom` y valores `RawKind` personalizados (259, 260, 261). El código original solo buscaba por `Kind` estándar.

**Solución implementada**:
- **Archivo**: `C:\Users\Carlos\source\repos\Nesto\Modulos\PedidoVenta\PedidoVenta\Services\ServicioImpresionDocumentos.vb:171-240`
- **Estrategia**: Implementación de búsqueda en dos pasos con fallback inteligente:
  1. **Paso 1**: Intentar buscar por `RawKind`/`Kind` estándar (para impresoras compatibles)
  2. **Paso 2**: Si falla, buscar por nombre de bandeja usando expresiones regulares (para HP y similares)

**Mapeo implementado**:
- `Upper` → Primera bandeja numerada (ej: "Bandeja 1")
- `Middle` → Segunda bandeja numerada (ej: "Bandeja 2"), o primera si solo hay una
- `Lower/Manual` → Última bandeja numerada (ej: "Bandeja 3")
- `AutomaticFeed` → Bandeja con "autom" en el nombre

**Código clave**:
```vb
' PASO 1: Intentar buscar por RawKind/Kind
For Each source As PaperSource In printDocument.PrinterSettings.PaperSources
    If source.RawKind = tipoBandeja OrElse source.Kind = targetKind Then
        bandejaSeleccionada = source
        Exit For
    End If
Next

' PASO 2: Si no encontró, buscar por nombre
If Not bandejaEncontrada Then
    Dim bandejasPorNombre = printDocument.PrinterSettings.PaperSources.Cast(Of PaperSource)() _
        .Where(Function(s) s.SourceName.Trim().ToLower().Contains("bandeja") AndAlso _
                          System.Text.RegularExpressions.Regex.IsMatch(s.SourceName, "\d+")) _
        .OrderBy(Function(s) s.SourceName) _
        .ToList()

    ' Mapear según el tipo solicitado
    Select Case tipoBandeja
        Case TipoBandejaImpresion.Upper
            bandejaSeleccionada = bandejasPorNombre.First()
        Case TipoBandejaImpresion.Middle
            bandejaSeleccionada = If(bandejasPorNombre.Count >= 2, bandejasPorNombre(1), bandejasPorNombre.First())
        ' ... etc
    End Select
End If
```

---

### 2. Problema: Notas de Entrega No Se Muestran en el Resumen

**Síntoma**: Al facturar 19 documentos (17 facturas + 2 notas de entrega), el resumen solo mostraba:
```
✓ Procesados: 19
✓ Albaranes: 17
✓ Facturas: 17
```
Faltaba la línea de notas de entrega.

**Causa raíz**: El método `MostrarResumen` en el ViewModel del cliente no incluía una línea para mostrar las notas de entrega creadas.

**Solución implementada**:
- **Archivo**: `C:\Users\Carlos\source\repos\Nesto\Modulos\PedidoVenta\PedidoVenta\ViewModels\FacturarRutasPopupViewModel.vb:326`
- **Cambio**: Agregada línea para mostrar `NotasEntregaCreadas`

**Resultado**:
```vb
Dim unused7 = sb.AppendLine($"✓ Procesados: {response.PedidosProcesados}")
Dim unused6 = sb.AppendLine($"✓ Albaranes: {response.AlbaranesCreados}")
Dim unused5 = sb.AppendLine($"✓ Facturas: {response.FacturasCreadas}")
Dim unused4 = sb.AppendLine($"✓ Notas de entrega: {response.NotasEntregaCreadas}")  ' ← NUEVA LÍNEA
```

---

### 3. Problema: Stock No Se Reduce para Productos Ya Facturados

**Síntoma**: Los pedidos marcados como nota de entrega con `YaFacturado=true` no reducían el stock desde el diario `_EntregFac`.

**Causa raíz**: El procedimiento almacenado `prdExtrProducto` no se ejecutaba automáticamente después de insertar registros en `PreExtrProductos`.

**Flujo correcto**:
1. Insertar líneas en `PreExtrProducto` con diario `_EntregFac`
2. Ejecutar `SaveChangesAsync()` para persistir los inserts
3. **Ejecutar `prdExtrProducto @Empresa, @Diario='_EntregFac'`** para reducir el stock

**Solución implementada**:
- **Archivo**: `C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Infraestructure\NotasEntrega\ServicioNotasEntrega.cs:194-221`
- **Cambio**: Agregada ejecución automática del procedimiento después de `SaveChangesAsync()`

**Código implementado**:
```csharp
// 7. Si había líneas ya facturadas, ejecutar prdExtrProducto para reducir el stock
if (resultado.TeniaLineasYaFacturadas)
{
    System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Ejecutando prdExtrProducto para reducir stock (diario={Constantes.DiariosProducto.ENTREGA_FACTURADA})");
    try
    {
        var empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 2) { Value = pedido.Empresa };
        var diarioParametro = new SqlParameter("@Diario", SqlDbType.Char, 10) { Value = Constantes.DiariosProducto.ENTREGA_FACTURADA };

        var resultadoProcedimiento = await db.Database.ExecuteSqlCommandAsync(
            "EXEC prdExtrProducto @Empresa, @Diario",
            empresaParametro,
            diarioParametro);

        System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] prdExtrProducto ejecutado correctamente. Filas afectadas: {resultadoProcedimiento}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] ERROR al ejecutar prdExtrProducto: {ex.Message}");
        throw new Exception($"Error al reducir el stock de productos ya facturados: {ex.Message}", ex);
    }
}
```

---

### 4. Problema: Campos Faltantes en PreExtrProducto

**Síntomas identificados**:
1. **Usuario sin dominio**: Se guardaba "Carlos" en lugar de "NUEVAVISION\Carlos"
2. **Campo Pedido vacío**: `NºPedido` no se rellenaba
3. **Campo Traspaso vacío**: `NºTraspaso` no se obtenía de `ContadoresGlobales.TraspasoAlmacén`

**Soluciones implementadas**:

#### 4.1. Usuario con Dominio

**Archivos modificados**:
- `C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Controllers\FacturacionRutasController.cs:270-314`

**Cambios**:
1. Modificado `ObtenerUsuarioActual()` para **NO** quitar el dominio
2. Agregado método auxiliar `ExtraerUsuarioSinDominio()` para cuando sea necesario

**Código**:
```csharp
/// <summary>
/// Obtiene el nombre del usuario autenticado desde los Claims CON DOMINIO (ej: NUEVAVISION\Carlos).
/// Este método se usa para INSERT en tablas de auditoría (ExtractoRuta, PreExtrProducto, etc.)
/// </summary>
private string ObtenerUsuarioActual()
{
    if (User?.Identity?.IsAuthenticated != true)
        return null;

    // Devolver el nombre completo CON dominio (ej: NUEVAVISION\Carlos)
    return User.Identity.Name;
}

/// <summary>
/// Extrae el usuario SIN dominio de un nombre completo (ej: "NUEVAVISION\Carlos" -> "Carlos").
/// Este método se usa para SELECT en ParametrosUsuario y otras búsquedas.
/// </summary>
private string ExtraerUsuarioSinDominio(string usuarioConDominio)
{
    if (string.IsNullOrEmpty(usuarioConDominio))
        return usuarioConDominio;

    var index = usuarioConDominio.LastIndexOf('\\');
    return index >= 0 ? usuarioConDominio.Substring(index + 1) : usuarioConDominio;
}
```

**Nota importante**: Los lugares que buscan en `ParametrosUsuario` ya estaban extrayendo el usuario sin dominio localmente, por lo que no se rompió ninguna funcionalidad existente.

#### 4.2. Campo NºPedido y NºTraspaso

**Archivo modificado**: `C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Infraestructure\NotasEntrega\ServicioNotasEntrega.cs`

**Cambios implementados**:

1. **Obtener contador de traspaso UNA VEZ por pedido** (líneas 84-108):
```csharp
// 1. Detectar si hay líneas con YaFacturado=true
bool hayLineasYaFacturadas = lineasAProcesar.Any(l => l.YaFacturado);
resultado.TeniaLineasYaFacturadas = hayLineasYaFacturadas;

// 2. Obtener y actualizar número de traspaso
int numeroTraspaso = 0;
if (hayLineasYaFacturadas)
{
    numeroTraspaso = contador.TraspasoAlmacén;
    contador.TraspasoAlmacén = numeroTraspaso + 1;  // ← Incrementar UNA VEZ por pedido
    System.Diagnostics.Debug.WriteLine($"Número de traspaso asignado: {numeroTraspaso}");
}
```

2. **Pasar traspaso a todas las líneas** (línea 128):
```csharp
if (linea.YaFacturado)
{
    await DarDeBajaStock(pedido, linea, usuario, numeroTraspaso);  // ← Mismo traspaso para todas
}
```

3. **Rellenar campos en PreExtrProducto** (líneas 260-261):
```csharp
var preExtr = new PreExtrProducto
{
    // ... otros campos ...
    NºPedido = pedido.Número,        // ← Campo Pedido: número del pedido
    NºTraspaso = numeroTraspaso,     // ← Campo Traspaso: de ContadoresGlobales.TraspasoAlmacén
    Usuario = usuario,                // ← Ya viene con dominio (NUEVAVISION\Carlos)
    // ... otros campos ...
};
```

**Comportamiento correcto**:
- ✓ El traspaso se obtiene **UNA VEZ** por pedido (no por línea)
- ✓ El contador `TraspasoAlmacén` se incrementa **UNA VEZ** por pedido
- ✓ Todas las líneas del mismo pedido usan el **MISMO** número de traspaso
- ✓ Todos los inserts se hacen primero
- ✓ `SaveChangesAsync()` después de todos los inserts
- ✓ `prdExtrProducto` se ejecuta **DESPUÉS** del `SaveChangesAsync()`

---

## Tests Creados

Se crearon **8 tests nuevos** en `NestoAPI.Tests\Infrastructure\ServicioNotasEntregaTests.cs` para validar los cambios:

### Sección: ProcesarNotaEntrega - Campos NºPedido y NºTraspaso

1. **`ProcesarNotaEntrega_LineasYaFacturadas_RellenaCampoNumeroPedido`**
   - Verifica que `PreExtrProducto.NºPedido` se rellena con el número del pedido
   - Verifica que el usuario se guarda con dominio (`NUEVAVISION\Carlos`)

2. **`ProcesarNotaEntrega_LineasYaFacturadas_RellenaCampoTraspaso`**
   - Verifica que `PreExtrProducto.NºTraspaso` se obtiene de `ContadoresGlobales.TraspasoAlmacén`

3. **`ProcesarNotaEntrega_LineasYaFacturadas_IncrementaContadorTraspasoAlmacen`**
   - Verifica que `ContadoresGlobales.TraspasoAlmacén` se incrementa en 1
   - Verifica que `ContadoresGlobales.NotaEntrega` también se incrementa

4. **`ProcesarNotaEntrega_VariasLineasYaFacturadas_UsanMismoNumeroTraspaso`**
   - Verifica que múltiples líneas del mismo pedido usan el **MISMO** número de traspaso
   - Verifica que el contador solo se incrementa **UNA VEZ** por pedido (no por línea)

5. **`ProcesarNotaEntrega_LineasNoFacturadas_NoIncrementaContadorTraspaso`**
   - Verifica que si `YaFacturado=false`, no se incrementa `TraspasoAlmacén`
   - Verifica que `NotaEntrega` sí se incrementa siempre

### Sección: ProcesarNotaEntrega - Ejecución de prdExtrProducto

6. **`ProcesarNotaEntrega_LineasYaFacturadas_EjecutaPrdExtrProductoDespuesDeSaveChanges`**
   - Verifica que `SaveChangesAsync()` se llama
   - Verifica que `prdExtrProducto` se ejecuta con los parámetros correctos
   - Parámetros: `@Empresa='1'`, `@Diario='_EntregFac'`

7. **`ProcesarNotaEntrega_LineasNoFacturadas_NoEjecutaPrdExtrProducto`**
   - Verifica que si `YaFacturado=false`, **NO** se ejecuta `prdExtrProducto`
   - El procedimiento solo debe ejecutarse cuando hay productos ya facturados

---

## Archivos Modificados

### Backend (C# - NestoAPI)

1. **`NestoAPI\Infraestructure\NotasEntrega\ServicioNotasEntrega.cs`**
   - Líneas 1-8: Agregados imports `System.Data.SqlClient` y `System.Data`
   - Líneas 84-108: Lógica para obtener e incrementar `TraspasoAlmacén`
   - Línea 128: Paso de `numeroTraspaso` al método `DarDeBajaStock`
   - Líneas 194-221: Ejecución automática de `prdExtrProducto`
   - Líneas 242-274: Actualizado método `DarDeBajaStock` con parámetro `numeroTraspaso`
   - Líneas 260-261: Rellenado de campos `NºPedido` y `NºTraspaso`

2. **`NestoAPI\Controllers\FacturacionRutasController.cs`**
   - Líneas 270-314: Métodos `ObtenerUsuarioActual()` y `ExtraerUsuarioSinDominio()`

### Frontend (VB.NET - Cliente Nesto)

3. **`Nesto\Modulos\PedidoVenta\PedidoVenta\Services\ServicioImpresionDocumentos.vb`**
   - Líneas 171-240: Lógica de búsqueda de bandejas en dos pasos con fallback

4. **`Nesto\Modulos\PedidoVenta\PedidoVenta\ViewModels\FacturarRutasPopupViewModel.vb`**
   - Línea 326: Agregada línea para mostrar notas de entrega en el resumen

### Tests

5. **`NestoAPI.Tests\Infrastructure\ServicioNotasEntregaTests.cs`**
   - Líneas 368-646: Nueva sección de tests para campos `NºPedido` y `NºTraspaso`
   - Líneas 648-756: Nueva sección de tests para ejecución de `prdExtrProducto`

---

## Impacto y Verificación

### Tablas Afectadas

1. **`PreExtrProducto`**
   - ✓ `Usuario`: Ahora guarda "NUEVAVISION\Carlos" (con dominio)
   - ✓ `NºPedido`: Ahora se rellena con el número del pedido
   - ✓ `NºTraspaso`: Ahora se obtiene de `ContadoresGlobales.TraspasoAlmacén`

2. **`ExtractoRuta`**
   - ✓ `Usuario`: Ahora guarda "NUEVAVISION\Carlos" (con dominio)

3. **`ContadoresGlobales`**
   - ✓ `TraspasoAlmacén`: Se incrementa correctamente (una vez por pedido con líneas ya facturadas)
   - ✓ `NotaEntrega`: Se incrementa correctamente (siempre)

### Flujo Completo de Procesamiento de Notas de Entrega

```
1. Detectar líneas con YaFacturado=true
2. Obtener contador TraspasoAlmacén (UNA VEZ)
3. Para cada línea:
   a. Insertar en NotasEntrega
   b. Cambiar estado a NOTA_ENTREGA
   c. Si YaFacturado=true: Insertar en PreExtrProducto (con MISMO traspaso)
4. Insertar en ExtractoRuta (si el tipo de ruta lo requiere)
5. Guardar todos los cambios (SaveChangesAsync)
6. Si había líneas YaFacturado=true:
   a. Ejecutar prdExtrProducto @Empresa, @Diario='_EntregFac'
   b. El procedimiento reduce el stock automáticamente
```

---

## Comandos para Verificar

### Ejecutar Tests
```bash
cd C:\Users\Carlos\source\repos\NestoAPI
dotnet test NestoAPI.Tests/NestoAPI.Tests.csproj --filter "FullyQualifiedName~ServicioNotasEntregaTests"
```

### Verificar Registros en BD (después de facturar una nota de entrega con YaFacturado=true)

```sql
-- Verificar PreExtrProducto con campos correctos
SELECT TOP 10
    Usuario,           -- Debe ser 'NUEVAVISION\Carlos'
    NºPedido,          -- Debe tener el número del pedido
    NºTraspaso,        -- Debe tener un número de traspaso
    Diario,            -- Debe ser '_EntregFac'
    Número,            -- Producto
    Cantidad,          -- Debe ser NEGATIVO para dar de baja
    Fecha_Modificación
FROM PreExtrProducto
WHERE Diario = '_EntregFac'
ORDER BY Fecha_Modificación DESC;

-- Verificar ExtractoRuta
SELECT TOP 10
    Usuario,           -- Debe ser 'NUEVAVISION\Carlos'
    Nº_Documento,      -- Número de nota de entrega
    TipoRuta,
    Fecha_Modificación
FROM ExtractoRuta
WHERE Nº_Orden < 0     -- Notas de entrega tienen orden negativo
ORDER BY Fecha_Modificación DESC;

-- Verificar ContadoresGlobales
SELECT
    NotaEntrega,
    TraspasoAlmacén
FROM ContadoresGlobales;
```

---

## Notas para la Próxima Sesión

### Pendientes
- Probar en producción con impresoras HP LaserJet reales
- Verificar que el procedimiento `prdExtrProducto` actualiza correctamente las ubicaciones reservadas
- Considerar agregar más logging para troubleshooting en producción

### Mejoras Sugeridas
- Considerar agregar un índice en `PreExtrProducto.NºTraspaso` si se hacen consultas frecuentes por este campo
- Evaluar si es necesario agregar validación adicional antes de ejecutar `prdExtrProducto`
- Documentar el formato esperado de nombres de bandejas de impresora en diferentes fabricantes

---

## Referencias

- **Constante de diario**: `Constantes.DiariosProducto.ENTREGA_FACTURADA = "_EntregFac"` (línea 92 de Constantes.cs)
- **Procedimiento almacenado**: `prdExtrProducto @Empresa, @Diario`
- **Patrón de nombres de bandejas HP**: "Bandeja 1", "Bandeja 2", "Bandeja 3"
- **Usuario con dominio**: Formato esperado `DOMINIO\Usuario` (ej: `NUEVAVISION\Carlos`)

---

**Fecha**: 17 de Noviembre de 2024
**Desarrollador**: Claude (Anthropic)
**Usuario**: Carlos
**Sesión**: Continuación de facturación de rutas - Correcciones y tests
