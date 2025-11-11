# Script simple para arreglar ViewModel

$file = "C:/Users/Carlos/source/repos/Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/FacturarRutasPopupViewModel.vb"
$content = Get-Content $file -Raw -Encoding UTF8

# 1. Arreglar línea 70-73: Agregar End Function y cerrar región
$content = $content -replace `
    "(\s+Public Function CanCloseDialog\(\) As Boolean Implements IDialogAware\.CanCloseDialog\s+Return Not EstaProcesando\s+)(\s*''' <summary>)", `
    "`$1    End Function`r`n`r`n    #End Region`r`n`r`n    #Region `"Métodos Privados`"`r`n`r`n    `$2"

# 2. Eliminar End Function duplicado en línea 110
$content = $content -replace "End Function\s+End Function", "End Function"

# 3. Actualizar OnDialogOpened para llamar a CargarTiposRuta
$content = $content -replace `
    "Public Sub OnDialogOpened\(parameters As IDialogParameters\) Implements IDialogAware\.OnDialogOpened\s+' Procesar par.+metros si es necesario\s+End Sub", `
    "Public Async Sub OnDialogOpened(parameters As IDialogParameters) Implements IDialogAware.OnDialogOpened`r`n        ' Cargar tipos de ruta desde la API`r`n        Await CargarTiposRuta()`r`n    End Sub"

# 4. Arreglar emojis corruptos
$content = $content -replace "Ã¢Å"â€œ", "✓"
$content = $content -replace "Ã°Å¸â€œâ€ž", "📄"
$content = $content -replace "Ã¢Å¡Â ", "⚠"

# Guardar
[System.IO.File]::WriteAllText($file, $content, [System.Text.Encoding]::UTF8)

Write-Host "ViewModel arreglado exitosamente!"
Write-Host "Cambios aplicados:"
Write-Host "- Agregado End Function faltante"
Write-Host "- Eliminado End Function duplicado"
Write-Host "- OnDialogOpened ahora llama a CargarTiposRuta()"
Write-Host "- Emojis corregidos"
