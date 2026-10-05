<#
    NestoAPI#579: ritual del deploy de NestoApp, gemelo de AvisarNuevaVersionNesto.ps1. Después de promocionar la
    versión a Production en AppFlow y ejecutar sus Novedades, manda una PUSH a todos los móviles con NestoApp
    diciendo cómo estrenarla (dos cierres en frío: Live Updates está en modo background). Al tocarla, la app abre
    el perfil (Datos.ruta = /profile), con las novedades.

    Sin tandas: cada móvil se actualiza solo, no carga RDS2016.

    Uso (siempre DESPUÉS del visto bueno de Carlos):
      .\AvisarNuevaVersionNestoApp.ps1 -Version 2.22.0 -SoloListar   # a quién se mandaría, sin mandar nada
      .\AvisarNuevaVersionNestoApp.ps1 -Version 2.22.0 -Prueba       # solo a Carlos, para verla antes
      .\AvisarNuevaVersionNestoApp.ps1 -Version 2.22.0               # a todos los dispositivos activos de NestoApp
      .\AvisarNuevaVersionNestoApp.ps1 -Version 2.22.0 -Texto "Texto distinto del de siempre"

    Necesita el usuario de Windows de alguien de Dirección o Informática (pide el windows-token con él).
    Endpoint: POST api/Notificaciones/NuevaVersionNestoApp (API posterior a la 1.10.36.2).
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Texto,
    [switch]$SoloListar,
    [switch]$Prueba,
    [string]$UsuarioPrueba = "Carlos",
    [string]$Api = "http://api.nuevavision.es"
)

$ErrorActionPreference = "Stop"
# PowerShell 7 no manda las credenciales de Windows por http sin este permiso explícito (la API va por http).
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }

$datos = @{ Version = $Version; Texto = $Texto; SoloListar = [bool]$SoloListar }
if ($Prueba) { $datos.Usuarios = @($UsuarioPrueba) }
$bytes = [System.Text.Encoding]::UTF8.GetBytes(($datos | ConvertTo-Json))
$resultado = Invoke-RestMethod -Method Post -Uri "$Api/api/Notificaciones/NuevaVersionNestoApp" `
    -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json; charset=utf-8" -Body $bytes

if ($SoloListar) {
    Write-Host "Se mandaría la push de la $($resultado.Version) a $(@($resultado.Usuarios).Count) usuarios con NestoApp:" -ForegroundColor Cyan
} else {
    Write-Host "Push de la $($resultado.Version) mandada a $($resultado.Avisados) usuarios:" -ForegroundColor Green
}
$resultado.Usuarios | ForEach-Object { Write-Host "  $_" }
