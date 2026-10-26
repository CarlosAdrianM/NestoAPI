# Tras publicar la API y Nesto 1.10.49.1:
# - 782 (Sancho): Implementada en 1.10.49.1 (los contadores del panel de contactos cuentan clientes, no rapports).
# - 648 (Paloma): ya era novedad de la 1.10.49.0; se actualiza su descripción (ahora el Excel llega por correo) sin cambiar
#   la versión, y se le avisa en su hilo.
param([string]$Api = "http://api.nuevavision.es")
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

function Implementar($id, $version, $categoria, $perfiles, $titulo, $descripcion) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Version = $version; Categoria = $categoria; Perfiles = $perfiles; Titulo = $titulo; Descripcion = $descripcion } | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id implementada en $version." -ForegroundColor Green
}

Implementar 782 "1.10.49.1" "Corregido" "Vendedores,Dirección" "El panel de contactos cuenta clientes, no rapports" `
    "En «Clientes para contactar», el mes cuenta los clientes distintos de su cartera con los que ha hablado, sobre el total de la cartera (por ejemplo, «151 de 246»), y «Ya ha hablado con toda la cartera» solo sale cuando de verdad ha hablado con todos. Varios rapports al mismo cliente el mismo día cuentan uno. El objetivo de hoy reparte las llamadas que faltan este mes según lo a menudo que hay que llamar a cada cliente. Lo avisó Sancho."
Implementar 648 "1.10.49.0" "Nuevo" "Tiendas" "«Facturado en tienda»: lo facturado en la semana, en Excel a su correo" `
    "En Productos, grupo Tienda, el botón «Facturado en tienda» le manda a su correo un Excel con todo lo facturado en su tienda entre dos fechas (por defecto, la semana anterior), cursos incluidos: fecha, factura, pedido, cliente, dirección de entrega, comercial, forma de venta e importes, con una segunda hoja con el total por cliente. Lo pidió Paloma."

& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 782 -Texto "@Sancho, ya está corregido (versión 1.10.49.1): el panel cuenta clientes distintos de la cartera y no rapports. A Javier hoy le sale «151 de 246» en el mes, y el objetivo de hoy reparte las llamadas que le faltan según la cadencia de cada cliente. ¿Nos dice si ahora le cuadra con lo que ve en los rapports?" | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 648 -Texto "@Paloma, ya está en la versión 1.10.49.1: en «Facturado en tienda» el botón «Mandar el Excel a mi correo» se lo manda a su correo, y lo puede abrir en su ordenador o en el móvil. ¿Nos dice si le llega bien y le sirve para el informe de cifras?" | Out-Null
Write-Host "Hecho." -ForegroundColor Green
