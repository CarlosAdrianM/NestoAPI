# Refactorización: CCC y Formas de Pago en DetallePedidoVenta
**Fecha:** 20 de Noviembre de 2024
**Contexto:** Sesión de continuación después del trabajo del 18/11/2024 sobre CCC

---

## 📋 Resumen Ejecutivo

### Problemas a Resolver

1. **Binding roto en facturación**: Cuando se cambian formas de pago, plazos de pago y CCC en la UI y se da click en "Crear albarán y factura", la factura no refleja estos cambios. Usa los datos antiguos de la base de datos.

2. **Falta opción "(Sin CCC)"**: El combo de CCC actual no permite poner el campo a NULL explícitamente. Necesitamos una opción para indicar que no hay CCC.

3. **Lógica automática de CCC**: Cuando cambia la forma de pago:
   - Si es "RCB" (Recibo) → Poner el CCC por defecto del cliente (de su ficha empresa/cliente/contacto)
   - Si es otra forma de pago → Poner NULL (Sin CCC)

### Estrategia de Implementación

Refactorización en **5 FASES** con tests para evitar regresiones, ya que el código de DetallePedidoVenta es delicado y se usa en múltiples flujos de la aplicación.

---

## 🔍 Análisis del Problema 1: Binding Roto

### Causa Raíz

**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVenta/PedidoVentaService.vb` (líneas 492-522)

El método `CrearFacturaVenta` solo envía a la API:
```vb
Dim parametro As New With {
    .Empresa = empresaParametro,
    .Pedido = numeroPedido,
    .Usuario = usuarioParametro
}
```

**NO** envía los campos `formaPago`, `plazosPago` ni `CCC` que el usuario modificó en la UI.

La API lee estos valores directamente desde la base de datos, ignorando los cambios en memoria del objeto `pedido` en el ViewModel.

### Bindings en XAML (Correctos)

**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVenta/Views/DetallePedidoView.xaml`

```xaml
<!-- Línea 221: Forma de Pago -->
<controles:SelectorFormaPago
    Seleccionada="{Binding pedido.formaPago, Mode=TwoWay}">
</controles:SelectorFormaPago>

<!-- Línea 222: Plazos de Pago -->
<controles:SelectorPlazosPago
    Seleccionada="{Binding pedido.plazosPago, Mode=TwoWay}">
</controles:SelectorPlazosPago>

<!-- Línea 232: CCC -->
<TextBox Text="{Binding pedido.ccc, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}">
</TextBox>
```

Los bindings son correctos (`Mode=TwoWay`), pero los cambios no se guardan en BD antes de crear la factura.

### Solución

**Guardar el pedido antes de crear albarán y factura** usando el método existente `servicio.modificarPedido(pedido.Model)`.

**Archivo a modificar:** `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb` (línea 981)

---

## 🔍 Análisis del Problema 2: Opción "(Sin CCC)"

### Estado Actual

**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb` (líneas 629-687)

El método `CargarCCCDisponibles()` carga solo el CCC del contacto actual:

```vb
Private Async Sub CargarCCCDisponibles()
    ' ... código ...
    Dim listaCC As New ObservableCollection(Of CCCDisponible)

    If Not IsNothing(direccionContacto) Then
        Dim cccItem As New CCCDisponible(
            If(String.IsNullOrWhiteSpace(direccionContacto.ccc), "", direccionContacto.ccc),
            direccionContacto.contacto, direccionContacto.nombreContacto)
        listaCC.Add(cccItem)
    End If

    CCCDisponibles = listaCC
