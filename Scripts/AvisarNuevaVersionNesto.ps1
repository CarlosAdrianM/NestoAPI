<#
    Ritual del deploy de Nesto (Carlos, 25/09/26): después de publicar la ClickOnce y de que Carlos compruebe que
    actualiza sin error, avisa en la campana a todos los que tienen Nesto abierto de que cierren y vuelvan a abrir
    cuando les venga bien. Los que no lo tienen abierto se actualizan solos al abrirlo, así que no se les avisa.

    Uso (siempre DESPUÉS del visto bueno de Carlos):
      .\AvisarNuevaVersionNesto.ps1 -Version 1.10.32.0
      .\AvisarNuevaVersionNesto.ps1 -Version 1.10.32.0 -Texto "Texto distinto del de siempre"

    Necesita el usuario de Windows de alguien de Dirección o Informática (pide el windows-token con él).
    Endpoint: POST api/Notificaciones/NuevaVersionNesto (NestoAPI, desde la publicación posterior a la 1.10.32.0).
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Texto,
    [string]$Api = "http://api.nuevavision.es"
)

$ErrorActionPreference = "Stop"
# PowerShell 7 no manda las credenciales de Windows por http sin este permiso explícito (la API va por http).
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }

$cuerpo = @{ Version = $Version; Texto = $Texto } | ConvertTo-Json
$bytes = [System.Text.Encoding]::UTF8.GetBytes($cuerpo)
$resultado = Invoke-RestMethod -Method Post -Uri "$Api/api/Notificaciones/NuevaVersionNesto" `
    -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json; charset=utf-8" -Body $bytes

Write-Host "Avisados $($resultado.Avisados) usuarios con Nesto abierto de la versión $($resultado.Version):" -ForegroundColor Green
$resultado.Usuarios | ForEach-Object { Write-Host "  $_" }
