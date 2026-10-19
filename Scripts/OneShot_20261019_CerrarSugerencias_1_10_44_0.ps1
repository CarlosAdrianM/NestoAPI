# Tras publicar la API y Nesto 1.10.44.0: las sugerencias 584 y 672 (María José) y 673 (Aida) pasan a Implementadas en esa versión
# (con título y descripción para el usuario: la sugerencia ES la novedad) y se contesta a cada autora en su hilo.
# Las de Andre en Ariadna (674 y 675) se cierran con la 2.13 cuando llegue a la prueba cerrada: ver el final.
param([string]$Api = "http://api.nuevavision.es", [switch]$AriadnaCerrada)
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

function Implementar($id, $version, $categoria, $perfiles, $titulo, $descripcion) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Version = $version; Categoria = $categoria; Perfiles = $perfiles; Titulo = $titulo; Descripcion = $descripcion } | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id implementada en $version." -ForegroundColor Green
}

if (-not $AriadnaCerrada) {
    Implementar 584 "1.10.44.0" "Mejorado" "Vendedores,Administración" "El correo de pedidos retenidos llega al comercial del cliente" `
        "El correo de «Pedidos retenidos» ya no va a quien grabó el pedido, sino al comercial del cliente según lo que lleve: si tiene productos de estética, al comercial de estética; si tiene de peluquería, al de peluquería; y si tiene de los dos, a los dos. Si un comercial no tiene correo, sigue llegando a quien grabó el pedido. La copia a administración se mantiene. Lo pidió María José."
    Implementar 672 "1.10.44.0" "Nuevo" "Vendedores,Administración" "Aviso al comercial cuando la agencia pone una incidencia en un envío" `
        "Cuando la agencia pone una incidencia en un envío, el comercial del pedido (el de estética, el de peluquería o los dos, según lo que lleve) recibe un correo, el aviso en la campana de Nesto y una notificación en NestoApp, con el pedido, el cliente, la agencia, el número de seguimiento y el motivo. Se avisa una sola vez por incidencia. Lo pidió María José."
    Implementar 673 "1.10.44.0" "Nuevo" "Administración,Almacén" "Agencias: «Dar por cerrado» un envío incidentado que la agencia nunca cerró" `
        "En la pestaña Incidentados de Agencias hay un botón «Dar por cerrado». Pide el motivo, pasa el envío al estado «Cerrado a mano» y lo quita de la lista. En el historial del envío queda quién lo cerró y por qué. Si la agencia confirma después la entrega, pasa a Entregado con su fecha. Lo pidió Aida."

    & "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 584 -Texto "@MariaJose, ya está en la versión 1.10.44.0 de Nesto, la de hoy: el correo de pedidos retenidos le llega al comercial del cliente (al de estética, al de peluquería o a los dos, según lo que lleve el pedido). En el 925137 le llegaría a Santiago. ¿Nos dice si es lo que quería?" | Out-Null
    & "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 672 -Texto "@MariaJose, ya está en la versión 1.10.44.0: cuando la agencia pone una incidencia en un envío, el comercial del pedido recibe un correo, el aviso en la campana de Nesto y una notificación en NestoApp. ¿Nos dice si le llega como esperaba?" | Out-Null
    & "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 673 -Texto "@Aida, ya está en la versión 1.10.44.0 de Nesto, la de hoy: en Agencias, pestaña Incidentados, el botón «Dar por cerrado» le pide el motivo, pasa el envío a «Cerrado a mano» y lo quita de la lista; en el historial queda quién y por qué. ¿Nos dice si es lo que necesitaba?" | Out-Null
}
else {
    Implementar 674 "2.13" "Mejorado" "Almacén,Tiendas" "Un código recién corregido en la ficha vale sin salir de la reposición" `
        "Si un código no coincide con la lista, Ariadna lo comprueba antes con el servidor: si se acaba de corregir en la ficha, lo da por bueno sin tener que salir y volver a entrar. Lo pidió Andre."
    Implementar 675 "2.13" "Mejorado" "Almacén,Tiendas" "El nombre del producto se ve entero en la cabecera" `
        "En la tarjeta de arriba el nombre del producto ya no se corta: si es largo, la letra se hace algo más pequeña. Lo pidió Andre."
    & "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 674 -Texto "@Andre, ya está en Ariadna 2.13: si un código no coincide con la lista, Ariadna lo comprueba con el servidor antes de dar el error, así que un código recién corregido vale sin salir de la reposición. ¿Nos dice si le funciona?" | Out-Null
    & "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 675 -Texto "@Andre, ya está en Ariadna 2.13: el nombre del producto se ve entero en la cabecera. ¿Nos dice si ahora se lee bien?" | Out-Null
}
