# Nesto 1.10.58.0 y Ariadna 2.32 (09/11/26): lanzar DESPUÉS de publicar la ClickOnce y de que la 2.32 llegue a la prueba cerrada.
# Marca Implementada cada sugerencia (la sugerencia ES la novedad: no hay fila aparte en Novedades_1_10_58_0.sql ni en
# Novedades_Ariadna_2_32.sql) y después contesta en cada hilo con la versión. Idempotente en el PUT; el comentario saldría dos veces:
# lanzarlo una sola vez (o con -SinComentarios para repetir solo los PUT).
# Después del OneShot: subir el vídeo de la botonera (botonera-nueva.mp4) a la 1144 con POST api/Novedades/1144/Video.
# Perfiles va como LISTA (ActualizarSugerenciaNovedadDTO.Perfiles es List<string>; en el OneShot del 06/11 iba como texto y se perdió:
# las 9 de la 1.10.57.0 / 2.31 se quedaron con Perfiles NULL). Solo valen Vendedores, Almacén, Tiendas, Administración y Compras
# (Dirección no es un perfil: lo ve todo); sin Perfiles = para todos.
# Uso:  pwsh -File Scripts/OneShot_20261109_CerrarSugerencias_1_10_58_0.ps1
param([string]$Api = "http://api.nuevavision.es", [string]$VersionNesto = "1.10.58.0", [string]$VersionAriadna = "2.32", [switch]$SinComentarios)
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

function Implementar([int]$id, [hashtable]$datos) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes(($datos | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id implementada en $($datos.Version)." -ForegroundColor Green
}
function Cerrar([int]$id) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Estado = "Cerrada" } | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id cerrada." -ForegroundColor Green
}
function Contestar([int]$id, [string]$texto, [int[]]$contestados = @()) {
    if ($SinComentarios) { return }
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Texto = $texto; ComentariosContestados = @($contestados) } | ConvertTo-Json))
    Invoke-RestMethod -Method Post -Uri "$Api/api/Novedades/$id/Comentarios/Asistente" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Contestada la $id." -ForegroundColor Cyan
}

# ---------------- Nesto 1.10.58.0 ----------------
Implementar 1060 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = @("Vendedores", "Tiendas", "Administración")
    Titulo = "Plantilla de venta: Intro en la cantidad vuelve al buscador"
    Descripcion = "En la plantilla de venta, al escribir la cantidad y pulsar Intro, la cantidad se confirma y el cursor vuelve solo al buscador, con el texto seleccionado para escribir el siguiente producto. Si la cantidad no se puede confirmar, el cursor se queda en ella. Tab y los atajos del buscador siguen igual. Vale también en la selección de regalos. Lo pidió Paloma."
}
Contestar 1060 "@Paloma, ya está en Nesto 1.10.58.0: en la plantilla, al escribir la cantidad y pulsar Intro, el cursor vuelve solo al buscador con el texto seleccionado, listo para el siguiente producto. Revise si es lo que quería."

Implementar 1058 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = @("Vendedores", "Administración")
    Titulo = "Alta de cliente: las direcciones de otros países salen en su formato"
    Descripcion = "Al elegir en el alta de cliente la dirección que propone Google, sale en el formato de su país: en Francia, «12 Rue de la Paix» (primero el número); en España, «Calle Mayor, 12». Antes Nesto las pasaba todas al formato español. Lo pidió Enrique."
}
Contestar 1058 "@Enrique, ya está en Nesto 1.10.58.0: al dar de alta un cliente de Francia, la dirección que propone Google se queda en el formato francés (primero el número y luego la calle). Revise con el próximo cliente extranjero si es lo que quería."

Implementar 1141 @{
    Version = $VersionNesto; Categoria = "Nuevo"; Perfiles = @("Tiendas", "Administración")
    Titulo = "Eventos: «Ver anuladas» en los inscritos"
    Descripcion = "En la cabecera de los inscritos de un evento hay una casilla «Ver anuladas», desmarcada: sin marcarla, las inscripciones anuladas no salen en la lista (las caducadas sí, porque aún se pueden pagar). Nesto recuerda lo que elija cada uno. Los contadores no cambian. Lo pidió Paloma."
}
Contestar 1141 "@Paloma, ya está en Nesto 1.10.58.0: en los inscritos de un evento, las anuladas ya no salen salvo que marque «Ver anuladas» (Nesto recuerda lo que elija). Revise si es lo que quería."

