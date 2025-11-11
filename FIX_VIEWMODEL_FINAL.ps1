# Script para arreglar ViewModel - sin emojis

$file = "C:/Users/Carlos/source/repos/Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/FacturarRutasPopupViewModel.vb"
$content = Get-Content $file -Raw

# 1. Arreglar End Function faltante y duplicado
# Primero eliminar la inserción incorrecta del método CargarTiposRuta
$oldBadBlock = @'
    Public Function CanCloseDialog() As Boolean Implements IDialogAware.CanCloseDialog
        Return Not EstaProcesando

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
    End Function    End Function
'@

$newGoodBlock = @'
    Public Function CanCloseDialog() As Boolean Implements IDialogAware.CanCloseDialog
        Return Not EstaProcesando
    End Function

    #End Region

    #Region "Metodos Privados"

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

$content = $content.Replace($oldBadBlock, $newGoodBlock)

# 2. Actualizar OnDialogOpened
$oldOnDialogOpened = @'
    Public Sub OnDialogOpened(parameters As IDialogParameters) Implements IDialogAware.OnDialogOpened
        ' Procesar parámetros si es necesario
    End Sub
'@

$newOnDialogOpened = @'
    Public Async Sub OnDialogOpened(parameters As IDialogParameters) Implements IDialogAware.OnDialogOpened
        ' Cargar tipos de ruta desde la API
        Await CargarTiposRuta()
    End Sub
'@

$content = $content.Replace($oldOnDialogOpened, $newOnDialogOpened)

# Guardar
[System.IO.File]::WriteAllText($file, $content)

Write-Host "ViewModel arreglado exitosamente!"
