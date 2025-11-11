# Cambios Pendientes en Frontend Nesto - Facturación Dinámica de Rutas

## ✅ Backend Completado

Todo el backend (NestoAPI) está listo y funcionando:
- ✅ Nuevo endpoint GET `/api/FacturacionRutas/TiposRuta`
- ✅ TipoRutaInfoDTO creado
- ✅ FacturarRutasRequestDTO ahora usa `string TipoRuta` en lugar de enum
- ✅ Servicio adaptado para usar string

## ⚠️ Cambios Pendientes en Frontend (Nesto)

### 1. Actualizar `FacturarRutasRequestDTO.vb`

**Archivo**: `Nesto/Modulos/PedidoVenta/PedidoVenta/Models/Facturas/FacturarRutasRequestDTO.vb`

**Cambiar de**:
```vb
''' <summary>
''' Request para facturación masiva de pedidos por rutas
''' </summary>
Public Class FacturarRutasRequestDTO
    ''' <summary>
    ''' Tipo de ruta a facturar (propia o agencias)
    ''' </summary>
    Public Property TipoRuta As TipoRutaFacturacion

    ''' <summary>
    ''' Fecha de entrega desde la cual filtrar pedidos.
    ''' Si es Nothing, usa DateTime.Today
    ''' </summary>
    Public Property FechaEntregaDesde As DateTime?
End Class
```

**Cambiar a**:
```vb
Imports System

Namespace Models.Facturas
    ''' <summary>
    ''' Request para facturación masiva de pedidos por rutas.
    ''' REFACTORIZACIÓN: TipoRuta ahora es String (Id del tipo) en lugar de enum.
    ''' </summary>
    Public Class FacturarRutasRequestDTO
        ''' <summary>
        ''' Id del tipo de ruta a facturar (ej: "PROPIA", "AGENCIA")
        ''' </summary>
        Public Property TipoRuta As String

        ''' <summary>
        ''' Fecha de entrega desde la cual filtrar pedidos.
        ''' Si es Nothing, usa DateTime.Today
        ''' </summary>
        Public Property FechaEntregaDesde As DateTime?
    End Class
End Namespace
```

---

### 2. Refactorizar `FacturarRutasPopupViewModel.vb`

**Archivo**: `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/FacturarRutasPopupViewModel.vb`

#### A. Agregar imports necesarios

```vb
Imports System.Net.Http
Imports Newtonsoft.Json
```

#### B. Eliminar propiedades booleanas (líneas 76-110)

**ELIMINAR**:
```vb
Private _esRutaPropia As Boolean
Public Property EsRutaPropia As Boolean
    Get
        Return _esRutaPropia
    End Get
    Set(value As Boolean)
        If SetProperty(_esRutaPropia, value) Then
            If value Then
                EsRutasAgencias = False
            End If
            LimpiarResumen()
            VerResumenCommand.RaiseCanExecuteChanged()
            FacturarCommand.RaiseCanExecuteChanged()
        End If
    End Set
End Property

Private _esRutasAgencias As Boolean
Public Property EsRutasAgencias As Boolean
    ' ... código similar
End Property
```

#### C. Agregar nuevas propiedades (después de línea 74)

```vb
Private _tiposRutaDisponibles As ObservableCollection(Of TipoRutaInfoDTO)
''' <summary>
''' Lista de tipos de ruta disponibles, cargada dinámicamente desde la API
''' </summary>
Public Property TiposRutaDisponibles As ObservableCollection(Of TipoRutaInfoDTO)
    Get
        Return _tiposRutaDisponibles
    End Get
    Set(value As ObservableCollection(Of TipoRutaInfoDTO))
        SetProperty(_tiposRutaDisponibles, value)
    End Set
End Property

Private _tipoRutaSeleccionado As TipoRutaInfoDTO
''' <summary>
''' Tipo de ruta actualmente seleccionado por el usuario
''' </summary>
Public Property TipoRutaSeleccionado As TipoRutaInfoDTO
    Get
        Return _tipoRutaSeleccionado
    End Get
    Set(value As TipoRutaInfoDTO)
        If SetProperty(_tipoRutaSeleccionado, value) Then
            LimpiarResumen()
            VerResumenCommand.RaiseCanExecuteChanged()
            FacturarCommand.RaiseCanExecuteChanged()
        End If
    End Set
End Property
```

#### D. Modificar Constructor (línea 32-46)

**Cambiar**:
```vb
' Por defecto: Ruta propia
EsRutaPropia = True
```

**Por**:
```vb
' Inicializar colección vacía
TiposRutaDisponibles = New ObservableCollection(Of TipoRutaInfoDTO)()
```

