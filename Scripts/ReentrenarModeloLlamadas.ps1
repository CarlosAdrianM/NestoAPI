<#
    NestoAPI#619: reentrenamiento mensual del modelo de llamadas de Rapports (sugerencias de contacto, #603) con puerta
    de calidad. Lo lanza la tarea programada «NestoAPI - Reentrenar modelo de llamadas» (primer domingo de cada mes a
    las 02:30; se registra con ReentrenarModeloLlamadas_RegistrarTarea.ps1).

    Qué hace:
      1. Compila ModeloLlamadaPedido en Release (repo local C:\Users\Carlos\source\repos\ModeloLlamadaPedido).
      2. Lo lanza con --completo --guardar --promover (3 años, todos los vendedores; carga la BD de producción NV,
         así que SOLO fuera de horario: entre las 07:00 y las 21:00 se niega salvo con -Forzar).
         El modelo nuevo se compara con el de producción (NestoAPI\ModelsIA\modelo_llamadas.zip) sobre la misma
         validación temporal. Pasa la puerta si su P@20 media no baja más de 1 punto y su AUC no baja.
         Si pasa, el programa copia el zip encima del de NestoAPI y hace commit SOLO de ese fichero (sin push):
         sale a producción con el próximo deploy de la API. Si no pasa, no toca nada.
      3. Guarda el log en C:\Users\Carlos\ModelosML\logs\reentrenar_AAAAMMDD_HHMMSS.log (el README con las métricas
         queda en C:\Users\Carlos\ModelosML junto al zip nuevo).
      4. Avisa en la campana de Nesto (POST api/Notificaciones/AvisoNesto con el windows-token del usuario que lo
         ejecuta, que tiene que ser de Dirección o Informática): promovido, no promovido (motivo) o error (ver log).

    Códigos de salida (los ve el Programador de tareas como «Resultado de la última ejecución»):
      10 promovido · 11 no promovido · 1 error (incluye datos insuficientes, fallo de compilación o del aviso)

    Uso:
      .\ReentrenarModeloLlamadas.ps1                              # lo que hace la tarea
      .\ReentrenarModeloLlamadas.ps1 -Simular                     # todo menos copiar y hacer commit
      # Prueba ligera en horario: un vendedor, pocos meses, contra una COPIA del zip (nunca el de NestoAPI):
      .\ReentrenarModeloLlamadas.ps1 -Vendedor MPP -Meses 6 -ModeloActual C:\temp\prueba\ModelsIA\modelo_llamadas.zip -SinAviso
      # -CorteActual AAAA-MM-DD fuerza desde qué día compara la puerta; -Extra pasa más opciones al programa
      # (separadas por espacios; p. ej. -Extra "--max-bajada-precision 3").
#>
param(
    [string]$RepoModelo = "C:\Users\Carlos\source\repos\ModeloLlamadaPedido",
    [string]$ModeloActual = (Join-Path $PSScriptRoot "..\NestoAPI\ModelsIA\modelo_llamadas.zip"),
    [string]$CarpetaLogs = "C:\Users\Carlos\ModelosML\logs",
    [string]$Vendedor,
    [int]$Meses = 6,
    [string]$CorteActual,
    [string]$Extra,
    [switch]$Simular,
    [switch]$Forzar,
    [switch]$SinAviso,
    [string]$Usuario = $env:USERNAME,
    [string]$Api = "http://api.nuevavision.es"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

New-Item -ItemType Directory -Force -Path $CarpetaLogs | Out-Null
$marca = Get-Date -Format "yyyyMMdd_HHmmss"
$log = Join-Path $CarpetaLogs "reentrenar_$marca.log"
$json = Join-Path $CarpetaLogs "reentrenar_$marca.json"
$ModeloActual = [System.IO.Path]::GetFullPath($ModeloActual)

function Log([string]$texto) {
    Write-Host $texto
    Add-Content -LiteralPath $log -Value $texto -Encoding UTF8
}

function Avisar([string]$texto) {
    Log "Aviso: $texto"
    if ($SinAviso) { Log "(-SinAviso: no se manda a la campana)"; return }
    # PowerShell 7 no manda las credenciales de Windows por http sin este permiso explícito (la API va por http).
    $sinCifrar = @{}
    if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
    $token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
    if (-not $token) { throw "No se ha obtenido el token de Windows." }
    $cuerpo = @{ Usuarios = @($Usuario); Titulo = "Modelo de llamadas"; Texto = $texto } | ConvertTo-Json
    $null = Invoke-RestMethod -Method Post -Uri "$Api/api/Notificaciones/AvisoNesto" `
        -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json; charset=utf-8" `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($cuerpo))
    Log "Aviso mandado a la campana de $Usuario."
}

