# Sesión: Refactorización Traspaso de Empresa + Fix CCC
**Fecha:** 18 de Noviembre de 2024

## Resumen Ejecutivo

Esta sesión abordó dos mejoras críticas:
1. **Refactorización del traspaso de empresa**: Evitar deshabilitar FK constraints usando INSERT+UPDATE
2. **Fix del campo CCC**: Corregir que no se copiaba al crear pedidos desde DetallePedidoVenta

---

## 1. Refactorización: Traspaso de Empresa sin NOCHECK CONSTRAINT

### Problema Original
El método `TraspasarPedidoAEmpresa` deshabilitaba temporalmente las FK constraints usando:
```sql
ALTER TABLE LinPedidoVta NOCHECK CONSTRAINT ALL
-- Operaciones
ALTER TABLE LinPedidoVta CHECK CONSTRAINT ALL
```

**Riesgo**: Si falla entre NOCHECK y CHECK, las constraints quedan deshabilitadas.

### Solución Implementada
**Enfoque INSERT+UPDATE** (sin deshabilitar constraints):

#### Flujo del Nuevo Traspaso

**Archivo:** `ServicioTraspasoEmpresa.cs` (líneas 220-509)

1. **Copiar dependencias** (clientes, productos, cuentas contables)
2. **Verificar** si existe cabecera en empresa destino
3. **Si NO existe → INSERT cabecera** completa en empresa destino
   - Lee parámetro `SerieFacturacionDefecto` del **usuario autenticado**
   - Usa `Constantes.Empresas.IVA_POR_DEFECTO` (G21)
   - Copia todos los campos de la cabecera original
4. **UPDATE líneas** para cambiar Empresa a destino
5. **Verificar** si quedan líneas en origen
6. **Si NO quedan → DELETE cabecera huérfana** de origen
7. **Refrescar** objeto en memoria (Detach + Reload)
8. **Recalcular** importes con ParámetrosIVA de empresa destino

#### Cambios en Firma del Método

**Antes:**
```csharp
Task TraspasarPedidoAEmpresa(CabPedidoVta pedido, string empresaOrigen, string empresaDestino)
```

**Después:**
```csharp
Task TraspasarPedidoAEmpresa(CabPedidoVta pedido, string empresaOrigen, string empresaDestino, string usuario)
```

**Razón:** Necesario para leer parámetros del usuario autenticado (no del pedido.Usuario).

#### Campos Copiados en INSERT de Cabecera

**Archivo:** `ServicioTraspasoEmpresa.cs` (líneas 380-407)

```sql
INSERT INTO CabPedidoVta (
    Empresa, Número, [Nº Cliente], Contacto, Fecha, [Forma Pago], PlazosPago, [Primer Vencimiento],
    IVA, Vendedor, Comentarios, ComentarioPicking, [Periodo Facturacion], Ruta, Serie, CCC, Origen,
    Agrupada, MotivoDevolución, ContactoCobro, NoComisiona, NotaEntrega, vtoBuenoPlazosPago,
    Operador, FijarPrimerVto, MantenerJunto, ServirJunto, Usuario, [Fecha Modificación]
)
SELECT
    @EmpresaDestino,     -- Empresa destino (parámetro)
    Número,              -- Mismo número de pedido
    [Nº Cliente],        -- Cliente original
    Contacto, Fecha, [Forma Pago], PlazosPago, [Primer Vencimiento],
    @IVA,                -- Constantes.Empresas.IVA_POR_DEFECTO
    Vendedor, Comentarios, ComentarioPicking, [Periodo Facturacion], Ruta,
    @Serie,              -- Serie del parámetro usuario (si existe)
    CCC, Origen,         -- Campos originales
    Agrupada, MotivoDevolución, ContactoCobro, NoComisiona, NotaEntrega, vtoBuenoPlazosPago,
    Operador, FijarPrimerVto, MantenerJunto, ServirJunto, Usuario, [Fecha Modificación]
FROM CabPedidoVta
WHERE Empresa = @EmpresaOrigen AND Número = @NumeroPedido
```

**Campos modificados:**
- `Empresa` → @EmpresaDestino (empresa destino)
- `IVA` → @IVA (`Constantes.Empresas.IVA_POR_DEFECTO` = "G21")
- `Serie` → @Serie (leído de `ParametrosUsuario.SerieFacturacionDefecto` para el usuario autenticado)

### Lectura de Parámetros de Usuario

**Archivo:** `ServicioTraspasoEmpresa.cs` (líneas 346-368)