#### E. Modificar OnDialogOpened (línea 64-66)

**Cambiar**:
```vb
Public Sub OnDialogOpened(parameters As IDialogParameters) Implements IDialogAware.OnDialogOpened
    ' Procesar parámetros si es necesario
End Sub
```

**Por**:
```vb
Public Async Sub OnDialogOpened(parameters As IDialogParameters) Implements IDialogAware.OnDialogOpened
    ' Cargar tipos de ruta desde la API
    Await CargarTiposRuta()
End Sub
```

#### F. Agregar método CargarTiposRuta (después de línea 72)

```vb
''' <summary>
''' Carga los tipos de ruta disponibles desde la API
''' </summary>
Private Async Function CargarTiposRuta() As Task
    Try
        Dim baseUrl As String = configuracion.Leer("ServidorAPI")
        If String.IsNullOrEmpty(baseUrl) Then
            Throw New InvalidOperationException("No se pudo obtener la URL del servidor API")
        End If

        Using client As New HttpClient()
            ' Configurar autenticación si es necesaria
            ' client.DefaultRequestHeaders.Authorization = ...

            Dim url As String = $"{baseUrl}/api/FacturacionRutas/TiposRuta"
            Dim response = Await client.GetAsync(url)

            If response.IsSuccessStatusCode Then
                Dim json = Await response.Content.ReadAsStringAsync()
                Dim tipos = JsonConvert.DeserializeObject(Of List(Of TipoRutaInfoDTO))(json)

                TiposRutaDisponibles.Clear()
                For Each tipo In tipos
                    TiposRutaDisponibles.Add(tipo)
                Next

                ' Seleccionar el primero por defecto
                If TiposRutaDisponibles.Count > 0 Then
                    TipoRutaSeleccionado = TiposRutaDisponibles(0)
                End If
            Else
                Throw New HttpRequestException($"Error al cargar tipos de ruta: {response.StatusCode}")
            End If
        End Using

    Catch ex As Exception
        MensajeEstado = $"Error al cargar tipos de ruta: {ex.Message}"
        ColorMensaje = Brushes.Red
        dialogService.ShowError($"No se pudieron cargar los tipos de ruta: {ex.Message}")
    End Try
End Function
```

#### G. Modificar CanVerResumen (línea 173-175)

**Cambiar**:
```vb
Private Function CanVerResumen() As Boolean
    Return Not EstaProcesando AndAlso (EsRutaPropia OrElse EsRutasAgencias)
End Function
```

**Por**:
```vb
Private Function CanVerResumen() As Boolean
    Return Not EstaProcesando AndAlso TipoRutaSeleccionado IsNot Nothing
End Function
```

#### H. Modificar VerResumen (líneas 177-214)

**Cambiar**:
```vb
' Determinar tipo de ruta
Dim tipoRuta As TipoRutaFacturacion = If(EsRutaPropia,
    TipoRutaFacturacion.RutaPropia,
    TipoRutaFacturacion.RutasAgencias)

' Crear request
Dim request As New FacturarRutasRequestDTO With {
    .TipoRuta = tipoRuta,
    .FechaEntregaDesde = DateTime.Today
}
```

**Por**:
```vb
' Validar que hay un tipo seleccionado
If TipoRutaSeleccionado Is Nothing Then
    MensajeEstado = "Por favor seleccione un tipo de ruta"
    ColorMensaje = Brushes.Orange
    Return
End If

' Crear request con el ID del tipo seleccionado
Dim request As New FacturarRutasRequestDTO With {
    .TipoRuta = TipoRutaSeleccionado.Id,
    .FechaEntregaDesde = DateTime.Today
}
```

#### I. Modificar CanFacturar (línea 216-218)

**Cambiar**:
```vb
Private Function CanFacturar() As Boolean
    Return Not EstaProcesando AndAlso (EsRutaPropia OrElse EsRutasAgencias) AndAlso PreviewData IsNot Nothing
End Function
```

**Por**:
```vb
Private Function CanFacturar() As Boolean
    Return Not EstaProcesando AndAlso TipoRutaSeleccionado IsNot Nothing AndAlso PreviewData IsNot Nothing
End Function
```

#### J. Modificar FacturarRutas (líneas 220-268)

**Cambiar**:
```vb
' Determinar tipo de ruta
Dim tipoRuta As TipoRutaFacturacion = If(EsRutaPropia,
    TipoRutaFacturacion.RutaPropia,
    TipoRutaFacturacion.RutasAgencias)

' Crear request
Dim request As New FacturarRutasRequestDTO With {
    .TipoRuta = tipoRuta,
    .FechaEntregaDesde = DateTime.Today
}
```

