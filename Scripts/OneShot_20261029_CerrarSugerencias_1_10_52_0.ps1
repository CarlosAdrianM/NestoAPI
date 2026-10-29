# Tras publicar la API y Nesto 1.10.52.0 (29/10/26): las sugerencias de hoy que ya están hechas pasan a Implementadas en esa
# versión (la sugerencia ES la novedad: sus filas duplicadas del script de Novedades se retiran con
# OneShot_20261029_DespublicarDuplicadas_1_10_52_0.sql) y se contesta a cada autor en su hilo pidiéndole que lo revise.
# No se tocan: 866 y 867 (WhatsApp, en estudio), 869 (bultos al pulsar F8, sin hacer), 870 (ya contestada, no hay nada que hacer).
# Con -Ariadna222, lo de Ariadna 2.22 (872 y 873), cuando la 2.22 (o la siguiente) llegue a la prueba cerrada.
# Uso:  pwsh -File Scripts/OneShot_20261029_CerrarSugerencias_1_10_52_0.ps1   [-Ariadna222 [-VersionAriadna 2.23]]
param([string]$Api = "http://api.nuevavision.es", [switch]$Ariadna222, [string]$VersionAriadna = "2.22")
$ErrorActionPreference = "Stop"
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials -AllowUnencryptedAuthentication).token
$h = @{ Authorization = "Bearer $token" }

function Implementar($id, $version, $categoria, $perfiles, $titulo, $descripcion) {
    $cuerpo = [System.Text.Encoding]::UTF8.GetBytes((@{ Version = $version; Categoria = $categoria; Perfiles = $perfiles; Titulo = $titulo; Descripcion = $descripcion } | ConvertTo-Json))
    Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$id" -Headers $h -ContentType "application/json; charset=utf-8" -Body $cuerpo | Out-Null
    Write-Host "Sugerencia $id implementada en $version." -ForegroundColor Green
}
function Contestar($novedad, $texto) {
    & "$PSScriptRoot\ContestarComoAsistente.ps1" -Api $Api -Novedad $novedad -Texto $texto | Out-Null
    Write-Host "Contestada $novedad." -ForegroundColor Green
}

if ($Ariadna222) {
    Implementar 872 ${VersionAriadna} "Mejorado" "Almacén" "Salidas y Entradas: filtro por tipo" `
        "En Salidas y en Entradas de Ariadna hay un selector de tipo (F3, o en el menú «⋯» de abajo a la derecha) para ver solo los pickings, solo los packings, solo las reposiciones o todo junto. Ariadna recuerda lo que eligió en cada pantalla. Lo pidió Pedro."
    Contestar 872 "@Pedro, ya está en Ariadna ${VersionAriadna}, que les llega sola por Play Store: en Salidas y en Entradas puede elegir el tipo con F3 o en el menú «⋯» (tecla TAB) y ver solo pickings, solo packings o todo junto. Ariadna se acuerda de lo que eligió. ¿Es lo que necesitaba?"
    Implementar 873 ${VersionAriadna} "Nuevo" "Almacén" "Histórico de pickings y packings" `
        "En Salidas de Ariadna, F6 abre el histórico (para quien tiene permiso): pickings y packings de un rango de fechas (ayer y hoy por defecto, hasta 31 días), por pedido o por producto, con quién recogió cada línea, qué faltó, y el contenido y la foto de cada caja. Lo pidió Andre."
    Contestar 873 "@Andre @Alfredo @Santiago, ya está en Ariadna ${VersionAriadna}: en Salidas, F6 abre el histórico de pickings y packings. Puede buscar por fechas (hasta 31 días), por pedido o por producto, y ver quién recogió cada línea, qué faltó y el contenido y la foto de cada caja. De momento lo ven Alfredo y Santiago. Revísenlo y díganos aquí si le falta algo."
    Write-Host "Hecho (Ariadna ${VersionAriadna})." -ForegroundColor Green
    return
}