End Sub
```

**Problema:** Si el contacto tiene CCC, no hay forma de quitarlo (poner NULL).

### Solución

Añadir un elemento extra `"(Sin CCC)"` a la lista con `CCC = null`.

```vb
' Antes de asignar CCCDisponibles
Dim sinCCC As New CCCDisponible(Nothing, "", "Sin CCC")
listaCC.Insert(0, sinCCC) ' Insertar al principio
```

---

## 🔍 Análisis del Problema 3: Lógica Automática de CCC

### Requisito

Cuando cambia la forma de pago:
- **Forma de pago = "RCB"** (Recibo Bancario) → Cargar CCC del cliente (contacto actual)
- **Forma de pago ≠ "RCB"** → Poner NULL (Sin CCC)

### Constante Disponible

**Archivo:** `Nesto/Infrastructure/Shared/Constantes.cs` (línea 79)

```csharp
public class FormasPago
{
    public const string RECIBO = "RCB";
}
```

### Implementación

Agregar un setter reactivo a la propiedad `pedido.formaPago` o usar PropertyChanged.

**Opción 1: PropertyChanged en ViewModel**
```vb
AddHandler pedido.PropertyChanged, AddressOf OnPedidoPropertyChanged

Private Sub OnPedidoPropertyChanged(sender As Object, e As PropertyChangedEventArgs)
    If e.PropertyName = NameOf(pedido.formaPago) Then
        ActualizarCCCSegunFormaPago()
    End If
End Sub

Private Sub ActualizarCCCSegunFormaPago()
    If pedido.formaPago?.Trim() = Constantes.FormasPago.RECIBO Then
        ' Poner CCC del cliente (del contacto actual)
        If CCCDisponibles?.Count > 0 Then
            Dim cccConValor = CCCDisponibles.FirstOrDefault(Function(c) Not String.IsNullOrWhiteSpace(c.CCC))
            If Not IsNothing(cccConValor) Then
                CCCSeleccionado = cccConValor
            End If
        End If
    Else
        ' Poner Sin CCC (null)
        Dim sinCCC = CCCDisponibles?.FirstOrDefault(Function(c) String.IsNullOrWhiteSpace(c.CCC))
        If Not IsNothing(sinCCC) Then
            CCCSeleccionado = sinCCC
        End If
    End If
End Sub
```

---

## 🏗️ Plan de Implementación en 5 FASES

### FASE 1: Crear Tests para Comportamiento Actual ✅

**Objetivo:** Documentar el comportamiento actual con tests antes de modificar nada.

**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVentaTests/DetallePedidoViewModelTests.cs`

**Tests a crear:**

#### Test 1.1: Cambiar forma de pago en UI y crear factura
```csharp
[TestMethod]
public async Task CrearFactura_CambiosEnFormaPagoEnUI_DeberiaReflejarEnFactura()
{
    // Arrange: Crear pedido con forma de pago "EFC" y plazosPago "CONTADO"
    // Cambiar en UI a "RCB" y "30 DIAS"

    // Act: Llamar OnCrearFacturaVenta()

    // Assert: Verificar que la factura tiene "RCB" y "30 DIAS"
    // NOTA: Este test FALLARÁ inicialmente (Red), lo cual es esperado
}
```

#### Test 1.2: Cargar CCC disponibles devuelve solo el del contacto
```csharp
[TestMethod]
public async Task CargarCCCDisponibles_ContactoConCCC_DevuelveSoloUno()
{
    // Arrange: Mock de API que devuelve dirección con CCC

    // Act: Llamar CargarCCCDisponibles()

    // Assert: CCCDisponibles.Count == 1
}
```

#### Test 1.3: No existe opción "(Sin CCC)"
```csharp
[TestMethod]
public async Task CargarCCCDisponibles_NoTieneOpcionSinCCC()
{
    // Arrange: Cargar CCC

    // Act: Buscar elemento con CCC == null

    // Assert: No debe existir (este test PASARÁ inicialmente)
}
```

**Comandos:**
```bash
# Ejecutar solo estos tests
dotnet test --filter "FullyQualifiedName~DetallePedidoViewModelTests"
```

**Estado esperado:**
- Test 1.1: ❌ FALLA (comportamiento actual incorrecto)
- Test 1.2: ✅ PASA
- Test 1.3: ✅ PASA (confirma que no existe la opción)

---