**Por**:
```vb
' Validar que hay un tipo seleccionado
If TipoRutaSeleccionado Is Nothing Then
    MensajeEstado = "Por favor seleccione un tipo de ruta"
    ColorMensaje = Brushes.Orange
    Return
End If

' Crear request con el ID del tipo seleccionado
Dim request As New FacturarRutasRequestDTO With {
    .TipoRuta = TipoRutaSeleccionado.Id,
    .FechaEntregaDesde = DateTime.Today
}
```

---

### 3. Actualizar `FacturarRutasPopup.xaml`

**Archivo**: `Nesto/Modulos/PedidoVenta/PedidoVenta/Views/FacturarRutasPopup.xaml`

#### Reemplazar el StackPanel de opciones (líneas 24-35)

**Eliminar**:
```xml
<!-- Opciones de ruta -->
<StackPanel Grid.Row="1" Margin="0,0,0,20">
    <RadioButton Content="Ruta propia (16, AT)"
                 IsChecked="{Binding EsRutaPropia}"
                 Margin="0,0,0,10"
                 FontSize="13"
                 GroupName="TipoRuta"/>
    <RadioButton Content="Rutas de agencias (FW, 00)"
                 IsChecked="{Binding EsRutasAgencias}"
                 FontSize="13"
                 GroupName="TipoRuta"/>
</StackPanel>
```

**Reemplazar por**:
```xml
<!-- Opciones de ruta DINÁMICAS -->
<ItemsControl Grid.Row="1"
              ItemsSource="{Binding TiposRutaDisponibles}"
              Margin="0,0,0,20">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <RadioButton Content="{Binding DisplayText}"
                         IsChecked="{Binding Path=DataContext.TipoRutaSeleccionado,
                                           RelativeSource={RelativeSource AncestorType=UserControl},
                                           Converter={StaticResource EqualityConverter},
                                           ConverterParameter={Binding}}"
                         GroupName="TipoRuta"
                         Margin="0,0,0,10"
                         FontSize="13"
                         Tag="{Binding}"
                         Command="{Binding Path=DataContext.SeleccionarTipoRutaCommand,
                                         RelativeSource={RelativeSource AncestorType=UserControl}}"
                         CommandParameter="{Binding}"/>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**ALTERNATIVA MÁS SIMPLE** (si no quieres usar el converter):

```xml
<!-- Opciones de ruta DINÁMICAS (versión simple con ListBox) -->
<ListBox Grid.Row="1"
         ItemsSource="{Binding TiposRutaDisponibles}"
         SelectedItem="{Binding TipoRutaSeleccionado}"
         Margin="0,0,0,20"
         BorderThickness="0"
         Background="Transparent">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <RadioButton Content="{Binding DisplayText}"
                         IsChecked="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}"
                         GroupName="TipoRuta"
                         FontSize="13"/>
        </DataTemplate>
    </ListBox.ItemTemplate>
    <ListBox.ItemContainerStyle>
        <Style TargetType="ListBoxItem">
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <ContentPresenter Margin="0,0,0,10"/>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </ListBox.ItemContainerStyle>
</ListBox>
```

---

### 4. Opcional: Eliminar archivo obsoleto

**Archivo**: `Nesto/Modulos/PedidoVenta/PedidoVenta/Models/Facturas/TipoRutaFacturacion.vb`

Este archivo del enum ya no se necesita y puede ser eliminado del proyecto una vez que todo compile sin errores.

---

## 📊 Resumen de Cambios

### Backend ✅ COMPLETO
- Endpoint dinámico para obtener tipos de ruta
- TipoRutaInfoDTO para transferir información
- Request usa string en lugar de enum

### Frontend ⚠️ PENDIENTE
- DTO actualizado a string
- ViewModel carga tipos dinámicamente desde API
- XAML genera RadioButtons dinámicamente

---

## 🎯 Beneficio Final

Una vez aplicados estos cambios, agregar un nuevo tipo de ruta (ej: "RUTA EXPRESS") solo requiere:

1. Crear clase `RutaExpress.cs` en NestoAPI
2. Registrarla en `TipoRutaFactory`
3. ¡Listo! Automáticamente aparece en la UI sin tocar frontend

---

**Nota**: El archivo `TipoRutaInfoDTO.vb` ya fue creado en:
`Nesto/Modulos/PedidoVenta/PedidoVenta/Models/Facturas/TipoRutaInfoDTO.vb`
