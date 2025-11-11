# Script para arreglar FacturarRutasPopupViewModel.vb correctamente

$file = "C:/Users/Carlos/source/repos/Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/FacturarRutasPopupViewModel.vb"

# Leer contenido
$content = Get-Content $file -Raw

# PROBLEMA 1: Arreglar línea 70-72 - Falta End Function para CanCloseDialog
$content = $content -replace `
    "Public Function CanCloseDialog\(\) As Boolean Implements IDialogAware\.CanCloseDialog\s+Return Not EstaProcesando\s+''' <summary>", `
    @"
Public Function CanCloseDialog() As Boolean Implements IDialogAware.CanCloseDialog
        Return Not EstaProcesando
    End Function

    #End Region

    #Region "Métodos Privados"

    ''' <summary>
"@

# PROBLEMA 2: Arreglar doble End Function en línea 110
$content = $content -replace "End Function\s+End Function", "End Function"

# PROBLEMA 3: Modificar OnDialogOpened para que llame a CargarTiposRuta
$content = $content -replace `
    "Public Sub OnDialogOpened\(parameters As IDialogParameters\) Implements IDialogAware\.OnDialogOpened\s+' Procesar par.+metros si es necesario\s+End Sub", `
    @"
Public Async Sub OnDialogOpened(parameters As IDialogParameters) Implements IDialogAware.OnDialogOpened
        ' Cargar tipos de ruta desde la API
        Await CargarTiposRuta()
    End Sub
"@

# Guardar
[System.IO.File]::WriteAllText($file, $content)

Write-Host "ViewModel arreglado exitosamente!"
