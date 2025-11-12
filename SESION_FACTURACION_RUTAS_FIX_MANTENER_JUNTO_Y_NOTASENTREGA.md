# Sesión: Fix Facturación Rutas - MantenerJunto y NotasEntrega

**Fecha:** 2025-01-12
**Objetivo:** Resolver problemas en facturación de rutas y error de NotasEntrega

---

## 📋 Problemas Identificados y Resueltos

### 1. ✅ Pedidos con MantenerJunto no se facturaban después de crear albarán

**Problema:**
- Pedidos NRM con `MantenerJunto=1` mostraban error: "No se puede facturar porque tiene MantenerJunto=1 y hay X línea(s) sin albarán"
- Esto ocurría **incluso cuando el albarán acababa de crearse** y todas las líneas ya tenían Estado >= 2
- Causa: El objeto `pedido` en memoria no se actualizaba después de que `CrearAlbaran()` modificara la BD

**Causa raíz:**
```csharp
// ServicioAlbaranesVenta.cs:13
using (NVEntities db = new NVEntities())  // ← Contexto DIFERENTE
{
    // Ejecuta procedimiento almacenado que actualiza Estados en BD
    await db.Database.ExecuteSqlCommandAsync("EXEC prdCrearAlbaránVta ...")
}
// El objeto 'pedido' del GestorFacturacionRutas NO se actualiza
```

**Solución implementada:**
```csharp
// GestorFacturacionRutas.cs:265-271
// Después de crear el albarán, RECARGAR las líneas del pedido
await db.Entry(pedido).Collection(p => p.LinPedidoVtas).LoadAsync();
System.Diagnostics.Debug.WriteLine($"Líneas recargadas. Estados actuales: ...");
```

**Archivos modificados:**
- `NestoAPI/Infraestructure/Facturas/GestorFacturacionRutas.cs` (líneas 265-271)

**Tests creados:**
- `FacturarRutas_PedidoNRMMantenerJuntoQueQuedaCompleto_CreaAlbaranYFactura()`
- `FacturarRutas_PedidoNRMMantenerJuntoQueSigueIncompleto_CreaSoloAlbaranConError()`
- `FacturarRutas_PedidoNRMMantenerJuntoTodasLineasAlbaranadasAntes_CreaAlbaranYFactura()`

Ubicación: `NestoAPI.Tests/Infrastructure/GestorFacturacionRutasTests.cs:200-420`

---

### 2. ✅ Ventana de errores no se redimensionaba correctamente

**Problema:**
- Al maximizar la ventana de errores, el DataGrid no se ajustaba
- No se podía ver el mensaje de error completo
- No había forma de copiar los errores para documentarlos

**Solución implementada:**

**A. Ventana redimensionable:**
```xml
<!-- ErroresFacturacionRutasPopup.xaml:6 -->
<!-- ANTES: Width="1000" Height="600" -->
MinWidth="800" MinHeight="400"
```

**B. Menú contextual para copiar:**
```xml
<!-- ErroresFacturacionRutasPopup.xaml:50-58 -->
<DataGrid.ContextMenu>
    <ContextMenu>
        <MenuItem Header="Copiar error completo" Click="CopiarErrorCompleto_Click"/>
        <MenuItem Header="Copiar solo mensaje" Click="CopiarSoloMensaje_Click"/>
        <Separator/>
        <MenuItem Header="Copiar número de pedido" Click="CopiarNumeroPedido_Click"/>
    </ContextMenu>
</DataGrid.ContextMenu>
```

**Archivos modificados:**
- `Nesto/Modulos/PedidoVenta/PedidoVenta/Views/ErroresFacturacionRutasPopup.xaml`
- `Nesto/Modulos/PedidoVenta/PedidoVenta/Views/ErroresFacturacionRutasPopup.xaml.vb`

---

### 3. ✅ Error "NotaEntrega is not part of the model"

**Problema:**
```
System.InvalidOperationException: The entity type NotaEntrega is not part of the model for the current context.
```

**Causa raíz:**
La tabla `NotasEntrega` en SQL Server **NO tenía PRIMARY KEY definida**.

Entity Framework la detectaba como **tabla de solo lectura** e infería una clave incorrecta:
```xml
<!-- EDMX generaba esto: -->
<Key>
  <PropertyRef Name="NºOrden" />
  <PropertyRef Name="NotaEntrega" />
  <PropertyRef Name="Fecha" />  <!-- ❌ Fecha NO debería ser clave -->
</Key>
```

**Solución completa (4 fases):**

#### Fase 1: Agregar PRIMARY KEY en SQL Server
```sql
-- FIX_NOTAENTREGA_TABLE.sql
ALTER TABLE [dbo].[NotasEntrega]
ADD CONSTRAINT PK_NotasEntrega PRIMARY KEY CLUSTERED
(
    [NºOrden] ASC,
    [NotaEntrega] ASC
)
```

