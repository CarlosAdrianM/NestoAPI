<#
    NestoAPI#890: carga las novedades de una versión por la API (POST api/Novedades/Version), en vez de lanzar como sa un
    Scripts/Novedades_X_Y_Z_R.sql. Idempotente: lo que ya está (misma versión, ámbito y título) no se repite, así que se puede
    lanzar otra vez si algo falla.

    Primero hace el ENSAYO (no escribe nada: dice cuántas crearía, cuáles ya estaban y qué sugerencias pasaría a Implementada)
    y pregunta antes de cargarlas de verdad, salvo con -SinPreguntar. Con -SoloEnsayo se queda en el ensayo.

    Entra con tu usuario de Windows (api/auth/windows-token, como Nesto): hace falta ser de Dirección o Informática.

    El fichero JSON (UTF-8):
      {
        "Ambito": "Nesto",                       // Nesto | NestoAPI | NestoApp | Ariadna (o -Ambito)
        "Fecha": "2026-11-26",                   // opcional: sin ella, hoy
        "Novedades": [
          { "Categoria": "Nuevo", "Titulo": "…", "Descripcion": "…", "Perfiles": ["Almacén", "Tiendas"] },
          { "Categoria": "Mejorado", "Titulo": "…", "Descripcion": "…", "SugerenciaId": 1460 }
        ]
      }
    o directamente la lista de novedades (entonces -Ambito es obligatorio). Con "SugerenciaId" no se crea otra novedad: esa
    sugerencia pasa a Implementada en esta versión, con ese título y esa descripción.
    Categoria: Nuevo, Mejorado o Corregido. Perfiles: Vendedores, Almacén, Tiendas, Administración, Compras (sin ellos, para todos).
    Título hasta 200 caracteres y descripción hasta 1000.

    Para revisar lo cargado:  GET api/Novedades/Version?version=1.10.68.0&ambito=Nesto
    Para corregir una:        PUT api/Novedades/{id}  { Titulo?, Descripcion?, Categoria?, Perfiles?, Publicada? }
    Para quitar una:          DELETE api/Novedades/{id}  (sin comentarios ni votos; si los tiene, PUT con Publicada = false)

    Uso:
      .\PublicarNovedades.ps1 -Version 1.10.68.0 -Fichero .\novedades.json
      .\PublicarNovedades.ps1 -Version 2.46 -Ambito Ariadna -Fichero .\ariadna.json -SoloEnsayo
      .\PublicarNovedades.ps1 -Version 1.10.68.0 -Fichero .\novedades.json -Api http://localhost:53364
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Fichero,
    [string]$Ambito,
    [switch]$SoloEnsayo,
    [switch]$SinPreguntar,
    [string]$Api = "https://api.nuevavision.es"
)

$ErrorActionPreference = "Stop"

$contenido = Get-Content -LiteralPath $Fichero -Raw -Encoding UTF8 | ConvertFrom-Json
if ($contenido -is [System.Array]) {
    $carga = [ordered]@{ Novedades = $contenido }
} else {
    $carga = [ordered]@{ Novedades = $contenido.Novedades }
    if ($contenido.Ambito) { $carga.Ambito = $contenido.Ambito }
    if ($contenido.Fecha) { $carga.Fecha = $contenido.Fecha }
}
$carga.Version = $Version
if ($Ambito) { $carga.Ambito = $Ambito }
if (-not $carga.Ambito) { throw "Falta el ámbito: póngalo en el fichero («Ambito») o con -Ambito." }

$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials).token
if (-not $token) { throw "No se ha podido entrar con su usuario de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }
$cuerpo = [System.Text.Encoding]::UTF8.GetBytes(($carga | ConvertTo-Json -Depth 6))

function Cargar([bool]$ensayo) {
    $uri = "$Api/api/Novedades/Version?ensayo=$($ensayo.ToString().ToLowerInvariant())"
    try {
        return Invoke-RestMethod -Method Post -Uri $uri -Headers $cabeceras -ContentType "application/json; charset=utf-8" -Body $cuerpo
    } catch {
        Write-Host "La API no ha cargado las novedades: $($_.Exception.Message) $($_.ErrorDetails.Message)" -ForegroundColor Red
        return $null
    }
}

function Pintar($r) {
    Write-Host $r.Mensaje -ForegroundColor Cyan
    foreach ($n in $r.Creadas) { Write-Host ("  + [{0}] {1} {2}" -f $n.Categoria, $n.Titulo, $(if ($n.Id) { "(Id $($n.Id))" } else { "" })) -ForegroundColor Green }
    foreach ($n in $r.SugerenciasImplementadas) { Write-Host ("  * sugerencia {0}: [{1}] {2}" -f $n.SugerenciaId, $n.Categoria, $n.Titulo) -ForegroundColor Yellow }
    foreach ($n in $r.YaEstaban) { Write-Host ("  = ya estaba: {0}" -f $n.Titulo) -ForegroundColor DarkGray }
}

$ensayo = Cargar $true
if (-not $ensayo) { exit 1 }
Pintar $ensayo
if ($SoloEnsayo) { return }
if (($ensayo.Creadas.Count + $ensayo.SugerenciasImplementadas.Count) -eq 0) {
    Write-Host "No hay nada que cargar." -ForegroundColor Cyan
    return
}
if (-not $SinPreguntar) {
    $respuesta = Read-Host "¿Cargarlas de verdad? (s/n)"
    if ($respuesta -notmatch '^[sS]') { Write-Host "No se ha cargado nada."; return }
}
$hecho = Cargar $false
if (-not $hecho) { exit 1 }
Pintar $hecho