```csharp
string serieFacturacion = null;

if (!string.IsNullOrEmpty(usuario))
{
    serieFacturacion = Controllers.ParametrosUsuarioController.LeerParametro(
        empresaDestino.Trim(),
        usuario.Trim(),
        "SerieFacturacionDefecto"
    );
}

// Si no se encuentra, mantener serie original
if (string.IsNullOrEmpty(serieFacturacion))
{
    System.Diagnostics.Debug.WriteLine($"  ⚠ No se encontró SerieFacturacionDefecto para usuario '{usuario}', se mantendrá la serie original");
}
```

**Método centralizado:** `ParametrosUsuarioController.LeerParametro()`
- Extrae usuario sin dominio: `Substring(usuario.IndexOf("\\") + 1)`
- Busca parámetro específico en `ParametrosUsuario`
- Devuelve valor trimmed o null

### Archivos Modificados

1. **IServicioTraspasoEmpresa.cs**
   - Agregado parámetro `usuario` a la firma (línea 35)

2. **ServicioTraspasoEmpresa.cs**
   - Agregado helper `ExecuteSqlQueryScalarAsync<T>` (líneas 146-185)
   - Refactorizado `TraspasarPedidoAEmpresa` con INSERT+UPDATE (líneas 220-509)
   - Agregado parámetro `usuario` (línea 220)
   - Lee `SerieFacturacionDefecto` con método centralizado (líneas 353-357)
   - Usa `Constantes.Empresas.IVA_POR_DEFECTO` (línea 414)

3. **ServicioFacturas.cs**
   - Actualizada llamada a `TraspasarPedidoAEmpresa` con parámetro `usuario` (línea 299)

4. **GestorFacturacionRutas.cs**
   - Actualizada llamada a `TraspasarPedidoAEmpresa` con parámetro `usuario` (línea 419)

5. **ServicioTraspasoEmpresaTests.cs**
   - Actualizados todos los tests para pasar parámetro `"TEST\\usuario"` (replace_all)

### Ventajas del Nuevo Enfoque

✅ **Seguridad**: No deshabilita constraints temporalmente
✅ **Atomicidad**: Todo en una transacción (rollback si falla)
✅ **Trazabilidad**: Logs detallados de cada paso
✅ **Centralización**: Usa método centralizado para leer parámetros
✅ **Constantes**: Usa `IVA_POR_DEFECTO` en lugar de hardcoded 'G21'
✅ **Flexibilidad**: Lee serie de facturación del usuario autenticado
✅ **Integridad**: Mantiene todos los campos originales excepto los modificados explícitamente

---

## 2. Fix: Campo CCC No Se Copiaba en DetallePedidoVenta

### Problema Detectado

Al crear pedidos desde DetallePedidoVenta y facturarlos directamente, el campo **CCC** no se pasaba a la factura.

#### Diagnóstico Inicial (Incorrecto)
❌ Pensamos que faltaba pasar CCC desde DetallePedidoVenta → API → stored procedure

#### Diagnóstico Real (Correcto)
✅ El CCC **NO se estaba copiando** del selector de dirección de entrega al objeto pedido en el ViewModel

### Arquitectura de DetallePedidoVenta

**Archivo:** `DetallePedidoViewModel.vb`

DetallePedidoVenta tiene dos selectores:

1. **SelectorCliente** → `ClienteCompleto` (ClienteDTO)
   - Copia: `origen`, `contactoCobro`
   - NO tiene CCC (correcto)

2. **SelectorDireccionEntrega** → `DireccionEntregaSeleccionada` (DireccionesEntregaCliente)
   - Copiaba: `formaPago`, `plazosPago`, `iva`, `vendedor`, `ruta`, `periodoFacturacion`
   - ❌ **Faltaba copiar**: `CCC`

### Solución Implementada

**Archivo:** `DetallePedidoViewModel.vb` (línea 189)

**Setter de DireccionEntregaSeleccionada:**
```vb
Set(value As DireccionesEntregaCliente)
    If SetProperty(_direccionEntregaSeleccionada, value) Then
        If EstaCreandoPedido AndAlso Not IsNothing(pedido) Then
            pedido.formaPago = value.formaPago
            pedido.plazosPago = value.plazosPago
            pedido.iva = value.iva
            pedido.vendedor = value.vendedor
            pedido.ruta = value.ruta
            pedido.periodoFacturacion = value.periodoFacturacion
            pedido.CCC = value.ccc  ' ✅ AGREGADO
        End If
    End If
End Set
```

### Modelo DireccionesEntregaCliente

