<#
    Ariadna (NestoAPI#556): da de alta a los mozos de almacén como usuarios de la app, igual que
    los de NestoApp (Identity, entran por /oauth/token), con la contraseña «Algete.» + 3 cifras al
    azar, y le manda a cada uno la suya por correo.

    CUÁNDO: el día que ya puedan usar la app (Carlos, 30/09/26: antes es tontería, la olvidan).
    La contraseña la pueden cambiar ellos con «He olvidado mi contraseña», como en NestoApp.

    Los nombres de usuario van sin dominio y sin tilde, para que cuadren con los de Windows
    (Santiago, Alfredo, Pedro, Andre): así la auditoría dice lo mismo desde Nesto y desde Ariadna.

    Uso:
      # 1) Ver lo que haría, sin crear nada ni mandar nada:
      .\Ariadna_CrearUsuarios.ps1 -Simular
      # 2) De verdad (pide el usuario Admin del API y su contraseña; no se guardan):
      .\Ariadna_CrearUsuarios.ps1 -ServidorSmtp smtp.loquesea -Remitente informatica@nuevavision.es

    Hay que rellenar antes los correos en la lista de abajo. Sin -ServidorSmtp no manda correos:
    enseña las contraseñas en pantalla una sola vez para dárselas en mano. No las escribe en ningún fichero.
#>
param(
    [string]$Api = "https://api.nuevavision.es",
    [string]$ServidorSmtp,
    [int]$PuertoSmtp = 587,
    [string]$Remitente,
    [switch]$Simular
)

$ErrorActionPreference = "Stop"

# Rellenar los correos antes de lanzarlo
$mozos = @(
    @{ Usuario = "Santiago"; Nombre = "Santiago"; Apellidos = "Almacén"; Correo = "santiagocampillo@nuevavision.es" },
    @{ Usuario = "Alfredo";  Nombre = "Alfredo";  Apellidos = "Almacén"; Correo = "alfredo@nuevavision.es" },
    @{ Usuario = "Pedro";    Nombre = "Pedro";    Apellidos = "Jurado Lara"; Correo = "pjuradolara@gmail.com" },
    @{ Usuario = "Andre";    Nombre = "Andre";    Apellidos = "Kuznetsov"; Correo = "andrekuznetson@nuevavision.es" }
)

$sinCorreo = @($mozos | Where-Object { -not $_.Correo })
if ($sinCorreo.Count -gt 0 -and -not $Simular) {
    throw "Faltan los correos de: $(($sinCorreo | ForEach-Object { $_.Usuario }) -join ', '). Rellénalos en la lista del script."
}

function Nueva-Contrasena {
    # «Algete.» + 3 cifras al azar (Algete.123, Algete.777…), con el generador criptográfico
    $octetos = New-Object byte[] 4
    $generador = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generador.GetBytes($octetos) } finally { $generador.Dispose() }
    "Algete.{0:D3}" -f ([System.BitConverter]::ToUInt32($octetos, 0) % 1000)
}

if ($Simular) {
    foreach ($m in $mozos) {
        "Se crearía el usuario {0} ({1}) con una contraseña del estilo Algete.123" -f $m.Usuario, $(if ($m.Correo) { $m.Correo } else { "SIN CORREO" })
    }
    return
}

