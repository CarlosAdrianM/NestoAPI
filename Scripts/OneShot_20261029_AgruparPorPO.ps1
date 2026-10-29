<#
    29/10/26: tras publicar el fix de NestoAPI#727 (c5141790), lanza a mano la agrupación por P.O. (lo primero que hace
    Facturar rutas): reúne los 6 pedidos de Chanel 27593 con PO00495702 en uno y lo factura. Sirve para comprobar en
    producción que el motor ya no revienta el contexto. Necesita el usuario de Windows de Dirección/Almacén.

    Uso:  .\Scripts\OneShot_20261029_AgruparPorPO.ps1
#>
param([string]$Api = "http://api.nuevavision.es")

$ErrorActionPreference = "Stop"
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }

$resultado = Invoke-RestMethod -Method Post -Uri "$Api/api/FacturacionRutas/AgruparPorPO" -Headers $cabeceras -ContentType "application/json" -Body "{}"
$resultado | ConvertTo-Json -Depth 5