**Archivo:** `SelectorDireccionEntregaModel.cs` (línea 32)

```csharp
public class DireccionesEntregaCliente : IFiltrableItem
{
    public string contacto { get; set; }
    public string nombre { get; set; }
    public string direccion { get; set; }
    // ... otros campos ...
    public string iva { get; set; }
    public string ccc { get; set; }  // ✅ Campo ya existía
    public string ruta { get; set; }
    public string formaPago { get; set; }
    public string plazosPago { get; set; }
    // ... más campos ...
}
```

### Flujo Completo de CCC

```
1. Usuario selecciona CLIENTE
   ↓
   ClienteCompleto se asigna (NO tiene CCC)
   ↓
2. Usuario selecciona DIRECCIÓN DE ENTREGA
   ↓
   DireccionEntregaSeleccionada se asigna
   ↓
   Setter copia: formaPago, plazosPago, iva, vendedor, ruta, periodoFacturacion, CCC ✅
   ↓
3. Usuario hace clic en "Crear Factura"
   ↓
   Pedido tiene CCC correcto
   ↓
   API recibe pedido con CCC
   ↓
   Stored procedure prdCrearFacturaVta usa CCC
   ↓
   Factura creada con CCC correcto ✅
```

### Archivos Modificados

1. **DetallePedidoViewModel.vb**
   - Agregado `pedido.CCC = value.ccc` en setter de `DireccionEntregaSeleccionada` (línea 189)

### Por Qué el CCC Está en la Dirección de Entrega

El **CCC** (Código de Cuenta Corriente) está asociado a la **dirección de entrega** y no al cliente porque:

1. Un cliente puede tener **múltiples direcciones de entrega**
2. Cada dirección puede tener su **propio CCC para facturación**
3. La factura se emite a la dirección de entrega seleccionada

Esto es el comportamiento correcto del sistema.

---

## 3. Cambios Adicionales

### Agregado using System.Collections.Generic

**Archivo:** `ServicioTraspasoEmpresa.cs` (línea 4)

Necesario para usar `List<SqlParameter>` en el INSERT condicional de la cabecera.

---

## Tests

### Tests Existentes Actualizados

**Archivo:** `ServicioTraspasoEmpresaTests.cs`

Todos los tests que llamaban a `TraspasarPedidoAEmpresa` fueron actualizados para pasar el nuevo parámetro `usuario`:

```csharp
// Antes
servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino)

// Después
servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario")
```

**Tests afectados:**
- `TraspasarPedidoAEmpresa_PedidoNull_LanzaArgumentNullException`
- `TraspasarPedidoAEmpresa_EmpresaOrigenNullOVacia_LanzaArgumentException`
- `TraspasarPedidoAEmpresa_EmpresaDestinoNullOVacia_LanzaArgumentException`
- `TraspasarPedidoAEmpresa_EmpresaOrigenIgualEmpresaDestino_LanzaArgumentException`
- Todos los tests de casos límite

### Tests de Integración Requeridos

**IMPORTANTE:** Los siguientes escenarios deben probarse en Visual Studio con base de datos real:

#### Test 1: Traspaso con Serie Personalizada del Usuario
```
GIVEN: Usuario "TEST\\usuario" tiene parámetro "SerieFacturacionDefecto" = "FAC"
  AND: Pedido 12345 en empresa "1" sin IVA
 WHEN: Se llama TraspasarPedidoAEmpresa(pedido, "1", "3", "TEST\\usuario")
 THEN: Cabecera en empresa "3" debe tener:
       - IVA = "G21" (Constantes.Empresas.IVA_POR_DEFECTO)
       - Serie = "FAC" (del parámetro del usuario)
```

#### Test 2: Traspaso sin Serie del Usuario (Mantener Original)
```
GIVEN: Usuario "TEST\\usuario" NO tiene parámetro "SerieFacturacionDefecto"
  AND: Pedido 12345 en empresa "1" con Serie = "PED"
 WHEN: Se llama TraspasarPedidoAEmpresa(pedido, "1", "3", "TEST\\usuario")
 THEN: Cabecera en empresa "3" debe tener:
       - IVA = "G21" (Constantes.Empresas.IVA_POR_DEFECTO)
       - Serie = "PED" (serie original mantenida)
```

#### Test 3: CCC Se Copia al Crear Pedido
```
GIVEN: DireccionEntregaCliente con CCC = "ES1234567890123456789012"
 WHEN: Se asigna DireccionEntregaSeleccionada en DetallePedidoViewModel
 THEN: pedido.CCC debe ser "ES1234567890123456789012"
```

