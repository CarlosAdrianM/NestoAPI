<#
.SYNOPSIS
    Importa el certificado de representante de la AEAT (FNMT) en el almacén de Windows
    (LocalMachine\My) con la clave privada NO exportable, y opcionalmente da permiso de
    lectura de la clave al app pool de IIS.

.DESCRIPTION
    NestoAPI#388. Este script ES el proceso de renovación del certificado: NestoAPI lee el
    certificado del almacén de Windows y elige solo el más nuevo vigente, así que importar
    el renovado basta — sin tocar el repo, sin secretos.config, sin redesplegar, sin
    reciclar IIS. Ver RENOVAR_CERTIFICADO_AEAT.md en esta misma carpeta.

.EXAMPLE
    # En el servidor (PowerShell COMO ADMINISTRADOR):
    .\ImportarCertificadoAeat.ps1 -RutaPfx C:\Temp\certificado_nuevo.pfx -AppPool Api

.EXAMPLE
    # En la máquina de desarrollo (sin IIS, no hace falta -AppPool):
    .\ImportarCertificadoAeat.ps1 -RutaPfx C:\Temp\certificado_nuevo.pfx
#>
param(
    [Parameter(Mandatory = $true)][string]$RutaPfx,
    [string]$AppPool,
    # Solo para automatizacion; si no se indica, se pide por pantalla (recomendado).
    [string]$Password
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $RutaPfx)) {
    throw "No existe el fichero: $RutaPfx"
}

if ($Password) {
    $passwordSegura = ConvertTo-SecureString $Password -AsPlainText -Force
} else {
    $passwordSegura = Read-Host -AsSecureString "Contraseña del .pfx"
}

# Sin -Exportable: la clave privada queda NO exportable en el almacén (nadie puede volver
# a sacar un .pfx de ella; para moverla a otra maquina se usa el .pfx original de la FNMT).
$cert = Import-PfxCertificate -FilePath $RutaPfx -CertStoreLocation Cert:\LocalMachine\My -Password $passwordSegura

Write-Host ""
Write-Host "=== Certificado importado en LocalMachine\My ===" -ForegroundColor Green
Write-Host "  Sujeto:     $($cert.Subject)"
Write-Host "  Caduca:     $($cert.NotAfter.ToString('dd/MM/yyyy'))"
Write-Host "  Huella:     $($cert.Thumbprint)"
Write-Host "  Clave priv: $($cert.HasPrivateKey)"

if ($AppPool) {
    # El app pool de IIS necesita LEER la clave privada para autenticarse por TLS en la AEAT.
    # El fichero de la clave puede estar en Crypto\Keys (CNG) o en Crypto\RSA\MachineKeys
    # (CSP); el tipo del wrapper de .NET no es fiable para saberlo (RSACng puede envolver
    # una clave CSP), asi que se prueba en ambos y, si no, se busca por nombre.
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
    if (-not $rutaClave) {
        throw "Importado, pero no se encontro el fichero de la clave '$nombreClave' bajo $env:ProgramData\Microsoft\Crypto para dar permiso al pool '$AppPool'."
    }
    icacls $rutaClave /grant "IIS AppPool\${AppPool}:(R)" | Out-Null
    Write-Host "  Permiso de lectura de la clave concedido a 'IIS AppPool\$AppPool'" -ForegroundColor Green
} else {
    Write-Host "  (Sin -AppPool: no se ha tocado la ACL de la clave. En el SERVIDOR es obligatorio" -ForegroundColor Yellow
    Write-Host "   indicarlo; el nombre del pool se ve con: Get-Website | Select Name, ApplicationPool)" -ForegroundColor Yellow
}

# Resumen: todos los certificados de la empresa en el almacén; NestoAPI usara el de
# caducidad mas lejana de los vigentes.
Write-Host ""
Write-Host "=== Certificados de la empresa en LocalMachine\My (NestoAPI usa el mas nuevo vigente) ===" -ForegroundColor Cyan
Get-ChildItem Cert:\LocalMachine\My |
    Where-Object { $_.Subject -match 'VATES-A78368255|R: ?A78368255' } |
    Sort-Object NotAfter -Descending |
    Format-Table @{L='Caduca';E={$_.NotAfter.ToString('dd/MM/yyyy')}},
                 @{L='ClavePrivada';E={$_.HasPrivateKey}},
                 @{L='Sujeto';E={($_.Subject -split ',')[3]}},
                 Thumbprint -AutoSize

Write-Host "Hecho. Recuerda BORRAR el .pfx de $RutaPfx cuando verifiques que funciona." -ForegroundColor Yellow
