<#
    NestoAPI#559/#553: ENSAYO de «Terminar» una recepción (COMP: lo que llega de un proveedor; REPO: la entrada de una
    reposición) contra producción, sin guardar nada.

    Llama a POST api/Almacen/Recepciones/{tipo}/{documento}/Terminar?ensayo=true: la API hace EXACTAMENTE lo mismo que al
    terminar de verdad (su código, los triggers, prdCrearAlbaránCmp / prdExtrProducto y las restricciones) dentro de una
    transacción que deshace SIEMPRE, sin avisar a Compras, y devuelve las filas implicadas antes y después (LinPedidoCmp,
    CabAlbaránCmp, PreExtrProducto, ExtractoProducto nuevos y Ubicaciones). Este script enseña los avisos y la diferencia
    fila a fila.

    Lo leído: si no se pasa -Lecturas, se usa LO ESPERADO (lo que enseña Ariadna: todo cuadra). Para ensayar diferencias,
    -Lecturas @{ "44725" = 2; "43890" = 1 } (producto = unidades). En una REPO que no cuadre entra lo leído (04/10/26): se
    enseñan las diferencias y el diario de entrada (PreExtrProducto) antes y tras ajustarlo a lo leído.

    Entra con tu usuario de Windows (api/auth/windows-token, como Nesto): no pide contraseña. Solo pueden ensayar
    Admin o Dirección (no hace falta tener el almacén de destino ni ser de Almacén o Compras).

    Mientras dura, bloquea esas filas (y prdExtrProducto el diario entero): lanzarlo cuando no trabaje nadie.

    Uso:
      .\Issue559_EnsayoRecepcion_Lanzar.ps1 -Tipo REPO -Documento 80871
      .\Issue559_EnsayoRecepcion_Lanzar.ps1 -Tipo COMP -Documento 1462
      .\Issue559_EnsayoRecepcion_Lanzar.ps1 -Tipo REPO -Documento 80871 -Lecturas @{ "25279" = 1 }
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet("COMP", "REPO")][string]$Tipo,
    [Parameter(Mandatory = $true)][string]$Documento,
    [hashtable]$Lecturas,
    [string]$Almacen = "ALG",
    [string]$Empresa = "1",
    [string]$Api = "https://api.nuevavision.es"
)

$ErrorActionPreference = "Stop"

$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials).token
if (-not $token) { throw "No se ha podido entrar con tu usuario de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }
$ruta = "$Api/api/Almacen/Recepciones/$Tipo/$([uri]::EscapeDataString($Documento))"
$filtro = "almacen=$Almacen&empresa=$Empresa"

if (-not $Lecturas) {
    try {
        $esperado = Invoke-RestMethod -Method Get -Uri "$($ruta)?$filtro" -Headers $cabeceras
    } catch {
        Write-Host "No se ha podido leer lo esperado de $Tipo $($Documento): $($_.Exception.Message) $($_.ErrorDetails.Message)" -ForegroundColor Red
        return
    }
    $Lecturas = @{}
    foreach ($l in @($esperado.Lineas)) { if ($l -and $l.Cantidad -gt 0) { $Lecturas[$l.Producto] = [int]$l.Cantidad } }
    Write-Host "Se ensaya con LO ESPERADO de $($esperado.Titulo): $($Lecturas.Count) productos." -ForegroundColor Cyan
}

$cuerpo = @{
    IdRecepcion = [guid]::NewGuid().ToString()
    Dispositivo = "Ensayo $env:COMPUTERNAME"
    Lecturas    = @($Lecturas.GetEnumerator() | ForEach-Object { @{ Producto = "$($_.Key)"; Cantidad = [int]$_.Value } })
} | ConvertTo-Json -Depth 4

try {
    $r = Invoke-RestMethod -Method Post -Uri "$ruta/Terminar?$filtro&ensayo=true" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($cuerpo))
} catch {
    Write-Host "La API no ha hecho el ensayo: $($_.Exception.Message) $($_.ErrorDetails.Message)" -ForegroundColor Red
    return
}

if (-not $r.Ensayo) { throw "La respuesta no es de un ensayo: no se sigue (¿API sin publicar?)." }

Write-Host ""
foreach ($a in @($r.Avisos)) { if ($a) { Write-Host $a -ForegroundColor Cyan } }
if ($r.ErrorEnsayo) {
    Write-Host ""
    Write-Host "ERROR REAL (no se ha guardado nada): $($r.ErrorEnsayo)" -ForegroundColor Red
}
foreach ($d in @($r.Documentos)) { if ($d) { Write-Host "  Albarán $($d.Albaran) del pedido $($d.Pedido)" -ForegroundColor Yellow } }
foreach ($n in @($r.NoEsperados)) { if ($n) { Write-Host "  No esperado (no entra): $($n.Producto) x$($n.Leido)" -ForegroundColor Yellow } }
foreach ($x in @($r.Recuperadas)) { if ($x) { Write-Host "  $($x.Texto)" -ForegroundColor Yellow } }
foreach ($x in @($r.Diferencias)) {
    if ($x) {
        $enviado = if ($x.Ajeno) { "no venía" } else { "enviado $($x.Esperado)" }
        Write-Host "  Diferencia (entra lo leído): $($x.Producto): $enviado, leído $($x.Leido), diferencia $($x.Diferencia)" -ForegroundColor Yellow
    }
}

function Comparar($filasAntes, $filasDespues, $titulo, $soloTabla) {
    $antes = @{}
    foreach ($f in @($filasAntes)) { if ($f -and (-not $soloTabla -or $f.Tabla -eq $soloTabla)) { $antes["$($f.Tabla) $($f.Clave)"] = $f.Datos } }
    $despues = @{}
    foreach ($f in @($filasDespues)) { if ($f -and (-not $soloTabla -or $f.Tabla -eq $soloTabla)) { $despues["$($f.Tabla) $($f.Clave)"] = $f.Datos } }
    Write-Host ""
    Write-Host "$titulo ($($antes.Count) antes, $($despues.Count) después):" -ForegroundColor Yellow
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
}

if ($r.FilasTrasAjustar) {
    Comparar $r.FilasAntes $r.FilasTrasAjustar "Diario de entrada ajustado a lo leído, antes de contabilizar" "PreExtrProducto"
}
Comparar $r.FilasAntes $r.FilasDespues "Filas que cambiarían" $null
Write-Host ""
if ($Tipo -eq "REPO") {
    Write-Host "En una REPO de ALG tiene que salir una fila «+ Ubicaciones … Estado=2; Hueco=-; NºTraspasoRepo=$Documento» por producto (pendiente de ubicar)." -ForegroundColor Cyan
}
Write-Host "ENSAYO: no se ha guardado nada." -ForegroundColor Cyan
