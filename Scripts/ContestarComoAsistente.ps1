<#
    Contesta comentarios de Novedades como «Claude (asistente IA)» (NestoAPI#531) y, de paso, prueba
    los avisos: el autor del comentario contestado recibe el aviso en su campana (Nesto, por SignalR
    desde NestoAPI#536) o una push (NestoApp).

    Uso:
      # Prueba local (NestoAPI y Nesto arrancados en Visual Studio, Nesto apuntando a localhost):
      .\ContestarComoAsistente.ps1 -Api http://localhost:53364 -Novedad 350 -Contestados 12 -Texto "Prueba de avisos"
      # Producción, varias respuestas desde un JSON [{novedad, contestados:[..], texto, esperaRespuesta}]:
      .\ContestarComoAsistente.ps1 -Fichero respuestas.json
      # Si la respuesta le PREGUNTA algo al autor, -EsperaRespuesta (o "esperaRespuesta": true en el JSON): si no
      # contesta, el job de las 9:00 se lo recuerda cada 2 días laborables (dos veces) y a la tercera la cierra.
      .\ContestarComoAsistente.ps1 -Novedad 584 -Contestados 150 -Texto "¿Qué correo es exactamente?" -EsperaRespuesta

    Necesita el usuario de Windows de alguien de Dirección o Informática (pide el windows-token con él).
    OJO: la API local escribe en la BD de producción: lo que se conteste es real.
#>
param(
    [string]$Api = "http://api.nuevavision.es",
    [int]$Novedad,
    [int[]]$Contestados = @(),
    [string]$Texto,
    [string]$Fichero,
    [switch]$EsperaRespuesta
)

$ErrorActionPreference = "Stop"
# PowerShell 7 no manda las credenciales de Windows por http sin este permiso explícito (la API va por http).
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }

$respuestas = if ($Fichero) {
    Get-Content -Raw -Encoding UTF8 $Fichero | ConvertFrom-Json
} else {
    @([pscustomobject]@{ novedad = $Novedad; contestados = $Contestados; texto = $Texto; esperaRespuesta = [bool]$EsperaRespuesta })
}

foreach ($r in $respuestas) {
    $espera = [bool]$EsperaRespuesta -or ($r.PSObject.Properties.Name -contains 'esperaRespuesta' -and [bool]$r.esperaRespuesta)
    $cuerpo = @{ Texto = $r.texto; ComentariosContestados = @($r.contestados); EsperaRespuesta = $espera } | ConvertTo-Json
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($cuerpo)
    $resultado = Invoke-RestMethod -Method Post -Uri "$Api/api/Novedades/$($r.novedad)/Comentarios/Asistente" `
        -Headers $cabeceras -ContentType "application/json; charset=utf-8" -Body $bytes
    Write-Host "Novedad $($r.novedad): contestado ($($r.contestados -join ', '))$(if ($espera) { '; esperando respuesta del autor' })." -ForegroundColor Green
    $resultado | ConvertTo-Json -Depth 4
}