### FASE 2: Fix Binding - Guardar Pedido Antes de Crear Factura ✅

**Objetivo:** Asegurar que los cambios en formaPago, plazosPago y CCC se guarden en BD antes de facturar.

**Archivo a modificar:** `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb`

#### Cambio 2.1: Modificar OnCrearFacturaVenta (línea 876)

**Antes:**
```vb
Private Async Sub OnCrearFacturaVenta()
    If Not dialogService.ShowConfirmationAnswer("Crear factura", "¿Desea crear la factura del pedido?") Then
        Return
    End If
    Try
        Dim resultado As CrearFacturaResponseDTO = Await servicio.CrearFacturaVenta(pedido.empresa.ToString, pedido.numero.ToString)
        ' ... resto del código
```

**Después:**
```vb
Private Async Sub OnCrearFacturaVenta()
    If Not dialogService.ShowConfirmationAnswer("Crear factura", "¿Desea crear la factura del pedido?") Then
        Return
    End If
    Try
        ' ✨ NUEVO: Guardar cambios del pedido antes de crear factura
        ' Carlos 20/11/24: Asegurar que cambios en formaPago, plazosPago y CCC se reflejen
        Await servicio.modificarPedido(pedido.Model)

        Dim resultado As CrearFacturaResponseDTO = Await servicio.CrearFacturaVenta(pedido.empresa.ToString, pedido.numero.ToString)
        ' ... resto del código sin cambios
```

#### Cambio 2.2: Modificar OnCrearAlbaranYFacturaVenta (línea 981)

**Antes:**
```vb
Private Async Sub OnCrearAlbaranYFacturaVenta()
    If Not dialogService.ShowConfirmationAnswer("Crear albarán y factura", "¿Desea crear la factura del pedido directamente?") Then
        Return
    End If
    Try
        Dim albaran As Integer = Await servicio.CrearAlbaranVenta(pedido.empresa.ToString, pedido.numero.ToString)
        Dim resultado As CrearFacturaResponseDTO = Await servicio.CrearFacturaVenta(pedido.empresa.ToString, pedido.numero.ToString)
        ' ... resto del código
```

**Después:**
```vb
Private Async Sub OnCrearAlbaranYFacturaVenta()
    If Not dialogService.ShowConfirmationAnswer("Crear albarán y factura", "¿Desea crear la factura del pedido directamente?") Then
        Return
    End If
    Try
        ' ✨ NUEVO: Guardar cambios del pedido antes de crear albarán y factura
        ' Carlos 20/11/24: Asegurar que cambios en formaPago, plazosPago y CCC se reflejen
        Await servicio.modificarPedido(pedido.Model)

        Dim albaran As Integer = Await servicio.CrearAlbaranVenta(pedido.empresa.ToString, pedido.numero.ToString)
        Dim resultado As CrearFacturaResponseDTO = Await servicio.CrearFacturaVenta(pedido.empresa.ToString, pedido.numero.ToString)
        ' ... resto del código sin cambios
```

#### Cambio 2.3: Modificar OnCrearAlbaranVenta (opcional, por consistencia)

Similar a los anteriores, agregar `Await servicio.modificarPedido(pedido.Model)` antes de crear albarán.

**Verificación:**
```bash
# Ejecutar test 1.1
dotnet test --filter "CrearFactura_CambiosEnFormaPagoEnUI_DeberiaReflejarEnFactura"
```

**Estado esperado:** Test 1.1 ahora debe ✅ PASAR (Green)

---

### FASE 3: Añadir Opción "(Sin CCC)" ✅

**Objetivo:** Permitir al usuario quitar explícitamente el CCC del pedido.

**Archivo a modificar:** `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb`

#### Cambio 3.1: Modificar CargarCCCDisponibles (línea 629)

**Antes:**
```vb
Dim listaCC As New ObservableCollection(Of CCCDisponible)

If Not IsNothing(direccionContacto) Then
    Dim cccItem As New CCCDisponible(...)
    listaCC.Add(cccItem)
Else
    Debug.WriteLine($"[CCC] ADVERTENCIA: No se encontró dirección para contacto '{contactoActual}'")
End If

CCCDisponibles = listaCC
```

