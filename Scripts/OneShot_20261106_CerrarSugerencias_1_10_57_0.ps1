# Nesto 1.10.57.0 y Ariadna 2.31 (06/11/26): lanzar DESPUÉS de publicar la ClickOnce y de que la 2.31 llegue a la prueba cerrada.
# Marca Implementada cada sugerencia (la sugerencia ES la novedad: no hay fila aparte en Novedades_1_10_57_0.sql ni en
# Novedades_Ariadna_2_31.sql) y después contesta en cada hilo con la versión. Idempotente en el PUT; el comentario saldría dos veces:
# lanzarlo una sola vez (o con -SinComentarios para repetir solo los PUT).
# Uso:  pwsh -File Scripts/OneShot_20261106_CerrarSugerencias_1_10_57_0.ps1
param([string]$Api = "http://api.nuevavision.es", [string]$VersionNesto = "1.10.57.0", [string]$VersionAriadna = "2.31", [switch]$SinComentarios)
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

# ---------------- Nesto 1.10.57.0 ----------------
Implementar 1106 @{
    Version = $VersionNesto; Categoria = "Nuevo"; Perfiles = "Almacén,Compras,Administración,Dirección"
    Titulo = "Ficha de producto: ver los pedidos pendientes de servir"
    Descripcion = "En la ficha del producto, el número de «Pendientes de Entregar» de cada almacén es ahora un enlace que abre Ventas › Pedidos › Pendientes de entregar ya filtrado por ese producto y ese almacén, con el cliente y el pedido de cada línea pendiente; doble clic en una fila abre el pedido. Lo pidió Alfredo."
}
Contestar 1106 "@Alfredo, ya está en Nesto 1.10.57.0: en la ficha del producto, el número de «Pendientes de Entregar» de cada almacén abre Pendientes de entregar filtrado por ese producto y almacén, y con doble clic en la fila se abre el pedido. Revise si es lo que quería."

Implementar 1110 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = "Almacén,Dirección,Administración"
    Titulo = "Picking: la ruta 16 y la AT entregan el mismo día en que sale el picking"
    Descripcion = "El picking contaba que lo de nuestra ruta se entregaba al día siguiente, y un cliente que cierra los lunes se quedaba fuera el viernes. Ahora cuenta que se entrega el mismo día del picking (el automático de las 9), así que ese pedido sale el viernes. Almacén prepara los pedidos de la 16 el mismo día en que sale el picking, no la víspera. Lo avisó Alfredo."
}
Contestar 1110 "@Alfredo, ya está en la versión de hoy: la ruta 16 cuenta como entrega el mismo día del picking. El pedido de Valdetorres del viernes ya habría salido. Recuerde: los de la 16 se preparan el día en que sale el picking de las 9."

Implementar 1113 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = "Tiendas,Almacén"
    Titulo = "Recibir reposición escaneando: apuntar varias unidades sin etiqueta de una vez"
    Descripcion = "En el cuadro «Una unidad sin etiqueta de … ¿Por qué?» hay una casilla «Unidades» (con el máximo de lo que falta de esa línea) y el botón pasa a «Apuntar N unidades»: una sola operación y un solo aviso a Algete y a Compras con la cantidad. En Ariadna, después del motivo se pregunta «¿Cuántas?». Lo pidió Reina."
}
Contestar 1113 "@Reina, ya está en Nesto 1.10.57.0 (y en Ariadna 2.31): en el cuadro de «sin etiqueta» hay una casilla «Unidades» con el máximo de lo que falta, y se apuntan todas de una vez con un solo aviso. Revise si es lo que quería."

Implementar 1115 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = "Tiendas,Almacén"
    Titulo = "Etiqueta de producto: la referencia sale en letra grande"
    Descripcion = "En la etiqueta 3×2 con el código de barras, la referencia («Ref. 39189») sale ahora en letra grande debajo del nombre, y el código de barras un poco más abajo. Cuando el tamaño no cabe junto a la referencia, va al final del nombre. Lo pidió Reina."
}
Contestar 1115 "@Reina, ya está en la versión de hoy: en las etiquetas nuevas la referencia sale en letra grande. Las que ya tiene impresas siguen valiendo; las próximas que imprima saldrán así. Diga aquí si se ve bien o quiere la referencia aún mayor."

Implementar 752 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Dirección,Vendedores"
    Titulo = "Resumen de rapports: no marca «cliente de otro vendedor» si el cambio de vendedor es posterior al rapport"
    Descripcion = "El resumen del día miraba quién lleva al cliente en ese momento, no en el del rapport: si en ese mismo seguimiento el vendedor marcó «Quitar de mi listado», el cliente ya tenía otro vendedor y salía como ajeno. Ahora, si el cambio de vendedor es posterior al rapport y el vendedor anterior era el autor, no se marca. Lo avisó Sancho."
}
Contestar 752 "@Sancho, ya está en la versión de hoy: el resumen del día ya no marca como «cliente de otro vendedor» un rapport cuando el cambio de vendedor (por ejemplo, «Quitar de mi listado») es posterior al rapport. Revise el próximo resumen y diga aquí si sigue saliendo algo raro."

# ---------------- Ariadna 2.31 ----------------
Implementar 1104 @{
    Version = $VersionAriadna; Categoria = "Nuevo"; Perfiles = "Almacén,Tiendas"
    Titulo = "Inventario: botones «+N», «+1» y «−1» para sumar unidades sin saberse el atajo"
    Descripcion = "En la fila elegida del inventario hay tres botones: «−1», «+1» y «+N» (teclee las que faltan y pulse «+N»). Es lo mismo que N + F1 y F2, pero tocando la pantalla, también en el móvil. La instrucción de la pantalla dice «Lea una, teclee las que faltan y pulse +N». Lo pidieron Andre, Santiago y Alfredo."
}
Contestar 1104 "@Andre, ya está en Ariadna 2.31: en la fila elegida del inventario tiene los botones «−1», «+1» y «+N» (teclee las que faltan y pulse «+N»). Revise si es lo que quería."

Implementar 1109 @{
    Version = $VersionAriadna; Categoria = "Nuevo"; Perfiles = "Almacén,Tiendas"
    Titulo = "Inventario en el móvil: sumar unidades sin F1"
    Descripcion = "En el móvil no hay tecla F1: en la fila elegida del inventario hay ahora botones «−1», «+1» y «+N» para sumar unidades tocando la pantalla. Lo pidieron Santiago, Andre y Alfredo (misma mejora que la sugerencia 1104)."
}
Contestar 1109 "@Santiago, ya está en Ariadna 2.31: en el móvil, en la fila elegida del inventario, tiene los botones «−1», «+1» y «+N». Revise si le sirve."

Implementar 1111 @{
    Version = $VersionAriadna; Categoria = "Nuevo"; Perfiles = "Almacén,Tiendas"
    Titulo = "Inventario: sumar 60 unidades sin leerlas una a una"
    Descripcion = "En la fila elegida del inventario, teclee las que faltan y pulse «+N» (o «+1» / «−1»); también vale N + F1 en la PDA. Lo pidió Alfredo (misma mejora que la sugerencia 1104)."
}
Contestar 1111 "@Alfredo, ya está en Ariadna 2.31: con la fila elegida, teclee las que faltan y pulse «+N» (o «+1» / «−1» en la pantalla). Revise si le sirve."

Write-Host "Hecho." -ForegroundColor Green
