<#
    NestoAPI#776: deja en blanco los códigos de barras que no son válidos (EAN-8, UPC-A, EAN-13 o ITF-14 con su dígito de
    control; la misma regla que el informe semanal «informe-codigos-barras»): POST api/Productos/CodigosBarras/LimpiarNoValidos.
    De todos los productos y empresas, también los no activos. La ficha (Productos.CodBarras) queda a NULL y las filas de
    ProductosCodigosBarras se borran; cada código, con su fila en Modificaciones. Los internos del 29 (#762) no se tocan.
    Necesita el usuario de Windows de alguien de Dirección, Informática o Compras (windows-token).

    Uso:
      .\Issue776_LimpiarCodigosBarras.ps1                 (solo mirar: enseña los candidatos y no toca nada)
      .\Issue776_LimpiarCodigosBarras.ps1 -Aplicar        (los deja en blanco; una segunda pasada ya no encuentra nada)
      .\Issue776_LimpiarCodigosBarras.ps1 -Csv candidatos.csv   (además, guarda los candidatos en un CSV para Excel)
#>
param(
    [string]$Api = "http://api.nuevavision.es",
    [switch]$Aplicar,
    [string]$Csv
)

$ErrorActionPreference = "Stop"
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }

$aplicarTexto = if ($Aplicar) { "true" } else { "false" }
$resultado = Invoke-RestMethod -Method Post -Uri "$Api/api/Productos/CodigosBarras/LimpiarNoValidos?aplicar=$aplicarTexto" -Headers $cabeceras -TimeoutSec 600

$resultado.Candidatos |
    Select-Object Empresa, Producto, @{ n = "Activo"; e = { if ($_.Activo) { "Sí" } else { "No" } } }, Codigo, Donde, Nombre, Motivo |
    Format-Table -AutoSize | Out-String -Width 250 | Write-Host

if ($Csv) {
    $resultado.Candidatos | Select-Object Empresa, Producto, Nombre, Activo, Codigo, Donde, Motivo |
        Export-Csv -Path $Csv -NoTypeInformation -Delimiter ";" -Encoding UTF8
    Write-Host "Candidatos guardados en $Csv"
}

$enFicha = @($resultado.Candidatos | Where-Object { $_.Donde -eq "Ficha" }).Count
$enTabla = @($resultado.Candidatos | Where-Object { $_.Donde -eq "Tabla" }).Count
$activos = @($resultado.Candidatos | Where-Object { $_.Activo }).Count
Write-Host ("Candidatos: {0} ({1} en la ficha, {2} en la tabla; {3} de productos activos)" -f $resultado.Candidatos.Count, $enFicha, $enTabla, $activos)
if ($resultado.Aplicar) {
    Write-Host "Limpiados: $($resultado.Limpiados)" -ForegroundColor Green
}
foreach ($e in $resultado.Errores) { Write-Host "  Error: $e" -ForegroundColor Red }
Write-Host $resultado.Mensaje -ForegroundColor $(if ($resultado.Errores.Count -gt 0) { "Yellow" } elseif ($resultado.Aplicar) { "Green" } else { "Cyan" })
