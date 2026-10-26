# Tras publicar la API y Nesto 1.10.49.0: las sugerencias 648 y 781 (Paloma), 776 y 779 (Reina) y 777 (Laura) pasan a
# Implementadas en esa versión (con título y descripción para el usuario: la sugerencia ES la novedad) y se contesta a cada
# autora en su hilo. La 779 ya se le contestó a Reina al arreglarla (API de esta mañana): solo se marca.
# La 778 (Andre, ámbito Ariadna) NO se marca aquí: la parte de la API ya funciona, pero la de Ariadna va en la 2.18; se le
# avisa de lo que ya cambia. La 780 (Laura) es un arreglo de datos (script sa), se contesta cuando se lance.
param([string]$Api = "http://api.nuevavision.es")
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

function Implementar($id, $version, $categoria, $perfiles, $titulo, $descripcion) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Version = $version; Categoria = $categoria; Perfiles = $perfiles; Titulo = $titulo; Descripcion = $descripcion } | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id implementada en $version." -ForegroundColor Green
}

Implementar 648 "1.10.49.0" "Nuevo" "Tiendas" "«Facturado en tienda»: lo facturado en la semana, en Excel" `
    "En Productos, grupo Tienda, el botón «Facturado en tienda» saca en Excel todo lo facturado en su tienda entre dos fechas (por defecto, la semana anterior), cursos incluidos: fecha, factura, pedido, cliente, dirección de entrega, comercial, forma de venta e importes, con una segunda hoja con el total por cliente. Lo pidió Paloma."
Implementar 781 "1.10.49.0" "Mejorado" "Tiendas" "Aviso a la tienda cuando su reposición llega con diferencias" `
    "Cuando en Algete reciben una reposición de su tienda y lo leído no coincide con lo enviado, el aviso de diferencias le llega también a la gente de la tienda (campana de Nesto y Ariadna), para que las compruebe en «Diferencias de reposición». Lo pidió Paloma."
Implementar 776 "1.10.49.0" "Mejorado" "Tiendas" "Aviso al inscribir en un evento si a la ficha le falta el teléfono o el correo" `
    "Al inscribir a una clienta en un evento, Nesto avisa si a su ficha le falta el teléfono o el correo electrónico, para completarlo y poder avisarla del evento. La inscripción se hace igual. Lo pidió Reina."
Implementar 777 "1.10.49.0" "Corregido" "Administración" "Mensaje claro al crear un pedido con una línea sin Tipo de IVA" `
    "Si una línea del pedido (un producto o una cuenta) no tiene Tipo de IVA, o tiene uno que no corresponde al cliente, Nesto dice qué línea es y qué le falta, en vez de un error que no se entendía. Lo pidió Laura."
Implementar 779 "1.10.49.0" "Corregido" "Tiendas" "Terminar y mandar una reposición a Algete vuelve a funcionar" `
    "Se corrigió el error («El parámetro opcional 'peticion'…») que impedía terminar una reposición desde las tiendas. Lo avisó Reina."

& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 648 -Texto "@Paloma, ya está en la versión 1.10.49.0 de Nesto, la de hoy: en Productos, grupo Tienda, el botón «Facturado en tienda» saca en Excel lo facturado en su tienda (por defecto la semana anterior, cursos incluidos), con cliente, dirección de entrega, comercial e importe, y una hoja con el total por cliente. ¿Nos dice si le vale para el informe de cifras?" | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 781 -Texto "@Paloma, ya está en la versión 1.10.49.0: cuando una reposición de Alcobendas llegue a Algete con diferencias, el aviso le llegará también a usted (campana de Nesto y Ariadna), para comprobarlas en «Diferencias de reposición». ¿Nos dice si le llega bien la próxima vez?" | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 776 -Texto "@Reina, ya está en la versión 1.10.49.0 de Nesto: al inscribir a una clienta en un evento, si a su ficha le falta el teléfono o el correo, Nesto se lo avisa para que lo complete. ¿Nos dice si le resulta útil?" | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 777 -Texto "@Laura, ya está publicado: si a una línea del pedido le falta el Tipo de IVA, Nesto le dice qué línea es en vez del error de antes. ¿Pudo crear el pedido de los ingresos de YouTube con G21?" | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 778 -Texto "@Andre, desde hoy el servidor cambia el código de barras por la referencia al recibir, así que un producto que no se esperaba ya no da el error «no existe» al terminar. En la próxima versión de Ariadna (2.18) además se verá en la lista con su referencia y su nombre, y un código que no sea de ningún producto se rechazará al leerlo. Si vuelve a pasarle, escríbalo aquí." | Out-Null
Write-Host "Hecho." -ForegroundColor Green
