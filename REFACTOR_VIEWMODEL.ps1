# Script para refactorizar FacturarRutasPopupViewModel.vb

$file = "C:/Users/Carlos/source/repos/Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/FacturarRutasPopupViewModel.vb"
$content = Get-Content $file -Raw

# Paso 1: Eliminar propiedades booleanas EsRutaPropia y EsRutasAgencias (líneas 76-110 aprox)
$oldBooleanProps = @'
    Private _esRutaPropia As Boolean
    Public Property EsRutaPropia As Boolean
        Get
            Return _esRutaPropia
        End Get
        Set\(value As Boolean\)
            If SetProperty\(_esRutaPropia, value\) Then
                If value Then
                    EsRutasAgencias = False
                End If
                ' Limpiar resumen al cambiar tipo de ruta
                LimpiarResumen\(\)
                VerResumenCommand\.RaiseCanExecuteChanged\(\)
                FacturarCommand\.RaiseCanExecuteChanged\(\)
            End If
        End Set
    End Property

    Private _esRutasAgencias As Boolean
    Public Property EsRutasAgencias As Boolean
        Get
            Return _esRutasAgencias
        End Get
        Set\(value As Boolean\)
            If SetProperty\(_esRutasAgencias, value\) Then
                If value Then
                    EsRutaPropia = False
                End If
                ' Limpiar resumen al cambiar tipo de ruta
                LimpiarResumen\(\)
                VerResumenCommand\.RaiseCanExecuteChanged\(\)
                FacturarCommand\.RaiseCanExecuteChanged\(\)
            End If
        End Set
    End Property
'@

$newProperties = @'
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
'@

$content = $content -replace $oldBooleanProps, $newProperties

# Paso 2: Modificar constructor - cambiar EsRutaPropia = True por inicialización de colección
$content = $content -replace "        ' Por defecto: Ruta propia\s+EsRutaPropia = True", @'
        ' Inicializar colección vacía
        TiposRutaDisponibles = New ObservableCollection(Of TipoRutaInfoDTO)()
'@

# Paso 3: Modificar OnDialogOpened para cargar tipos de ruta
$oldOnDialogOpened = @'
    Public Sub OnDialogOpened\(parameters As IDialogParameters\) Implements IDialogAware\.OnDialogOpened
        ' Procesar parámetros si es necesario
    End Sub
'@

$newOnDialogOpened = @'
    Public Async Sub OnDialogOpened(parameters As IDialogParameters) Implements IDialogAware.OnDialogOpened
        ' Cargar tipos de ruta desde la API
        Await CargarTiposRuta()
    End Sub
'@

$content = $content -replace $oldOnDialogOpened, $newOnDialogOpened

# Paso 4: Agregar método CargarTiposRuta después de CanCloseDialog
$cargarTiposRutaMethod = @'

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
'@

$content = $content -replace '(    End Function\s+#End Region\s+#Region "Propiedades")', "$cargarTiposRutaMethod`$1"

# Paso 5: Modificar CanVerResumen
$content = $content -replace 'Return Not EstaProcesando AndAlso \(EsRutaPropia OrElse EsRutasAgencias\)', 'Return Not EstaProcesando AndAlso TipoRutaSeleccionado IsNot Nothing'

# Paso 6: Modificar CanFacturar
$content = $content -replace 'Return Not EstaProcesando AndAlso \(EsRutaPropia OrElse EsRutasAgencias\) AndAlso PreviewData IsNot Nothing', 'Return Not EstaProcesando AndAlso TipoRutaSeleccionado IsNot Nothing AndAlso PreviewData IsNot Nothing'

# Paso 7: Modificar VerResumen - reemplazar el bloque de determinación de tipo
$oldVerResumenBlock = @'
            ' Determinar tipo de ruta
            Dim tipoRuta As TipoRutaFacturacion = If\(EsRutaPropia,
                TipoRutaFacturacion\.RutaPropia,
                TipoRutaFacturacion\.RutasAgencias\)

            ' Crear request
            Dim request As New FacturarRutasRequestDTO With \{
                \.TipoRuta = tipoRuta,
                \.FechaEntregaDesde = DateTime\.Today
            \}
'@

$newVerResumenBlock = @'
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
'@

$content = $content -replace $oldVerResumenBlock, $newVerResumenBlock

# Paso 8: Modificar FacturarRutas - reemplazar el bloque de determinación de tipo
$oldFacturarBlock = @'
            ' Determinar tipo de ruta
            Dim tipoRuta As TipoRutaFacturacion = If\(EsRutaPropia,
                TipoRutaFacturacion\.RutaPropia,
                TipoRutaFacturacion\.RutasAgencias\)

            ' Crear request
            Dim request As New FacturarRutasRequestDTO With \{
                \.TipoRuta = tipoRuta,
                \.FechaEntregaDesde = DateTime\.Today
            \}
'@

$newFacturarBlock = @'
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
'@

$content = $content -replace $oldFacturarBlock, $newFacturarBlock

# Guardar archivo
[System.IO.File]::WriteAllText($file, $content)

Write-Host "ViewModel refactorizado exitosamente!"
