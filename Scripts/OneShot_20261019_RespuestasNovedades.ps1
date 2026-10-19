# 19/10/26: respuestas de Novedades (615 Alfredo, 637 y 671 Andre), sugerencia 637 a Implementada en Ariadna 2.11
# y marcar revisados los comentarios ya contestados el 16/10 (167, 169, 178, 179).
param([string]$Api = "http://api.nuevavision.es")
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

$cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{
    Version = "2.11"
    Categoria = "Mejorado"
    Perfiles = "Almacén,Tiendas"
    Titulo = "En el móvil, la parte de arriba ya no tapa la lista"
    Descripcion = "En un móvil, el cliente, el producto y los botones ocupan menos y siempre quedan a la vista unas cuatro líneas de la lista. En el packing, el cliente sale en dos líneas; la dirección y las observaciones se ven tocándolo o con F5 (si el pedido tiene observaciones, sale ya desplegado). Lo pidió Andre."
} | ConvertTo-Json))
Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/637" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
Write-Host "Sugerencia 637 implementada en 2.11." -ForegroundColor Green

& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Fichero "$PSScriptRoot\OneShot_20261019_RespuestasNovedades.json" | Out-Null

foreach ($id in 167, 169, 178, 179) {
    Invoke-RestMethod -Method Post -Uri "$Api/api/Novedades/Comentarios/$id/Revisado" -Headers $h | Out-Null
    Write-Host "Comentario $id revisado."
}