function Pct($v) { if ($null -eq $v) { "—" } else { ($v * 100).ToString("0.0", [Globalization.CultureInfo]::GetCultureInfo("es-ES")) + " %" } }
function Auc($v) { if ($null -eq $v) { "—" } else { $v.ToString("0.000", [Globalization.CultureInfo]::GetCultureInfo("es-ES")) } }

$codigo = 1
$texto = $null
try {
    Log "Reentrenamiento del modelo de llamadas — $(Get-Date -Format 'dd/MM/yyyy HH:mm') en $env:COMPUTERNAME como $env:USERDOMAIN\$env:USERNAME"
    $hora = (Get-Date).Hour
    if (-not $Vendedor -and -not $Forzar -and $hora -ge 7 -and $hora -lt 21) {
        throw "El reentrenamiento completo carga la BD de producción: solo fuera de horario (antes de las 07:00 o desde las 21:00). Usa -Forzar si de verdad hace falta."
    }

    $proyecto = Join-Path $RepoModelo "ModeloLlamadaPedido\ModeloLlamadaPedido.csproj"
    Log "Compilando $proyecto en Release..."
    & { $ErrorActionPreference = "Continue"; & dotnet build $proyecto -c Release --nologo -v q 2>&1 } | ForEach-Object { Log "$_" }
    if ($LASTEXITCODE -ne 0) { throw "Falla la compilación de ModeloLlamadaPedido (dotnet build → $LASTEXITCODE)." }

    $exe = Join-Path $RepoModelo "ModeloLlamadaPedido\bin\Release\net8.0\ModeloLlamadaPedido.exe"
    $argumentos = @()
    if ($Vendedor) { $argumentos += @("--muestra", $Vendedor, "$Meses") } else { $argumentos += "--completo" }
    $argumentos += @("--guardar", "--promover", "--modelo-actual", $ModeloActual, "--resultado", $json)
    if ($Simular) { $argumentos += "--simular" }
    if ($CorteActual) { $argumentos += @("--corte-actual", $CorteActual) }
    if ($Extra) { $argumentos += @($Extra -split '\s+' | Where-Object { $_ }) }
    Log "> $exe $($argumentos -join ' ')"
    & { $ErrorActionPreference = "Continue"; & $exe @argumentos 2>&1 } | ForEach-Object { Log "$_" }
    $codigo = $LASTEXITCODE
    Log "Código de salida: $codigo"

    $r = if (Test-Path -LiteralPath $json) { Get-Content -Raw -Encoding UTF8 -LiteralPath $json | ConvertFrom-Json } else { $null }
    $cambio = if ($r) { "P@$($r.K) $(Pct $r.PrecisionActual) → $(Pct $r.PrecisionNuevo), AUC $(Auc $r.AucActual) → $(Auc $r.AucNuevo)" } else { "" }
    $prefijo = if ($Simular) { "Modelo de llamadas (simulación)" } elseif ($Vendedor) { "Modelo de llamadas (prueba $Vendedor)" } else { "Modelo de llamadas" }
    $texto = switch ($codigo) {
        10 {
            if ($Simular) { "${prefijo}: pasaría la puerta ($cambio); no se ha copiado ni hecho commit." }
            else { "${prefijo}: promovido ($cambio); sale con el próximo deploy. Commit $($r.Commit) en NestoAPI, sin push." }
        }
        11 { "${prefijo}: no promovido ($($r.Motivo)). El de producción no se toca. Informe: $($r.Readme)" }
        default {
            $motivo = if ($r -and $r.Motivo) { ": $($r.Motivo)" } else { "" }
            $codigo = 1
            "${prefijo}: error$motivo (ver log $log)."
        }
    }
}
catch {
    Log "ERROR: $($_.Exception.Message)"
    $codigo = 1
    $texto = "Modelo de llamadas: error ($($_.Exception.Message)) (ver log $log)."
}

try {
    Avisar $texto
}
catch {
    Log "ERROR al avisar en la campana: $($_.Exception.Message)"
    if ($codigo -ne 1) { $codigo = 1 }
}
Log "Fin: código $codigo."
exit $codigo
