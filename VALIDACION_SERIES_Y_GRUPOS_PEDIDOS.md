# Validación de Series y Grupos en Pedidos
## Documentación para Implementación - 12 de Enero de 2025

---

## 📋 Resumen Ejecutivo

### Objetivo
Implementar validación para evitar mezclar cursos (exentos de IVA, prorrata) con otros productos (IVA general) en el mismo pedido, basándose en la serie de facturación y el grupo del producto.

### Reglas de Negocio

#### **Series de Facturación:**
- **"CV"** (Constantes.Series.SERIE_CURSOS): Para cursos (exentos de IVA, prorrata)
- **"NV"** (Constantes.Series.SERIE_DEFECTO): Para productos normales (IVA general)
- **"UL"** (Constantes.Series.UNION_LASER): Para distribuidores con productos Union Laser
- **"EV"** (Constantes.Series.EVA_VISNU): Para distribuidores con productos Eva Visnu

#### **Grupos de Productos:**
- **"CUR"**: Cursos (exentos de IVA)
- **Cualquier otro grupo**: Productos normales (con IVA)

---

## 🔍 Investigación Realizada

### 1. Backend (NestoAPI)

#### ✅ ProductoDTO tiene grupo y familia
**Archivo:** `NestoAPI/Models/ProductoDTO.cs`
```csharp
public class ProductoDTO
{
    public string Grupo { get; set; }        // ✅ EXISTE (línea 27)
    public string Subgrupo { get; set; }     // ✅ EXISTE (línea 28)
    public string Familia { get; set; }      // ✅ EXISTE (línea 23)
    // ... otras propiedades
}
```

#### ❌ ProductoPlantillaDTO NO tiene grupo ni familia
**Archivo:** `NestoAPI/Models/NestoDTO.cs` (línea 169)
```csharp
public class ProductoPlantillaDTO
{
    public string producto { get; set; }
    public string nombre { get; set; }
    public decimal precio { get; set; }
    public bool aplicarDescuento { get; set; }
    public decimal descuento { get; set; }
    public string iva { get; set; }
    // ❌ NO TIENE: grupo, subgrupo, familia
}
```

**Usado por:** `ProductosController.GetProducto(empresa, id, cliente, contacto, cantidad)` (línea 216)

#### ✅ LineaPedidoVentaDTO tiene GrupoProducto
**Archivo:** `NestoAPI/Models/PedidosVenta/LineaPedidoVentaDTO.cs`
```csharp
public class LineaPedidoVentaDTO : LineaPedidoBase
{
    public string GrupoProducto { get; set; }      // ✅ EXISTE (línea 17)
    public string SubgrupoProducto { get; set; }   // ✅ EXISTE (línea 21)
    // ... otras propiedades
}
```

### 2. Frontend - PlantillaVenta (Nesto)

#### ❌ Modelo Producto NO tiene grupo ni familia
**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVenta/PedidoVentaModel.vb` (línea 230)
```vb
Public Class Producto
    Public Property producto() As String
    Public Property nombre() As String
    Public Property precio() As Decimal
    Public Property aplicarDescuento() As Boolean
    Public Property Stock() As Integer
    Public Property CantidadReservada() As Integer
    Public Property CantidadDisponible() As Integer
    Public Property descuento() As Decimal
    Public Property iva() As String
    ' ❌ NO TIENE: grupo, subgrupo, familia
End Class
```

#### ✅ LineaPlantillaVenta tiene familia y subGrupo (pero NO grupo)
**Archivo:** `Nesto/Modulos/PlantillaVenta/Models/LineaPlantillaVenta.vb`
```vb
Public Class LineaPlantillaVenta
    Public Property producto() As String
    Public Property familia() As String        ' ✅ EXISTE (línea 38)
    Public Property subGrupo() As String       ' ✅ EXISTE (línea 39)
    ' ❌ NO TIENE: grupo
