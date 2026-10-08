<#
    Adjunta ficheros (PDF o imágenes) a una novedad (NestoAPI#616): POST api/Novedades/{id}/Adjuntos, multipart, campo
    «fichero» (varios permitidos). Necesita el usuario de Windows de alguien de Dirección o Informática (windows-token).

    Uso:
      .\AdjuntarANovedad.ps1 -Novedad 572 -Ficheros "C:\ruta\Guia.pdf"
      .\AdjuntarANovedad.ps1 -Titulo "Las reposiciones de las tiendas se rellenan solas a su hora" -Ambito Nesto -Ficheros a.pdf, b.png
        (busca la novedad publicada más reciente con ese título y ámbito)
#>
param(
    [string]$Api = "http://api.nuevavision.es",
    [int]$Novedad,
    [string]$Titulo,
    [string]$Ambito = "Nesto",
    [Parameter(Mandatory = $true)][string[]]$Ficheros
)

$ErrorActionPreference = "Stop"
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }

if (-not $Novedad) {
    if (-not $Titulo) { throw "Indica -Novedad o -Titulo." }
    $novedades = Invoke-RestMethod -Uri "$Api/api/Novedades?ambito=$Ambito" -Headers $cabeceras
    $encontrada = $novedades | Where-Object { $_.Titulo -eq $Titulo } | Sort-Object Id -Descending | Select-Object -First 1
    if (-not $encontrada) { throw "No hay ninguna novedad con el título «$Titulo» en $Ambito." }
    $Novedad = $encontrada.Id
    Write-Host "Novedad $Novedad ($($encontrada.Version)): $($encontrada.Titulo)"
}

$form = @{}
$i = 0
foreach ($f in $Ficheros) {
    $item = Get-Item $f
    # Invoke-RestMethod -Form manda cada FileInfo como una parte con el nombre del fichero; la API lee las partes «fichero».
    $form["fichero$i"] = $item
    $i++
}
$resultado = Invoke-RestMethod -Method Post -Uri "$Api/api/Novedades/$Novedad/Adjuntos" -Headers $cabeceras -Form $form
Write-Host "Adjuntados a la novedad ${Novedad}:" -ForegroundColor Green
$resultado | ForEach-Object { Write-Host "  $($_.Id)  $($_.Nombre)  $($_.Tipo)  $([math]::Round($_.Tamano / 1KB)) KB" }
