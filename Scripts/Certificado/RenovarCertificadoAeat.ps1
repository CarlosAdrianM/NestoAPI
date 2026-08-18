<#
.SYNOPSIS
    Renueva el certificado de la AEAT con UN solo comando: lo importa en el servidor de
    producción por remoto (detectando solo el app pool de IIS y dándole permiso) y deja
    NestoAPI usándolo automáticamente. Sin tocar código, config ni redesplegar.

.DESCRIPTION
    NestoAPI#388. Pensado para dentro de dos años: cuando llegue el certificado renovado
    de la FNMT, exportarlo a un .pfx CON clave privada (a Descargas) y ejecutar este
    script sin parámetros — él encuentra el .pfx, pide la contraseña una vez y hace todo
    lo demás. Ver RENOVAR_CERTIFICADO_AEAT.md.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File C:\Users\Carlos\source\repos\NestoAPI\Scripts\Certificado\RenovarCertificadoAeat.ps1
#>
param(
    # Ruta del .pfx; si no se indica, se propone el .pfx mas reciente de Descargas.
    [string]$RutaPfx,
    [string]$Servidor = 'RDS2016',
    # Importarlo tambien en ESTA maquina (solo hace falta para depurar VNifV2 en local).
    [switch]$TambienEnLocal,
    # Uso interno del relanzamiento elevado; no usar a mano.
    [switch]$SoloLocal,
    # Solo automatizacion; mejor no usarlo y dejar que la pida (no queda en el historial).
    [string]$Password
)

$ErrorActionPreference = 'Stop'
$patronEmpresa = 'VATES-A78368255|R: ?A78368255'

# === 1. Localizar el .pfx ===
if (-not $RutaPfx) {
    $descargas = Join-Path $env:USERPROFILE 'Downloads'
    $candidato = Get-ChildItem $descargas -Filter *.pfx -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $candidato) {
        throw "No hay ningun .pfx en $descargas. Exporta el certificado (CON clave privada) o indica -RutaPfx."
    }
    $respuesta = Read-Host "¿Usar '$($candidato.Name)' de Descargas (del $($candidato.LastWriteTime.ToString('dd/MM/yyyy HH:mm')))? [S/n]"
    if ($respuesta -match '^[nN]') { throw 'Cancelado. Indica el fichero con -RutaPfx.' }
    $RutaPfx = $candidato.FullName
}
if (-not (Test-Path $RutaPfx)) { throw "No existe el fichero: $RutaPfx" }

# === 2. Contraseña (una sola vez) y comprobacion del .pfx antes de tocar nada ===
if ($Password) {
    $passwordSegura = ConvertTo-SecureString $Password -AsPlainText -Force
} else {
    $passwordSegura = Read-Host -AsSecureString "Contraseña del .pfx"
}
$credencial = New-Object System.Management.Automation.PSCredential('pfx', $passwordSegura)

$certificado = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    $RutaPfx, $passwordSegura,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
Write-Host ""
Write-Host "Certificado: $($certificado.Subject)"
Write-Host "Caduca:      $($certificado.NotAfter.ToString('dd/MM/yyyy'))"
if (-not $certificado.HasPrivateKey) {
    throw 'Este .pfx NO lleva la clave privada. Exportalo de nuevo marcando "Si, exportar la clave privada".'
}
if ($certificado.Subject -notmatch $patronEmpresa) {
    throw "Este certificado no parece el de la empresa (falta A78368255 en el sujeto): $($certificado.Subject)"
}