**Después:**
```vb
Dim listaCC As New ObservableCollection(Of CCCDisponible)

' ✨ NUEVO: Añadir opción "(Sin CCC)" que pone el campo a NULL
' Carlos 20/11/24: Permite al usuario quitar explícitamente el CCC
Dim sinCCC As New CCCDisponible(Nothing, "", "(Sin CCC)")
listaCC.Add(sinCCC)

If Not IsNothing(direccionContacto) Then
    Dim cccItem As New CCCDisponible(...)
    listaCC.Add(cccItem)
Else
    Debug.WriteLine($"[CCC] ADVERTENCIA: No se encontró dirección para contacto '{contactoActual}'")
End If

CCCDisponibles = listaCC
```

#### Cambio 3.2: Modificar clase CCCDisponible (línea 1319)

**Antes:**
```vb
Public Sub New(ccc As String, contacto As String, nombreContacto As String)
    Me.CCC = If(String.IsNullOrWhiteSpace(ccc), "", ccc.Trim())
    ' ...
    If String.IsNullOrWhiteSpace(Me.CCC) Then
        Descripcion = $"Contacto {contacto}: Sin CCC"
    Else
        ' ...
    End If
End Sub
```

**Después:**
```vb
Public Sub New(ccc As String, contacto As String, nombreContacto As String)
    ' ✨ MODIFICADO: Permitir NULL explícito
    ' Carlos 20/11/24: Distinguir entre "" y Nothing para opción "(Sin CCC)"
    Me.CCC = ccc?.Trim() ' Mantener Nothing si es Nothing

    ' ...
    If String.IsNullOrWhiteSpace(Me.CCC) Then
        ' Si nombreContacto es "(Sin CCC)", usarlo directamente
        If nombreContacto = "(Sin CCC)" Then
            Descripcion = nombreContacto
        Else
            Descripcion = $"Contacto {contacto}: Sin CCC"
        End If
    Else
        ' ... código existente
    End If
End Sub
```

#### Test 3.1: Verificar que existe opción "(Sin CCC)"
```csharp
[TestMethod]
public async Task CargarCCCDisponibles_TieneOpcionSinCCC()
{
    // Arrange & Act: Cargar CCC

    // Assert:
    // CCCDisponibles.Count >= 1
    // CCCDisponibles[0].CCC == null
    // CCCDisponibles[0].Descripcion == "(Sin CCC)"
}
```

**Verificación:**
```bash
dotnet test --filter "CargarCCCDisponibles_TieneOpcionSinCCC"
```

**Estado esperado:** Test 3.1 debe ✅ PASAR

---

### FASE 4: Implementar Lógica Automática de CCC según Forma de Pago ✅

**Objetivo:** Cuando cambia la forma de pago, actualizar automáticamente el CCC:
- "RCB" → CCC del cliente
- Otra → NULL (Sin CCC)

**Archivo a modificar:** `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb`

#### Cambio 4.1: Agregar handler de PropertyChanged en constructor (después línea 70)

```vb
Public Sub New(...)
    ' ... código existente ...

    Dim unused2 = eventAggregator.GetEvent(Of PedidoCreadoEvent).Subscribe(AddressOf OnPedidoCreadoEnDetalle)

    ' ✨ NUEVO: Escuchar cambios en pedido para reaccionar a cambios de forma de pago
    ' Carlos 20/11/24: Actualizar CCC automáticamente según forma de pago
    ' Se hace aquí porque pedido.Model se crea después, en OnNavigatedTo
End Sub
```

#### Cambio 4.2: Agregar método para conectar handler cuando se carga el pedido

Buscar el método `OnNavigatedTo` o `CargarPedido` y agregar:

