# Nesto 1.10.56.0 y Ariadna 2.30 (05/11/26): lanzar DESPUÉS de publicar la ClickOnce y de que la 2.30 llegue a la prueba cerrada.
# Marca Implementada cada sugerencia (la sugerencia ES la novedad: no hay fila aparte en Novedades_1_10_56_0.sql ni en
# Novedades_Ariadna_2_30.sql) y después contesta en cada hilo con la versión. Idempotente (el PUT y el comentario se pueden repetir,
# pero el comentario saldría dos veces: lanzarlo una sola vez).
# Uso:  pwsh -File Scripts/OneShot_20261105_CerrarSugerencias_1_10_56_0.ps1
param([string]$Api = "http://api.nuevavision.es", [string]$VersionNesto = "1.10.56.0", [string]$VersionAriadna = "2.30", [switch]$SinComentarios)
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

function Implementar([int]$id, [hashtable]$datos) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes(($datos | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id implementada en $($datos.Version)." -ForegroundColor Green
}
function Contestar([int]$id, [string]$texto) {
    if ($SinComentarios) { return }
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Texto = $texto; ComentariosContestados = @() } | ConvertTo-Json))
    Invoke-RestMethod -Method Post -Uri "$Api/api/Novedades/$id/Comentarios/Asistente" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Contestada la $id." -ForegroundColor Cyan
}

# ---------------- Nesto 1.10.56.0 ----------------
Implementar 793 @{
    Version = $VersionNesto; Categoria = "Nuevo"; Perfiles = "Compras,Dirección"
    Titulo = "Cambiar el estado de muchos productos a la vez"
    Descripcion = "Productos › Compras › «Cambiar estado de productos»: se pegan las referencias (una columna de Excel vale), se elige el estado nuevo, se ve una vista previa de lo que va a cambiar (con los controles de stock que se quitan o se recrean) y se confirma. Solo Compras y Dirección. Y en la ficha del producto, el estado es un desplegable con los mismos valores. Lo pidió Manuel."
}
Contestar 793 "@Manuel, ya está en Nesto 1.10.56.0: Productos › Compras › «Cambiar estado de productos» (pegar referencias, elegir estado, vista previa y confirmar) y el desplegable de estado en la ficha."

Implementar 959 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Dirección,Vendedores"
    Titulo = "Informe de rapports: cada persona sale en un solo bloque"
    Descripcion = "El informe de detalle de rapports partía a Daniel en dos bloques porque sus rapports estaban grabados con dos grafías del usuario (dlopez y Dlopez). El informe ya agrupa sin distinguir mayúsculas y, desde ahora, la API graba a cada persona siempre con la misma grafía (Carlos, DLopez, MariaJose…). Los rapports desde 2025 se unifican con un script."
}
Contestar 959 "@Sancho, ya está en Nesto 1.10.56.0: el informe de rapports agrupa por persona sin distinguir mayúsculas y Daniel sale en un solo bloque. Los rapports antiguos se unifican con un script que lanza Carlos fuera de horario."

Implementar 1049 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Vendedores,Tiendas,Administración"
    Titulo = "Alta de cliente: los textos se recortan y el error dice qué campo se pasa de largo"
    Descripcion = "Al crear o modificar un cliente, los espacios de más al final de cada campo se quitan antes de validar, y si un texto es demasiado largo (por ejemplo un correo de más de 50 caracteres) el mensaje dice qué campo es y cuál es el máximo, en vez de «La solicitud no es válida». Lo contó Alfredo."
}
Contestar 1049 "@Alfredo, ya está en Nesto 1.10.56.0: el alta de cliente recorta los espacios de más y, si un campo se pasa de largo, dice cuál y el máximo."

Implementar 1064 @{
    Version = $VersionNesto; Categoria = "Nuevo"; Perfiles = "Tiendas,Vendedores,Dirección"
    Titulo = "Eventos mixtos: en cada inscrita, si viene presencial u online"
    Descripcion = "El evento puede ser «Mixto (presencial y online)». En los mixtos, cada inscrita lleva un desplegable presencial / online que se guarda al elegirlo; la lista de inscritos tiene el contador «N presencial · M online · K sin indicar», un filtro y la opción de ordenar por eso. Lo pidió Reina."
}
Contestar 1064 "@Reina, ya está en Nesto 1.10.56.0: ponga el evento como «Mixto» en su ficha y en cada inscrita le saldrá el desplegable presencial / online, con el contador, el filtro y la ordenación en la lista."

Implementar 1065 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Almacén,Administración,Vendedores"
    Titulo = "Pedidos de la empresa 3: la ruta que vale es la de la ficha del cliente en la empresa 1"
    Descripcion = "Al grabar o modificar un pedido de la empresa 3, la ruta se coge de la ficha del cliente en la empresa 1 (la de verdad), no de la ficha de la 3, que en 58 contactos tenía FW u otra. Así el pedido sale su día de ruta propia (AT, 16) sin cambiar ruta ni fecha a mano. Si alguien elige a mano otra ruta (Glovo, una agencia), se respeta. Lo contó Alfredo."
}
Contestar 1065 "@Alfredo, ya está en la API y en Nesto 1.10.56.0: los pedidos de la empresa 3 cogen la ruta de la ficha de la empresa 1 y salen su día de ruta propia, sin cambiar nada a mano."

# ---------------- Ariadna 2.30 ----------------
Implementar 1054 @{
    Version = $VersionAriadna; Categoria = "Corregido"; Perfiles = "Almacén,Tiendas"
    Titulo = "La familia y el tamaño ya no se recortan en el móvil"
    Descripcion = "En la tarjeta del producto, el tamaño va delante de la familia («250 ml. · Eva Visnú») y el texto pasa a una segunda línea en vez de cortarse con puntos suspensivos. Lo contó Paloma."
}
Contestar 1054 "@Paloma, ya está en Ariadna 2.30 (llega sola por Play Store): el tamaño va delante de la familia y el texto pasa a dos líneas en vez de recortarse."

Implementar 1056 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén"
    Titulo = "Packing: cada pedido de Amazon o de la tienda online va solo"
    Descripcion = "Los pedidos de los clientes de factura simplificada (Amazon, tienda online, Glovo…) ya no se juntan en una misma entrega del packing aunque sean del mismo cliente y dirección: cada pedido es una entrega, con sus cajas y su cerrojo, porque cada uno es para un cliente final distinto. Lo contó Santiago."
}
Contestar 1056 "@Santiago, ya está (API del 05/11): en el packing cada pedido de Amazon o de la tienda online es una entrega aparte, con sus propias cajas."

Write-Host "Hecho." -ForegroundColor Green