End Class
```

**Nota:** PlantillaVenta usa `familia` en `CalcularSerie()` (línea 1658) para determinar si es UL o EV, pero NO verifica grupo "CUR".

#### ⚠️ CalcularSerie actual
**Archivo:** `Nesto/Modulos/PlantillaVenta/ViewModels/PlantillaVentaViewModel.vb` (línea 1658)
```vb
Private Function CalcularSerie() As String
    Dim estadosValidos = {Constantes.Clientes.ESTADO_DISTRIBUIDOR,
                          Constantes.Clientes.ESTADO_DISTRIBUIDOR_NO_VISITABLE}
    Return If(estadosValidos.Contains(clienteSeleccionado.estado) AndAlso
              listaProductosPedido.All(Function(l) l.familia = Constantes.Familias.UNION_LASER_NOMBRE),
        Constantes.Series.UNION_LASER,
        If(estadosValidos.Contains(clienteSeleccionado.estado) AndAlso
           listaProductosPedido.All(Function(l) l.familia = Constantes.Familias.EVA_VISNU_NOMBRE),
        Constantes.Series.EVA_VISNU,
        Constantes.Series.SERIE_DEFECTO))
End Function
```

**Lógica actual:**
- Si es distribuidor Y todas las líneas son Union Laser → "UL"
- Si es distribuidor Y todas las líneas son Eva Visnu → "EV"
- En cualquier otro caso → "NV"
- ❌ **NO verifica grupo "CUR" para devolver "CV"**

### 3. Frontend - DetallePedidoVenta (Nesto)

#### ✅ Inicialización de serie correcta
**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb`
- Línea 1104: `.serie = SerieFacturacionDefecto`
- Línea 1146: Lee del parámetro usuario "SerieFacturacionDefecto"

#### ❌ NO hay validación de grupo al añadir líneas
**Método:** `CargarDatosProducto()` (línea 643)
- Solo carga: producto, precio, texto, descuento, iva
- NO carga ni valida: grupo, subgrupo, familia

---

## 🎯 Cambios Necesarios

### FASE 1: Backend - Añadir grupo y familia a ProductoPlantillaDTO

#### 1.1. Actualizar ProductoPlantillaDTO
**Archivo:** `NestoAPI/Models/NestoDTO.cs` (línea 169)

```csharp
public class ProductoPlantillaDTO
{
    // Propiedades existentes
    public string producto { get; set; }
    public string nombre { get; set; }
    public decimal precio { get; set; }
    public bool aplicarDescuento { get; set; }
    public decimal descuento { get; set; }
    public string iva { get; set; }

    // ✨ NUEVAS PROPIEDADES
    public string grupo { get; set; }
    public string subgrupo { get; set; }
    public string familia { get; set; }
}
```

#### 1.2. Actualizar ProductosController.GetProducto()
**Archivo:** `NestoAPI/Controllers/ProductosController.cs` (línea 228)

```csharp
ProductoPlantillaDTO productoDTO = new ProductoPlantillaDTO()
{
    producto = producto.Número.Trim(),
    nombre = producto.Nombre.Trim(),
    precio = (decimal)producto.PVP,
    aplicarDescuento = producto.Aplicar_Dto,
    iva = producto.IVA_Repercutido,

    // ✨ NUEVOS CAMPOS
    grupo = producto.Grupo?.Trim(),
    subgrupo = producto.SubGrupo?.Trim(),
    familia = producto.Familia?.Trim()
};
```

### FASE 2: Frontend - Actualizar modelo Producto

#### 2.1. Actualizar clase Producto
**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVenta/PedidoVentaModel.vb` (línea 230)

```vb
Public Class Producto
    ' Propiedades existentes
    Public Property producto() As String
    Public Property nombre() As String
    Public Property precio() As Decimal
    Public Property aplicarDescuento() As Boolean
    Public Property Stock() As Integer
    Public Property CantidadReservada() As Integer
    Public Property CantidadDisponible() As Integer
    Public Property descuento() As Decimal
    Public Property iva() As String

    ' ✨ NUEVAS PROPIEDADES
    Public Property grupo() As String
    Public Property subgrupo() As String
    Public Property familia() As String