```vb
Private Sub ConectarHandlerFormaPago()
    If Not IsNothing(pedido?.Model) Then
        AddHandler pedido.Model.PropertyChanged, AddressOf OnPedidoModelPropertyChanged
    End If
End Sub

Private Sub OnPedidoModelPropertyChanged(sender As Object, e As PropertyChangedEventArgs)
    ' Solo reaccionar a cambios en formaPago
    If e.PropertyName = "formaPago" Then
        ActualizarCCCSegunFormaPago()
    End If
End Sub
```

#### Cambio 4.3: Implementar ActualizarCCCSegunFormaPago

```vb
''' <summary>
''' Actualiza el CCC seleccionado según la forma de pago del pedido.
''' Carlos 20/11/24: RCB (Recibo) requiere CCC, otras formas de pago no.
''' </summary>
Private Sub ActualizarCCCSegunFormaPago()
    If IsNothing(pedido) OrElse IsNothing(CCCDisponibles) Then
        Return
    End If

    Dim formaPago As String = pedido.formaPago?.Trim()

    Debug.WriteLine($"[CCC] Forma de pago cambió a: {formaPago}")

    If formaPago = Constantes.FormasPago.RECIBO Then
        ' Es Recibo (RCB) → Poner el CCC del cliente (primer CCC válido)
        Dim cccConValor = CCCDisponibles.FirstOrDefault(Function(c) Not String.IsNullOrWhiteSpace(c.CCC))

        If Not IsNothing(cccConValor) Then
            CCCSeleccionado = cccConValor
            Debug.WriteLine($"[CCC] Auto-seleccionado CCC para Recibo: {cccConValor.Descripcion}")
        Else
            Debug.WriteLine($"[CCC] ADVERTENCIA: Forma de pago es RCB pero no hay CCC disponible")
        End If
    Else
        ' NO es Recibo → Poner "(Sin CCC)" (null)
        Dim sinCCC = CCCDisponibles.FirstOrDefault(Function(c) String.IsNullOrWhiteSpace(c.CCC))

        If Not IsNothing(sinCCC) Then
            CCCSeleccionado = sinCCC
            Debug.WriteLine($"[CCC] Auto-seleccionado Sin CCC (forma de pago: {formaPago})")
        End If
    End If
End Sub
```

#### Cambio 4.4: Llamar ActualizarCCCSegunFormaPago después de cargar CCC

Modificar `CargarCCCDisponibles` para llamar a la lógica automática al final:

```vb
Private Async Sub CargarCCCDisponibles()
    ' ... código existente que carga la lista ...

    CCCDisponibles = listaCC
    Debug.WriteLine($"[CCC] Cargado CCC del contacto {contactoActual}")

    ' ✨ NUEVO: Aplicar lógica automática de CCC según forma de pago
    ' Carlos 20/11/24: Después de cargar, ajustar según la forma de pago actual
    ActualizarCCCSegunFormaPago()

    ' NOTA: El código existente de auto-selección se ejecutará solo si
    ' ActualizarCCCSegunFormaPago no encuentra nada que hacer
End Sub
```

#### Test 4.1: CCC automático cuando forma de pago es RCB
```csharp
[TestMethod]
public async Task CambiarFormaPago_ARCBConCCCDisponible_SeleccionaCCCAutomaticamente()
{
    // Arrange: Pedido con forma de pago "EFC", CCC disponible

    // Act: Cambiar pedido.formaPago a "RCB"

    // Assert: CCCSeleccionado.CCC != null
}
```

#### Test 4.2: Sin CCC cuando forma de pago no es RCB
```csharp
[TestMethod]
public async Test CambiarFormaPago_AEfectivo_SeleccionaSinCCC()
{
    // Arrange: Pedido con forma de pago "RCB" y CCC seleccionado

    // Act: Cambiar pedido.formaPago a "EFC"

    // Assert: CCCSeleccionado.CCC == null
}
```

**Verificación:**
```bash
dotnet test --filter "FullyQualifiedName~DetallePedidoViewModelTests"
```

