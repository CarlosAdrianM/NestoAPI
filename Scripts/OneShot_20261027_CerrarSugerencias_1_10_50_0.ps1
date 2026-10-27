# Tras publicar la API y Nesto 1.10.50.0: las sugerencias de hoy que ya están hechas pasan a Implementadas en esa versión
# (con título y descripción para el usuario: la sugerencia ES la novedad) y se contesta a cada autor en su hilo.
#   783 Aida (columna Vendedor en el extracto), 784 Enrique (stock de la tienda, API #705), 787 Paloma (etiqueta tecleada),
#   788 Paloma (recepción bloqueada por inventario, API #695), 789 Alfredo/Carlos (manifiesto, API), 790 Paloma (doble clic en
#   Diferencias), 733 Paloma (reimprimir etiquetas de bulto; en Nesto Alt+R en Enviar reposición; en Ariadna 2.19, F4).
# La 785 (Santiago, Ariadna) se marca en la 2.19 de Ariadna cuando Google la acepte en el canal cerrado (ver más abajo).
# La 791 (Andre) se contesta al lanzar el script OneShot_20261027_Repo81187_FechaEntrada40275.sql. La 786 (Laura) espera su respuesta.
param([string]$Api = "http://api.nuevavision.es", [switch]$Ariadna219)
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

function Implementar($id, $version, $categoria, $perfiles, $titulo, $descripcion) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Version = $version; Categoria = $categoria; Perfiles = $perfiles; Titulo = $titulo; Descripcion = $descripcion } | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id implementada en $version." -ForegroundColor Green
}