End Class
```

#### 2.2. Actualizar LineaPlantillaVenta
**Archivo:** `Nesto/Modulos/PlantillaVenta/Models/LineaPlantillaVenta.vb`

```vb
Public Class LineaPlantillaVenta
    ' ... propiedades existentes ...
    Public Property familia() As String        ' ✅ Ya existe
    Public Property subGrupo() As String       ' ✅ Ya existe

    ' ✨ NUEVA PROPIEDAD
    Public Property grupo() As String
End Class
```

#### 2.3. Actualizar LineaPedidoVentaWrapper (DetallePedidoVenta)
**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVenta/Models/LineaPedidoVentaWrapper.vb`

**Verificar si ya tiene:**
- `GrupoProducto` (probablemente sí, porque el DTO del backend ya lo tiene)
- `FamiliaProducto` o `Familia`

**Si no las tiene, añadirlas:**
```vb
Public Property GrupoProducto As String
Public Property SubgrupoProducto As String
Public Property FamiliaProducto As String
```

### FASE 3: Implementar Validación en DetallePedidoViewModel

#### 3.1. Método auxiliar para validar grupo vs serie
**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb`

```vb
''' <summary>
''' Valida si se puede añadir una línea con el grupo especificado según la serie actual.
''' Carlos 12/01/25: No se pueden mezclar cursos (CUR) con otros productos.
''' </summary>
''' <param name="grupoProducto">Grupo del producto a añadir</param>
''' <param name="tipoLinea">Tipo de línea (1=Producto, 2=CuentaContable, etc)</param>
''' <returns>True si es válido, False si hay conflicto</returns>
Private Function ValidarGrupoContraSerie(grupoProducto As String, tipoLinea As Byte?) As Boolean
    ' Si no hay líneas todavía, siempre es válido
    If pedido.Lineas.Count = 0 Then
        Return True
    End If

    Dim esProductoCurso As Boolean = (grupoProducto?.Trim().ToUpper() = "CUR")
    Dim hayLineasCurso As Boolean = pedido.Lineas.Any(Function(l)
        l.GrupoProducto?.Trim().ToUpper() = "CUR" AndAlso l.tipoLinea = 1)
    Dim hayLineasNoCurso As Boolean = pedido.Lineas.Any(Function(l)
        l.GrupoProducto?.Trim().ToUpper() <> "CUR" AndAlso l.tipoLinea = 1)

    ' Caso 1: Intentamos añadir un curso
    If esProductoCurso Then
        ' Si ya hay productos NO curso → conflicto
        If hayLineasNoCurso Then
            Return False
        End If
        ' Si solo hay cursos o no hay líneas → OK
        Return True
    End If

    ' Caso 2: Intentamos añadir un producto NO curso
    ' Si ya hay cursos → conflicto
    If hayLineasCurso Then
        Return False
    End If

    ' Si solo hay NO cursos o no hay líneas → OK
    Return True
End Function

''' <summary>
''' Pregunta al usuario si desea cambiar la serie del pedido.
''' Carlos 12/01/25: Cuando la serie no coincide con el tipo de producto.
''' </summary>
''' <param name="grupoProducto">Grupo del producto</param>
''' <param name="serieActual">Serie actual del pedido</param>
''' <returns>True si el usuario acepta el cambio, False si cancela</returns>
Private Async Function PreguntarCambioSerie(grupoProducto As String, serieActual As String) As Task(Of Boolean)
    Dim esProductoCurso As Boolean = (grupoProducto?.Trim().ToUpper() = "CUR")

    Dim mensaje As String
    Dim nuevaSerie As String

    If esProductoCurso AndAlso serieActual <> Constantes.Series.SERIE_CURSOS Then
        ' Queremos añadir un curso pero la serie es NV
        mensaje = $"Este producto es un curso (grupo CUR)." & vbCrLf &
                  $"La serie actual es '{serieActual}' pero para cursos debe ser 'CV'." & vbCrLf &
                  "¿Desea cambiar la serie a 'CV'?"
        nuevaSerie = Constantes.Series.SERIE_CURSOS
    ElseIf Not esProductoCurso AndAlso serieActual = Constantes.Series.SERIE_CURSOS Then
        ' Queremos añadir un NO curso pero la serie es CV
        mensaje = $"Este producto NO es un curso (grupo {grupoProducto})." & vbCrLf &
                  $"La serie actual es 'CV' (para cursos)." & vbCrLf &
                  "¿Desea cambiar la serie a 'NV'?"
        nuevaSerie = Constantes.Series.SERIE_DEFECTO
    Else
        ' No hay conflicto
        Return True
    End If

    Dim confirmar As Boolean = Await dialogService.ShowConfirmationAsync("Cambio de Serie", mensaje)

    If confirmar Then
        pedido.Model.serie = nuevaSerie
        Return True
    End If

    Return False