# Dar de alta usuarios pide el rol Admin del API (api/accounts/create)
$admin = Read-Host "Usuario Admin del API"
$claveSegura = Read-Host "Contraseña" -AsSecureString
$clave = [System.Net.NetworkCredential]::new("", $claveSegura).Password
try {
    $token = (Invoke-RestMethod -Method Post -Uri "$Api/oauth/token" -ContentType "application/x-www-form-urlencoded" `
        -Body @{ grant_type = "password"; username = $admin; password = $clave }).access_token
} finally {
    $clave = $null
}
if (-not $token) { throw "No se ha podido entrar con ese usuario." }
$cabeceras = @{ Authorization = "Bearer $token" }

$credencialSmtp = $null
if ($ServidorSmtp) {
    if (-not $Remitente) { throw "Con -ServidorSmtp hace falta -Remitente." }
    $credencialSmtp = Get-Credential -UserName $Remitente -Message "Contraseña de la cuenta de correo que manda las contraseñas ($Remitente)"
    # Antes de crear a nadie: si la cuenta de correo no vale, que no quede ningún usuario creado con una contraseña
    # que nadie ha visto (03/10/26: se creó Santiago y falló el correo)
    try {
        Send-MailMessage -SmtpServer $ServidorSmtp -Port $PuertoSmtp -UseSsl -Credential $credencialSmtp -From $Remitente -To $Remitente `
            -Subject "Prueba: alta de usuarios de Ariadna" -Body "Prueba antes de dar de alta a los mozos. Se puede borrar." -Encoding ([System.Text.Encoding]::UTF8)
    } catch {
        throw "No se puede mandar correo con $Remitente ($($_.Exception.Message)). No se ha creado nadie: revisa la contraseña o lánzalo sin -ServidorSmtp."
    }
}

foreach ($m in $mozos) {
    # Si ya existe (Alfredo, que también usa NestoApp; Santiago, creado el 03/10 cuando falló el correo) no se toca:
    # entra con la contraseña que ya tiene o la cambia con «He olvidado mi contraseña»
    $existe = $true
    try {
        $null = Invoke-RestMethod -Method Get -Uri "$Api/api/accounts/user/$([uri]::EscapeDataString($m.Usuario))" -Headers $cabeceras
    } catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { $existe = $false } else { throw }
    }
    if ($existe) {
        "Ya existe $($m.Usuario): no se toca. Si no sabe su contraseña, «He olvidado mi contraseña» con $($m.Correo)."
        continue
    }

    $contrasena = Nueva-Contrasena
    $cuerpo = @{
        Email = $m.Correo; Username = $m.Usuario; FirstName = $m.Nombre; LastName = $m.Apellidos
        Password = $contrasena; ConfirmPassword = $contrasena
    } | ConvertTo-Json
    try {
        $null = Invoke-RestMethod -Method Post -Uri "$Api/api/accounts/create" -Headers $cabeceras `
            -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($cuerpo))
    } catch {
        Write-Warning "No se ha creado $($m.Usuario): $($_.ErrorDetails.Message) $($_.Exception.Message)"
        continue
    }

    if ($ServidorSmtp) {
        $texto = @"
Hola, $($m.Nombre):

Ya puedes entrar en Ariadna, la nueva aplicación del almacén.

  Usuario:     $($m.Usuario)
  Contraseña:  $contrasena

Si quieres cambiarla, pulsa «He olvidado mi contraseña» en la pantalla de entrada.
Cualquier cosa que no funcione o que se te ocurra para mejorarla, dínoslo desde la propia
aplicación, en Novedades («Algo no funciona» o «Sugerencia»).
"@
        try {
            Send-MailMessage -SmtpServer $ServidorSmtp -Port $PuertoSmtp -UseSsl -Credential $credencialSmtp `
                -From $Remitente -To $m.Correo -Subject "Tu usuario de Ariadna" -Body $texto -Encoding ([System.Text.Encoding]::UTF8)
            "Creado $($m.Usuario) y enviada la contraseña a $($m.Correo)."
        } catch {
            # El usuario ya está creado: que la contraseña no se pierda
            Write-Warning "Creado $($m.Usuario) pero NO se ha podido mandar el correo ($($_.Exception.Message))."
            "Contraseña de $($m.Usuario): $contrasena   (apúntala ahora y dásela en mano: no queda guardada en ningún sitio)"
        }
    } else {
        "Creado $($m.Usuario). Contraseña: $contrasena   (apúntala ahora: no queda guardada en ningún sitio)"
    }
    $contrasena = $null
}
