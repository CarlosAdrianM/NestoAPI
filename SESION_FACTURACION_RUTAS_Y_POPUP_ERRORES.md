# Sesión: Facturación de Rutas y Popup de Errores

**Fecha:** 2025-01-10
**Objetivo:** Solucionar dos problemas en el sistema de facturación de rutas

---

## Problema 1: Popup de Errores Vacío

### Síntoma
Al facturar rutas, cuando había errores:
- El resumen mostraba correctamente "Errores: 18"
- El popup de errores se abría pero mostraba "0 errores"
- El DataGrid estaba completamente vacío

### Diagnóstico

#### Intento 1: Auto-wiring duplicado (descartado)
**Hipótesis inicial:** El auto-wiring de Prism estaba duplicado entre:
- `prism:ViewModelLocator.AutoWireViewModel="True"` en el XAML
- `containerRegistry.RegisterDialog<>()` en PedidoVenta.vb

**Verificación:** Se comprobó con breakpoint que `OnDialogOpened` solo se ejecuta una vez, descartando esta hipótesis.

#### Diagnóstico correcto: Reutilización del ViewModel
Los logs mostraban:
```
CargarErrores - Errores recibidos: 18
CargarErrores - Limpiando colección (tenía 18 elementos)
CargarErrores - Colección final tiene 0 elementos
```

**Problema real:** El loop `For Each` no se estaba ejecutando (no había logs de "Agregando pedido").

### Solución Implementada

**Archivo:** `ErroresFacturacionRutasPopupViewModel.vb`

#### Cambio 1: Crear nueva colección en lugar de Clear()
```vb
' ANTES (problemático):
Errores.Clear()
For Each errorItem In errores
    Errores.Add(errorItem)
Next

' DESPUÉS (correcto):
Dim nuevaColeccion As New ObservableCollection(Of PedidoConErrorDTO)()

For Each errorItem In errores
    System.Diagnostics.Debug.WriteLine($"CargarErrores - Agregando pedido {errorItem.NumeroPedido}: {errorItem.MensajeError}")
    nuevaColeccion.Add(errorItem)
Next

Me.Errores = nuevaColeccion  ' Usar Me. para desambiguar del parámetro
```

**Razón:** En VB no es case-sensitive, por lo que `Errores` (propiedad) y `errores` (parámetro) son ambiguos. Usar `Me.Errores` fuerza la referencia a la propiedad.

#### Cambio 2: Corregir el setter de Errores
```vb
Public Property Errores As ObservableCollection(Of PedidoConErrorDTO)
    Get
        Return _errores
    End Get
    Set(value As ObservableCollection(Of PedidoConErrorDTO))
        If SetProperty(_errores, value) Then
            RaisePropertyChanged(NameOf(NumeroErrores))
        End If
    End Set
End Property
```

#### Cambio 3: Implementación correcta de IDialogAware
```vb
Public Sub OnDialogOpened(parameters As IDialogParameters) Implements IDialogAware.OnDialogOpened
    System.Diagnostics.Debug.WriteLine($"OnDialogOpened - parameters is Nothing: {parameters Is Nothing}")

    If parameters IsNot Nothing AndAlso parameters.ContainsKey("errores") Then
        System.Diagnostics.Debug.WriteLine("OnDialogOpened - Clave 'errores' encontrada")
        Dim errores = parameters.GetValue(Of List(Of PedidoConErrorDTO))("errores")
        System.Diagnostics.Debug.WriteLine($"OnDialogOpened - Errores recibidos: {If(errores Is Nothing, 0, errores.Count)}")
        CargarErrores(errores)
    Else
        System.Diagnostics.Debug.WriteLine("OnDialogOpened - Clave 'errores' NO encontrada o parameters es Nothing")
    End If
End Sub
```

### Archivos Modificados
1. `ErroresFacturacionRutasPopupViewModel.vb` - Lógica de carga de errores
2. `FacturarRutasPopupViewModel.vb` - Llamada a dialogService con parámetros
3. `ErroresFacturacionRutasPopup.xaml` - Convertido de Window a UserControl
4. `ErroresFacturacionRutasPopup.xaml.vb` - Actualizado para usar IDialogAware
5. `PedidoVenta.vb` - Registrado el diálogo

### Estado Final
- ✅ Popup convertido a UserControl con DialogService de Prism
- ✅ Auto-wiring mantenido (no era el problema)
- ✅ Logging completo agregado para diagnóstico
- ⏳ **Pendiente de prueba:** Recompilar proyecto VB y verificar funcionamiento

---

## Problema 2: Orden Incorrecto de Operaciones en ExtractoRuta

### Síntoma
Al facturar pedidos NRM (Normal):
1. Se creaba el albarán ✅
2. Se intentaba crear ExtractoRuta desde el albarán ❌ (prematuro)
3. Se creaba la factura ✅
4. Se intentaba crear ExtractoRuta desde la factura ❌ (duplicado)

**Error resultante:** "No se encontró el ExtractoCliente" al intentar insertar desde factura, porque se intentaba insertar dos veces.

### Solución Implementada

**Archivo:** `GestorFacturacionRutas.cs`

