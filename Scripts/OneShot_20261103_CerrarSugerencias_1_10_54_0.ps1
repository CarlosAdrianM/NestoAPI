# Nesto 1.10.54.0 y Ariadna 2.28 (03/11/26): lanzar DESPUÉS de publicar la ClickOnce y de que la 2.28 llegue a la prueba cerrada.
# Las sugerencias 911 (Sancho, «Sin contacto este mes», Nesto#587) y 962 (Manuel, aviso de comisión, Nesto#595) pasan a novedad de
# la 1.10.54.0 y la 960 (Carlos, reimprimir desde Salidas, Ariadna#78) a novedad de la 2.28: la sugerencia ES la novedad, no hay
# fila aparte en Novedades_1_10_54_0.sql ni en Novedades_Ariadna_2_28.sql. Se contesta en cada hilo.
# Uso:  pwsh -File Scripts/OneShot_20261103_CerrarSugerencias_1_10_54_0.ps1
param([string]$Api = "http://api.nuevavision.es", [string]$VersionNesto = "1.10.54.0", [string]$VersionAriadna = "2.28")
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

function Implementar([int]$id, [hashtable]$datos) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes(($datos | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id implementada en $($datos.Version)." -ForegroundColor Green
}

Implementar 911 @{
    Version = $VersionNesto; Categoria = "Nuevo"; Perfiles = "Vendedores,Tiendas,Dirección"
    Titulo = "Clientes para contactar: «Sin contacto este mes»"
    Descripcion = "En la lista de clientes para contactar, los que no han tenido ningún contacto (llamada, visita o rapport de cualquier compañero) desde el día 1 salen con un punto naranja y el texto «Sin contacto este mes», para verlos de un vistazo. Lo pidió Sancho."
}
Implementar 962 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Vendedores,Tiendas"
    Titulo = "Plantilla: aviso de comisión por reembolso con el cliente que paga por banco"
    Descripcion = "Al desmarcar «servir junto» en la plantilla, el aviso de que el reembolso lleva comisión salía aunque el cliente pagara por banco. Ahora la plantilla mira las condiciones de pago que se aplican de verdad a ese pedido antes de avisar. Lo avisó Manuel."
}
Implementar 960 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén"
    Titulo = "Salidas: reimprimir las etiquetas de una reposición ya enviada"
    Descripcion = "Si tecleas el número de una reposición que ya salió (no está en la lista) e Intro, Ariadna te ofrece «Reimprimir etiquetas de bulto», para cuando la impresora falló al terminarla. Lo pidió Carlos."
}

& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 911 -Texto "@Sancho, ya está en Nesto ${VersionNesto}: en «Clientes para contactar», los que no han tenido ningún contacto desde el día 1 (llamada, visita o rapport de cualquier compañero) salen con un punto naranja y «Sin contacto este mes». Pruébelo y díganos aquí si es lo que necesitaba." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 962 -Texto "@Manuel, en Nesto ${VersionNesto} el aviso de comisión por reembolso al desmarcar «servir junto» ya no sale cuando el cliente paga por banco: la plantilla mira las condiciones de pago que se aplican de verdad al pedido. Lo del modo de servicio (por qué se queda en «Todo junto» cuando un producto sale desmontando un kit) lo tenemos apuntado para explicarlo en la propia pantalla en una versión próxima." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 960 -Texto "@Carlos, desde Ariadna ${VersionAriadna} (llega sola por Play Store), al teclear en Salidas el número de una reposición que ya salió e Intro, Ariadna ofrece «Reimprimir etiquetas de bulto»." | Out-Null
Write-Host "Hecho (Nesto $VersionNesto, Ariadna $VersionAriadna)." -ForegroundColor Green