**Estado esperado:** Todos los tests deben ✅ PASAR

---

### FASE 5: Ejecutar Tests Completos y Verificar No Hay Regresiones ✅

**Objetivo:** Confirmar que todos los cambios funcionan y no rompieron nada existente.

#### Verificación 5.1: Tests Unitarios

```bash
# Todos los tests de DetallePedidoViewModel
dotnet test --filter "FullyQualifiedName~DetallePedidoViewModelTests" --logger "console;verbosity=detailed"

# Todos los tests del módulo PedidoVenta
dotnet test Nesto/Modulos/PedidoVenta/PedidoVentaTests/PedidoVentaTests.csproj
```

**Checklist:**
- [ ] Todos los tests nuevos (FASE 1-4) pasan
- [ ] Tests existentes siguen pasando (no regresiones)
- [ ] Coverage de código aceptable

#### Verificación 5.2: Pruebas Manuales en Visual Studio

**Escenario 1: Cambiar forma de pago y crear factura**
1. Abrir DetallePedidoVenta con pedido existente
2. Cambiar forma de pago de "EFC" a "RCB"
3. Cambiar plazos de pago de "CONTADO" a "30 DIAS"
4. Hacer clic en "Crear Albarán y Factura"
5. ✅ Verificar que la factura tiene "RCB" y "30 DIAS"

**Escenario 2: Opción "(Sin CCC)" funciona**
1. Abrir DetallePedidoVenta con pedido existente que tiene CCC
2. Abrir combo de CCC
3. ✅ Verificar que aparece "(Sin CCC)" como primera opción
4. Seleccionar "(Sin CCC)"
5. ✅ Verificar que `pedido.ccc` queda a NULL

**Escenario 3: CCC automático con RCB**
1. Crear nuevo pedido
2. Seleccionar cliente con dirección que tiene CCC
3. Cambiar forma de pago a "RCB"
4. ✅ Verificar que CCC se selecciona automáticamente
5. Cambiar forma de pago a "EFC"
6. ✅ Verificar que CCC se pone a "(Sin CCC)" automáticamente

#### Verificación 5.3: Logs y Debug

Revisar los mensajes de Debug.WriteLine durante las pruebas:
```
[CCC] DireccionEntregaSeleccionada cambiada:
[CCC] Forma de pago cambió a: RCB
[CCC] Auto-seleccionado CCC para Recibo: ...
[CCC] Auto-seleccionado Sin CCC (forma de pago: EFC)
```

---

## 🎯 Consideración: Control de Usuario SelectorCCC (OPCIONAL)

### ¿Por qué crear SelectorCCC?

**Ventajas:**
1. **Reutilización**: Podría usarse en otros formularios (PlantillaVenta, otros módulos)
2. **Encapsulación**: Toda la lógica de CCC en un solo lugar
3. **Mantenibilidad**: Más fácil de probar y modificar
4. **Consistencia**: Mismo comportamiento en toda la aplicación

**Desventajas:**
1. **Overhead**: Más complejo para un caso simple
2. **Tiempo**: Más trabajo de desarrollo y testing
3. **Acoplamiento**: Necesita conocer formas de pago, direcciones, etc.

### Decisión Propuesta

**POSTPONER** la creación de SelectorCCC hasta que:
1. Se necesite en un segundo formulario (YAGNI principle)
2. Los cambios actuales estén probados y estabilizados
3. Se tenga tiempo para diseñar una API limpia del control

**Por ahora:** Implementar la lógica directamente en DetallePedidoViewModel (FASES 1-5).

**Futuro:** Si se decide crear SelectorCCC, refactorizar en una FASE 6 posterior.

---

## 📊 Resumen de Archivos Modificados

### Backend (NestoAPI)
- ❌ Ninguno (los endpoints ya funcionan correctamente)

### Frontend (Nesto)