# === 3. Servidor de produccion (remoto): importar + ACL al app pool + verificar ===
if (-not $SoloLocal) {
    Write-Host ""
    Write-Host "=== Instalando en $Servidor (produccion) ===" -ForegroundColor Cyan
    $rutaTempRemota = "\\$Servidor\c$\Windows\Temp\cert_aeat_renovacion.pfx"
    try {
        Copy-Item $RutaPfx $rutaTempRemota -Force
        $resultado = Invoke-Command -ComputerName $Servidor -ArgumentList $credencial, $patronEmpresa -ScriptBlock {
            param($cred, $patron)
            $rutaTemp = 'C:\Windows\Temp\cert_aeat_renovacion.pfx'
            try {
                # Sin -Exportable: la clave privada queda NO exportable en el almacen.
                $cert = Import-PfxCertificate -FilePath $rutaTemp -CertStoreLocation Cert:\LocalMachine\My -Password $cred.Password

                # App pool del API: el sitio de IIS cuyo physicalPath es C:\inetpub\Api.
                Import-Module WebAdministration
                $sitio = Get-Website | Where-Object { $_.physicalPath -like '*inetpub\Api*' } | Select-Object -First 1
                $pool = if ($sitio) { $sitio.applicationPool } else { $null }
                $avisoPool = $null
                if ($pool) {
                    # El fichero de la clave puede estar en Crypto\Keys (CNG) o en
                    # Crypto\RSA\MachineKeys (CSP); el tipo del wrapper de .NET no es fiable
                    # para saberlo (RSACng puede envolver una clave CSP), asi que se prueba
                    # en ambos y, si no, se busca por nombre.
                    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
                    if ($rsa -is [System.Security.Cryptography.RSACng]) {
                        $nombreClave = $rsa.Key.UniqueName
                    } else {
                        $nombreClave = $rsa.CspKeyContainerInfo.UniqueKeyContainerName
                    }
                    $rutaClave = @(
                        (Join-Path $env:ProgramData "Microsoft\Crypto\Keys\$nombreClave"),
                        (Join-Path $env:ProgramData "Microsoft\Crypto\RSA\MachineKeys\$nombreClave")
                    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
                    if (-not $rutaClave) {
                        $rutaClave = Get-ChildItem (Join-Path $env:ProgramData 'Microsoft\Crypto') -Recurse -Filter $nombreClave -File -ErrorAction SilentlyContinue |
                            Select-Object -First 1 -ExpandProperty FullName
                    }
                    if ($rutaClave) {
                        icacls $rutaClave /grant "IIS AppPool\${pool}:(R)" | Out-Null
                    } else {
                        $avisoPool = "Importado, pero no se encontro el fichero de la clave '$nombreClave' bajo $env:ProgramData\Microsoft\Crypto para dar permiso al pool '$pool'."
                        $pool = $null
                    }
                }
                if (-not $pool -and -not $avisoPool) {
                    $avisoPool = "No se encontro un sitio IIS con physicalPath *inetpub\Api*. Sitios: " +
                        ((Get-Website | ForEach-Object { "$($_.Name) [$($_.physicalPath)] -> $($_.applicationPool)" }) -join '; ') +
                        ". Da el permiso a mano: repite con ImportarCertificadoAeat.ps1 -AppPool <pool>."
                }

                $certificadosEmpresa = Get-ChildItem Cert:\LocalMachine\My |
                    Where-Object { $_.Subject -match $patron } |
                    Sort-Object NotAfter -Descending |
                    ForEach-Object { "$($_.NotAfter.ToString('dd/MM/yyyy'))  clave=$($_.HasPrivateKey)  $($_.Thumbprint)" }

                [pscustomobject]@{
                    Huella = $cert.Thumbprint
                    Caduca = $cert.NotAfter
                    Pool = $pool
                    AvisoPool = $avisoPool
                    CertificadosEmpresa = $certificadosEmpresa
                }
            }
            finally {
                Remove-Item $rutaTemp -Force -ErrorAction SilentlyContinue
            }
        }

        Write-Host "  Importado (no exportable). Huella: $($resultado.Huella)" -ForegroundColor Green
        if ($resultado.Pool) {
            Write-Host "  Permiso de lectura de la clave dado a 'IIS AppPool\$($resultado.Pool)'" -ForegroundColor Green
        } elseif ($resultado.AvisoPool) {
            Write-Host "  OJO: $($resultado.AvisoPool)" -ForegroundColor Yellow
        }
        Write-Host "  Certificados de la empresa en el servidor (NestoAPI usa el primero):"
        $resultado.CertificadosEmpresa | ForEach-Object { Write-Host "    $_" }
        Write-Host "  PRODUCCION LISTA: NestoAPI usara este certificado automaticamente (sin reciclar ni redesplegar)." -ForegroundColor Green
    }
    catch {
        Write-Host "  No se pudo hacer por remoto: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "  PLAN B — en el servidor $Servidor, PowerShell COMO ADMINISTRADOR:" -ForegroundColor Yellow
        Write-Host "    1) Copia el .pfx a C:\Temp del servidor"
        Write-Host "    2) powershell -ExecutionPolicy Bypass -File <repo>\Scripts\Certificado\ImportarCertificadoAeat.ps1 -RutaPfx C:\Temp\<fichero>.pfx -AppPool <pool>"
        Write-Host "       (el pool se ve con: Get-Website | Select Name, ApplicationPool)"
    }
    finally {
        Remove-Item $rutaTempRemota -Force -ErrorAction SilentlyContinue
    }
}

# === 4. Esta maquina (opcional, solo para depurar en local) ===
if ($TambienEnLocal -or $SoloLocal) {
    $esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($esAdmin) {
        $certLocal = Import-PfxCertificate -FilePath $RutaPfx -CertStoreLocation Cert:\LocalMachine\My -Password $passwordSegura
        Write-Host ""
        Write-Host "=== Esta maquina: importado (huella $($certLocal.Thumbprint)) ===" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "=== Esta maquina: hace falta elevar (aceptar el aviso de Windows; pedira la contraseña otra vez) ===" -ForegroundColor Yellow
        Start-Process powershell -Verb RunAs -ArgumentList @(
            '-ExecutionPolicy', 'Bypass', '-NoExit', '-File', "`"$PSCommandPath`"",
            '-RutaPfx', "`"$RutaPfx`"", '-SoloLocal')
    }
}

# === 5. Recordatorio de limpieza ===
if (-not $SoloLocal) {
    Write-Host ""
    $borrar = Read-Host "¿Borrar ya el .pfx de '$RutaPfx'? (recomendado tras verificar) [s/N]"
    if ($borrar -match '^[sS]') {
        Remove-Item $RutaPfx -Force
        Write-Host "Borrado." -ForegroundColor Green
    } else {
        Write-Host "Recuerda borrarlo cuando compruebes que funciona (y de descargas/correo si quedo por ahi)." -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Verificacion final: comprobar un NIF desde Nesto (ficha de cliente). Si fallara, el motivo sale en ELMAH como 'CertificadoAeat: ...'."
}
