<#
    29/10/26: tras lanzar Scripts/Issue729_prdCrearAlbaranVta_ReservasEmpresa1.sql (NestoAPI#729), factura el pedido 928217
    (empresa 3, segunda entrega: línea 45958 con picking 99962) por el mismo núcleo que Facturar rutas.
    Uso:  ! pwsh -File Scripts/OneShot_20261029_Facturar928217.ps1
#>
param([string]$Api = "http://api.nuevavision.es")

$ErrorActionPreference = "Stop"
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }

$cuerpo = @{ Empresa = "3"; Pedido = 928217 } | ConvertTo-Json
$resultado = Invoke-RestMethod -Method Post -Uri "$Api/api/FacturacionRutas/FacturarPedido" -Headers $cabeceras -ContentType "application/json" -Body $cuerpo
# Sin los PDF (DatosImpresion), que son largos.
$resultado | Select-Object PedidosProcesados, AlbaranesCreados, FacturasCreadas, NotasEntregaCreadas, PedidosConErrores | ConvertTo-Json -Depth 4
