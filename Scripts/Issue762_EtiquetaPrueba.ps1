<#
    NestoAPI#762: imprimir UNA etiqueta de prueba 3x2 con el EAN-13 de un producto, para leerla con la PDA (EDA50K) y con
    el móvil antes de dar por bueno el tamaño (módulo de 2 puntos a 203 ppp, ~80 % del nominal; 80 puntos = 10 mm de barras).

    Llama a POST api/Almacen/EtiquetasProducto/Imprimir con Formato = Ean13 y Copias = 1. Sale por la impresora de etiquetas
    de TU usuario (parámetro ImpresoraCodBarras, la misma de las etiquetas de hueco y de producto), en ZPL o EPL según su driver.

    Primero hace el ENSAYO (no imprime ni guarda: dice qué código llevaría y en qué impresora) y luego imprime una, salvo con
    -SoloEnsayo.

    OJO con el producto que elijas:
      - Uno CON código de barras del fabricante (Cantidad 1): la etiqueta lleva ese EAN-13 y no se guarda nada.
      - Uno SIN código: se le da en ese momento su código interno (290 + referencia + control, p. ej. 17404 -> 2900000174047),
        que se queda en ProductosCodigosBarras (no principal, no va a la ficha ni a la tienda online). Es lo que va a pasar
        igualmente al etiquetar en las reposiciones; para probar sin escribir nada, usa uno con código.

    Entra con tu usuario de Windows (api/auth/windows-token, como Nesto): no pide contraseña. Hace falta ser de Compras,
    Almacén, Tiendas, Dirección o Informática. Necesita la API ya publicada con NestoAPI#762.

    Qué comprobar con la etiqueta en la mano:
      1. La PDA la lee a la primera (lector) y el móvil con la cámara (Ariadna, Producto (0) y leerla: tiene que salir el producto).
      2. Las cifras de debajo se leen y no se montan con las barras; el nombre y «Ref. ...» caben arriba.
      3. Si no lee bien o no cabe, las medidas están en GeneradorEtiquetasProducto (EAN_MODULO, EAN_ALTO, EAN_Y) de
         NestoAPI/Infraestructure/PreparacionAlmacen/EtiquetasProductoAlmacen.cs.

    Uso:
      .\Issue762_EtiquetaPrueba.ps1 -Producto 17404
      .\Issue762_EtiquetaPrueba.ps1 -Producto 32565 -SoloEnsayo
      .\Issue762_EtiquetaPrueba.ps1 -Producto 17404 -Api http://localhost:53364
#>
param(
    [Parameter(Mandatory = $true)][string]$Producto,
    [switch]$SoloEnsayo,
    [string]$Empresa = "1",
    [string]$Api = "https://api.nuevavision.es"
)

$ErrorActionPreference = "Stop"

$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials).token
if (-not $token) { throw "No se ha podido entrar con tu usuario de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }
$cuerpo = @{ Producto = $Producto; Copias = 1; Formato = "Ean13" } | ConvertTo-Json

function Imprimir([bool]$ensayo) {
    $uri = "$Api/api/Almacen/EtiquetasProducto/Imprimir?empresa=$Empresa&ensayo=$($ensayo.ToString().ToLowerInvariant())"
    try {
        return Invoke-RestMethod -Method Post -Uri $uri -Headers $cabeceras -ContentType "application/json; charset=utf-8" `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($cuerpo))
    } catch {
        Write-Host "La API no ha impreso la etiqueta: $($_.Exception.Message) $($_.ErrorDetails.Message)" -ForegroundColor Red
        return $null
    }
}

$ensayo = Imprimir $true
if (-not $ensayo) { return }
if (-not $ensayo.CodigoBarras -and -not $ensayo.CodigoInterno) {
    Write-Host "La API todavía no tiene NestoAPI#762 (no devuelve CodigoBarras): saldría la etiqueta de siempre. No se imprime." -ForegroundColor Red
    return
}
Write-Host "Ensayo: $($ensayo.Mensaje)" -ForegroundColor Cyan
Write-Host ("  Texto: " + (@($ensayo.Lineas) -join " | "))
if ($SoloEnsayo) { return }

$hecha = Imprimir $false
if (-not $hecha) { return }
Write-Host $hecha.Mensaje -ForegroundColor Green
if ($hecha.CodigoAsignado) {
    Write-Host "Se le ha dado al $Producto su código interno $($hecha.CodigoBarras)." -ForegroundColor Yellow
}
Write-Host "Léala con la PDA y con el móvil (Ariadna, Producto (0)): tiene que salir el $Producto."
