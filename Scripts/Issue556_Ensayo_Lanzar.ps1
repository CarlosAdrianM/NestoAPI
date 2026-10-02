<#
    NestoAPI#556: ENSAYO de «Terminar» una salida (picking o reposición) contra producción, sin guardar nada.

    Llama a POST api/Almacen/Recogidas/{tipo}/{numero}/Terminar?ensayo=true: la API hace EXACTAMENTE lo mismo que al
    terminar de verdad (su código, los triggers, los procedimientos y las restricciones) dentro de una transacción que
    deshace SIEMPRE, y devuelve las filas implicadas antes y después. Este script enseña el mensaje, los cambios y la
    diferencia fila a fila.

    Entra con tu usuario de Windows (api/auth/windows-token, como Nesto): no pide contraseña. Solo pueden ensayar
    Admin o Dirección.

    Antes, la salida tiene que estar recogida en Ariadna (todo leído o dado por «No está»): si no, la API contesta 409
    con lo que queda. Mientras dura, bloquea esas filas: lanzarlo cuando no trabaje nadie (fin de semana).

    Uso:
      .\Issue556_Ensayo_Lanzar.ps1 -Tipo PICK -Numero 99739
      .\Issue556_Ensayo_Lanzar.ps1 -Tipo REPO -Numero 80900
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet("PICK", "REPO")][string]$Tipo,
    [Parameter(Mandatory = $true)][int]$Numero,
    [string]$Api = "https://api.nuevavision.es"
)

$ErrorActionPreference = "Stop"

$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials).token
if (-not $token) { throw "No se ha podido entrar con tu usuario de Windows." }

try {
    $r = Invoke-RestMethod -Method Post -Uri "$Api/api/Almacen/Recogidas/$Tipo/$Numero/Terminar?ensayo=true" `
        -Headers @{ Authorization = "Bearer $token" }
} catch {
    $detalle = $_.ErrorDetails.Message
    Write-Host "La API no ha hecho el ensayo: $($_.Exception.Message) $detalle" -ForegroundColor Red
    return
}

if (-not $r.Ensayo) { throw "La respuesta no es de un ensayo: no se sigue (¿API sin publicar?)." }

Write-Host ""
Write-Host $r.Mensaje -ForegroundColor Cyan
if ($r.ErrorEnsayo) {
    Write-Host ""
    Write-Host "ERROR REAL (no se ha guardado nada): $($r.ErrorEnsayo)" -ForegroundColor Red
}
if ($r.Cambios) {
    Write-Host ""
    Write-Host "Cambios:" -ForegroundColor Yellow
    $r.Cambios | ForEach-Object { Write-Host "  - $_" }
}

$antes = @{}
foreach ($f in @($r.FilasAntes)) { if ($f) { $antes["$($f.Tabla) $($f.Clave)"] = $f.Datos } }
$despues = @{}
foreach ($f in @($r.FilasDespues)) { if ($f) { $despues["$($f.Tabla) $($f.Clave)"] = $f.Datos } }

Write-Host ""
Write-Host "Filas que cambiarían ($($antes.Count) antes, $($despues.Count) después):" -ForegroundColor Yellow
$claves = @($antes.Keys) + @($despues.Keys) | Sort-Object -Unique
$hay = $false
foreach ($clave in $claves) {
    $a = $antes[$clave]
    $d = $despues[$clave]
    if ($a -eq $d) { continue }
    $hay = $true
    if ($null -eq $a) {
        Write-Host "  + $clave  $d" -ForegroundColor Green
    } elseif ($null -eq $d) {
        Write-Host "  - $clave  $a" -ForegroundColor Red
    } else {
        Write-Host "  ~ $clave"
        Write-Host "      antes:   $a"
        Write-Host "      después: $d"
    }
}
if (-not $hay) { Write-Host "  (ninguna)" }
Write-Host ""
Write-Host "ENSAYO: no se ha guardado nada." -ForegroundColor Cyan
