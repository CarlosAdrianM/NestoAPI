<#
    13/10/26: cierra con su versión sugerencias que ya estaban hechas (504/506 en la 1.10.37.0; 540/550 en la 1.10.40.0) y
    avisa a Alfredo en la 575 del aviso de rutas del picking (1.10.41.0). Usuario de Windows de Dirección o Informática.
#>
param([string]$Api = "http://api.nuevavision.es")
$ErrorActionPreference = "Stop"
$sinCifrar = @{}
if ($PSVersionTable.PSVersion.Major -ge 6 -and $Api.StartsWith("http:")) { $sinCifrar.AllowUnencryptedAuthentication = $true }
$token = (Invoke-RestMethod -Method Post -Uri "$Api/api/auth/windows-token" -UseDefaultCredentials @sinCifrar).token
$cabeceras = @{ Authorization = "Bearer $token" }

function Contestar($id, $texto) {
    $c = @{ Texto = $texto; ComentariosContestados = @() } | ConvertTo-Json
    $null = Invoke-RestMethod -Method Post -Uri "$Api/api/Novedades/$id/Comentarios/Asistente" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($c))
}

$cierres = @(
    @{ Id = 504; Version = "1.10.37.0"; Categoria = "Corregido"; Perfiles = @("Almacén")
       Titulo = "Las pegatinas de Portugal salen aunque el código postal lleve espacio"
       Descripcion = "El código postal de los envíos a Portugal se manda a CTT siempre en su formato (4480-670), se escriba como se escriba en la ficha. Lo avisó Alfredo."
       Respuesta = "Hola, Alfredo. Esto quedó corregido en la versión 1.10.37.0: el código postal de Portugal se manda a CTT en su formato aunque en la ficha lleve un espacio. Y desde la 1.10.41.0, si CTT no responde, Nesto te lo dice claro en vez de «HTTP 500». ¿Te ha vuelto a pasar con alguna pegatina?" }
    @{ Id = 506; Version = "1.10.37.0"; Categoria = "Corregido"; Perfiles = @("Almacén")
       Titulo = "Un envío de Portugal ya no frena las pegatinas que van detrás"
       Descripcion = "El código postal de Portugal se manda a CTT en su formato, así que ese envío ya no se rechaza ni deja sin pegatina a los siguientes. Lo avisó Alfredo."
       Respuesta = "Hola, Alfredo. Quedó corregido en la versión 1.10.37.0: el código postal de Portugal va a CTT en su formato, así que ya no se para la tanda de pegatinas por él. Si ves que vuelve a pasar, dínoslo aquí." }
    @{ Id = 540; Version = "1.10.40.0"; Categoria = "Mejorado"; Perfiles = @("Tiendas")
       Titulo = "Las reposiciones de las tiendas a Algete se cierran a las 9:00"
       Descripcion = "La reposición de Reina y de Alcobendas a Algete se rellena sola a las 9:00 en vez de a las 10:00, porque a las 10 ya está allí quien la recoge. Lo pidió Paloma."
       Respuesta = "Hola, Paloma. Esto quedó así desde el lunes 12 (versión 1.10.40.0): la reposición a Algete se rellena a las 9:00. Y desde hoy (1.10.41.0), si después te piden algo suelto, puedes mandarlo con «Reposición puntual» hasta que marques que el repartidor se la ha llevado." }
    @{ Id = 550; Version = "1.10.40.0"; Categoria = "Mejorado"; Perfiles = @("Vendedores")
       Titulo = "El filtro de «clientes para contactar» cambia los porcentajes y el orden"
       Descripcion = "En Rapports, los botones Teléfono, WhatsApp y Visita del panel de clientes para contactar filtran según cómo vas a contactar: cambian los porcentajes y el orden de la lista. No cambian el tipo del rapport. Lo preguntó Javier."
       Respuesta = "Hola, Javier. Desde la versión 1.10.40.0, al cambiar ese filtro (Teléfono, WhatsApp o Visita) cambian también los porcentajes y el orden de la lista de clientes para contactar. Recuerda que el filtro no cambia el tipo del rapport." }
)

foreach ($c in $cierres) {
    $cuerpo = @{ Titulo = $c.Titulo; Descripcion = $c.Descripcion; Version = $c.Version; Categoria = $c.Categoria; Perfiles = @($c.Perfiles) } | ConvertTo-Json
    $null = Invoke-RestMethod -Method Put -Uri "$Api/api/Novedades/Sugerencias/$($c.Id)" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($cuerpo))
    Contestar $c.Id $c.Respuesta
    "Novedad $($c.Id): versión $($c.Version) puesta y contestado."
}

Contestar 575 "Hola, Alfredo. Con lo que nos contaste y los datos de los últimos seis meses, desde la versión 1.10.41.0, al sacar el picking Nesto te enseña antes los pedidos que van por agencia pero podrían ir por nuestra ruta, con la ruta propuesta (AT para Alcalá, Coslada, Torrejón y el sur; la 16 para el resto) y el motivo: empresa 3 con código postal de Madrid, el último pedido del cliente salió por nuestra ruta, o aparatos de 1.500 € o más en la empresa 1. Marcas los que quieras pasar, puedes elegir la otra ruta, y «Cambiar la ruta de los marcados y sacar el picking». Solo lo ve almacén y nunca cambia nada solo. ¿Nos dices si acierta?"
"Novedad 575: contestado a Alfredo."