# La 823 ya era Implementada en 1.10.52.0 (la letra a 12). Aida contestó el 09/11 que le salen 3 filas y necesita 5: pasa a 1.10.58.0
# con el texto de las dos mejoras (así le sale como novedad de esta versión).
Implementar 823 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = @("Administración")
    Titulo = "Cajas › Cobros: el extracto para liquidar efectos enseña al menos 5 filas"
    Descripcion = "En la pestaña de cobros de las cajas, la lista del extracto para liquidar efectos tiene ahora más alto: al menos 5 filas, y más si cabe en la pantalla; los totales y el concepto ocupan menos. Gastos y Traspasos no cambian. Lo pidió Aida."
}
Contestar 823 "@Aida, ya está en Nesto 1.10.58.0: en Cobros, la lista del extracto enseña al menos 5 filas (más si su pantalla tiene sitio). Revise si ahora le basta." @(804)

Implementar 1130 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = @("Administración")
    Titulo = "Pedidos de la tienda online: el cheque regalo se mira con la fecha del pedido"
    Descripcion = "Un pedido de la tienda online hecho con la campaña del cheque regalo abierta conserva su cheque aunque se pase a Nesto cuando la campaña ya ha acabado (por ejemplo, hecho el viernes y pasado el lunes): el plazo se mira con la fecha del pedido en la tienda. Lo avisó Laura."
}
Contestar 1130 "@Laura, ya está en Nesto 1.10.58.0: al pasar a Nesto un pedido de la tienda online, el cheque regalo se mira con la fecha en que se hizo el pedido en la tienda, así que los del fin de semana ya no lo pierden. Revise en la próxima campaña si es lo que quería."

Implementar 1129 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = @("Almacén", "Administración")
    Titulo = "Cheque regalo: lo que ya estaba «a recoger» cuenta para el mínimo"
    Descripcion = "Al sacar el picking de un pedido con cheque regalo, las líneas que venían «a recoger» (producto en carpeta) de un pase anterior no contaban para el mínimo de 250 € y la línea del cheque se quedaba sin picking. Ahora cuentan, y el cheque sale con el mismo picking que el resto (igual para el mínimo de los cupones). Lo avisó Alfredo (pedido 930110)."
}
Contestar 1129 "@Alfredo, este arreglo (lo que ya estaba «a recoger» cuenta para el mínimo del cheque) está en el servidor desde esta mañana, como ya comprobó con el 930110, y queda apuntado en las novedades de Nesto 1.10.58.0. Gracias de nuevo por avisar."

Implementar 1116 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = @("Administración", "Tiendas")
    Titulo = "Cheque regalo: los cursos cuentan para el mínimo"
    Descripcion = "Como dicen las bases de la campaña, los cursos sí cuentan para llegar al mínimo del cheque regalo; el programa los dejaba fuera, como los portes. Está corregido en el servidor desde el 06/11 y vale para las próximas campañas. Lo avisó Pilar."
}
Contestar 1116 "@Pilar, queda apuntado en las novedades de Nesto 1.10.58.0: los cursos cuentan para el mínimo del cheque regalo (el arreglo está en el servidor desde el viernes 06/11). Si en algún pedido le sigue saliendo «Lleva 0,00 €» con un curso, díganoslo aquí."

Implementar 1139 @{
    Version = $VersionNesto; Categoria = "Nuevo"; Perfiles = @("Administración")
    Titulo = "Enlaces de pago: botón «Anular enlace»"
    Descripcion = "En Contabilidad › Cobros › «Enlaces de pago», con un enlace de pago pendiente elegido, el botón «Anular enlace» pide el motivo (por ejemplo, «Pedido ya cobrado por reembolso») y confirmación, y lo deja Cancelado. Si ya está pagado o anulado, Nesto dice por qué no se puede. Sirve, por ejemplo, para que el almacén pueda sacar la etiqueta de un pedido que esperaba un enlace que ya no se va a pagar. Lo preguntó Magan."
}
Contestar 1139 "@Magan, ya está en Nesto 1.10.58.0: además de la consulta del almacén que le explicamos, ahora puede anular un enlace de pago suelto en Contabilidad › Cobros › «Enlaces de pago»: elija el enlace pendiente, pulse «Anular enlace» y escriba el motivo. Revise si es lo que necesitaba."

Implementar 1140 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = @("Almacén", "Administración")
    Titulo = "Sacar la etiqueta: la consulta a Administración ve los enlaces de pago de otros pedidos"
    Descripcion = "Si la etiqueta no sale por un enlace de pago pendiente de otro pedido del mismo cliente, la consulta a Administración ahora lo encuentra y se puede anular desde ahí (antes decía «no tiene enlaces pendientes» y no había salida). Los enlaces de pedidos ya cobrados no cuentan. Lo avisó Alfredo (pedido 930167)."
}
Contestar 1140 "@Alfredo, ya está en la versión de hoy (Nesto 1.10.58.0): si la etiqueta se bloquea por un enlace de pago de otro pedido del cliente, la consulta a Administración ya lo ve y lo pueden anular desde ahí. Revise si le vuelve a pasar."

