<#
    Tras publicar la 1.10.38.0 (Nesto) y Ariadna 2.2: pone la versión a las sugerencias implementadas (la propia fila pasa
    a ser la novedad de la versión, con «sugerida por» y su hilo) y contesta como «Claude (asistente IA)».

    Lanzar DESPUÉS de publicar la API, la ClickOnce y Ariadna, con el usuario de Windows de Dirección o Informática.
      .\OneShot_20261007_CerrarSugerencias_1_10_38_0.ps1            # todo
      .\OneShot_20261007_CerrarSugerencias_1_10_38_0.ps1 -SoloVer   # enseña lo que haría
#>
param(
    [string]$Api = "http://api.nuevavision.es",
    [switch]$SoloVer
)

$ErrorActionPreference = "Stop"

$cierres = @(
    @{ Id = 518; Version = "1.10.38.0"; Categoria = "Corregido"
       Titulo = "La ventana de Novedades ya no se corta por la derecha"
       Descripcion = "La ventana de Novedades tenía un ancho fijo y, con la barra de botones más ancha, se cortaban el botón «Buscar», el texto y «Cerrar». Ahora nace con un tamaño que cabe, se puede redimensionar, los títulos se envuelven y el texto de ayuda del megáfono va en su propia línea debajo de los botones."
       Respuesta = "Ya está en la versión 1.10.38.0: la ventana de Novedades se abre con un tamaño que cabe y se puede redimensionar; los títulos largos se envuelven y el texto de ayuda va en su propia línea bajo los botones." }
    @{ Id = 519; Version = "1.10.38.0"; Categoria = "Corregido"
       Titulo = "Las pantallas nuevas ya no dejan ver el nombre de usuario y las sedes de fondo"
       Descripcion = "Enviar y Recibir reposición, Etiquetas de hueco, Ganavisiones, Ofertas combinadas y Productos de canales externos se abrían con fondo transparente y se veía detrás el nombre de usuario y las sedes de la pantalla de inicio. Ahora todas llevan fondo blanco."
       Respuesta = "Ya está en la versión 1.10.38.0: las seis pantallas que se abrían transparentes llevan fondo blanco. Queda como regla para cualquier pantalla nueva." }
    @{ Id = 520; Version = "1.10.38.0"; Categoria = "Mejorado"
       Titulo = "El correo diario de rapports enseña los «No contactado» y el PDF las llamadas con conversación"
       Descripcion = "El correo diario de actividad contaba solo los rapports en los que se habló con la clienta, y el PDF «Detalle de rapports» contaba también los «No contactado», por eso no cuadraban (Iñaki: 16 frente a 25). Ahora el correo lleva una columna «No contactado» y el PDF una línea «Total contactadas», así en los dos sitios están los dos datos sin perder el de contactados, que es el que importa."
       Respuesta = "Hola, @Sancho. Ya está en la versión 1.10.38.0: el correo diario de rapports lleva una columna «No contactado» al lado de «Rapports» (que siguen siendo las llamadas en las que se habló con la clienta), y el PDF «Detalle de rapports» dice debajo de «Total llamadas» cuántas fueron contactadas y cuántas acabaron en pedido. Con los datos de Iñaki del 06/10 verás 16 rapports, 9 no contactados y 25 llamadas en el PDF." }
    @{ Id = 503; Version = "2.2"; Categoria = "Corregido"
       Titulo = "Pinchar un picking en Salidas lo abre una sola vez"
       Descripcion = "Al pinchar un picking en la lista de Salidas en pantalla táctil, el toque podía abrirlo dos veces seguidas y el recorrido llegaba sin productos ni huecos (teclear el número + Intro sí funcionaba). Ahora, mientras se está abriendo uno, se ignoran los toques repetidos, en Salidas y en Entradas."
       Respuesta = "Hola, @Alfredo. Ya está en Ariadna 2.2 (actualiza desde Play): al pinchar un picking en Salidas se abre una sola vez aunque la pantalla registre dos toques, que es lo que pasaba con el 99757. Si lo vuelves a ver, dinos el número." }
)

if ($SoloVer) {
    foreach ($c in $cierres) { "{0} [{1}] {2} -> {3}" -f $c.Id, $c.Categoria, $c.Titulo, $c.Version; "    " + $c.Respuesta }
    return
}

# PowerShell 7 no manda las credenciales de Windows por http sin este permiso explícito (la API va por http).
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
if (-not $token) { throw "No se ha obtenido el token de Windows." }
$cabeceras = @{ Authorization = "Bearer $token" }

foreach ($c in $cierres) {
    $cuerpo = @{ Titulo = $c.Titulo; Descripcion = $c.Descripcion; Version = $c.Version; Categoria = $c.Categoria } | ConvertTo-Json
    $null = Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$($c.Id)" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($cuerpo))
    $respuesta = @{ Texto = $c.Respuesta; ComentariosContestados = @() } | ConvertTo-Json
    $null = Invoke-RestMethod -Method Post -Uri "$Api/api/Novedades/$($c.Id)/Comentarios/Asistente" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($respuesta))
    "Novedad $($c.Id): versión $($c.Version) puesta y contestado."
}
