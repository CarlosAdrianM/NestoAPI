# Nesto 1.10.55.0 y Ariadna 2.29 (04/11/26): lanzar DESPUÉS de publicar la ClickOnce y de que la 2.29 llegue a la prueba cerrada.
# Marca Implementada cada sugerencia (la sugerencia ES la novedad: no hay fila aparte en Novedades_1_10_55_0.sql ni en
# Novedades_Ariadna_2_29.sql) y después contesta en cada hilo con la versión. Idempotente (el PUT y el comentario se pueden repetir,
# pero el comentario saldría dos veces: lanzarlo una sola vez).
# Uso:  pwsh -File Scripts/OneShot_20261104_CerrarSugerencias_1_10_55_0.ps1
param([string]$Api = "http://api.nuevavision.es", [string]$VersionNesto = "1.10.55.0", [string]$VersionAriadna = "2.29", [switch]$SinComentarios)
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

# ---------------- Nesto 1.10.55.0 ----------------
Implementar 979 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Tiendas,Almacén"
    Titulo = "Recibir reposición: el botón «−1» siempre a la vista"
    Descripcion = "El botón «−1» (quita una unidad leída por error) va ahora en su propia columna y se ve siempre, sin ensanchar nada. Y si se pulsa «Sin etiqueta» sin querer, el aviso dice que se puede deshacer con «−1». Lo avisó Paloma."
}
Contestar 979 "@Paloma, ya está en Nesto 1.10.55.0: el botón «−1» tiene su propia columna y se ve siempre, y el aviso de «Sin etiqueta» recuerda que se deshace con «−1»."

Implementar 970 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = "Administración,Almacén"
    Titulo = "Agencias: copiar el Nº Envío"
    Descripcion = "En las rejillas de Agencias, Ctrl+C copia la celda pinchada (el Nº Envío, el pedido, el cliente…), y en Tramitados, Incidentados y Retrasados hay un botón «Copiar nº de envío» junto a «Abrir seguimiento». Lo pidió Aida."
}
Contestar 970 "@Aida, ya está en Nesto 1.10.55.0: Ctrl+C sobre la celda copia el Nº Envío (o lo que haya en la celda) y hay un botón «Copiar nº de envío» junto a «Abrir seguimiento»."

Implementar 969 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = "Tiendas"
    Titulo = "Cajas: las tiendas pueden consultar hasta 10 días hacia atrás"
    Descripcion = "La ventana de Cajas dejaba a las tiendas ir solo 4 días hacia atrás; ahora son 10. Lo pidió Paloma."
}
Contestar 969 "@Paloma, ya está en Nesto 1.10.55.0: en Cajas puede ir hasta 10 días hacia atrás."

Implementar 1003 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Vendedores,Tiendas,Dirección"
    Titulo = "Plantilla: el cheque regalo se ofrece aunque se haya generado después de elegir al cliente"
    Descripcion = "La plantilla vuelve a mirar el cheque regalo del cliente al llegar a Finalizar y antes de grabar, y si el pedido supera los 250 € la casilla «Usar el cheque regalo» sale marcada (se puede desmarcar). Además, «Ampliar en el pedido» y «Modificar con la plantilla» respetan el modo de entrega del pedido: ya no sale «Pedir el cambio a almacén» ni se pierde la ampliación. Lo avisó Reina (clienta 9238)."
}
Contestar 1003 "@Reina, ya está en Nesto 1.10.55.0: la plantilla vuelve a mirar el cheque al finalizar y, si el pedido pasa de 250 €, la casilla sale marcada; y ampliar un pedido con picking ya no pide el cambio a almacén ni pierde lo añadido."

Implementar 1006 @{
    Version = $VersionNesto; Categoria = "Nuevo"; Perfiles = "Vendedores,Tiendas,Dirección"
    Titulo = "Eventos: número de asistentes por inscripción"
    Descripcion = "Al inscribir a un cliente en un evento se indica cuántas personas vienen de ese centro («Asistentes», 1 por defecto); la inscripción ocupa esas plazas y, si el evento exige compra, hace falta una unidad comprada por asistente (dos kits, dos personas). En la ficha de la inscripción se puede cambiar con «Cambiar» y la lista de inscritos enseña los asistentes de cada uno. Lo pidió Reina."
}
Contestar 1006 "@Reina, ya está en Nesto 1.10.55.0: en «Inscribir en un evento» hay un campo «Asistentes» (ponga 2 para la 41286: tiene dos kits) y en la ficha de la inscripción se cambia con «Cambiar». Cada inscripción ocupa tantas plazas como asistentes."