Implementar 1131 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = @("Almacén")
    Titulo = "Picking: lo que la oficina ya ha albaranado no impide terminarlo en la PDA"
    Descripcion = "Si mientras el mozo recoge un picking en la PDA la oficina albarana o saca del picking alguno de sus pedidos, lo ya leído de esos pedidos ya no impide terminarlo; y si algo lo impide, la PDA dice qué productos son y qué hacer («apártelos para ese pedido y avise a la oficina») en vez de «Quedan 0 unidades». Para sacar un pedido del picking está el botón nuevo «Quitar picking del pedido» de la ventana de Picking. Lo avisaron Alfredo y Pedro (picking 100033)."
}
Contestar 1131 "@Alfredo @Pedro, ya está en Nesto 1.10.58.0 y Ariadna 2.32: lo ya albaranado no impide terminar el picking, y si algo lo impide la PDA dice qué productos son. Para sacar un pedido del picking, use en Nesto el botón «Quitar picking del pedido» de la ventana de Picking: a la PDA le llega al momento lo que hay que apartar. Revisen si es lo que querían."

Implementar 853 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = @("Vendedores", "Administración")
    Titulo = "Correo de pedidos con la fecha en riesgo: el motivo, el del momento del correo"
    Descripcion = "El correo de los pedidos cuya fecha de entrega está en riesgo rehace el porqué justo antes de enviarse, con el stock y las reposiciones de ese momento: si por la mañana el producto tenía que venir de Alcobendas y a la hora del correo ya está en Reina, el correo dice Reina. Lo avisó Lidia."
}
Contestar 853 "@Lidia, ya está en la versión de hoy (Nesto 1.10.58.0): el correo de pedidos en riesgo calcula el motivo justo antes de enviarse, así que si el producto ya está en otra tienda, el correo lo dice. Revise en los próximos correos si es lo que quería."

Implementar 1144 @{
    Version = $VersionNesto; Categoria = "Nuevo"
    Titulo = "Pedidos: botonera nueva, más clara y con menús"
    Descripcion = "En el detalle del pedido, los botones de abajo cambian: a la vista, con icono, solo los de cada día (Sacar picking, Descargar PDF, Enviar por correo… y Facturar rutas; y Aceptar presupuesto cuando toca). Los demás están en «Más», agrupados: Documentos (WhatsApp, Copiar factura), Facturación (Fin de mes, Pasar a presupuesto) y Notas (cobro, recogida, leer etiqueta). «Retener…» queda aparte, a la derecha y en otro color, con «Liberar» al lado si el pedido está retenido. Los atajos Ctrl+Mayús+P/D/E/F/R siguen igual. Tiene un vídeo corto."
}
Contestar 1144 "@Carlos, ya está en Nesto 1.10.58.0: la botonera del pedido con WPF-UI (a la vista Sacar picking, Descargar PDF, Enviar por correo y Facturar rutas; el resto en «Más» por grupos, y «Retener…» aparte). Lleva el vídeo de la botonera. Revise si es lo que quería."

Implementar 1132 @{
    Version = $VersionNesto; Categoria = "Nuevo"; Perfiles = @("Tiendas", "Administración")
    Titulo = "Eventos por compra: importe mínimo de compra"
    Descripcion = "En Clientes › Eventos, junto a «Hay que comprar», el campo «Importe mínimo de compra (€)»: lo que la clienta tiene que haber comprado o pedido de esos productos (base imponible, por asistente) para poder inscribirse. Vacío = sin mínimo (basta con haber comprado uno). Así un curso por compra como el de los viales de Eva Visnú (400 € en viales) se crea gratuito, con sus productos requeridos y su importe mínimo, sin señal. Si el evento ya tiene inscritos, pasarlo de pago a gratuito no se puede y Nesto explica qué hacer. Lo pidió Paloma."
}
Contestar 1132 "@Paloma, ya está en Nesto 1.10.58.0: en el evento, junto a «Hay que comprar», puede poner el «Importe mínimo de compra (€)» (por ejemplo, 400 € en viales de Eva Visnú). Y si intenta pasar a gratuito un evento que ya tiene inscritos, Nesto le dice por qué no se puede y qué hacer, en vez de no guardar sin avisar. Revise si es lo que quería."

# Novedad 1121 (no es sugerencia): el comentario 798 de Alfredo, «Insertar e Imprimir» de un pedido de nuestra ruta para mañana daba error.
Contestar 1121 "@Alfredo, ya está en la versión de hoy (Nesto 1.10.58.0): «Insertar e Imprimir» en Agencias ya saca la etiqueta de nuestra ruta de un pedido con fecha de mañana, con la fecha de reparto. Revise si le vuelve a dar el error." @(798)

