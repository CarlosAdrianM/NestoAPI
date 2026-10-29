# Ariadna 2.25 (29/10/26): cuando llegue a la prueba cerrada. La sugerencia 869 de Pedro (bultos al imprimir la etiqueta) pasa a
# novedad de la 2.25 (la sugerencia ES la novedad: no hay fila aparte en ningún script) y se contesta en su hilo; en la 870 se avisa
# de que el aviso del packing ya se ve entero. Necesita la API publicada con c970f4c8 o posterior (NestoAPI#740).
# Uso:  pwsh -File Scripts/OneShot_20261029_CerrarSugerencias_Ariadna_2_25.ps1
param([string]$Api = "http://api.nuevavision.es", [string]$Version = "2.25")
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

$cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{
    Version = $Version; Categoria = "Mejorado"; Perfiles = "Almacén"
    Titulo = "Packing: los bultos se dicen al imprimir la etiqueta (F8)"
    Descripcion = "Al pulsar F8 «Etiqueta de agencia» en el packing, Ariadna pregunta cuántos bultos lleva, junto al peso, como en la reposición. Propone las cajas cerradas (Intro las deja) y se puede teclear otro número. Ya no hace falta ir cerrando cajas mientras se prepara: se puede meter todo y decir los bultos al final. Los avisos largos del packing se ven enteros. Lo pidió Pedro."
} | ConvertTo-Json))
Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/869" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
Write-Host "Sugerencia 869 implementada en $Version." -ForegroundColor Green

& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 869 -Texto "@Pedro @Andre, ya está en Ariadna ${Version}, que les llega sola por Play Store: al pulsar F8 en el packing se preguntan los bultos junto al peso. Ariadna propone las cajas cerradas (Intro las deja) y pueden teclear otro número, así que ya no hace falta ir cerrando cajas mientras preparan. Pruébenlo y díganos aquí si es lo que necesitaban." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 870 -Texto "@Pedro, desde Ariadna ${Version} los avisos del packing se ven enteros, también en la PDA (antes se cortaban, como el de «adelante la fecha de entrega desde el…»)." | Out-Null
Write-Host "Hecho (Ariadna $Version)." -ForegroundColor Green
