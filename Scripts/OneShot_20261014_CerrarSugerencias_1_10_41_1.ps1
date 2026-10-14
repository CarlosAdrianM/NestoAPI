# Tras publicar Nesto 1.10.41.1: la sugerencia 599 (Paloma) pasa a Implementada en esa versión y se contesta en su hilo.
param([string]$Api = "http://api.nuevavision.es")
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }
$cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Version = "1.10.41.1"; Perfiles = "Tiendas" } | ConvertTo-Json))
Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/599" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
$texto = "Hola, Paloma. Ya está en la versión 1.10.41.1: en Enviar reposición (también en la puntual) y en las líneas de Recibir reposición, doble clic en la referencia abre la ficha del producto. Al volver a la pestaña, el cursor vuelve al lector. ¿Nos dices si es lo que querías?"
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 599 -Texto $texto | Select-Object -First 1