# ---------------- PENDIENTE si entra #847/#848 ----------------
# Descomentar SOLO si el commit está en master y entra en esta publicación (y ajustar el texto a lo que haga de verdad).
# Implementar 1103 @{
#     Version = $VersionNesto; Categoria = "Corregido"; Perfiles = @("Almacén")
#     Titulo = "Reparto propio: volver a imprimir la etiqueta no crea otro envío"
#     Descripcion = "Si la etiqueta de nuestra ruta no sale y se vuelve a pulsar «Insertar e Imprimir», se reimprime la del envío que ya tiene el pedido en vez de crear otro. Lo avisó Alfredo (envío triplicado del cliente 1179)."
# }
# Contestar 1103 "@Alfredo, ya está en Nesto 1.10.58.0: si la etiqueta de nuestra ruta no sale y vuelve a pulsar «Insertar e Imprimir», se reimprime la del mismo envío, sin crear otro. Revise si le vuelve a pasar."
#
# Implementar 1128 @{
#     Version = $VersionNesto; Categoria = "Nuevo"; Perfiles = @("Tiendas", "Administración", "Vendedores")
#     Titulo = "Alta de cliente: más orígenes (Página web, Amazon, tiendas y Cursos)"
#     Descripcion = "En el desplegable «Origen» del alta de cliente hay orígenes nuevos: Página web, Amazon, Tienda de Reina, Tienda de Alcobendas y Cursos. Son para medir de dónde vienen los clientes (no son campañas que haya que llamar). El origen se elige siempre a mano, pero Nesto propone el más probable según quién crea el cliente (por ejemplo, la tienda de quien lo da de alta). Lo pidió Laura."
# }
# Contestar 1128 "@Laura, ya está en Nesto 1.10.58.0: el desplegable «Origen» del alta de cliente tiene Página web, Amazon, Tienda de Reina, Tienda de Alcobendas y Cursos, y propone uno según quién crea el cliente. Revise si es lo que quería."

# ---------------- Ariadna 2.32 ----------------
Implementar 1135 @{
    Version = $VersionAriadna; Categoria = "Corregido"; Perfiles = @("Almacén")
    Titulo = "Terminar el picking: la PDA dice por qué no deja"
    Descripcion = "Cuando «Terminar» no deja terminar el picking, la PDA enseña el motivo (antes salía «NO SE HA TERMINADO» sin más). Y si la oficina albarana o saca del picking alguno de sus pedidos mientras se recoge, lo ya leído de esos pedidos no lo impide: si saca un pedido, la PDA lo sabe al momento y dice qué hay que apartar. Lo avisó Pedro (picking 100033)."
}
Contestar 1135 "@Pedro, ya está en Ariadna 2.32: si no le deja terminar un picking, la PDA le dice el motivo, y lo que la oficina ya ha albaranado no se lo impide. Revise si es lo que quería."

Implementar 1133 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = @("Tiendas")
    Titulo = "Reposiciones: la tienda se entera cuando una diferencia le devuelve stock"
    Descripcion = "Cuando una reposición llega a Algete con menos unidades de las que mandó la tienda, esas unidades vuelven a contar en la tienda. Ahora a la gente de la tienda y a quien envió la reposición les llega un aviso (en Ariadna y en Nesto), producto a producto: «La reposición 81296 llegó a Algete con 1 … menos de los que mandó: se ha vuelto a contar en su tienda. Si no lo tiene, márquelo «No está» en la próxima reposición o búsquelo en el bulto». Así se sabe por qué vuelve a salir. Lo avisó Paloma."
}
Contestar 1133 "@Paloma, ya está en la versión de hoy: cuando una reposición llega a Algete con menos unidades de las que mandó, le llega un aviso de que se han vuelto a contar en su tienda y qué hacer (marcarlo «No está» en la próxima reposición o buscarlo en el bulto). Revise si es lo que quería."

# Duplicadas: se cierran remitiendo a la buena (la respuesta está en el otro hilo).
Contestar 1143 "@Pedro, es lo mismo que la 1135: ya está en Ariadna 2.32 (la PDA dice el motivo y lo albaranado no impide terminar). Cerramos esta; si necesita algo más, escriba aquí y se reabre."
Cerrar 1143
Contestar 1134 "@Paloma, es lo mismo que la 1133: desde hoy le llega un aviso cuando una diferencia de reposición vuelve a contar unidades en su tienda. Cerramos esta; si necesita algo más, escriba aquí y se reabre."
Cerrar 1134

Write-Host "Hecho. Ahora: POST api/Novedades/1144/Video con botonera-nueva.mp4." -ForegroundColor Green