Implementar 1005 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Almacén,Administración"
    Titulo = "Reparto propio: al facturar una ampliación, Nesto pregunta si actualiza el reembolso del envío"
    Descripcion = "Si un pedido ya tiene envío de reparto propio (sin entregar) y al facturar lo facturado no coincide con el reembolso del envío (porque se amplió después), Nesto pregunta si lo actualiza y lo cambia sin tocar la contabilidad (el reparto propio cobra al entregar). Fue lo del 928723: 480,02 € en el envío y 510,15 € facturados. Lo avisó Alfredo."
}
Contestar 1005 "@Alfredo, ya está en Nesto 1.10.55.0: al facturar, si el envío de nuestra ruta lleva un reembolso distinto de lo facturado, Nesto pregunta si lo actualiza y lo cambia (sin contabilidad, que la hace Miguel al cobrar)."

Implementar 1010 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Almacén"
    Titulo = "Picking entre dos: lo leído se apunta a la parada de la que se leyó"
    Descripcion = "Con dos PDA en el mismo picking, lo que leía uno se apuntaba a la primera parada del producto en vez de a la parada desde la que lo leyó, y los productos pendientes de ubicar (sin hueco) les salían a los dos. Ahora cada lectura va a su parada y las dos PDA ven lo mismo. Fue lo del picking 100001. Lo avisó Alfredo."
}
Contestar 1010 "@Alfredo, ya está publicado en la API: con dos PDA en el mismo picking, lo que lee cada uno se apunta a la parada de la que lo leyó (antes iba a la primera del producto) y los pendientes de ubicar ya no salen a los dos."

Implementar 794 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = "Administración"
    Titulo = "Agencias: cambiar el reembolso de un envío ya entregado para que cuadre con lo que pagó la agencia"
    Descripcion = "Un envío entregado cuyo reembolso aún no ha pagado la agencia admite cambiar solo el reembolso (lo que cobró de verdad CTT), sin avisar a la agencia; Nesto deshace el apunte anterior y hace el nuevo, y «Contabilizar reembolsos» cuadra con la transferencia. La diferencia queda pendiente en el extracto del cliente. Lo pidió Aida."
}

# ---------------- Ariadna 2.29 ----------------
Implementar 968 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén,Tiendas"
    Titulo = "«Leídas 2 de 2 · quedan 0» en vez de «Quedan: 0/2»"
    Descripcion = "La línea destacada de todas las pantallas dice ahora «Leídas 2 de 2 · quedan 0» (y la lista de abajo «2 de 2»), que no se entiende al revés. Lo avisó Andre."
}
Contestar 968 "@Andre, ya está en Ariadna 2.29 (llega sola por Play Store): «Leídas 2 de 2 · quedan 0» en todas las pantallas."

Implementar 965 @{
    Version = $VersionAriadna; Categoria = "Nuevo"; Perfiles = "Almacén"
    Titulo = "Packing: siguiente caja sin volver a la lista"
    Descripcion = "Al cerrar una caja con su foto, si a la entrega le quedan unidades, Ariadna pregunta «¿Abrir la caja 2 de esta entrega?»: Intro (o seguir leyendo) abre la siguiente caja; Esc vuelve a la lista. Lo pidió Andre."
}
Contestar 965 "@Andre, ya está en Ariadna 2.29: al cerrar una caja con cosas pendientes, Intro abre la siguiente caja de la misma entrega sin volver a la lista."

Implementar 1007 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén,Tiendas"
    Titulo = "El botón «Leer» (cámara) a la derecha de la casilla del código"
    Descripcion = "En todas las pantallas, la casilla del código va primero y el botón de la cámara a la derecha. Lo pidió Paloma."
}
Contestar 1007 "@Paloma, ya está en Ariadna 2.29: el botón «Leer» va a la derecha de la casilla en todas las pantallas."

Implementar 1008 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén,Tiendas"
    Titulo = "La línea destacada enseña la familia y el tamaño del producto"
    Descripcion = "Bajo la referencia sale «Eva Visnú · 250 ml.» (la casa y el tamaño), también en Enviar reposición. Lo pidió Paloma."
}
Contestar 1008 "@Paloma, ya está en Ariadna 2.29: debajo de la referencia sale la familia y el tamaño («Eva Visnú · 250 ml.»)."

Implementar 958 @{
    Version = $VersionAriadna; Categoria = "Nuevo"; Perfiles = "Almacén"
    Titulo = "Packing: la etiqueta de un pedido de nuestra ruta"
    Descripcion = "En Empaquetar, F8 «Etiqueta de agencia» también vale para los pedidos que van por reparto propio: crea el envío y saca las etiquetas de bulto RP, igual que las de GLS o CTT. Antes decía «la agencia 14 no se puede insertar desde el servidor». Lo avisó Alfredo."
}
Contestar 958 "@Alfredo, ya está publicado: en Empaquetar, F8 saca también la etiqueta de los pedidos de nuestra ruta (crea el envío RP y sus etiquetas de bulto)."

Implementar 1004 @{
    Version = $VersionAriadna; Categoria = "Corregido"; Perfiles = "Almacén"
    Titulo = "Picking: Terminar ya no se bloquea por una falta de un producto que se quitó del pedido"
    Descripcion = "Si se da por falta un producto cuya línea alguien ha sacado del pedido después de crear el picking, Terminar ya no da «no hay tanto que sacar»: la falta se ignora y el picking se cierra. Fue lo del picking 99998. Lo avisó Alfredo."
}