if ($Ariadna219) {
    Implementar 785 "2.19" "Corregido" "Almacén" "Varias PDA en el mismo picking: cada una ve al momento lo que coge la otra" `
        "Cuando dos PDA preparan el mismo picking (o la misma reposición), cada lectura le llega a la otra al instante y se aplica sola, aunque esté ocupada con una pregunta o tecleando: ya no hace falta pulsar «Actualizar» para ver lo que ha cogido el compañero, y al terminar no falta nada de lo que leyó el otro. Lo avisó Santiago."
    & "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 785 -Texto "@Santiago, @Pedro, @Andre: ya está en Ariadna 2.19, que les llega sola por Play Store. Cada PDA ve al momento lo que lee la otra, sin pulsar «Actualizar». Y con F4 en Salidas se reimprimen las etiquetas de bulto de cualquier reposición por su número. Si algo no les cuadra en el próximo picking a dos, escríbanlo aquí." | Out-Null
    Write-Host "Hecho (Ariadna 2.19)." -ForegroundColor Green
    return
}

Implementar 783 "1.10.50.0" "Mejorado" "Administración" "Extracto de cliente: columna con el comercial" `
    "En el extracto del cliente hay una columna nueva, «Vendedor», antes de «Señal», con el comercial de cada apunte. Lo pidió Aida."
Implementar 784 "1.10.50.0" "Corregido" "Tiendas,Administración" "El stock de la tienda online se actualiza al grabar pedidos pendientes" `
    "La tienda online recibía el stock de un producto solo cuando tenía un movimiento de almacén, así que no se enteraba de los pedidos pendientes grabados en Nesto y podía vender unidades ya comprometidas (pasó con la 39598). Ahora, al grabar, modificar o borrar un pedido pendiente, el producto se vuelve a mandar a la tienda al momento, y cada noche se repasan además los pedidos cambiados. Lo avisó Enrique."
Implementar 787 "1.10.50.0" "Corregido" "Tiendas,Almacén" "Recibir reposición: la etiqueta del bulto tecleada a mano ya abre el bulto" `
    "Al teclear la etiqueta de un bulto (R81177-1) en el campo «Lector» de «Recibir reposición», se abría la lista del buscador de productos y el Intro cogía un producto en vez de la etiqueta. Ya no: lo que tiene forma de etiqueta de bulto no busca productos. Lo avisó Paloma."
Implementar 788 "1.10.50.0" "Corregido" "Tiendas,Almacén" "Recibir reposición: ya no se atasca por un inventario hecho después del envío" `
    "Las entradas de una reposición se dan de alta con la fecha en que se reciben, no con la del envío, así que ya no sale «Se ha realizado un inventario posterior a esta fecha». Si un producto se regularizó por inventario entre el envío y la recepción, al abrir la reposición sale un aviso para comprobar que no se cuenta dos veces. Lo avisó Paloma."
Implementar 789 "1.10.50.0" "Corregido" "Almacén" "El manifiesto de la agencia suma los bultos de los envíos del mismo cliente" `
    "Cuando un cliente tenía dos envíos el mismo día con la misma cantidad de bultos, el informe del manifiesto los juntaba en una línea y contaba los bultos una sola vez. Ahora suma los bultos y el reembolso de todos sus envíos, como en la pantalla. Lo avisó Alfredo."
Implementar 790 "1.10.50.0" "Mejorado" "Tiendas,Almacén" "Diferencias de reposición: doble clic en el producto abre su ficha" `
    "En «Diferencias de reposición», doble clic en la referencia o en la descripción abre la ficha del producto, igual que en «Recibir reposición». Lo pidió Paloma."
Implementar 757 "1.10.50.0" "Corregido" $null "Parámetros de usuario: el botón Guardar vuelve a verse" `
    "La ventana de «Parámetros de usuario» crece con su contenido cuando llegan los parámetros, así que los botones Guardar y Cerrar se ven siempre; si hay muchos parámetros, aparece una barra de desplazamiento y los botones siguen a la vista. Lo avisó Laura."
Implementar 733 "1.10.50.0" "Nuevo" "Tiendas,Almacén" "Reimprimir las etiquetas de bulto de cualquier reposición" `
    "Si la impresora se atasca o la etiqueta sale en blanco, ya se pueden reimprimir las etiquetas de bulto de una reposición por su número: en Nesto, en «Enviar reposición», la fila «Reimprimir etiquetas de bulto de la reposición nº» (Alt+R; propone la última enviada); en Ariadna, en Salidas, el número de reposición y F4. Lo pidió Paloma, y hoy también Santiago."

& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 783 -Texto "@Aida, ya está en la versión 1.10.50.0 de Nesto, la de hoy: en el extracto del cliente tiene la columna «Vendedor» antes de «Señal». ¿Le sirve así?" | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 784 -Texto "@Enrique, ya está publicado: cuando se graba, se modifica o se borra un pedido pendiente en Nesto, el producto se vuelve a mandar a la tienda al momento, y cada noche se repasan además los pedidos cambiados. Hemos vuelto a mandar los productos con pedidos pendientes para dejar la tienda cuadrada. Lo de las 2 unidades que subieron de madrugada se lo hemos preguntado al equipo de la tienda. Si ve otra referencia descuadrada, díganoslo." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 787 -Texto "@Paloma, ya está en la versión 1.10.50.0 de Nesto: al teclear la etiqueta de un bulto (R81177-1) en «Recibir reposición» ya no se abre la lista de productos y el Intro abre el bulto. Ya no hace falta pulsar Esc antes." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 788 -Texto "@Paloma, desde hoy las entradas de las reposiciones se dan de alta con la fecha en que se reciben, así que ya no volverá a salir el error del inventario. Y si un producto se regularizó entre el envío y la recepción, al abrir la reposición le avisará para que compruebe que no se cuenta dos veces." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 789 -Texto "@Alfredo, ya está publicado: el manifiesto suma los bultos y el reembolso de todos los envíos de cada cliente, como en la pantalla. Compruébelo con el próximo manifiesto." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 790 -Texto "@Paloma, ya está en la versión 1.10.50.0 de Nesto: en «Diferencias de reposición», doble clic en la referencia o en la descripción abre la ficha del producto." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 757 -Texto "@Laura, ya está en la versión 1.10.50.0 de Nesto: la ventana de Parámetros de usuario crece con su contenido y los botones Guardar y Cerrar se ven siempre." | Out-Null
& "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad 733 -Texto "@Paloma, ya está en la versión 1.10.50.0 de Nesto: en «Enviar reposición» tiene la fila «Reimprimir etiquetas de bulto de la reposición nº» (Alt+R), que propone la última enviada y vale para cualquier reposición, aunque ya esté recibida. @Santiago, en Ariadna 2.19 es el número de reposición y F4 en Salidas." | Out-Null
Write-Host "Hecho." -ForegroundColor Green
