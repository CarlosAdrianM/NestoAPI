<#
    Tras publicar la 1.10.40.0 (Nesto): pone la versión a las sugerencias implementadas (la propia fila pasa a ser la
    novedad de la versión, con «sugerida por» y su hilo), con sus perfiles (sugerencia 551), y contesta como
    «Claude (asistente IA)».

    Lanzar DESPUÉS de publicar la API y la ClickOnce, con el usuario de Windows de Dirección o Informática.
      .\OneShot_20261009_CerrarSugerencias_1_10_40_0.ps1            # todo
      .\OneShot_20261009_CerrarSugerencias_1_10_40_0.ps1 -SoloVer   # enseña lo que haría
#>
param(
    [string]$Api = "http://api.nuevavision.es",
    [switch]$SoloVer
)

$ErrorActionPreference = "Stop"

$cierres = @(
    @{ Id = 502; Version = "1.10.40.0"; Categoria = "Nuevo"; Perfiles = @("Administración")
       Titulo = "Los pedidos de la tienda de Eva Visnú entran por Canales externos con la serie EV"
       Descripcion = "En Canales externos › Pedidos, el desplegable de canales tiene «PrestashopEV»: los pedidos de evavisnu.com entran igual que los de la tienda de Nueva Visión (cliente, cobros, regalos, portes, cupones), pero con la serie EV. Los cobros con tarjeta de esa tienda entran como prepagados. Lo pidió Laura."
       Respuesta = "Hola, Laura. Ya está en la versión 1.10.40.0: en Canales externos › Pedidos, elige «PrestashopEV» en el desplegable y los pedidos de evavisnu.com entran con la serie EV, igual que los de la tienda de Nueva Visión. Un aviso: en la tienda hay pedidos de 2017 a 2022 que siguen «en preparación» o «pendiente de transferencia»; conviene pasarlos a Entregado o Cancelado en el panel de la tienda para que no salgan siempre en la lista." }
    @{ Id = 549; Version = "1.10.40.0"; Categoria = "Nuevo"; Perfiles = @("Vendedores")
       Titulo = "Los jefes de ventas ven los clientes para contactar de su equipo"
       Descripcion = "En Rapports, en el panel de clientes para contactar, los jefes de ventas tienen un desplegable para elegir a cualquier vendedor de su equipo y ver su lista del día tal como la ve él (objetivo, prioridades y frase). Dirección puede elegir a cualquier vendedor. Mirar la lista de otro no cambia nada para él. Lo pidió Alberto."
       Respuesta = "Hola, Alberto. Ya está en la versión 1.10.40.0: en Rapports, encima del panel de clientes para contactar, tienes un desplegable con los vendedores de tu equipo; al elegir uno ves su lista del día tal como la ve él. Mirarla no le marca nada ni le cambia la lista. En NestoApp llegará más adelante." }
    @{ Id = 551; Version = "1.10.40.0"; Categoria = "Nuevo"; Perfiles = @()
       Titulo = "Cada uno ve las novedades que le afectan"
       Descripcion = "Las novedades llevan a quién afectan (vendedores, almacén, tiendas o administración) y cada uno ve por defecto las de su puesto y las que son para todos. Con la casilla «Ver todas las novedades» se ven todas. El aviso de versión nueva solo sale si hay algo de tu puesto. Lo pidió Alberto."
       Respuesta = "Hola, Alberto. Ya está en la versión 1.10.40.0: en Novedades verás por defecto las de vendedores y las que son para todos; si quieres verlas todas, marca «Ver todas las novedades»." }
    @{ Id = 564; Version = "1.10.40.0"; Categoria = "Nuevo"; Perfiles = @("Tiendas")
       Titulo = "Imprimir la reposición para prepararla o comprobarla"
       Descripcion = "En «Enviar reposición» y en «Recibir reposición» hay un botón «Imprimir» que saca la lista de la reposición en PDF, ordenada para recorrer la tienda (por hueco y después por familia), con una casilla para ir marcando. Además, al leer con el lector suena un pitido de error si el código no cuadra. Lo pidió Paloma."
       Respuesta = "Hola, Paloma. Ya está en la versión 1.10.40.0: en «Enviar reposición» y en «Recibir reposición» tienes el botón «Imprimir», con la lista ordenada para recorrer la tienda y una casilla para marcar. Si preparas leyendo con un lector, suena un pitido de error cuando el código no está en la reposición. También puedes hacerlo con Ariadna en el móvil: en la guía adjunta a la novedad de reposiciones están las dos formas." }
    @{ Id = 565; Version = "1.10.40.0"; Categoria = "Corregido"; Perfiles = @("Almacén")
       Titulo = "Las facturas con «factura física» vuelven a imprimirse solas"
       Descripcion = "Al facturar desde la etiqueta de la agencia o con «Facturar rutas», los pedidos con «factura física», «factura en papel» o «albarán físico» vuelven a imprimir el documento solo. Desde principios de semana solo se miraba en los comentarios del pedido, y en esos clientes la frase está en el comentario de picking. Ahora se miran los dos. Lo avisó Alfredo."
       Respuesta = "Hola, Alfredo. Ya está en la versión 1.10.40.0: las facturas con «factura física» en el comentario de picking vuelven a imprimirse solas al facturar desde la etiqueta y en «Facturar rutas»." }
)

if ($SoloVer) {
    foreach ($c in $cierres) { "{0} [{1}] {2} -> {3} ({4})" -f $c.Id, $c.Categoria, $c.Titulo, $c.Version, ($c.Perfiles -join ', '); "    " + $c.Respuesta }
    return
}

# PowerShell 7 no manda las credenciales de Windows por http sin este permiso explícito (la API va por http).
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }

foreach ($c in $cierres) {
    $cuerpo = @{ Titulo = $c.Titulo; Descripcion = $c.Descripcion; Version = $c.Version; Categoria = $c.Categoria; Perfiles = @($c.Perfiles) } | ConvertTo-Json
    $null = Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$($c.Id)" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($cuerpo))
    $respuesta = @{ Texto = $c.Respuesta; ComentariosContestados = @() } | ConvertTo-Json
    $null = Invoke-RestMethod -Method Post -Uri "$Api/api/Novedades/$($c.Id)/Comentarios/Asistente" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($respuesta))
    "Novedad $($c.Id): versión $($c.Version) puesta y contestado."
}