#### Cambio: Insertar ExtractoRuta solo cuando corresponde

**Para pedidos FDM (Fin de Mes):**
```csharp
// Líneas 269-279
bool esNRM = pedido.Periodo_Facturacion?.Trim() == Constantes.Pedidos.PERIODO_FACTURACION_NORMAL;

if (tipoRuta?.DebeInsertarEnExtractoRuta() == true && !esNRM)
{
    System.Diagnostics.Debug.WriteLine($"  → Insertando en ExtractoRuta desde albarán (FDM)");
    await servicioExtractoRuta.InsertarDesdeAlbaran(pedido, numeroAlbaran, usuario, autoSave: false);
}
```

**Para pedidos NRM (Normal):**
```csharp
// Líneas 378-385
// Para NRM, el ExtractoRuta se inserta desde la factura (no desde el albarán)
var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
if (tipoRuta?.DebeInsertarEnExtractoRuta() == true)
{
    System.Diagnostics.Debug.WriteLine($"  → Insertando en ExtractoRuta desde factura (NRM)");
    await servicioExtractoRuta.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: true);
}
```

### Flujo Correcto Resultante

**Pedidos FDM (Fin de Mes):**
1. Crear albarán
2. Insertar ExtractoRuta desde albarán
3. Guardar cambios

**Pedidos NRM (Normal):**
1. Crear albarán
2. Crear factura
3. Insertar ExtractoRuta desde factura (usa datos del ExtractoCliente)
4. Guardar cambios

---

## Problema 3: ExtractoRuta y NotaEntrega No Estaban en el Modelo EF

### Síntoma
```
System.InvalidOperationException: The entity type ExtractoRuta is not part of the model for the current context.
```

### Causa
Las entidades `ExtractoRuta` y `NotaEntrega` estaban definidas manualmente con Data Annotations, pero el EDMX (Database First) no las reconocía automáticamente.

### Solución Implementada

#### Paso 1: Agregar al EDMX manualmente
- Abierto el diseñador EDMX en Visual Studio
- Agregadas las tablas `ExtractoRuta` y `NotaEntrega` desde "Update Model from Database"

#### Paso 2: Corregir plurales generados

El EDMX generó nombres incorrectos:
- ❌ `DbSet<ExtractoRuta> ExtractoRutas` (debería ser `ExtractosRuta`)
- ✅ `DbSet<NotaEntrega> NotasEntregas` (correcto)

**Decisión:** Mantener el plural generado y actualizar el código para usar `ExtractoRutas`.

#### Paso 3: Eliminar duplicados

**Archivos modificados:**

1. **NVEntities.Partial.cs** - Eliminadas definiciones duplicadas de DbSets
2. **NestoEntities.Context.cs** - DbSets generados por EDMX (líneas 115-116)
3. **ServicioExtractoRuta.cs** - Actualizado para usar `db.ExtractoRutas`
4. **ServicioNotasEntrega.cs** - Actualizado para usar `db.ExtractoRutas`
5. **NotasEntrega.cs** - Eliminado archivo generado conflictivo
6. **NestoAPI.csproj** - Actualizado para usar `RutaAlmacen.cs`

#### Paso 4: Resolver conflicto de NotaEntrega

El EDMX generó dos archivos:
- `NotaEntrega.cs` (manual, con Data Annotations) ✅ Mantener
- `NotasEntrega.cs` (generado, con nombre plural) ❌ Eliminar

**Solución:**
```csharp
// En NestoEntities.Context.cs (línea 116):
public virtual DbSet<NotaEntrega> NotasEntregas { get; set; }
```

### Estado Final de DbSets

| Entidad | Clase (Singular) | DbSet (Plural) | Origen |
|---------|------------------|----------------|---------|
| ExtractoRuta | `ExtractoRuta` | `ExtractoRutas` | EDMX |
| NotaEntrega | `NotaEntrega` | `NotasEntregas` | Manual + EDMX |

---

## Mejora Adicional: Nueva Ruta de Prueba "Almacén"

### Motivación
Necesidad de una ruta específica para hacer pruebas de facturación sin afectar producción.

### Implementación

**Archivo creado:** `RutaAlmacen.cs`

```csharp
public class RutaAlmacen : ITipoRuta
{
    private static readonly List<string> rutasAlmacen = new List<string> { "AM" };

    public string Id => "ALMACEN";
    public string NombreParaMostrar => "Ruta Almacén";
    public string Descripcion => "Ruta de prueba. No imprime copias. Inserta en ExtractoRuta.";

    public int ObtenerNumeroCopias(...) => 0;  // No imprime
    public bool DebeInsertarEnExtractoRuta() => true;  // Sí inserta para pruebas
}
```

**Archivo modificado:** `TipoRutaFactory.cs`
```csharp
private static readonly List<ITipoRuta> tiposRutaRegistrados = new List<ITipoRuta>
{
    new RutaPropia(),
    new RutaAgencia(),
    new RutaAlmacen()  // ← Agregado
};
```

### Características de la Ruta AM