#### Archivos de Código
1. **`Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb`**
   - FASE 2: `OnCrearFacturaVenta()` - Agregar `modificarPedido` antes de crear factura
   - FASE 2: `OnCrearAlbaranYFacturaVenta()` - Agregar `modificarPedido` antes de crear factura
   - FASE 2: `OnCrearAlbaranVenta()` - Agregar `modificarPedido` (opcional)
   - FASE 3: `CargarCCCDisponibles()` - Añadir opción "(Sin CCC)"
   - FASE 4: Nuevo método `ActualizarCCCSegunFormaPago()`
   - FASE 4: Nuevo método `OnPedidoModelPropertyChanged()`
   - FASE 4: Conectar PropertyChanged handler en carga de pedido
   - FASE 4: Llamar `ActualizarCCCSegunFormaPago()` al final de `CargarCCCDisponibles()`

2. **`Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb` - Clase CCCDisponible (línea 1319)**
   - FASE 3: Modificar constructor para manejar NULL explícito
   - FASE 3: Actualizar lógica de `Descripcion` para "(Sin CCC)"

#### Archivos de Tests
3. **`Nesto/Modulos/PedidoVenta/PedidoVentaTests/DetallePedidoViewModelTests.cs`** (NUEVO o ampliar existente)
   - FASE 1: Test 1.1 - `CrearFactura_CambiosEnFormaPagoEnUI_DeberiaReflejarEnFactura()`
   - FASE 1: Test 1.2 - `CargarCCCDisponibles_ContactoConCCC_DevuelveSoloUno()`
   - FASE 1: Test 1.3 - `CargarCCCDisponibles_NoTieneOpcionSinCCC()`
   - FASE 3: Test 3.1 - `CargarCCCDisponibles_TieneOpcionSinCCC()`
   - FASE 4: Test 4.1 - `CambiarFormaPago_ARCBConCCCDisponible_SeleccionaCCCAutomaticamente()`
   - FASE 4: Test 4.2 - `CambiarFormaPago_AEfectivo_SeleccionaSinCCC()`

#### Archivos XAML
- ❌ Ninguno (el XAML actual ya funciona correctamente)

---

## ⚠️ Riesgos y Precauciones

### Riesgo 1: Regresiones en Flujos Existentes

**Mitigación:**
- ✅ Tests unitarios completos (FASE 1)
- ✅ Pruebas manuales de escenarios críticos (FASE 5)
- ✅ Logs de Debug para troubleshooting

### Riesgo 2: PropertyChanged Handler Crea Bucles

**Problema:** Si `ActualizarCCCSegunFormaPago()` modifica `pedido.ccc`, podría disparar otro PropertyChanged.

**Mitigación:**
- Solo escuchar cambios en `formaPago`, NO en `ccc`
- Usar flag `_estaCargandoCCC` existente si es necesario

### Riesgo 3: Guardar Pedido Puede Fallar

**Problema:** Si `modificarPedido()` falla antes de crear factura, el usuario ve un error confuso.

**Mitigación:**
- Capturar excepción específica de `modificarPedido()`
- Mostrar mensaje claro al usuario
- Ejemplo:
```vb
Try
    Await servicio.modificarPedido(pedido.Model)
Catch ex As ValidationException
    dialogService.ShowError($"No se pudo guardar el pedido antes de facturar:\n{ex.Message}")
    Return
End Try
```

### Riesgo 4: Performance - Guardar Antes de Facturar

**Problema:** Llamada extra a API podría hacer más lento el proceso.

**Mitigación:**
- Impacto mínimo (1 PUT request adicional)
- Usuario ya espera un proceso no instantáneo
- Ventaja de correctitud supera el mínimo overhead

---

## 📝 Checklist de Implementación

### Preparación
- [ ] Crear rama Git: `feature/fix-ccc-facturacion-20nov2024`
- [ ] Commit inicial con estado actual
- [ ] Revisar este documento con el equipo