End Function

''' <summary>
''' Determina el grupo y familia para líneas de tipo Cuenta Contable (tipoLinea = 2).
''' Carlos 12/01/25: Para líneas sin producto, el grupo depende de la serie.
''' </summary>
''' <returns>Tupla con (grupo, familia) o Nothing si el usuario cancela</returns>
Private Async Function DeterminarGrupoYFamiliaParaCuentaContable() As Task(Of (grupo As String, familia As String)?)
    ' Si la serie es CV (cursos), asignamos directamente CUR
    If pedido.Model.serie = Constantes.Series.SERIE_CURSOS Then
        Return ("CUR", "Cursos")
    End If

    ' Si la serie es otra, preguntamos al usuario
    ' TODO: Implementar diálogo para seleccionar grupo y familia
    ' Por ahora, podemos usar un diálogo simple de confirmación
    Dim mensaje As String = "Esta línea es de tipo Cuenta Contable." & vbCrLf &
                           "Debe especificar el grupo y familia." & vbCrLf & vbCrLf &
                           "¿Es para cursos (CUR)?"

    Dim esCurso As Boolean = Await dialogService.ShowConfirmationAsync("Grupo y Familia", mensaje)

    If esCurso Then
        Return ("CUR", "Cursos")
    Else
        ' TODO: Aquí debería abrirse un diálogo más completo para elegir grupo/familia
        ' Por ahora, devolvemos valores por defecto
        Return (Nothing, Nothing) ' El usuario tendrá que especificarlo manualmente
    End If
End Function
```

#### 3.2. Modificar CargarDatosProducto()
**Archivo:** `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb` (línea 643)

```vb
Private Async Function CargarDatosProducto(numeroProducto As String, cantidad As Short) As Task
    Dim lineaCambio As LineaPedidoVentaWrapper = lineaActual
    Dim producto As Producto = Await servicio.cargarProducto(pedido.empresa, numeroProducto,
                                                             pedido.cliente, pedido.contacto, cantidad)
    If Not IsNothing(producto) Then
        ' ✨ NUEVA VALIDACIÓN: Verificar grupo vs serie
        If Not ValidarGrupoContraSerie(producto.grupo, 1) Then ' tipoLinea = 1 (Producto)
            ' Hay conflicto - no se puede añadir
            dialogService.ShowError("No se pueden mezclar cursos (grupo CUR) con otros productos en el mismo pedido.")
            lineaCambio.Producto = String.Empty
            Return
        End If

        ' ✨ NUEVA VALIDACIÓN: Si no hay líneas, verificar si necesita cambio de serie
        If pedido.Lineas.Count = 0 Then
            Dim puedeAnadir As Boolean = Await PreguntarCambioSerie(producto.grupo, pedido.Model.serie)
            If Not puedeAnadir Then
                lineaCambio.Producto = String.Empty
                Return
            End If
        End If

        ' Código existente
        If lineaCambio.Producto <> producto.producto Then
            lineaCambio.Producto = producto.producto
        End If
        lineaCambio.PrecioUnitario = producto.precio
        lineaCambio.texto = producto.nombre
        lineaCambio.AplicarDescuento = producto.aplicarDescuento
        lineaCambio.DescuentoProducto = producto.descuento
        lineaCambio.iva = producto.iva

        ' ✨ NUEVOS CAMPOS
        lineaCambio.GrupoProducto = producto.grupo
        lineaCambio.SubgrupoProducto = producto.subgrupo
        lineaCambio.FamiliaProducto = producto.familia

        If IsNothing(lineaCambio.Usuario) Then
            lineaCambio.Usuario = configuracion.usuario
        End If
    End If
    If pedido.EsPresupuesto Then
        lineaCambio.estado = -3
    End If
End Function
```

#### 3.3. Añadir validación para líneas de tipo Cuenta Contable (tipoLinea = 2)
**Nota:** Comentado hasta que se active esta funcionalidad

```vb
' TODO: Descomentar cuando se permitan líneas de tipo Cuenta Contable
'
' Private Async Function ValidarLineaCuentaContable(linea As LineaPedidoVentaWrapper) As Task(Of Boolean)
'     ' Si la línea no tiene grupo, determinarlo
'     If String.IsNullOrWhiteSpace(linea.GrupoProducto) Then
'         Dim grupoFamilia = Await DeterminarGrupoYFamiliaParaCuentaContable()
'         If Not grupoFamilia.HasValue Then
'             ' Usuario canceló
'             Return False
'         End If
'         linea.GrupoProducto = grupoFamilia.Value.grupo
'         linea.FamiliaProducto = grupoFamilia.Value.familia
'     End If
'
'     ' Validar que el grupo sea compatible con las líneas existentes
'     If Not ValidarGrupoContraSerie(linea.GrupoProducto, 2) Then
'         dialogService.ShowError("No se pueden mezclar cursos (grupo CUR) con otros productos en el mismo pedido.")
'         Return False
'     End If
'
'     Return True
' End Function
```

### FASE 4: Implementar Validación en PlantillaVentaViewModel

#### 4.1. Actualizar CalcularSerie()
**Archivo:** `Nesto/Modulos/PlantillaVenta/ViewModels/PlantillaVentaViewModel.vb` (línea 1658)

```vb
Private Function CalcularSerie() As String
    Dim estadosValidos = {Constantes.Clientes.ESTADO_DISTRIBUIDOR,
                          Constantes.Clientes.ESTADO_DISTRIBUIDOR_NO_VISITABLE}

    ' ✨ NUEVA LÓGICA: Si todas las líneas son cursos (grupo CUR) → CV
    If listaProductosPedido.Count > 0 AndAlso
       listaProductosPedido.All(Function(l) l.grupo?.Trim().ToUpper() = "CUR") Then
        Return Constantes.Series.SERIE_CURSOS
    End If

    ' Lógica existente para UL y EV (solo si NO son cursos)
    Return If(estadosValidos.Contains(clienteSeleccionado.estado) AndAlso
              listaProductosPedido.Where(Function(l) l.precio <> 0 AndAlso
                                                     l.descuento <> 1 AndAlso
                                                     l.descuentoProducto <> 1).
                                   All(Function(l) l.familia = Constantes.Familias.UNION_LASER_NOMBRE),
        Constantes.Series.UNION_LASER,
        If(estadosValidos.Contains(clienteSeleccionado.estado) AndAlso
           listaProductosPedido.Where(Function(l) l.precio <> 0 AndAlso
                                                   l.descuento <> 1 AndAlso
                                                   l.descuentoProducto <> 1).
                              All(Function(l) l.familia = Constantes.Familias.EVA_VISNU_NOMBRE),
        Constantes.Series.EVA_VISNU,
        Constantes.Series.SERIE_DEFECTO))
End Function
```

#### 4.2. Añadir validación al insertar productos
**Archivo:** `Nesto/Modulos/PlantillaVenta/ViewModels/PlantillaVentaViewModel.vb`

Buscar dónde se añaden productos a `listaProductosPedido` y añadir validación similar a DetallePedidoViewModel.

**Métodos a modificar:**
- `OnInsertarProducto()` (línea 1680)
- Cualquier otro método que añada líneas

```vb
Private Sub OnInsertarProducto(arg As Object)
    ' Código existente para verificar si el producto ya está
    If IsNothing(arg) OrElse Not IsNothing(ListaFiltrableProductos.ListaOriginal.
                                           Where(Function(p) CType(p, LineaPlantillaVenta).producto = arg.producto).
                                           FirstOrDefault) Then
        Return
    End If

    ' ✨ NUEVA VALIDACIÓN: Verificar grupo antes de añadir
    Dim lineaNueva As LineaPlantillaVenta = CType(arg, LineaPlantillaVenta)

    ' Validar grupo vs serie
    If Not ValidarGrupoContraSerieEnPlantilla(lineaNueva.grupo) Then
        dialogService.ShowError("No se pueden mezclar cursos (grupo CUR) con otros productos en el mismo pedido.")
        Return
    End If

    ' Si es la primera línea, verificar si necesita cambio de serie
    If listaProductosPedido.Count = 0 Then
        ' Aquí podríamos preguntar si desea cambiar la serie
        ' pero en PlantillaVenta la serie se calcula automáticamente con CalcularSerie()
        ' así que no es necesario preguntar
    End If

    ' Código existente
    ListaFiltrableProductos.ListaOriginal.Add(arg)
    RaisePropertyChanged(NameOf(baseImponiblePedido))
End Sub

Private Function ValidarGrupoContraSerieEnPlantilla(grupoProducto As String) As Boolean
    ' Si no hay líneas todavía, siempre es válido
    If listaProductosPedido.Count = 0 Then
        Return True
    End If

    Dim esProductoCurso As Boolean = (grupoProducto?.Trim().ToUpper() = "CUR")
    Dim hayLineasCurso As Boolean = listaProductosPedido.Any(Function(l)
        l.grupo?.Trim().ToUpper() = "CUR")
    Dim hayLineasNoCurso As Boolean = listaProductosPedido.Any(Function(l)
        l.grupo?.Trim().ToUpper() <> "CUR")

    ' Caso 1: Intentamos añadir un curso
    If esProductoCurso Then
        ' Si ya hay productos NO curso → conflicto
        If hayLineasNoCurso Then
            Return False
        End If
        Return True
    End If

    ' Caso 2: Intentamos añadir un producto NO curso
    ' Si ya hay cursos → conflicto
    If hayLineasCurso Then
        Return False
    End If

    Return True
End Function
```

---

## 📝 Notas Importantes

### Diferencias entre PlantillaVenta y DetallePedidoVenta

| Aspecto | PlantillaVenta | DetallePedidoVenta |
|---------|----------------|-------------------|
| Modelo de línea | `LineaPlantillaVenta` | `LineaPedidoVentaWrapper` |
| Tiene `grupo` | ❌ NO (solo tiene `subGrupo`) | ✅ SÍ (como `GrupoProducto`) |
| Tiene `familia` | ✅ SÍ | ⚠️ Verificar |
| Serie inicial | Calculada con `CalcularSerie()` | Del parámetro usuario |
| Validación actual | Por `familia` (UL, EV) | Ninguna |

### Orden de Implementación Recomendado

1. ✅ **Backend primero**: Añadir grupo/subgrupo/familia a ProductoPlantillaDTO y controller
2. ✅ **Modelo Producto frontend**: Añadir propiedades al modelo Producto
3. ✅ **DetallePedidoViewModel**: Implementar validación completa
4. ✅ **LineaPlantillaVenta**: Añadir propiedad `grupo`
5. ✅ **PlantillaVentaViewModel**: Actualizar `CalcularSerie()` y añadir validación

### Testing

#### Escenarios a probar:

**DetallePedidoVenta:**
1. Usuario con serie "NV" añade producto normal → ✅ OK
2. Usuario con serie "NV" añade curso (CUR) como primera línea → ❓ Pregunta si cambiar a "CV"
3. Usuario con serie "CV" añade curso → ✅ OK
4. Usuario con serie "CV" añade producto normal como primera línea → ❓ Pregunta si cambiar a "NV"
5. Usuario con líneas normales intenta añadir curso → ❌ Error
6. Usuario con líneas de curso intenta añadir producto normal → ❌ Error

**PlantillaVenta:**
1. Cliente normal añade productos normales → ✅ Calcula "NV"
2. Cliente añade solo cursos → ✅ Calcula "CV"
3. Distribuidor añade solo Union Laser → ✅ Calcula "UL"
4. Distribuidor añade solo Eva Visnu → ✅ Calcula "EV"
5. Cliente intenta mezclar cursos con productos normales → ❌ Error al añadir

---

## 🚀 Próximos Pasos (Para Mañana)

### ☑️ Checklist de Implementación

- [ ] **Backend**
  - [ ] Añadir `grupo`, `subgrupo`, `familia` a `ProductoPlantillaDTO`
  - [ ] Actualizar `ProductosController.GetProducto()` para llenar nuevos campos
  - [ ] Compilar y verificar

- [ ] **Frontend - Modelos**
  - [ ] Añadir `grupo`, `subgrupo`, `familia` a clase `Producto` (PedidoVentaModel.vb)
  - [ ] Añadir `grupo` a `LineaPlantillaVenta`
  - [ ] Verificar que `LineaPedidoVentaWrapper` tenga `GrupoProducto`, `SubgrupoProducto`, `FamiliaProducto`

- [ ] **Frontend - DetallePedidoViewModel**
  - [ ] Implementar `ValidarGrupoContraSerie()`
  - [ ] Implementar `PreguntarCambioSerie()`
  - [ ] Implementar `DeterminarGrupoYFamiliaParaCuentaContable()` (comentado)
  - [ ] Modificar `CargarDatosProducto()` para validar y llenar nuevos campos
  - [ ] Testing manual

- [ ] **Frontend - PlantillaVentaViewModel**
  - [ ] Actualizar `CalcularSerie()` para detectar grupo "CUR"
  - [ ] Implementar `ValidarGrupoContraSerieEnPlantilla()`
  - [ ] Modificar `OnInsertarProducto()` para validar antes de añadir
  - [ ] Testing manual

- [ ] **Testing E2E**
  - [ ] Probar todos los escenarios listados arriba
  - [ ] Verificar que los mensajes de error sean claros
  - [ ] Verificar que los cambios de serie funcionen correctamente

---

## 📚 Referencias

### Constantes Relevantes
**Archivo:** `Nesto/Infrastructure/Shared/Constantes.cs`
```csharp
public class Series
{
    public const string SERIE_CURSOS = "CV";      // Para cursos
    public const string SERIE_DEFECTO = "NV";     // Por defecto
    public const string UNION_LASER = "UL";       // Union Laser
    public const string EVA_VISNU = "EV";         // Eva Visnu
}
```

### Grupos de Productos
- **"CUR"**: Cursos (exentos de IVA, prorrata)
- **Otros**: Productos normales (IVA general)

---

**Documentado por:** Claude Code
**Fecha:** 12 de Enero de 2025
**Estado:** ✅ Listo para implementar mañana