#### Test 4: CCC Llega a la Factura
```
GIVEN: Pedido con CCC = "ES1234567890123456789012"
 WHEN: Se crea factura desde DetallePedidoVenta
 THEN: Factura generada debe tener CCC = "ES1234567890123456789012"
```

---

## Notas de Implementación

### Campos Reales de CabPedidoVta

Los nombres de los campos en la base de datos tienen **espacios y caracteres especiales**:

```sql
-- Campos con espacios (requieren corchetes en SQL)
[Nº Cliente]
[Forma Pago]
[Primer Vencimiento]
[Periodo Facturacion]
[Fecha Modificación]

-- Campos sin espacios
Empresa, Número, Contacto, Fecha, PlazosPago, IVA, Vendedor,
Comentarios, ComentarioPicking, Ruta, Serie, CCC, Origen,
Agrupada, MotivoDevolución, ContactoCobro, NoComisiona, NotaEntrega,
vtoBuenoPlazosPago, Operador, FijarPrimerVto, MantenerJunto,
ServirJunto, Usuario
```

**Fuente:** `CabPedidoVta.cs` (modelo Entity Framework)

### Logging y Debug

El método `TraspasarPedidoAEmpresa` tiene logging extensivo:

```csharp
System.Diagnostics.Debug.WriteLine($"  → Verificando si existe cabecera en empresa destino");
System.Diagnostics.Debug.WriteLine($"  → Leyendo parámetros para empresa destino");
System.Diagnostics.Debug.WriteLine($"  ✓ Serie para facturación: {serieFacturacion}");
System.Diagnostics.Debug.WriteLine($"  → Copiando cabecera a empresa destino con INSERT");
System.Diagnostics.Debug.WriteLine($"  → Moviendo líneas a empresa destino");
System.Diagnostics.Debug.WriteLine($"  ✓ {lineasMovidas} líneas movidas a empresa destino");
System.Diagnostics.Debug.WriteLine($"  ✓✓✓ Traspaso completado exitosamente ✓✓✓");
```

Útil para debugging en Visual Studio Output Window.

---

## Verificación Post-Implementación

### Checklist de Verificación

- [ ] Tests de unidad pasan (ServicioTraspasoEmpresaTests)
- [ ] Tests de integración pasan (GestorFacturacionRutasTests)
- [ ] Facturación de rutas funciona correctamente
- [ ] Traspaso de empresa funciona sin deshabilitar constraints
- [ ] Serie se lee del parámetro del usuario autenticado
- [ ] IVA se establece a "G21" (constante)
- [ ] CCC se copia desde DireccionEntregaSeleccionada
- [ ] Facturas creadas desde DetallePedidoVenta tienen CCC correcto
- [ ] No hay regresiones en funcionalidad existente

### Comandos de Verificación

```bash
# Tests del traspaso
dotnet test --filter "FullyQualifiedName~ServicioTraspasoEmpresaTests"

# Tests de facturación de rutas
dotnet test --filter "FullyQualifiedName~GestorFacturacionRutasTests"

# Todos los tests
dotnet test
```

---

## Próximos Pasos (PENDIENTES)

1. ✅ ~~Refactorizar traspaso sin NOCHECK CONSTRAINT~~
2. ✅ ~~Agregar parámetro usuario a TraspasarPedidoAEmpresa~~
3. ✅ ~~Leer serie de facturación del parámetro de usuario~~
4. ✅ ~~Usar constante IVA_POR_DEFECTO~~
5. ✅ ~~Fix del CCC en DetallePedidoVenta~~
6. ⏳ **Probar en Visual Studio con base de datos real**
7. ⏳ **Verificar facturas creadas desde DetallePedidoVenta tienen CCC correcto**
8. ⏳ **Deploy a producción después de pruebas exitosas**

---

## Conclusiones

Esta sesión logró dos mejoras críticas:

1. **Traspaso más seguro**: Ya no deshabilita FK constraints temporalmente, usa INSERT+UPDATE para copiar cabecera y mover líneas de forma atómica.

2. **CCC correcto**: Ahora se copia desde la dirección de entrega seleccionada, asegurando que las facturas tengan el CCC correcto para cobro.

Ambas mejoras mantienen compatibilidad total con el código existente y mejoran la robustez del sistema.

**Estado:** ✅ Implementación completa, pendiente pruebas en Visual Studio
**Autor:** Claude Code (con supervisión de Carlos)
**Fecha:** 18 de Noviembre de 2024
