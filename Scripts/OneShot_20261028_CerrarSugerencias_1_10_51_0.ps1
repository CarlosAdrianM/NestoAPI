# Tras publicar la API y Nesto 1.10.51.0 (28/10/26): las sugerencias de hoy que ya están hechas pasan a Implementadas en esa
# versión (la sugerencia ES la novedad) y se contesta a cada autor en su hilo; se avisa en las novedades del P.O. y del fin de
# mes a quienes les toca; y se corrigen los perfiles de las novedades del P.O. (afectan a comerciales y almacén, no solo a
# Administración). Con -Ariadna221, lo de Ariadna 2.21 (822 y 824) cuando Google la acepte en el canal cerrado.
param([string]$Api = "http://api.nuevavision.es", [switch]$Ariadna221)
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

function Implementar($id, $version, $categoria, $perfiles, $titulo, $descripcion) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Version = $version; Categoria = $categoria; Perfiles = $perfiles; Titulo = $titulo; Descripcion = $descripcion } | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id implementada en $version." -ForegroundColor Green
}
function Perfiles($id, $perfiles) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Perfiles = $perfiles } | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/$id/Perfiles" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Novedad $id → perfiles: $perfiles" -ForegroundColor Green
}
function Contestar($novedad, $texto) {
    & "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad $novedad -Texto $texto | Out-Null
    Write-Host "Contestada $novedad." -ForegroundColor Green
}

if ($Ariadna221) {
    Implementar 822 "2.21" "Mejorado" "Almacén,Tiendas" "Un código que se parece a uno del producto se relee antes de añadirlo" `
        "Si lee un código que no es de ningún producto pero se diferencia en una o dos cifras de uno del producto que tiene delante, Ariadna no lo añade a la primera: pide leerlo dos veces más. Si las tres lecturas coinciden se añade y se avisa a Compras; si no, se descarta. La cámara también exige dos detecciones seguidas de un código que no conoce. Lo avisó Santiago (el 28893 leído como 9… en vez de 8…)."
    Contestar 822 "@Santiago, ya está en Ariadna 2.21, que les llega sola por Play Store: un código que se parece a uno del producto se tiene que leer tres veces antes de añadirse, y se avisa a Compras para que lo compruebe. El 27091 que se creó mal ya está dado de baja. Si vuelve a colarse alguno, escríbalo aquí con el producto."
    Contestar 846 "@Andre, ya está publicado lo de la lista de salidas: cuando una entrega tiene todas las etiquetas hechas desde Nesto, el picking desaparece de la lista aunque no tenga cajas con foto. Y con Ariadna 2.21, F8 en el packing imprime también las etiquetas de GLS. Si mañana ve un picking que no desaparece, díganos cuál."
    Implementar 824 "2.21" "Nuevo" "Almacén,Tiendas" "Ficha del producto desde el inicio: «Producto (0)»" `
        "En el inicio de Ariadna hay un botón nuevo, «Producto» (tecla 0): leyendo o tecleando un código sale la ficha del producto con la foto, el nombre, la familia, la referencia del proveedor, los códigos de barras con sus unidades y, por cada almacén, el stock, lo pendiente de entregar y los huecos. F2 «Algo está mal», F3 «Añadir código», F4 etiqueta, F5 recargar, Esc volver. Lo pidió Andre."
    Contestar 824 "@Andre @Alfredo @Santiago, ya está en Ariadna 2.21, que les llega sola por Play Store: en el inicio, «Producto» (tecla 0). Lea o teclee un código y sale la ficha con foto, nombre, familia, referencia del proveedor, códigos de barras con unidades y, por almacén, stock, pendiente de entregar y huecos. F2 avisa de un dato mal, F3 añade un código (con la relectura de seguridad), F4 imprime la etiqueta y Esc vuelve. Lo que echen en falta, aquí."
    Write-Host "Hecho (Ariadna 2.21)." -ForegroundColor Green
    return
}

# Perfiles de las novedades del P.O. y del fin de mes: también comerciales (Paloma) y almacén (Alfredo)
Perfiles 838 "Vendedores,Tiendas,Administración,Dirección"
Perfiles 839 "Vendedores,Tiendas,Almacén,Administración,Dirección"
Perfiles 840 "Vendedores,Tiendas,Almacén,Administración,Dirección"
Perfiles 841 "Almacén,Administración,Dirección"

Implementar 770 "1.10.51.0" "Nuevo" "Administración,Dirección,Tiendas,Vendedores" "Crear cliente: alumnas y público final" `
    "En el asistente de crear cliente, en «Datos Comisiones», hay dos casillas nuevas: «¿Es alumna?» (cursos y eventos) y «¿Es público final?» (profesional de otro sector que pide factura). Con una marcada no hace falta elegir estética ni peluquería: el estado y el vendedor (NV) los pone Nesto. Si una alumna tiene centro y hay que visitarla, créela como estética o peluquería. Lo pidieron Paloma y Laura."
# 797 es la NOVEDAD de «Retener pedido» (1.10.50.0), no una sugerencia: solo se contesta.
Implementar 830 "1.10.51.0" "Corregido" "Vendedores,Administración" "Resumir rapports vuelve a funcionar" `
    "El botón «Resumir rapports» del cliente daba un aviso sin explicación: el modelo de inteligencia artificial que usaba lo han retirado. Ya funciona con el modelo actual y, si fallara, dice el motivo. Lo avisó Javier."
