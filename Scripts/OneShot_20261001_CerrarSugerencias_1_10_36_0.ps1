<#
    Tras publicar la 1.10.36.0: pone la versión a lo que pidieron las compañeras en Novedades (la propia fila de la
    sugerencia o de la incidencia pasa a ser la novedad de la versión, con «sugerida por» y su hilo) y les contesta
    como «Claude (asistente IA)» para que lo revisen.

    Lanzar DESPUÉS de publicar la API y la ClickOnce, con el usuario de Windows de Dirección o Informática.
      .\OneShot_20261001_CerrarSugerencias_1_10_36_0.ps1            # todo
      .\OneShot_20261001_CerrarSugerencias_1_10_36_0.ps1 -SoloVer   # enseña lo que haría
#>
param(
    [string]$Api = "http://api.nuevavision.es",
    [string]$Version = "1.10.36.0",
    [switch]$SoloVer
)

$ErrorActionPreference = "Stop"

$cierres = @(
    @{ Id = 450; Categoria = "Corregido"
       Titulo = "Los cursos de la tienda online salen exentos de IVA"
       Descripcion = "Al traer a Nesto un pedido de la tienda online, cada producto lleva el IVA de su ficha. Los cursos, que en su ficha son exentos, ya no salen con el 21 % y se pueden facturar sin cambiar nada a mano."
       Respuesta = "Hola, @Laura. Corregido en la versión ${Version}: al traer un pedido de la tienda online, cada producto coge el IVA de su ficha, así que los cursos ya salen exentos sin tocar la cabecera del pedido. Los pedidos que ya estaban en Nesto antes de la actualización no cambian solos; los nuevos ya entran bien. Cuando llegue el próximo curso, dinos si sale como esperabas." },
    @{ Id = 451; Categoria = "Corregido"
       Titulo = "Un picking no saca solo los regalos si lo que se paga sigue pendiente"
       Descripcion = "En un pedido que se sirve «según vaya entrando», si lo que se paga no tenía stock, el picking podía sacar solo los regalos (por ejemplo, de Ganavisiones) y, con ellos, los portes y el contrarreembolso. Ahora, si lo único que hay para servir son regalos y lo que se paga sigue pendiente, el picking espera a que haya algo de pago que mandar."
       Respuesta = "Hola, @Lidia. Corregido en la versión ${Version}: si en un pedido lo único que hay para servir son regalos y lo que se paga sigue pendiente, el picking ya no los saca solos, así que no se volverá a mandar un envío solo con regalos, portes y contrarreembolso como el del 926495. Si ves otro caso parecido, escríbelo aquí con el número de pedido." }
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
