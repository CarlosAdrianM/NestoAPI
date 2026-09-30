<#
    Tras publicar la 1.10.35.0: pone la versión a lo que pidieron los compañeros en Novedades (la propia
    fila de la sugerencia o de la incidencia pasa a ser la novedad de la versión, con «sugerida por» y su
    hilo) y les contesta como «Claude (asistente IA)» para que lo revisen.

    Lanzar DESPUÉS de publicar la API y la ClickOnce, con el usuario de Windows de Dirección o Informática.
      .\OneShot_20260930_CerrarSugerencias_1_10_35_0.ps1            # todo
      .\OneShot_20260930_CerrarSugerencias_1_10_35_0.ps1 -SoloVer   # enseña lo que haría
#>
param(
    [string]$Api = "http://api.nuevavision.es",
    [string]$Version = "1.10.35.0",
    [switch]$SoloVer
)

$ErrorActionPreference = "Stop"

$cierres = @(
    @{ Id = 433; Categoria = "Corregido"
       Titulo = "La factura en PDF enseña el impagado"
       Descripcion = "Una factura con un recibo impagado salía en el PDF como «Pagado» cuando el impagado estaba apuntado con una fecha distinta a la del vencimiento (lo normal, porque se apunta con la fecha del cargo de la remesa). Ahora se reconoce por el número de efecto y el vencimiento sale como «Impagado». Si el impagado está compensado en parte con un abono, dice cuánto queda pendiente."
       Respuesta = "Hola, @Magan. Ya está corregido en la versión ${Version}: la factura en PDF enseña el impagado aunque esté apuntado con otra fecha que el vencimiento, y si está compensado en parte dice cuánto queda pendiente (en la NV2615330, «Impagado, pendientes 16,00 €»). Cuando puedas, saca esa factura otra vez y dinos si sale como esperabas." },
    @{ Id = 434; Categoria = "Mejorado"
       Titulo = "«Copiar Factura» también para tienda online"
       Descripcion = "El botón «Copiar Factura» de la ficha del pedido (para crear rectificativas o traspasar una factura de un cliente a otro) lo ven y lo pueden usar también los usuarios de tienda online, además de los de almacén."
       Respuesta = "Hola, @Laura. Desde la versión $Version ya tienes el botón «Copiar Factura» en la ficha del pedido, para los traspasos entre clientes. Pruébalo cuando te actualice Nesto y dinos si te funciona bien." },
    @{ Id = 435; Categoria = "Corregido"
       Titulo = "La ventana de «Copiar Factura» cabe en la pantalla de un portátil"
       Descripcion = "En pantallas pequeñas la ventana de «Copiar Factura» era más alta que la pantalla y no se llegaba al cliente destino ni al botón «Ejecutar». Ahora se ajusta al alto de la pantalla, el contenido se puede desplazar, los botones quedan siempre a la vista y la ventana se mantiene centrada."
       Respuesta = "Hola, @Enrique. Corregido en la versión ${Version}: la ventana de «Copiar Factura» se ajusta al alto de la pantalla, tiene barra de desplazamiento y los botones «Ejecutar» y «Cerrar» quedan siempre a la vista. Pruébala en el portátil cuando te actualice Nesto y dinos si ya llegas a todo." },
    @{ Id = 436; Categoria = "Nuevo"
       Titulo = "Al mandar el cobro por tarjeta, Nesto avisa de lo que el cliente tiene a su favor"
       Descripcion = "En el último paso de la plantilla de venta, al marcar «¿Mandar cobro por tarjeta?», Nesto avisa si el cliente tiene algún importe a su favor en el extracto, con el detalle de cada apunte (fecha, concepto e importe), y si además tiene algo pendiente de pago. No se descuenta solo: puede ser una entrega a cuenta de otro pedido o una reserva para un evento. Después de mirarlo en el extracto, se puede marcar «Descontar del enlace de pago» y el enlace sale por el importe del pedido menos ese saldo. Luego hay que aplicar ese saldo al pedido en el extracto."
       Respuesta = "Hola, @Paloma. Tu sugerencia está en la versión ${Version}. Al marcar «¿Mandar cobro por tarjeta?» en la plantilla, Nesto te avisa si la clienta tiene algo a su favor, con el detalle de cada apunte. No lo descuenta solo, porque ese saldo puede ser de otro pedido o una reserva: si después de mirarlo en el extracto quieres aplicarlo, marcas «Descontar del enlace de pago» y el enlace sale ya con el importe descontado. Pruébalo y dinos si es lo que tenías en mente." }
)

if ($SoloVer) {
    foreach ($c in $cierres) { "{0} [{1}] {2}" -f $c.Id, $c.Categoria, $c.Titulo; "    " + $c.Respuesta }
    return
}

# PowerShell 7 no manda las credenciales de Windows por http sin este permiso explícito (la API va por http).
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }

foreach ($c in $cierres) {
    $cuerpo = @{ Titulo = $c.Titulo; Descripcion = $c.Descripcion; Version = $Version; Categoria = $c.Categoria } | ConvertTo-Json
    $null = Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$($c.Id)" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($cuerpo))
    $respuesta = @{ Texto = $c.Respuesta; ComentariosContestados = @() } | ConvertTo-Json
    $null = Invoke-RestMethod -Method Post -Uri "$Api/api/Novedades/$($c.Id)/Comentarios/Asistente" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($respuesta))
    "Novedad $($c.Id): versión $Version puesta y contestado."
}