#### Fase 2: Limpiar EDMX
Se eliminaron todas las referencias antiguas de NotasEntrega del EDMX usando:
- Script: `limpiar_edmx.py`
- Eliminó: EntityType, EntitySet, EntitySetMapping

#### Fase 3: Renombrar clase de NotasEntrega a NotaEntrega
**Problema:** Conflicto con namespace `NestoAPI.Infraestructure.NotasEntrega`

Solución: Editar EDMX directamente para cambiar:
- EntityType Name: `NotasEntrega` → `NotaEntrega` (singular)
- EntitySet Name: Mantener `NotasEntregas` (plural)

Script: `renombrar_en_edmx.py`

#### Fase 4: Renombrar propiedad para evitar conflicto
**Problema:** No se puede tener una propiedad con el mismo nombre que la clase:
```csharp
public class NotaEntrega  // ← Nombre de clase
{
    public int NotaEntrega { get; set; }  // ❌ Mismo nombre
}
```

Solución: Renombrar propiedad a `Numero`:
- Propiedad en C#: `Numero`
- Columna en SQL: `NotaEntrega`
- Mapping correcto en EDMX

Script: `renombrar_propiedad_numero.py`

**Resultado final:**
```csharp
public partial class NotaEntrega
{
    public int NºOrden { get; set; }
    public int Numero { get; set; }  // Mapea a columna "NotaEntrega"
    public DateTime Fecha { get; set; }
}

// DbSet en NestoEntities.Context.cs
public virtual DbSet<NotaEntrega> NotasEntregas { get; set; }
```

**Archivos SQL creados:**
- `FIX_NOTAENTREGA_TABLE.sql` - Script para agregar PRIMARY KEY
- `VERIFICAR_ANTES_DE_FIX_NOTASENTREGA.sql` - Verificación pre-ejecución
- `LIMPIAR_DUPLICADOS_NOTASENTREGA.sql` - Limpieza de duplicados (si los hay)

**Scripts Python creados:**
- `limpiar_edmx.py` - Limpia NotasEntrega del EDMX
- `renombrar_en_edmx.py` - Renombra NotasEntrega a NotaEntrega
- `renombrar_propiedad_numero.py` - Renombra propiedad a Numero

**Documentación creada:**
- `SOLUCION_NOTASENTREGA_PRIMARY_KEY.md` - Documentación completa del problema

---

## 📊 Resumen de Cambios

### Backend (C#)

| Archivo | Cambio | Líneas |
|---------|--------|--------|
| `GestorFacturacionRutas.cs` | Recarga de líneas después de crear albarán | 265-271 |
| `GestorFacturacionRutasTests.cs` | 3 nuevos tests para MantenerJunto | 200-420 |
| `NestoEntities.edmx` | Entidad NotaEntrega con PRIMARY KEY correcta | - |

### Frontend (VB.NET/XAML)

| Archivo | Cambio |
|---------|--------|
| `ErroresFacturacionRutasPopup.xaml` | Ventana redimensionable + menú contextual |
| `ErroresFacturacionRutasPopup.xaml.vb` | Event handlers para copiar errores |

### Base de Datos

| Tabla | Cambio |
|-------|--------|
| `NotasEntrega` | PRIMARY KEY agregada: (NºOrden, NotaEntrega) |

---

## 🧪 Tests Implementados

### Grupo: Facturación después de crear albarán

**Test 1:** `FacturarRutas_PedidoNRMMantenerJuntoQueQuedaCompleto_CreaAlbaranYFactura`
- **Escenario:** Pedido NRM con MantenerJunto=1, después de crear albarán todas las líneas quedan albaranadas
- **Esperado:** ✅ Crea albarán Y factura
- **Verifica:** El bug está resuelto (antes no facturaba)

**Test 2:** `FacturarRutas_PedidoNRMMantenerJuntoQueSigueIncompleto_CreaSoloAlbaranConError`
- **Escenario:** Pedido NRM con MantenerJunto=1, después de crear albarán siguen quedando líneas pendientes
- **Esperado:** ✅ Crea solo albarán, registra error, NO crea factura
- **Verifica:** La validación sigue funcionando correctamente

**Test 3:** `FacturarRutas_PedidoNRMMantenerJuntoTodasLineasAlbaranadasAntes_CreaAlbaranYFactura`
- **Escenario:** Pedido NRM con MantenerJunto=1, todas las líneas ya estaban albaranadas antes
- **Esperado:** ✅ Crea albarán Y factura
- **Verifica:** Caso de control (siempre funcionó)

---

## 🔍 Verificación y Testing

### Pasos para verificar la solución:

1. **Verificar recarga de objeto pedido:**
   ```
   - Crear pedido NRM con MantenerJunto=1
   - Agregar 2 líneas: una EN_CURSO, otra PENDIENTE
   - Facturar ruta
   - Ver logs: "Recargando líneas del pedido desde BD..."
   - Verificar: Se crea albarán pero NO factura (correcto)
   ```

