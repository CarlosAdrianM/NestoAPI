<#
    Ritual del deploy de Nesto (Carlos, 25/09/26): después de publicar la ClickOnce y de que Carlos compruebe que
    actualiza sin error, avisa en la campana a los que tienen Nesto abierto de que cierren y vuelvan a abrir
    cuando les venga bien. Los que no lo tienen abierto se actualizan solos al abrirlo, así que no se les avisa.

    NestoAPI#568: el aviso va POR TANDAS. RDS2016 tiene dos núcleos y, el 29/09/26, trece usuarios actualizando a
    la vez lo dejaron lentísimo. Por defecto, 4 usuarios cada 3 minutos.

    Uso (siempre DESPUÉS del visto bueno de Carlos):
      .\AvisarNuevaVersionNesto.ps1 -Version 1.10.32.0                          # 4 usuarios cada 3 minutos
      .\AvisarNuevaVersionNesto.ps1 -Version 1.10.32.0 -Tanda 3 -PausaMinutos 5
      .\AvisarNuevaVersionNesto.ps1 -Version 1.10.32.0 -AlFinal Andrey,Pedro    # estos, en la última tanda
      .\AvisarNuevaVersionNesto.ps1 -Version 1.10.32.0 -TodosALaVez             # corrección urgente
      .\AvisarNuevaVersionNesto.ps1 -Version 1.10.32.0 -Texto "Texto distinto del de siempre"

    Necesita el usuario de Windows de alguien de Dirección o Informática (pide el windows-token con él).
    Endpoint: POST api/Notificaciones/NuevaVersionNesto. Las tandas necesitan la API posterior a la 1.10.34.0
    (acepta SoloListar y Usuarios); contra una API anterior, usar -TodosALaVez.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Texto,
    [int]$Tanda = 4,
    [int]$PausaMinutos = 3,
    [string[]]$AlFinal = @(),
    [switch]$TodosALaVez,
    [string]$Api = "http://api.nuevavision.es"
)

$ErrorActionPreference = "Stop"
# PowerShell 7 no manda las credenciales de Windows por http sin este permiso explícito (la API va por http).
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }

function Avisar([hashtable]$datos) {
    $cuerpo = (@{ Version = $Version; Texto = $Texto } + $datos) | ConvertTo-Json
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($cuerpo)
    Invoke-RestMethod -Method Post -Uri "$Api/api/Notificaciones/NuevaVersionNesto" `
        -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json; charset=utf-8" -Body $bytes
}

if ($TodosALaVez) {
    $resultado = Avisar @{}
    Write-Host "Avisados $($resultado.Avisados) usuarios con Nesto abierto de la versión $($resultado.Version):" -ForegroundColor Green
    $resultado.Usuarios | ForEach-Object { Write-Host "  $_" }
    return
}

if ($Tanda -lt 1) { throw "La tanda tiene que ser de al menos un usuario." }

# Quién tiene Nesto abierto AHORA. La lista se fija aquí: quien abra Nesto después ya entra con la versión nueva.
$conectados = @((Avisar @{ SoloListar = $true }).Usuarios)
if ($conectados.Count -eq 0) {
    Write-Host "Nadie tiene Nesto abierto: no hay a quién avisar." -ForegroundColor Yellow
    return
}

# Los de -AlFinal (con o sin dominio), al final de la cola
$sinDominio = { param($u) ($u -split '\\')[-1] }
$ultimos = @($AlFinal | ForEach-Object { (& $sinDominio $_).ToLowerInvariant() })
$cola = @($conectados | Where-Object { $ultimos -notcontains (& $sinDominio $_).ToLowerInvariant() }) +
        @($conectados | Where-Object { $ultimos -contains (& $sinDominio $_).ToLowerInvariant() })

$tandas = [Math]::Ceiling($cola.Count / $Tanda)
Write-Host "$($cola.Count) usuarios con Nesto abierto: $tandas tandas de $Tanda, una cada $PausaMinutos minutos." -ForegroundColor Cyan

$avisados = 0
for ($i = 0; $i -lt $tandas; $i++) {
    $estaTanda = @($cola | Select-Object -Skip ($i * $Tanda) -First $Tanda)
    $resultado = Avisar @{ Usuarios = $estaTanda }
    $avisados += $resultado.Avisados
    Write-Host "Tanda $($i + 1) de ${tandas}: avisados $($resultado.Avisados)." -ForegroundColor Green
    $resultado.Usuarios | ForEach-Object { Write-Host "  $_" }
    $noAvisados = @($estaTanda | Where-Object { $resultado.Usuarios -notcontains $_ })
    if ($noAvisados.Count -gt 0) {
        Write-Host "  Ya no tenían Nesto abierto (se actualizan al abrirlo): $($noAvisados -join ', ')" -ForegroundColor DarkGray
    }
    if ($i -lt $tandas - 1) {
        Write-Host "  Esperando $PausaMinutos minutos antes de la siguiente tanda..." -ForegroundColor DarkGray
        Start-Sleep -Seconds ($PausaMinutos * 60)
    }
}
Write-Host "Avisados $avisados usuarios de la versión $Version." -ForegroundColor Green
