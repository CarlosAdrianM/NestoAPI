<#
    Tras publicar la 1.10.36.1: pone la versión a la sugerencia 462 de Laura (la propia fila pasa a ser la novedad de
    la versión, con «sugerida por» y su hilo) y le contesta como «Claude (asistente IA)».

    Lanzar DESPUÉS de publicar la API y la ClickOnce, con el usuario de Windows de Dirección o Informática.
      .\OneShot_20261002_CerrarSugerencias_1_10_36_1.ps1            # todo
      .\OneShot_20261002_CerrarSugerencias_1_10_36_1.ps1 -SoloVer   # enseña lo que haría
#>
param(
    [string]$Api = "http://api.nuevavision.es",
    [string]$Version = "1.10.36.1",
    [switch]$SoloVer
)

$ErrorActionPreference = "Stop"

$cierres = @(
    @{ Id = 462; Categoria = "Mejorado"
       Titulo = "Los pedidos que solo llevan cursos pasan solos a la serie CV"
       Descripcion = "Los cursos solo se pueden facturar en la serie CV, pero todo pedido nace en la serie NV, así que si nadie la cambiaba a mano la factura daba error. Ahora, si un pedido en NV solo lleva cursos, al guardarlo pasa solo a la serie CV. Si lleva cursos y otros productos, no se toca."
       Respuesta = "Hola, @Laura. Ya está en la versión ${Version}: si un pedido en serie NV solo lleva cursos, al guardarlo pasa solo a la serie CV, así que ya no hace falta cambiarla a mano para poder facturarlo. Si un pedido mezcla cursos con otros productos, la serie no se toca. Cuando entre el próximo curso, dinos si sale como esperabas." }
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
