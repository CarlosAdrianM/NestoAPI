<#
    Ariadna: crea el rol «Almacén» en Identity (si no existe) y mete en él a los usuarios que se
    indiquen. Desde la API con el permiso de escritura (EscrituraSoloAlmacenAttribute), en api/Almacen
    leer basta con estar identificado, pero ESCRIBIR (escaneos, fotos de bultos, ubicar…) pide el rol
    «Almacén» (o «Dirección»). Sin rol, la API contesta 403.

    El nombre del rol es el mismo que el del grupo de Windows NUEVAVISION\Almacén: así vale una sola
    regla para Nesto (windows-token) y para Ariadna (/oauth/token). Con tilde.

    RevisionGoogle (los revisores de Google Play) NO debe estar en el rol: así solo puede leer.

    CUÁNDO: antes de publicar una versión de Ariadna que escriba (la que manda las lecturas), y otra
    vez cada vez que se dé de alta un mozo (Ariadna_CrearUsuarios.ps1). Se puede repetir sin miedo.

    Uso (pide el usuario Admin del API y su contraseña; no se guardan):
      .\Ariadna_RolAlmacen.ps1                                   # los 4 mozos
      .\Ariadna_RolAlmacen.ps1 -Usuarios Carlos                  # tu usuario de NestoApp, para probar
      .\Ariadna_RolAlmacen.ps1 -Usuarios Santiago,Andre -Simular # ver lo que haría
#>
param(
    [string]$Api = "https://api.nuevavision.es",
    [string[]]$Usuarios = @("Santiago", "Alfredo", "Pedro", "Andre"),
    [string]$Rol = "Almacén",
    [switch]$Simular
)

$ErrorActionPreference = "Stop"
# Con «pwsh -File … -Usuarios Paloma,Reina» el parámetro llega como UNA cadena «Paloma,Reina» (no como array): se parte por comas.
$Usuarios = @($Usuarios | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

if ($Usuarios -contains "RevisionGoogle") {
    throw "RevisionGoogle no puede tener el rol ${Rol}: es el usuario de los revisores de Google Play (solo lectura)."
}

if ($Simular) {
    "Se crearía (si no existe) el rol «$Rol» y se meterían en él: $($Usuarios -join ', ')"
    return
}

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

function Json($objeto) {
    # Texto, no bytes: en PowerShell 7 un byte[] en -Body llegaba como «123» (el primer byte, «{») y la API daba 400 (Carlos 04/11/26).
    # Invoke-RestMethod ya manda el texto en UTF-8 con el charset del ContentType.
    ($objeto | ConvertTo-Json -Compress)
}

# 1) El rol
$rolExistente = @(Invoke-RestMethod -Method Get -Uri "$Api/api/roles" -Headers $cabeceras) | Where-Object { $_.Name -eq $Rol }
if ($rolExistente) {
    $idRol = $rolExistente[0].Id
    "El rol «$Rol» ya existía."
} else {
    $creado = Invoke-RestMethod -Method Post -Uri "$Api/api/roles/create" -Headers $cabeceras `
        -ContentType "application/json; charset=utf-8" -Body (Json @{ Name = $Rol })
    $idRol = $creado.Id
    "Creado el rol «$Rol»."
}

# 2) Los usuarios (ManageUsersInRole pide los Id, no los nombres)
$ids = @()
foreach ($u in $Usuarios) {
    try {
        $usuario = Invoke-RestMethod -Method Get -Uri "$Api/api/accounts/user/$([uri]::EscapeDataString($u))" -Headers $cabeceras
        $ids += $usuario.Id
    } catch {
        Write-Warning "No existe el usuario $u (¿falta darlo de alta con Ariadna_CrearUsuarios.ps1?)."
    }
}
if ($ids.Count -eq 0) { "No hay nadie a quien meter en el rol."; return }

$null = Invoke-RestMethod -Method Post -Uri "$Api/api/roles/ManageUsersInRole" -Headers $cabeceras `
    -ContentType "application/json; charset=utf-8" -Body (Json @{ Id = $idRol; EnrolledUsers = $ids; RemovedUsers = @() })
"Hecho: $($ids.Count) usuario(s) en el rol «$Rol». Tienen que volver a entrar en Ariadna (el rol va en el token)."