| Característica | Valor |
|----------------|-------|
| Código de ruta | `AM` |
| Nombre visible | "Ruta Almacén" |
| Imprime copias | No (0 copias) |
| Inserta ExtractoRuta | Sí |
| Uso | Solo pruebas |

### Uso
1. Crear pedido con Ruta = "AM"
2. Abrir popup de facturación de rutas
3. Seleccionar "Ruta Almacén" (tercera opción)
4. Facturar normalmente
5. No se generan PDFs, solo se contabiliza

---

## Archivos Modificados - Resumen Completo

### Backend (NestoAPI)

**Infraestructura:**
1. `GestorFacturacionRutas.cs` - Orden correcto de ExtractoRuta
2. `ServicioExtractoRuta.cs` - Uso de `db.ExtractoRutas`
3. `ServicioNotasEntrega.cs` - Uso de `db.ExtractoRutas`

**Modelos:**
4. `NVEntities.Partial.cs` - Constructor sin ConfigurarMapeos
5. `NestoEntities.Context.cs` - DbSets de ExtractoRuta y NotaEntrega
6. `RutaAlmacen.cs` - Nueva clase para ruta de prueba
7. `TipoRutaFactory.cs` - Registro de RutaAlmacen

**Proyecto:**
8. `NestoAPI.csproj` - Referencia a RutaAlmacen.cs

### Frontend (Nesto - VB)

**ViewModels:**
9. `ErroresFacturacionRutasPopupViewModel.vb` - Lógica correcta con Me.Errores
10. `FacturarRutasPopupViewModel.vb` - Llamada a dialogService con logs

**Views:**
11. `ErroresFacturacionRutasPopup.xaml` - UserControl con AutoWireViewModel
12. `ErroresFacturacionRutasPopup.xaml.vb` - Code-behind actualizado

**Módulo:**
13. `PedidoVenta.vb` - Registro del diálogo

---

## Estado Final y Próximos Pasos

### ✅ Completado
1. Orden correcto de ExtractoRuta (FDM vs NRM)
2. ExtractoRuta y NotaEntrega agregados al EDMX
3. Ruta "Almacén" (AM) creada para pruebas
4. Popup de errores convertido a DialogService
5. Logging completo agregado para diagnóstico

### ⏳ Pendiente de Verificación
1. **Recompilar proyecto Nesto (VB)** en Visual Studio
2. **Probar facturación con ruta AM** mañana
3. **Verificar que el popup muestre los 18 errores** correctamente
4. **Revisar logs** de la siguiente ejecución:
   - Debe mostrar "Colección actual tiene X elementos" (no "Limpiando")
   - Debe mostrar "Agregando pedido X: mensaje" por cada error
   - Debe mostrar "Colección final tiene X elementos" con X > 0

### 🔍 Logs Esperados en Próxima Ejecución

```
MostrarVentanaErrores - Recibidos 18 errores
MostrarVentanaErrores - Llamando ShowDialog con 18 errores
OnDialogOpened - parameters is Nothing: False
OnDialogOpened - Clave 'errores' encontrada
OnDialogOpened - Errores recibidos: 18
CargarErrores - Errores recibidos: 18
CargarErrores - Colección actual tiene 0 elementos
CargarErrores - Agregando pedido 12345: Error al crear factura...
CargarErrores - Agregando pedido 12346: Error al crear factura...
... (16 más)
CargarErrores - Asignando nueva colección con 18 elementos
CargarErrores - Colección final tiene 18 elementos
CargarErrores - NumeroErrores: 18
NumeroErrores getter - Devolviendo: 18
MostrarVentanaErrores - ShowDialog completado
```

---

## Notas Técnicas Importantes

### Visual Basic Case-Insensitivity
En VB, `Errores` y `errores` son el mismo identificador. Cuando hay conflicto entre:
- Parámetro: `errores As List(Of PedidoConErrorDTO)`
- Propiedad: `Errores As ObservableCollection(Of PedidoConErrorDTO)`

**Solución:** Usar `Me.Errores` para forzar referencia a la propiedad de instancia.

### Entity Framework con EDMX (Database First)
No reconoce automáticamente `DbSet` adicionales definidos en clases parciales. Requiere:
1. Agregar entidades al EDMX mediante el diseñador, O
2. Usar comandos SQL directos con `ExecuteSqlCommand()`

**Decisión:** Se eligió opción 1 (agregar al EDMX) para mantener consistencia.

### Prism DialogService vs Window Manual
- `RegisterDialog<TView, TViewModel>()` maneja automáticamente el ciclo de vida
- NO requiere `AutoWireViewModel="True"` en teoría, pero funciona con ambos
- Los parámetros se pasan mediante `DialogParameters` en `ShowDialog()`
- El ViewModel debe implementar `IDialogAware` correctamente

---

## Referencias Útiles

**Documentación Prism:**
- https://prismlibrary.com/docs/dialog-service.html

**Patrón usado:**
```vb
' Llamada:
dialogService.ShowDialog("NombreView", parametros, callback)

' ViewModel:
Public Sub OnDialogOpened(parameters As IDialogParameters)
    Dim datos = parameters.GetValue(Of TipoDatos)("clave")
    ' Procesar datos
End Sub
```

---

**Fin del documento**
