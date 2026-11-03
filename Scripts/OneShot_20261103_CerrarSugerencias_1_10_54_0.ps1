# Nesto 1.10.54.0 y Ariadna 2.28 (03/11/26): lanzar DESPUÉS de publicar la ClickOnce y de que la 2.28 llegue a la prueba cerrada.
# Las sugerencias 911 (Sancho, «Sin contacto este mes», Nesto#587) y 962 (Manuel, aviso de comisión, Nesto#595) pasan a novedad de
# la 1.10.54.0 y la 960 (Carlos, reimprimir desde Salidas, Ariadna#78) a novedad de la 2.28: la sugerencia ES la novedad, no hay
# fila aparte en Novedades_1_10_54_0.sql ni en Novedades_Ariadna_2_28.sql. Se contesta en cada hilo.
# Además (tarde): 967 Alfredo (#792), 974 y 975 Reina (Nesto#604/#605) → 1.10.54.0; 963 Alfredo (Ariadna#83) y 971 Miguel (Ariadna#82) → 2.28.
# OJO: esas cinco SÍ tienen fila en los scripts de Novedades (se escribieron antes): el PUT las convierte en novedad duplicada, así que
# ANTES de lanzar los scripts de Novedades hay que quitar de ellos las entradas «Facturación de fin de mes…», «Eventos: la casilla…»,
# «Packing: el número del pedido…» y «Reparto: recoger la reposición…» (ya quitadas el 03/11 a las 16:50) o despublicar las dobles.
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

Implementar 967 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Administración"
    Titulo = "Facturación de fin de mes: una factura por tipo de IVA"
    Descripcion = "En el fin de mes, los pedidos de un mismo cliente con distinto tipo de IVA (normal y Global, o con y sin recargo) se facturan ahora en facturas separadas en vez de dar error de descuadre. Lo avisó Alfredo."
}
Implementar 974 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = "Vendedores,Tiendas,Dirección"
    Titulo = "Eventos: la casilla de «Hay que comprar» acepta referencia o código de barras"
    Descripcion = "En la ficha del evento, la casilla pequeña de «Hay que comprar» lleva el texto «Referencia o código de barras…», Intro añade el producto y acepta tanto la referencia como el código de barras leído con el lector. La lista de arriba explica para qué es. Lo pidió Reina."
}
Implementar 975 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Tiendas,Almacén"
    Titulo = "Recibir reposición: las muestras con la etiqueta de la referencia ya cuentan"
    Descripcion = "Las muestras (subgrupo MMP) que no tienen código de barras se cuentan al recibir la reposición tecleando la referencia o leyendo la etiqueta vieja de la referencia, sin pasar por «Sin etiqueta». Lo avisó Reina."
}
Implementar 963 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén"
    Titulo = "Packing: el número del pedido le lleva a su entrega"
    Descripcion = "Dentro de Empaquetar, teclee o lea el número de un pedido (6 cifras) y Ariadna le lleva a su entrega y a su caja. Si el pedido es de otro picking, le pregunta si quiere abrirlo. Las entregas siguen agrupadas por cliente, porque los pedidos del mismo cliente van en la misma caja. Lo pidió Alfredo."
}
Implementar 971 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén"
    Titulo = "Reparto: recoger la reposición desde la propia parada"
    Descripcion = "Si en «Entregar» lee un bulto R de la reposición que se recoge en esa misma tienda (o una NR del cliente de la parada), Ariadna le pregunta «¿Recogerlo ahora?» e Intro lo sube a la furgoneta sin salir de la parada, igual que desde «En la furgoneta». Lo avisó Miguel."
}

& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 911 -Texto "@Sancho, ya está en Nesto ${VersionNesto}: en «Clientes para contactar», los que no han tenido ningún contacto desde el día 1 (llamada, visita o rapport de cualquier compañero) salen con un punto naranja y «Sin contacto este mes». Pruébelo y díganos aquí si es lo que necesitaba." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 962 -Texto "@Manuel, en Nesto ${VersionNesto} el aviso de comisión por reembolso al desmarcar «servir junto» ya no sale cuando el cliente paga por banco: la plantilla mira las condiciones de pago que se aplican de verdad al pedido. Lo del modo de servicio (por qué se queda en «Todo junto» cuando un producto sale desmontando un kit) lo tenemos apuntado para explicarlo en la propia pantalla en una versión próxima." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 960 -Texto "@Carlos, desde Ariadna ${VersionAriadna} (llega sola por Play Store), al teclear en Salidas el número de una reposición que ya salió e Intro, Ariadna ofrece «Reimprimir etiquetas de bulto»." | Out-Null
Write-Host "Hecho (Nesto $VersionNesto, Ariadna $VersionAriadna)." -ForegroundColor Green