### FASE 1: Tests (Red-Green)
- [ ] Crear/abrir archivo de tests `DetallePedidoViewModelTests.cs`
- [ ] Implementar Test 1.1 (debe fallar ❌)
- [ ] Implementar Test 1.2 (debe pasar ✅)
- [ ] Implementar Test 1.3 (debe pasar ✅)
- [ ] Commit: "FASE 1: Tests para comportamiento actual"

### FASE 2: Fix Binding
- [ ] Modificar `OnCrearFacturaVenta()` - agregar `modificarPedido`
- [ ] Modificar `OnCrearAlbaranYFacturaVenta()` - agregar `modificarPedido`
- [ ] (Opcional) Modificar `OnCrearAlbaranVenta()`
- [ ] Ejecutar Test 1.1 (ahora debe pasar ✅)
- [ ] Commit: "FASE 2: Guardar pedido antes de crear factura"

### FASE 3: Opción "(Sin CCC)"
- [ ] Modificar `CargarCCCDisponibles()` - añadir elemento "(Sin CCC)"
- [ ] Modificar constructor `CCCDisponible` - manejar NULL
- [ ] Implementar Test 3.1 (debe pasar ✅)
- [ ] Commit: "FASE 3: Añadida opción Sin CCC"

### FASE 4: Lógica Automática
- [ ] Implementar `ActualizarCCCSegunFormaPago()`
- [ ] Implementar `OnPedidoModelPropertyChanged()`
- [ ] Conectar handler en carga de pedido
- [ ] Llamar lógica automática en `CargarCCCDisponibles()`
- [ ] Implementar Test 4.1 (debe pasar ✅)
- [ ] Implementar Test 4.2 (debe pasar ✅)
- [ ] Commit: "FASE 4: CCC automático según forma de pago"

### FASE 5: Verificación Final
- [ ] Ejecutar todos los tests unitarios
- [ ] Compilar en Visual Studio
- [ ] Prueba manual Escenario 1 (cambiar forma de pago y facturar)
- [ ] Prueba manual Escenario 2 (opción Sin CCC)
- [ ] Prueba manual Escenario 3 (CCC automático con RCB)
- [ ] Revisar logs de Debug
- [ ] Commit: "FASE 5: Tests y verificación completa"

### Deploy
- [ ] Merge a develop/main
- [ ] Compilar release en Visual Studio
- [ ] Deploy a entorno de pruebas
- [ ] Pruebas de aceptación con usuario final
- [ ] Deploy a producción

---

## 🎓 Lecciones Aprendidas

### De la Sesión del 18/11/2024

1. **CCC está en la dirección de entrega, NO en el cliente**: Cada dirección puede tener su propio CCC.

2. **Bindings de WPF son correctos**: El problema estaba en el backend/API, no en el XAML.

3. **Tests primero**: Documentar comportamiento actual antes de modificar.

### Para Esta Refactorización

1. **5 FASES aseguran seguridad**: Red-Green-Refactor con tests evita regresiones.

2. **YAGNI**: No crear SelectorCCC hasta que se necesite en segundo lugar.

3. **Logs de Debug**: Invaluables para debugging de cambios en propiedades.

4. **Constantes centralizadas**: `Constantes.FormasPago.RECIBO` evita magic strings.

---

## 📞 Contacto y Soporte

**Documentación relacionada:**
- `SESION_TRASPASO_CCC_18NOV2024.md` - Contexto del trabajo anterior
- `RESUMEN_SESION_18NOV2024.md` - Resumen ejecutivo de la sesión previa

**Tests:**
- `Nesto/Modulos/PedidoVenta/PedidoVentaTests/DetallePedidoViewModelTests.cs`

**Código principal:**
- `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb`
- `Nesto/Modulos/PedidoVenta/PedidoVenta/PedidoVentaService.vb`

---

**Autor:** Claude Code (Anthropic)
**Fecha:** 20 de Noviembre de 2024
**Estado:** 📋 Documento de planificación - Pendiente aprobación para empezar FASE 1
**Contexto:** Continuación del trabajo iniciado el 18/11/2024 sobre CCC en facturación