2. **Verificar NotasEntrega funciona:**
   ```
   - Crear pedido con NotaEntrega=true
   - Facturar ruta
   - Verificar: Se crea nota de entrega sin error
   ```

3. **Verificar ventana de errores:**
   ```
   - Generar errores de facturación (pedidos sin visto bueno)
   - Abrir ventana de errores
   - Maximizar ventana → DataGrid se ajusta
   - Clic derecho → Copiar error → Se copia al portapapeles
   ```

### Tests automáticos:

```bash
# Ejecutar todos los tests
dotnet test NestoAPI.Tests/NestoAPI.Tests.csproj

# Ejecutar solo tests de GestorFacturacionRutas
dotnet test --filter "FullyQualifiedName~GestorFacturacionRutasTests"
```

---

## 📝 Lecciones Aprendidas

### 1. Entity Framework y contextos separados

**Problema:** Servicios que usan `using (NVEntities db = new NVEntities())` crean contextos independientes.

**Solución:** Después de operaciones que modifican la BD en otro contexto, recargar entidades:
```csharp
await db.Entry(entidad).Collection(e => e.Relacionada).LoadAsync();
```

### 2. EDMX (Database First) vs Data Annotations (Code First)

**Conflicto:** No mezclar ambos enfoques en la misma entidad.

**Regla:** En Database First:
- ❌ NO usar `[Table]`, `[Key]`, `[Column]`
- ✅ Todo el mapping está en el EDMX
- ✅ Los archivos `.Partial.cs` deben estar vacíos o solo con lógica de negocio

### 3. Nombres de clases vs propiedades

**Error:** No se puede tener una propiedad con el mismo nombre que la clase:
```csharp
public class Foo { public int Foo { get; set; } }  // ❌ Error de compilación
```

**Solución:** Usar alias en el mapping:
- Clase: `NotaEntrega`
- Propiedad: `Numero`
- Columna DB: `NotaEntrega`

### 4. PRIMARY KEY es obligatoria en EF

**Regla:** Toda tabla que se use con Entity Framework **DEBE** tener PRIMARY KEY definida.

Si no la tiene:
- EF infiere una clave (a menudo incorrecta)
- La tabla se marca como "read-only"
- `Add()` y `SaveChanges()` fallan

---

## 🎯 Próximos Pasos (Pendientes)

### Para mañana:
1. ✅ Probar facturación de rutas en entorno real
2. ✅ Verificar que pedidos con MantenerJunto se facturan correctamente
3. ✅ Verificar que notas de entrega funcionan sin error
4. ✅ Probar ventana de errores redimensionable
5. ✅ Probar menú contextual para copiar errores

### Mejoras futuras (opcionales):
- Agregar columna "Estado después de albarán" en ventana de errores
- Agregar filtro en ventana de errores por tipo de error
- Crear alerta visual cuando hay pedidos con MantenerJunto pendientes
- Agregar test de integración completo (BD real)

---

## 📚 Referencias

### Documentación relacionada:
- `ROADMAP_FACTURAR_RUTAS.md` - Roadmap general
- `SESION_FACTURACION_RUTAS_Y_POPUP_ERRORES.md` - Sesión anterior
- `SOLUCION_NOTASENTREGA_PRIMARY_KEY.md` - Detalle del problema NotasEntrega
- `INSTRUCCIONES_ACTUALIZAR_EDMX.md` - Cómo actualizar EDMX

### Scripts útiles:
- `FIX_NOTAENTREGA_TABLE.sql` - Agregar PRIMARY KEY
- `VERIFICAR_ANTES_DE_FIX_NOTASENTREGA.sql` - Verificación pre-cambios
- `limpiar_edmx.py` - Limpiar entidades del EDMX
- `renombrar_en_edmx.py` - Renombrar entidades
- `renombrar_propiedad_numero.py` - Renombrar propiedades

### Tests:
- `NestoAPI.Tests/Infrastructure/GestorFacturacionRutasTests.cs`
  - Grupo 2: Facturación después de crear albarán (líneas 200-420)

---

## ✅ Estado Final

| Componente | Estado | Notas |
|------------|--------|-------|
| PRIMARY KEY NotasEntrega | ✅ Agregada | (NºOrden, NotaEntrega) |
| EDMX NotaEntrega | ✅ Correcto | Clase: NotaEntrega, DbSet: NotasEntregas |
| Recarga de pedido | ✅ Implementada | Después de crear albarán |
| Tests MantenerJunto | ✅ Creados | 3 tests |
| Ventana errores | ✅ Mejorada | Redimensionable + menú contextual |
| Compilación | ✅ Sin errores | Verificado |
| Ejecución | ⏳ Pendiente | Probar mañana |

---

**Última actualización:** 2025-01-12 17:30