Implementar 861 "1.10.52.0" "Corregido" "Vendedores,Tiendas,Administración" "Pedidos en riesgo: el mismo reparto de stock que el picking" `
    "El aviso de pedidos en riesgo y la fecha de entrega a la agencia reparten ahora el stock igual que el picking, por antigüedad del pedido. Antes restaban todos los pendientes y podían avisar de un pedido que sí iba a salir. Lo avisó María José."
Contestar 861 "@MariaJose, ya está en la versión 1.10.52.0 de Nesto, la de hoy: el aviso de pedidos en riesgo reparte el stock igual que el picking, por antigüedad, así que ya no debería avisar de pedidos que sí salen, como sus dos de esta mañana. Si le llega otro aviso que no cuadre, díganos aquí el pedido."

Implementar 862 "1.10.52.0" "Nuevo" "Tiendas,Vendedores,Administración" "Eventos: la persona que asiste, con el correo y la dirección" `
    "En los inscritos del evento se ve la persona que asiste (una persona de contacto del cliente) y el correo y la dirección de la ficha, para mandar el dosier sin ir a la ficha. Al inscribir se puede elegir la persona en el mismo desplegable. Lo pidió Reina."
Contestar 862 "@Reina, ya está en la versión 1.10.52.0 de Nesto, la de hoy: en los inscritos del evento salen la persona que asiste, el correo y la dirección del cliente, y al inscribir puede elegir la persona que asiste en un desplegable con las personas de contacto. Revíselo y díganos si es lo que necesitaba."

Implementar 863 "1.10.52.0" "Nuevo" "Vendedores,Tiendas,Administración" "Cada comercial inscribe a sus clientes en los eventos" `
    "Cada vendedor puede inscribir en un evento a sus propios clientes, y el jefe de equipo a los de su equipo; Administración, Tiendas y Dirección, a cualquiera. La ventana de inscribir usa el selector de clientes, propone los correos del cliente y deja copiado el enlace de pago. Lo pidió Paloma."
Contestar 863 "@Paloma, ya está en la versión 1.10.52.0 de Nesto, la de hoy: cada comercial puede inscribir a sus propios clientes en los eventos, y el jefe de equipo a los de su equipo. Al inscribir, el enlace de pago queda copiado para mandárselo a la clienta. En los eventos gratuitos por compra, el propio evento dice qué productos dan derecho a la inscripción. Revíselo y díganos si es lo que necesitaba."

Implementar 864 "1.10.52.0" "Mejorado" "Vendedores,Tiendas,Administración" "Datos de la transferencia también con transferencia al contado" `
    "El botón «Copiar datos de transferencia» (cuenta, importe y número de pedido) sale también cuando la forma de pago es transferencia al contado, y se ofrece al terminar la plantilla. Lo pidió Paloma."
Contestar 864 "@Paloma, ya está en la versión 1.10.52.0 de Nesto, la de hoy: los datos de la transferencia se pueden copiar también cuando el pedido es transferencia al contado, y Nesto se los ofrece al terminar la plantilla. Revíselo y díganos si es lo que necesitaba."

Implementar 865 "1.10.52.0" "Mejorado" "Vendedores" "Comentarios del rapport más cómodos" `
    "La casilla de comentarios del rapport crece mientras escribe, hasta unas quince líneas, y después se desplaza, así que se lee todo lo escrito. Lo pidió Iñaki."
Contestar 865 "@Inaki, ya está en la versión 1.10.52.0 de Nesto, la de hoy: la casilla de comentarios del rapport crece mientras escribe, hasta unas quince líneas, y luego se desplaza. Pruébela y díganos si así puede escribir cómodo."

Implementar 868 "1.10.52.0" "Mejorado" "Administración" "Reembolsos de CTT: el número de cliente ya viene puesto" `
    "Al contabilizar los reembolsos de CTT, el número de cliente (42073) sale ya puesto. Lo pidió Aida."
Contestar 868 "@Aida, ya está en la versión 1.10.52.0 de Nesto, la de hoy: al contabilizar los reembolsos de CTT, el 42073 sale ya puesto. Revíselo en la próxima tanda. Sigue en pie la pregunta: ¿qué número de cliente usa para los reembolsos de Innovatrans?"

Implementar 871 "1.10.52.0" "Corregido" "Tiendas,Administración,Dirección" "Ventana de Eventos en pantallas pequeñas" `
    "La ventana de Eventos se adapta a ventanas pequeñas: el título, el panel «Marcar pagada» y la lista de inscritos ya no se descolocan, y las columnas tienen un ancho más cómodo. Lo avisó Pilar."
Contestar 871 "@Pilar, ya está en la versión 1.10.52.0 de Nesto, la de hoy: la ventana de Eventos se ve bien en ventanas pequeñas y la lista de inscritos tiene un botón «Copiar enlace». Ábrala con su tamaño de siempre y díganos si ya se ve bien."

Write-Host "Hecho. Falta OneShot_20261029_DespublicarDuplicadas_1_10_52_0.sql (sa)." -ForegroundColor Green
