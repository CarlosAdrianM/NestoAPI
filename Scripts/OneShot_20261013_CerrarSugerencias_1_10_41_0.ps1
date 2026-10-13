<#
    Tras publicar la 1.10.41.0 (Nesto): pone la versión a las sugerencias implementadas (la propia fila pasa a ser la
    novedad de la versión, con «sugerida por» y su hilo), con sus perfiles, y contesta como «Claude (asistente IA)».

    Lanzar DESPUÉS de publicar la API y la ClickOnce, con el usuario de Windows de Dirección o Informática.
      .\OneShot_20261013_CerrarSugerencias_1_10_41_0.ps1            # todo
      .\OneShot_20261013_CerrarSugerencias_1_10_41_0.ps1 -SoloVer   # enseña lo que haría
#>
param(
    [string]$Api = "http://api.nuevavision.es",
    [switch]$SoloVer
)

$ErrorActionPreference = "Stop"

$cierres = @(
    @{ Id = 507; Version = "1.10.41.0"; Categoria = "Mejorado"; Perfiles = @("Almacén")
       Titulo = "Si CTT no responde, Nesto lo dice claro"
       Descripcion = "Al tramitar, imprimir, anular o modificar un envío, si CTT (o cualquier agencia) no responde, Nesto dice «CTT no responde ahora mismo (error de su servidor). Vuelve a intentarlo en unos minutos o, si es urgente, sácalo por GLS.» en vez de «HTTP 500». Si no contestó a tiempo al crear o modificar el envío, añade «Antes de reintentar, comprueba en la web de CTT que no se ha creado ya», porque puede haberlo creado. Al modificar en CTT ya no se reintenta solo, para no duplicar albaranes. Lo pidió Alfredo."
       Respuesta = "Hola, Alfredo. Ya está en la versión 1.10.41.0: cuando CTT no responde, Nesto lo dice claro («CTT no responde ahora mismo… vuelve a intentarlo en unos minutos o, si es urgente, sácalo por GLS») al tramitar, imprimir, anular o modificar. Si se quedó sin respuesta al crear el envío, te avisa de que mires antes en la web de CTT si ya está creado, para no duplicarlo." }
    @{ Id = 580; Version = "1.10.41.0"; Categoria = "Mejorado"; Perfiles = @("Tiendas")
       Titulo = "El listado de reposición impreso lleva el tamaño"
       Descripcion = "Al imprimir la reposición (enviar o recibir), cada producto lleva su tamaño (por ejemplo, «100 ml») junto a la descripción, para distinguir los que solo cambian de tamaño. Lo pidió Paloma."
       Respuesta = "Hola, Paloma. Ya está: al imprimir la reposición, cada producto lleva su tamaño junto a la descripción." }
    @{ Id = 581; Version = "1.10.41.0"; Categoria = "Nuevo"; Perfiles = @("Tiendas", "Almacén")
       Titulo = "Reposición puntual: mandar algo suelto a Algete después de la del día"
       Descripcion = "En Productos › Reposición › Enviar, «Reposición puntual…» permite mandar a Algete solo los productos que elijas (leyendo el código o con la referencia) cuando alguien te pide algo después de enviar la del día. Solo los días con viaje, cuando la del día ya se ha rellenado y enviado, y hasta que la tienda marca «El repartidor ya se la ha llevado». No deja mandar lo que necesitan los pedidos de la propia tienda. En Algete sale como «Reposición puntual … desde …» y se recibe como las demás. Lo pidió Paloma."
       Respuesta = "Hola, Paloma. Ya está en la versión 1.10.41.0: en Enviar reposición tienes «Reposición puntual…» para mandar a Algete algo suelto después de la del día, hasta que marques «El repartidor ya se la ha llevado». Solo deja mandar lo que no necesitan vuestros propios pedidos. Lo tienes explicado en la guía de reposiciones (forma C), adjunta a esta novedad." }
    @{ Id = 582; Version = "1.10.41.0"; Categoria = "Mejorado"; Perfiles = @("Vendedores")
       Titulo = "En Rapports, primero el contacto de venta telefónica"
       Descripcion = "Para quien vende por teléfono, al poner en Rapports un cliente con varios contactos, sale primero el contacto que está en estado 9 (el de venta telefónica), y al buscar por nombre ese contacto va el primero de su cliente. Si eliges otro contacto a mano, se respeta. Lo pidió María José."
       Respuesta = "Hola, María José. Ya está en la versión 1.10.41.0: en Rapports, cuando un cliente tiene varios contactos, te sale primero el que está en estado 9. Si eliges otro a mano, se queda el que elijas." }
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