Implementar 831 "1.10.51.0" "Nuevo" "Administración,Vendedores,Tiendas" "Enviar por correo el pedido, la factura o el mandato SEPA sin descargarlos" `
    "En el detalle del pedido, debajo de «Descargar PDF», hay un botón «Enviar por correo…»; en el menú de «Imprimir Factura» está también para la factura, y en la ficha del cliente, junto a «Imprimir» del mandato SEPA. Se abre un diálogo con los correos del cliente ya puestos (editables, varios separados por «;»), asunto y texto, y Nesto manda el correo con el PDF adjunto y copia a usted. Ya no hace falta descargar, copiar al escritorio y adjuntar. Lo pidió María José."
Implementar 832 "1.10.51.0" "Mejorado" "Vendedores,Administración" "Adelantar la fecha de entrega actualiza la fecha prometida" `
    "Si adelanta la fecha de entrega de un pedido que aún no tiene picking, la fecha de entrega a la agencia prometida al cliente (la naranja) pasa a la nueva si es anterior. Aplazarla no la cambia: ese retraso es lo que detecta la pantalla de pedidos en riesgo. Lo avisó Lidia."

Contestar 770 "@Paloma, @Laura, @Sancho: ya está en la versión 1.10.51.0 de Nesto, la de hoy. En «Crear cliente», paso «Datos Comisiones», tienen las casillas «¿Es alumna?» y «¿Es público final?». Con una marcada no hay que elegir estética ni peluquería: el estado (Alumna o público final) y el vendedor NV los pone Nesto. Si una alumna tiene centro y hay que visitarla, créenla como estética o peluquería."
Contestar 797 "@Alfredo, ya está en la versión 1.10.51.0 de Nesto, la de hoy: al retener un pedido con picking le llega el aviso por la campana y por el buzón de Ariadna, con los pickings afectados, y el pedido no se puede tramitar ni etiquetar hasta que se libere. Lo mismo si un vendedor aplaza la fecha de entrega de un pedido con picking y la agencia aún no lo tiene."
Contestar 830 "@Javier, ya está publicado: «Resumir rapports» funciona otra vez con el modelo actual y, si fallara, le dirá el motivo en pantalla. Actualice Nesto (cierre y vuelva a abrir) si todavía no le ha llegado la 1.10.51.0."
Contestar 831 "@MariaJose, ya está en la versión 1.10.51.0 de Nesto, la de hoy: en el detalle del pedido, debajo de «Descargar PDF», tiene «Enviar por correo…»; en el menú de «Imprimir Factura» está también para la factura, y en la ficha del cliente, junto a «Imprimir» del mandato SEPA. Se abre un cuadro con los correos del cliente ya puestos (puede cambiarlos; varios separados por «;»), asunto y texto, y el correo sale con el PDF adjunto y copia a usted. Ya no hace falta descargar nada. Si echa en falta algún sitio desde el que mandar, dígalo aquí."
Contestar 832 "@Lidia, ya está publicado: al adelantar la fecha de entrega de un pedido sin picking, la fecha prometida (la naranja) pasa a la nueva si es anterior. Si vuelve a grabar el 928658 con la fecha de entrega buena, verá que cambia."
Contestar 833 "@Alfredo @Aida, desde hoy, al cambiar con «Modificar Envío» (pestaña En curso) un dato que ya viajó a CTT o Innovatrans (reembolso, retorno, dirección, bultos, peso…) de un envío que ya tienen registrado, Nesto avisa en rojo de que ese cambio no llegaría a la agencia y solo lo guarda si lo confirma dos veces y escribe con quién ha hablado en la agencia y cuándo; queda anotado en la historia del envío. Para que la agencia reciba el cambio, el botón de la pestaña Tramitados se llama ahora «Modificar datos y reenviar a la agencia». Seguimos con el hilo: ¿cómo lo hacían hasta ahora cuando había que cambiar un retorno después de imprimir?"
Contestar 839 "@Paloma, le explico cómo queda lo de Chanel desde hoy. Chanel ya está marcada como cliente que exige el P.O.: cada pedido de Chanel tiene que llevar su P.O. en «Su pedido» (la plantilla y el pedido lo resaltan en rojo si falta) y sin él Nesto no lo graba. No tiene que preocuparse del contacto ni de juntar pedidos: todos los pedidos con el mismo P.O. se facturan juntos, en una sola factura con ese P.O., en cuanto todos están entregados, aunque sean de contactos distintos. Dos P.O. distintos nunca van en la misma factura. Si tiene un pedido viejo de Chanel sin P.O., pídaselo a ellos y Administración se lo pone con el botón «Poner P.O.»."
Contestar 841 "@Alfredo @Santiago, para el cierre de mes desde hoy: los pedidos con P.O. (por ejemplo los de Chanel) NO se agrupan en el fin de mes. Se facturan solos, una factura por P.O., cuando todos los pedidos de ese P.O. están entregados. Lo que tenga P.O. y todavía no se pueda facturar se deja como albarán, sin tocarlo. El fin de mes solo junta los pedidos de fin de mes que no llevan P.O., y ahora puede hacerse desde Nesto con el botón «Facturar Fin de Mes» del detalle del pedido (Administración, Dirección e Informática), mejor el último día del mes. Si al cerrar octubre tienen dudas con algún pedido, pregunten aquí antes de agrupar."
Write-Host "Hecho." -ForegroundColor Green