# ---------------- Añadidas por la tarde (04/11/26): sugerencias 1012-1020 ----------------
Implementar 1020 @{
    Version = $VersionNesto; Categoria = "Mejorado"; Perfiles = "Tiendas,Almacén,Dirección"
    Titulo = "Enviar reposición: la puntual que se lleva un compañero, también después de la recogida"
    Descripcion = "Cuando un compañero pasa por la tienda y va luego a Algete (Manuel, Lidia, Mª José…), se le puede dar una reposición puntual aunque Miguel ya se haya llevado la de hoy. Al mandarla, Nesto pregunta «¿Quién la lleva?»: el repartidor (como siempre) o un compañero, con su nombre. Con compañero queda entregada en el acto («Entregada a Manuel a las 13:10»), sin leer bultos ni pasar por la furgoneta, y en Algete la reciben como siempre. Lo pidió Paloma."
}
Contestar 1020 "@Paloma, ya está en Nesto 1.10.55.0: la puntual se puede mandar aunque Miguel ya se haya llevado la de hoy; al enviarla, elija «Un compañero» y escriba su nombre, y queda «Entregada a Manuel a las 13:10» sin leer bultos. En Algete la reciben como siempre."

Implementar 1013 @{
    Version = $VersionNesto; Categoria = "Corregido"; Perfiles = "Administración,Tiendas,Almacén"
    Titulo = "Picking: una factura contra reembolso que aún no se ha entregado no retiene los pedidos prepago del cliente"
    Descripcion = "Desde que el reembolso se contabiliza al entregar, la factura contra reembolso figuraba pendiente (y vencida) mientras la agencia la llevaba, y el picking la contaba como deuda: un pedido prepago del mismo cliente se quedaba retenido aunque tuviera su prepago (el 929888 del 31517). Ya no cuenta como deuda hasta que se entregue. Lo avisó Laura."
}
Contestar 1013 "@Laura, ya está publicado: una factura contra reembolso cuyo pedido aún no se ha entregado ya no cuenta como deuda y no retiene los pedidos prepago del cliente. El 929888 cogerá picking en la primera pasada."

Implementar 1012 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén,Tiendas"
    Titulo = "Recibir: «No lleva etiqueta» solo para los productos sin código de barras"
    Descripcion = "Si el producto tiene código de barras en la ficha, al elegir «1 No lleva etiqueta» Ariadna ya no avisa a Algete: le dice «el 14774 tiene código de barras 8436042870615: léalo del envase». Si el envase lleva otro código o no se lee, elija «2» o «3»: ese aviso va a Compras, que es quien tiene que arreglar el código. Lo avisó Alfredo."
}
Contestar 1012 "@Alfredo, @Reina, ya está en Ariadna 2.29 (llega sola por Play Store): con un producto que tiene código de barras en la ficha, «1 No lleva etiqueta» ya no avisa a Algete, sino que pide leer el código del envase; «2» y «3» avisan a Compras."

Implementar 1018 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén"
    Titulo = "Packing: el 3+1 se mete como 4 y la tarjeta se vacía al terminar"
    Descripcion = "Las líneas del mismo producto (las de pago y la de regalo de una oferta) salen como una sola tarjeta («Quedan: 4/4») y lo que mete se reparte entre ellas; y al meter la última unidad la tarjeta grande se vacía y queda solo «Todo dentro. Cierre la caja». Lo pidió Santiago."
}
Contestar 1018 "@Santiago, ya está en Ariadna 2.29: el 3+1 sale como una sola tarjeta de 4 y lo que mete se reparte entre las dos líneas."

Implementar 1015 @{
    Version = $VersionAriadna; Categoria = "Mejorado"; Perfiles = "Almacén"
    Titulo = "Packing: la tarjeta se vacía al meter la última unidad"
    Descripcion = "Al meter la última unidad de la entrega, la tarjeta grande del producto se vacía y queda solo «Todo dentro. Cierre la caja (Intro)» con la lista de líneas hechas debajo. Lo pidió Santiago."
}
Contestar 1015 "@Santiago, ya está en Ariadna 2.29: al meter la última unidad la tarjeta se vacía y queda solo «Todo dentro. Cierre la caja»."

Implementar 1017 @{
    Version = $VersionAriadna; Categoria = "Corregido"; Perfiles = "Almacén"
    Titulo = "Packing: la etiqueta de agencia vuelve a facturar el pedido"
    Descripcion = "Desde el 3 de noviembre, al sacar la etiqueta desde Ariadna el pedido no se albaranaba ni se facturaba (el parámetro «Facturar al imprimir etiqueta» estaba a 0 para los usuarios de la PDA). Desde el 4 de noviembre a las 14:00 vuelve a facturar igual que la casilla de Agencias en Nesto. Lo avisó Alfredo."
}
