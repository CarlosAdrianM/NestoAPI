<#
.SYNOPSIS
    Genera los CSV de audiencias para Meta Ads (lookalike y exclusión).

.DESCRIPTION
    Formato pedido por la agencia (una fila por identificador):
        email,phone,first_name,last_name,city,postal_code,country
        cliente@ejemplo.com,+34612345678,Laura,García,Madrid,28001,ES

    Se generan cuatro listas de una vez (marketing elige con cuál monta la campaña):
    - LOOKALIKE                 : clientes con más de $UmbralLookalike € de base imponible facturada en los últimos $Dias días.
    - EXCLUSION_Compras         : clientes con cualquier compra facturada en los últimos $Dias días.
    - EXCLUSION_Visitas         : clientes con algún seguimiento (visita, llamada, WhatsApp) en los últimos $Dias días.
    - EXCLUSION_ComprasOVisitas : unión de las dos anteriores (comprado O contactado).

    Fuentes: LinPedidoVta (Estado=4 = factura) de las empresas indicadas (1 = Nueva Visión, 3 = Global,
    cuyos clientes son los mismos números de cliente de la empresa 1); SeguimientoCliente (empresa 1,
    Estado >= 0, cualquier Tipo: T teléfono, V visita, W whatsapp). Ficha principal de Clientes
    (empresa 1) para nombre/teléfono/población/CP/país y PersonasContactoCliente para correos y móviles.

    Reto principal: Clientes.Nombre lleva nombre y apellidos sin separar y en orden variable
    ("MARIA JOSE LOPEZ FERNANDEZ" o "GARCIA ESTEVEZ MARIA DEL CARMEN"). El script construye dos
    diccionarios de frecuencias (nombres de pila y apellidos) a partir de la propia BD:
      1) Nombres de pila: PersonasContactoCliente.Nombre (unas 24.000 filas, casi todas de una palabra).
      2) Apellidos: se deducen de las fichas de personas físicas (NIF numérico o NIE) en las que el
         orden es inequívoco (empieza por un nombre claro y termina por un no-nombre, o al revés).
    Después cada ficha se separa eligiendo la hipótesis (nombre-primero / apellidos-primero, con 1-3
    palabras de nombre) de mayor verosimilitud. Las partículas (DE, DEL, DE LA, DE LOS, SAN, Y…) se
    pegan a la palabra siguiente.

    Persona física vs empresa: por la letra inicial del NIF (dígito o X/Y/Z = persona; A/B/E/F/G/J… =
    empresa). Sin NIF, por palabras clave (S.L., PELUQUERÍA, ESTÉTICA, CENTRO…). Para las empresas el
    nombre/apellidos salen de la persona de contacto (cargo 14 por defecto, después 22 facturas).

    Los CSV se escriben FUERA del repositorio (datos personales). Además se genera un fichero de
    revisión (Revision_Nombres.csv) con la decisión tomada en cada ficha para poder muestrearla.

.EXAMPLE
    .\Scripts\MetaAds_Audiencias.ps1
    .\Scripts\MetaAds_Audiencias.ps1 -ConnectionString "Server=...;Database=NV;User ID=...;Password=..."
#>
[CmdletBinding()]
param(
    [string]$ConnectionString = "Server=DC2016\SQL2017;Database=NV;Integrated Security=True;Encrypt=False",
    [string]$OutDir = (Join-Path $env:USERPROFILE "Documents\MetaAds"),
    [int]$Dias = 365,
    [decimal]$UmbralLookalike = 1000,
    [string[]]$Empresas = @('1', '3'),
    # Solo para depurar: imprime las hipótesis de separación de estos nombres y termina sin escribir CSV.
    [string[]]$Probar
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 1. Datos
# ---------------------------------------------------------------------------
function Invoke-Consulta([System.Data.SqlClient.SqlConnection]$cn, [string]$sql) {
    $da = New-Object System.Data.SqlClient.SqlDataAdapter($sql, $cn)
    $dt = New-Object System.Data.DataTable
    [void]$da.Fill($dt)
    return , $dt
}

$empresasIn = ($Empresas | ForEach-Object { "'$_'" }) -join ','
$filtroVentas = "Empresa IN ($empresasIn) AND Estado = 4 AND [Fecha Factura] >= DATEADD(day, -$Dias, GETDATE())"

$filtroSeguimientos = "Empresa = '1' AND Estado >= 0 AND Fecha >= DATEADD(day, -$Dias, GETDATE())"
# Universo = clientes con compra UNION clientes con seguimiento; cada uno lleva su base facturada y sus seguimientos.
$cteUniverso = @"
;WITH v AS (
    SELECT [Nº Cliente] c, SUM([Base Imponible]) base, COUNT(*) lineas
    FROM LinPedidoVta WHERE $filtroVentas
    GROUP BY [Nº Cliente]),
s AS (
    SELECT [Número] c, COUNT(*) visitas, MAX(Fecha) ultimaVisita
    FROM SeguimientoCliente WHERE $filtroSeguimientos
    GROUP BY [Número]),
u AS (SELECT c FROM v UNION SELECT c FROM s)
"@
$sqlClientes = @"
$cteUniverso
SELECT RTRIM(u.c) Cliente, ISNULL(v.base, 0) Base, ISNULL(v.lineas, 0) Lineas, ISNULL(s.visitas, 0) Visitas, s.ultimaVisita UltimaVisita,
       RTRIM(c1.Nombre) Nombre, RTRIM(c1.[Teléfono]) Telefono,
       RTRIM(c1.[Población]) Poblacion, RTRIM(c1.CodPostal) CodPostal, RTRIM(c1.Pais) Pais,
       RTRIM(c1.[CIF/NIF]) Nif, c1.Estado Estado
FROM u
JOIN Clientes c1 ON c1.Empresa = '1' AND c1.[Nº Cliente] = u.c AND c1.ClientePrincipal = 1
LEFT JOIN v ON v.c = u.c
LEFT JOIN s ON s.c = u.c
"@
$sqlContactos = @"
$cteUniverso
, v2 AS (SELECT c FROM u)
SELECT RTRIM(p.[NºCliente]) Cliente, RTRIM(p.Contacto) Contacto, RTRIM(p.Nombre) Nombre, p.Cargo Cargo,
       RTRIM(p.[Teléfono]) Telefono, RTRIM(p.[CorreoElectrónico]) Correo
FROM PersonasContactoCliente p JOIN v2 ON v2.c = p.[NºCliente]
WHERE p.Empresa = '1' AND p.Estado >= 0
"@
$sqlNombresPila = @"
SELECT UPPER(LTRIM(RTRIM(Nombre))) n, COUNT(*) f FROM PersonasContactoCliente
WHERE Nombre IS NOT NULL AND LTRIM(RTRIM(Nombre)) <> '' GROUP BY UPPER(LTRIM(RTRIM(Nombre)))
"@
$sqlPersonasFisicas = @"
SELECT RTRIM(Nombre) Nombre FROM Clientes
WHERE Empresa = '1' AND ClientePrincipal = 1 AND Nombre IS NOT NULL
  AND LEFT(LTRIM([CIF/NIF]), 1) LIKE '[0-9XYZxyz]'
"@

Write-Host "Leyendo la base de datos..."
$cn = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
$cn.Open()
try {
    $clientes = Invoke-Consulta $cn $sqlClientes
    $contactos = Invoke-Consulta $cn $sqlContactos
    $nombresPila = Invoke-Consulta $cn $sqlNombresPila
    $personasFisicas = Invoke-Consulta $cn $sqlPersonasFisicas
}
finally { $cn.Close() }
Write-Host ("  {0} clientes con compra o seguimiento, {1} personas de contacto, {2} nombres de pila distintos, {3} fichas de persona física" -f `
        $clientes.Rows.Count, $contactos.Rows.Count, $nombresPila.Rows.Count, $personasFisicas.Rows.Count)

# ---------------------------------------------------------------------------
# 2. Normalización de texto
# ---------------------------------------------------------------------------
function Remove-Acentos([string]$s) {
    $d = $s.Normalize([Text.NormalizationForm]::FormD)
    $sb = New-Object Text.StringBuilder
    foreach ($ch in $d.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch) -ne [Globalization.UnicodeCategory]::NonSpacingMark) { [void]$sb.Append($ch) }
    }
    return $sb.ToString().Normalize([Text.NormalizationForm]::FormC)
}

# Clave de comparación: mayúsculas, sin acentos, Mª/M.ª/Mº/MA. -> MARIA, sin puntuación.
function ConvertTo-Clave([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return '' }
    $t = (Remove-Acentos $s.ToUpperInvariant())
    $t = $t -replace '\bM[ªº]\.?', 'MARIA ' -replace '\bM\.[ªº]', 'MARIA ' -replace '\bMA\.', 'MARIA ' -replace '\bM\.(?=\s)', 'MARIA '
    $t = $t -replace "[^A-ZÑ\s\-']", ' ' -replace '\s+', ' '
    return $t.Trim()
}

# Título: "maria jose" -> "Maria Jose"; partículas en minúscula; "Mª" -> "María".
$particulasMin = @('DE', 'DEL', 'LA', 'LAS', 'LOS', 'Y', 'E', 'DA', 'DO', 'DOS', 'DAS', 'DI', 'VAN', 'VON', 'DER')
function ConvertTo-Titulo([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return '' }
    $ti = [Globalization.CultureInfo]::GetCultureInfo('es-ES').TextInfo
    $palabras = foreach ($p0 in ($s -split '\s+')) {
        if ($p0 -match '^M[ªº]\.?$' -or $p0 -match '^M\.[ªº]$' -or $p0 -eq 'MA.') { 'María'; continue }
        $p = $p0 -replace "[^\p{L}\-']", ''
        if (-not $p) { continue }
        $k = (Remove-Acentos $p.ToUpperInvariant())
        if ($particulasMin -contains $k) { $p.ToLowerInvariant(); continue }
        (($p -split '-') | ForEach-Object { $ti.ToTitleCase($_.ToLowerInvariant()) }) -join '-'
    }
    return ($palabras -join ' ').Trim()
}

# ---------------------------------------------------------------------------
# 3. Diccionarios de nombres de pila y apellidos
# ---------------------------------------------------------------------------
$nombresDict = @{}   # clave -> frecuencia como nombre de pila
$apellidosDict = @{} # clave -> frecuencia como apellido
$basura = @('NO', 'ENVIAR', 'FARMACIA', 'CENTRO', 'CLINICA', 'PELUQUERIA', 'ESTETICA', 'TIENDA', 'DIRECCION', 'ADMINISTRACION', 'RUTA', 'GERENTE', 'DUEÑA', 'DUEÑO', 'ENCARGADA', 'ENCARGADO', 'RECEPCION', 'SRA', 'SR', 'DÑA', 'DON', 'DOÑA', 'CONTABILIDAD', 'INFO', 'OFICINA', 'PEDIDOS', 'COMPRAS', 'FACTURAS', 'RESPONSABLE', 'ENFERMERIA', 'HOTEL', 'SPA')

# 3a. Nombres de pila desde las personas de contacto (solo entradas de 1 palabra; las compuestas
#     "Mª JOSE" alimentan sus dos palabras).
# Ojo: hay contactos con nombre y apellido ("ANA GONZALEZ"); de las entradas de dos palabras solo
# se toma la primera, y la segunda únicamente cuando la primera es un arranque típico de nombre
# compuesto (MARIA JOSE, ANA BELEN, JOSE LUIS...). Si no, los apellidos se cuelan como nombres.
$arranquesCompuestos = @('MARIA', 'ANA', 'JOSE', 'JUAN', 'LUIS', 'MIGUEL', 'ROSA', 'EVA', 'MARI', 'PEDRO', 'JESUS', 'FRANCISCO', 'CARLOS', 'ANTONIO', 'ANGEL', 'VICTOR', 'JOSEFA', 'ISABEL', 'ROSA', 'DOLORES', 'PILAR', 'CARMEN')
foreach ($r in $nombresPila.Rows) {
    $k = ConvertTo-Clave $r.n
    if (-not $k) { continue }
    $partes = @($k -split ' ')
    if ($partes.Count -gt 2) { continue }
    if ($partes.Count -eq 2 -and -not ($arranquesCompuestos -contains $partes[0])) { $partes = @($partes[0]) }
    foreach ($p in $partes) {
        if ($p.Length -lt 2 -or $basura -contains $p) { continue }
        $nombresDict[$p] = [int]($nombresDict[$p] ?? 0) + [int]$r.f
    }
}

# Semilla de apellidos muy frecuentes (por si el aprendizaje no cubre alguno).
$semillaApellidos = 'GARCIA,RODRIGUEZ,GONZALEZ,FERNANDEZ,LOPEZ,MARTINEZ,SANCHEZ,PEREZ,GOMEZ,MARTIN,JIMENEZ,RUIZ,HERNANDEZ,DIAZ,MORENO,MUÑOZ,ALVAREZ,ROMERO,ALONSO,GUTIERREZ,NAVARRO,TORRES,DOMINGUEZ,GIL,VAZQUEZ,SERRANO,RAMOS,BLANCO,MOLINA,SUAREZ,MORALES,ORTEGA,DELGADO,CASTRO,ORTIZ,RUBIO,MARIN,SANZ,NUÑEZ,IGLESIAS,MEDINA,GARRIDO,CORTES,CASTILLO,SANTOS,LOZANO,GUERRERO,CANO,PRIETO,MENDEZ,CRUZ,CALVO,GALLEGO,HERRERA,MARQUEZ,LEON,VIDAL,PEÑA,FLORES,CABRERA,CAMPOS,VEGA,FUENTES,CARRASCO,DIEZ,CABALLERO,NIETO,REYES,AGUILAR,PASCUAL,HERRERO,SANTANA,LORENZO,MONTERO,HIDALGO,GIMENEZ,IBAÑEZ,FERRER,DURAN,SANTIAGO,BENITEZ,VICENTE,MORA,ARIAS,VARGAS,CARMONA,CRESPO,ROMAN,PASTOR,SOTO,SAEZ,VELASCO,MOYA,SOLER,PARRA,ESTEBAN,BRAVO,GALLARDO,ROJAS,LARA,RIVERA,REDONDO,MARCOS,IZQUIERDO,PARDO,REY,CAMACHO,RIOS,SILVA,MERINO,ARROYO,BENITO,MOLINERO,SEGURA,CALDERON,ANDRES,RIVAS,PALACIOS,JUAN,MANZANO,LUQUE,VILLAR,ROBLES,SORIA,SALAS,VERA,BUENO,CASADO,CASTAÑO,ESCUDERO,ANTON,AYALA,GUERRA,VILLA,MARTI,SANTAMARIA,ROLDAN,ROCA,OTERO,PADILLA,SALVADOR,MENENDEZ,ALARCON,PEREIRA,MATEO,MATEOS,CUESTA,VALLE,VILA,LUNA,SOLANO,MENA,ESPINOSA,BARROSO,CASTELLANOS,ORTUÑO,QUINTANA,LLORENTE,PALOMO,PALOMARES,OLIVA,MIRANDA,EXPOSITO,BLAZQUEZ,SANTOS,ARANDA,SIERRA,CANTERO,REQUENA,CARRILLO,BALLESTEROS,CHAMORRO,RAMIREZ,ZAMORA,COLLADO,LOSADA,VILLANUEVA,DEL RIO,AMOR,ZAPATA,BARBERO,BERMEJO,OLMO,OLMOS,ROSA,VALVERDE,MONTES,VALERO,JURADO,CONDE,PINTO,GALAN,PAZ,MIGUEL,CORRAL,ESCRIBANO,SIMON,PLAZA,ALBA,MORATA,SASTRE,VILLALBA,LUCAS,PULIDO,AGUADO,ARENAS,SORIANO,MUÑIZ,BARRIOS,TEJERO,TEJEDOR,LAGO,ALCALDE,MONTOYA,NAVAS,PONCE,COBO,COBOS,VICARIO,CID,CARO,MILLAN,MUR,CARRERA,HARO,SAN JOSE,GRANDE,GRANADOS,LINARES,OJEDA,ESCOBAR,BUSTOS,GALINDO,MARTINS,CASAS,ROSADO,TORRALBA,CERVERA,LARA,PACHECO,CAÑADAS,DIAZ-GUERRA,ZURITA'
foreach ($a in ($semillaApellidos -split ',')) { $apellidosDict[$a] = [int]($apellidosDict[$a] ?? 0) + 20 }

# Particulas que se pegan a la palabra siguiente.
$particulas = @('DE', 'DEL', 'LA', 'LAS', 'LOS', 'Y', 'SAN', 'SANTA', 'DA', 'DO', 'DOS', 'DAS', 'DI', 'VAN', 'VON', 'DER', "D'")
$nombresConParticula = @('MARIA', 'JOSE', 'ANA', 'ROSA', 'JUAN', 'JESUS', 'ANGEL', 'PEDRO', 'LUIS')
# Tratamientos que sobran: "DRA. ANA LOPEZ", "SRA. DE GARCIA", "DON JOSE"...
$titulos = @('DR', 'DRA', 'DOCTOR', 'DOCTORA', 'DOCTEUR', 'SR', 'SRA', 'SRTA', 'DON', 'DOÑA', 'DÑA', 'D', 'MR', 'MRS', 'MS', 'MME', 'MLLE', 'HNOS', 'HERMANOS', 'HNAS')

# Divide una cadena en unidades: cada unidad = (texto original, clave de la última palabra, tiene partícula, precedida de nombre tipo MARIA).
function Split-Unidades([string]$nombre) {
    $origTokens = @(($nombre -replace ',', ' ' -replace '\s+', ' ').Trim() -split ' ')
    $unidades = New-Object Collections.Generic.List[object]
    $pendOrig = @(); $pendClave = @()
    foreach ($tok in $origTokens) {
        $k = ConvertTo-Clave $tok
        if (-not $k) { continue }
        if ($titulos -contains $k) { continue }
        $pendOrig += $tok; $pendClave += $k
        $esParticula = $particulas -contains $k
        if ($esParticula -and $pendClave.Count -lt 6) { continue }
        $ult = ($k -split ' ')[-1]
        $prev = if ($unidades.Count -gt 0) { $unidades[$unidades.Count - 1].Clave } else { '' }
        $unidades.Add([pscustomobject]@{
                Orig         = ($pendOrig -join ' ')
                Clave        = $ult
                Particula    = ($pendClave.Count -gt 1)
                Guion        = ($ult -match '-')
                TrasNombre   = ($nombresConParticula -contains $prev)
            })
        $pendOrig = @(); $pendClave = @()
    }
    if ($pendOrig.Count -gt 0) {
        # partícula colgando al final ("... DE"): se pega a la última unidad
        if ($unidades.Count -gt 0) { $unidades[$unidades.Count - 1].Orig += ' ' + ($pendOrig -join ' ') }
        else { $unidades.Add([pscustomobject]@{ Orig = ($pendOrig -join ' '); Clave = $pendClave[-1]; Particula = $false; Guion = $false; TrasNombre = $false }) }
    }
    return , $unidades
}

# Log-verosimilitud de que una unidad sea nombre de pila / apellido.
$script:totalNombres = 1; $script:totalApellidos = 1
function Get-LogP([object]$u) {
    $nf = [double]($nombresDict[$u.Clave] ?? 0)
    $af = [double]($apellidosDict[$u.Clave] ?? 0)
    $pn = ($nf + 0.05) / ($script:totalNombres + 5000)
    $pa = ($af + 0.5) / ($script:totalApellidos + 5000)
    if ($u.Guion) { $pn *= 0.02 }
    if ($u.Particula -and -not $u.TrasNombre) { $pn *= 0.05 }
    if ($u.Particula -and $u.TrasNombre) { $pa *= 0.2 }
    return @([math]::Log($pn), [math]::Log($pa))
}

# Devuelve la mejor separación: @{ Nombre; Apellidos; Orden ('NF'|'SF'); Margen }
$script:priorNF = 0.5
$script:Depurar = $false
function Split-NombrePersona([string]$nombre) {
    $u = Split-Unidades $nombre
    $n = $u.Count
    if ($n -eq 0) { return $null }
    if ($n -eq 1) {
        $lp = Get-LogP $u[0]
        if ($lp[0] -ge $lp[1]) { return @{ Nombre = $u[0].Orig; Apellidos = ''; Orden = 'NF'; Margen = 0 } }
        return @{ Nombre = ''; Apellidos = $u[0].Orig; Orden = 'SF'; Margen = 0 }
    }
    $lps = foreach ($x in $u) { , (Get-LogP $x) }
    $priorApellidos = @{ 1 = [math]::Log(0.12); 2 = [math]::Log(0.78); 3 = [math]::Log(0.08); 4 = [math]::Log(0.02) }
    $priorNombre = @{ 1 = [math]::Log(0.62); 2 = [math]::Log(0.34); 3 = [math]::Log(0.04) }
    $hip = New-Object Collections.Generic.List[object]
    for ($k = 1; $k -lt $n; $k++) {
        $resto = $n - $k
        if ($k -le 3 -and $resto -le 4) {
            # nombre primero: k palabras de nombre + resto de apellidos
            $s = [math]::Log($script:priorNF) + $priorNombre[$k] + $priorApellidos[$resto]
            for ($i = 0; $i -lt $n; $i++) { $s += if ($i -lt $k) { $lps[$i][0] } else { $lps[$i][1] } }
            $hip.Add(@{ Orden = 'NF'; K = $k; Score = $s })
        }
        if ($k -le 4 -and $resto -le 3) {
            # apellidos primero: k apellidos + resto de nombre
            $s = [math]::Log(1 - $script:priorNF) + $priorApellidos[$k] + $priorNombre[$resto]
            for ($i = 0; $i -lt $n; $i++) { $s += if ($i -lt $k) { $lps[$i][1] } else { $lps[$i][0] } }
            $hip.Add(@{ Orden = 'SF'; K = $k; Score = $s })
        }
    }
    if ($n -le 3) {
        # "Mª JOSE", "ANA BELEN": todo es nombre de pila (sin apellidos); solo gana si todas las palabras son claramente nombres.
        $s = [math]::Log(0.02)
        for ($i = 0; $i -lt $n; $i++) { $s += $lps[$i][0] }
        $hip.Add(@{ Orden = 'NF'; K = $n; Score = $s })
    }
    $ord = @($hip | Sort-Object { $_.Score } -Descending)
    if ($script:Depurar) {
        Write-Host ("  {0}" -f $nombre)
        foreach ($x in $u) { $lp = Get-LogP $x; Write-Host ("    {0,-22} nombre={1,6:N2} apellido={2,6:N2}  (n={3} a={4})" -f $x.Orig, $lp[0], $lp[1], ($nombresDict[$x.Clave] ?? 0), ($apellidosDict[$x.Clave] ?? 0)) }
        foreach ($h in $ord) { Write-Host ("    {0} k={1} score={2:N2}" -f $h.Orden, $h.K, $h.Score) }
    }
    $mejor = $ord[0]
    $margen = if ($ord.Count -gt 1) { $mejor.Score - $ord[1].Score } else { 99 }
    $origs = @($u | ForEach-Object { $_.Orig })
    if ($mejor.Orden -eq 'NF' -and $mejor.K -eq $n) { return @{ Nombre = ($origs -join ' '); Apellidos = ''; Orden = 'NF'; Margen = $margen } }
    if ($mejor.Orden -eq 'NF') {
        return @{ Nombre = ($origs[0..($mejor.K - 1)] -join ' '); Apellidos = ($origs[$mejor.K..($n - 1)] -join ' '); Orden = 'NF'; Margen = $margen }
    }
    return @{ Nombre = ($origs[$mejor.K..($n - 1)] -join ' '); Apellidos = ($origs[0..($mejor.K - 1)] -join ' '); Orden = 'SF'; Margen = $margen }
}

# 3b. Aprender apellidos (y reforzar nombres) con las fichas de persona física de orden inequívoco.
Write-Host "Aprendiendo apellidos de las fichas de personas físicas..."
$script:totalNombres = ($nombresDict.Values | Measure-Object -Sum).Sum
$script:totalApellidos = ($apellidosDict.Values | Measure-Object -Sum).Sum
$nf = 0; $sf = 0
foreach ($r in $personasFisicas.Rows) {
    $u = Split-Unidades $r.Nombre
    if ($u.Count -lt 3 -or $u.Count -gt 4) { continue }
    $primero = [int]($nombresDict[$u[0].Clave] ?? 0)
    $ultimo = [int]($nombresDict[$u[$u.Count - 1].Clave] ?? 0)
    $penultimo = [int]($nombresDict[$u[$u.Count - 2].Clave] ?? 0)
    $segundo = [int]($nombresDict[$u[1].Clave] ?? 0)
    if ($primero -ge 5 -and $ultimo -eq 0 -and $penultimo -eq 0) {
        # NOMBRE (+nombre) APELLIDO APELLIDO
        $nf++
        for ($i = $u.Count - 2; $i -lt $u.Count; $i++) { $apellidosDict[$u[$i].Clave] = [int]($apellidosDict[$u[$i].Clave] ?? 0) + 1 }
        if ($u.Count -eq 4) { $nombresDict[$u[1].Clave] = [int]($nombresDict[$u[1].Clave] ?? 0) + 1 }
    }
    elseif ($ultimo -ge 5 -and $primero -eq 0 -and $segundo -eq 0) {
        # APELLIDO APELLIDO NOMBRE (+nombre)
        $sf++
        for ($i = 0; $i -lt 2; $i++) { $apellidosDict[$u[$i].Clave] = [int]($apellidosDict[$u[$i].Clave] ?? 0) + 1 }
        if ($u.Count -eq 4) { $nombresDict[$u[2].Clave] = [int]($nombresDict[$u[2].Clave] ?? 0) + 1 }
    }
}
$script:priorNF = [math]::Max(0.2, [math]::Min(0.8, $nf / [math]::Max(1, $nf + $sf)))
$script:totalNombres = ($nombresDict.Values | Measure-Object -Sum).Sum
$script:totalApellidos = ($apellidosDict.Values | Measure-Object -Sum).Sum
Write-Host ("  Fichas inequívocas: {0} nombre-primero, {1} apellidos-primero (prior nombre-primero = {2:P0}); {3} apellidos distintos aprendidos" -f $nf, $sf, $script:priorNF, $apellidosDict.Count)

# 3c. Segunda vuelta: con el modelo ya entrenado se separan todas las fichas y las decisiones claras
#     (margen alto) refuerzan los dos diccionarios (cubre apellidos que también aparecen como nombre).
$nuevosNombres = @{}; $nuevosApellidos = @{}
foreach ($r in $personasFisicas.Rows) {
    $sp = Split-NombrePersona $r.Nombre
    if ($null -eq $sp -or $sp.Margen -lt 4 -or -not $sp.Apellidos -or -not $sp.Nombre) { continue }
    foreach ($x in (Split-Unidades $sp.Nombre)) { $nuevosNombres[$x.Clave] = [int]($nuevosNombres[$x.Clave] ?? 0) + 1 }
    foreach ($x in (Split-Unidades $sp.Apellidos)) { $nuevosApellidos[$x.Clave] = [int]($nuevosApellidos[$x.Clave] ?? 0) + 1 }
}
foreach ($k in $nuevosNombres.Keys) { $nombresDict[$k] = [int]($nombresDict[$k] ?? 0) + $nuevosNombres[$k] }
foreach ($k in $nuevosApellidos.Keys) { $apellidosDict[$k] = [int]($apellidosDict[$k] ?? 0) + $nuevosApellidos[$k] }
$script:totalNombres = ($nombresDict.Values | Measure-Object -Sum).Sum
$script:totalApellidos = ($apellidosDict.Values | Measure-Object -Sum).Sum
Write-Host ("  Segunda vuelta: {0} apellidos distintos, {1} nombres distintos" -f $apellidosDict.Count, $nombresDict.Count)

if ($Probar) {
    $script:Depurar = $true
    foreach ($p in $Probar) { $r = Split-NombrePersona $p; Write-Host ("  => [{0}] [{1}] {2} margen={3:N1}" -f $r.Nombre, $r.Apellidos, $r.Orden, $r.Margen) }
    return
}

# ---------------------------------------------------------------------------
# 4. Persona física vs empresa, teléfonos, correos
# ---------------------------------------------------------------------------
# Palabras que delatan un negocio (sobre el nombre en mayúsculas y sin acentos). Dos grupos: tokens
# enteros (SL, SAS, LDA, GMBH...) y raíces sin cierre de palabra (PELUQUER-IA, ESTETIC-A, CLINIC-A...).
$palabrasEmpresa = '(\b(?:S\.?L\.?U?|S\.?A\.?U?|S\.?A\.?S\.?U?|S\.?C\.?P?|C\.?B|S\.?L\.?L|S\.?COOP|S\.?R\.?L\.?S?|S\.?N\.?C|SLU|SLL|SL|SA|SAS|SASU|SARL|EURL|SRL|SRLS|SNC|LDA|GMBH|LTD|LIMITED|BV|EAR|INC|LLC|CORP|UNIPERSONAL|UNIPESSOAL|SOCIEDAD|CENTRO|CENTRE|CENTER|SALON|NAILS|STUDIO|ESTUDIO|BEAUTY|BEAUTE|BELLEZA|BELLEZZA|SPA|INSTITUTO|INSTITUT|ISTITUTO|ACADEMIA|ESCUELA|FARMACIA|HOTEL|LASHES|HAIR|BARBER|GABINETE|WELLNESS|BODY|SKIN|LASER|UÑAS|RESIDENCIA|AYUNTAMIENTO|IES|COLEGIO|COMUNIDAD|GRUPO|GROUP|SHOP|STORE|BOUTIQUE|IMAGEN|IMAGE|STYLE|STYLING|LOOK|PILATES|YOGA|GYM|FITNESS|DENTAL|OPTICA|ONLINE|WEB|COMERCIAL|SERVICIOS|PRODUCTOS|SUMINISTROS|LABORATOIRE|LABORATORIO|FORMATION|FORMACION|PARRUCCHIER|PROFUMERIA|KOSMETIK|DI|THE|LE|LES)\b|\b(?:PELUQUER|ESTETIC|ESTHETI|CLINIC|CLINIQUE|COSMETIC|COSMETIQ|MICROPIGMENT|GERIATR|VETERINARI|DEPILACI|DISTRIBUCI|ASOCIACI|FUNDACI|MEDIC|DERMO|FISIO|MASAJ|MASSAG|STYLIST|ESTILIST|BENESSERE|FRISEUR)|[&@])'
function Test-EsEmpresa([string]$nif, [string]$nombre) {
    # 1) Palabras clave en el nombre (también para extranjeros: el campo Pais no es fiable y sus NIF no siguen el patrón español).
    $k = (Remove-Acentos ($nombre ?? '').ToUpperInvariant())
    if ($k -match $palabrasEmpresa) { return $true }
    # 2) NIF con patrón español: DNI/NIE = persona; letra de sociedad = empresa.
    $n = (($nif ?? '') -replace '[\s\-\.]', '').ToUpperInvariant()
    if ($n -match '^\d{7,8}[A-Z]$' -or $n -match '^[XYZ]\d{7}[A-Z]$') { return $false }
    if ($n -match '^[ABCDEFGHJNPQRSUVW]\d{7}[0-9A-J]$') { return $true }
    # 3) Sin NIF útil: por la pinta del nombre.
    $u = Split-Unidades $nombre
    return ($u.Count -lt 2 -or $u.Count -gt 5)
}

$prefijoPais = @{ ES = '34'; PT = '351'; FR = '33'; IT = '39'; AD = '376'; IE = '353'; CH = '41'; DK = '45'; DE = '49'; GB = '44'; UK = '44'; NL = '31'; BE = '32'; AR = '54'; VE = '58'; MX = '52'; US = '1'; MA = '212' }
# Devuelve los teléfonos de un campo en formato E.164, móviles primero.
function Get-Telefonos([string]$raw, [string]$pais) {
    $res = New-Object Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($raw) -or $raw -match 'e\+\d') { return , $res }
    $pref = $prefijoPais[$pais] ?? '34'
    $texto = $raw
    # internacionales explícitos (+34 612..., 0033 6..., +1 (201) 702-4473)
    foreach ($m in [regex]::Matches($texto, '(\+|00)\s?\d[\d\s\-\.\(\)]{6,}\d')) {
        $d = $m.Value -replace '[^\d]', ''
        if ($m.Value.StartsWith('00')) { $d = $d.Substring(2) }
        # "+33 0612..." / "+49 0841...": en estos países el 0 inicial es el prefijo nacional y sobra en E.164 (en Italia NO).
        if ($d -match '^(33|44|49|31|32|46|43|41)0' -and $d.Length -gt 11) { $d = $d.Substring(0, 2) + $d.Substring(3) }
        if ($d.Length -ge 10 -and $d.Length -le 15) { $res.Add('+' + $d) }
        $texto = $texto.Replace($m.Value, ' ')
    }
    foreach ($m in [regex]::Matches($texto, '\d[\d\s\.\-]*\d')) {
        $d = $m.Value -replace '[^\d]', ''
        if ($d.Length -eq 9 -and $d -match '^[6789]' -and $pref -eq '34') { $res.Add('+34' + $d); continue }
        if ($d.Length -eq 11 -and $d.StartsWith('34')) { $res.Add('+' + $d); continue }
        if ($pref -ne '34') {
            if ($d.Length -eq 9 -and $pref -eq '351') { $res.Add('+351' + $d); continue }
            if ($d.Length -eq 10 -and $d.StartsWith('0') -and $pref -in @('33', '39', '44', '49', '31')) { $res.Add('+' + $pref + $d.Substring(1)); continue }
            if ($d.Length -ge 8 -and $d.Length -le 10) { $res.Add('+' + $pref + $d); continue }
        }
        # números partidos en dos bloques de 9 pegados (ej: "914135690C/5624050T" no llega): se ignoran
    }
    $movil = @($res | Where-Object { $_ -match '^\+34[67]' })
    $otros = @($res | Where-Object { $_ -notmatch '^\+34[67]' })
    $orden = New-Object Collections.Generic.List[string]
    foreach ($t in ($movil + $otros)) { if (-not $orden.Contains($t)) { $orden.Add($t) } }
    return , $orden
}

$paisPorPrefijo = @{ '351' = 'PT'; '352' = 'LU'; '353' = 'IE'; '376' = 'AD'; '420' = 'CZ'; '503' = 'SV'; '596' = 'MQ'; '212' = 'MA'; '240' = 'GQ'; '33' = 'FR'; '39' = 'IT'; '41' = 'CH'; '45' = 'DK'; '49' = 'DE'; '44' = 'GB'; '31' = 'NL'; '32' = 'BE'; '46' = 'SE'; '43' = 'AT'; '58' = 'VE'; '51' = 'PE'; '52' = 'MX'; '54' = 'AR'; '55' = 'BR'; '56' = 'CL'; '57' = 'CO'; '1' = 'US' }
function Get-PaisPorPrefijo([string]$tel) {
    $d = $tel.TrimStart('+')
    foreach ($len in 3, 2, 1) { $pre = $d.Substring(0, $len); if ($paisPorPrefijo.ContainsKey($pre)) { return $paisPorPrefijo[$pre] } }
    return 'ES'
}

$regexEmail = '^[A-Za-z0-9._%+\-'']+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$'
function Get-Email([string]$raw) {
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $e = $raw.Trim().ToLowerInvariant() -replace '^mailto:', '' -replace '[,;].*$', ''
    $e = $e.Trim().TrimEnd('.')
    if ($e -notmatch $regexEmail) { return $null }
    if ($e -like '*@nuevavision.es') { return $null }
    return $e
}

function Test-NombreContactoValido([string]$s) {
    if ([string]::IsNullOrWhiteSpace($s)) { return $false }
    $k = ConvertTo-Clave $s
    if (-not $k) { return $false }
    $partes = @($k -split ' ')
    if ($partes.Count -gt 5) { return $false }
    if ($s -match '\d' -or $s -match '@') { return $false }
    foreach ($p in $partes) { if ($basura -contains $p -or $titulos -contains $p) { return $false } }
    if ($partes.Count -ge 2) {
        $conocidos = @($partes | Where-Object { [int]($nombresDict[$_] ?? 0) -ge 2 })
        if ($conocidos.Count -eq 0) { return $false }   # "BELLA ESENCIA", "ESTETICA MARIBEL": es el negocio, no una persona
    }
    return $true
}

# Contactos agrupados por cliente, ordenados por prioridad de cargo.
$prioridadCargo = @{ 14 = 0; 22 = 1; 5 = 2; 1 = 3; 11 = 4 }
$contactosPorCliente = @{}
foreach ($c in ($contactos.Rows | Sort-Object { $prioridadCargo[[int]$_.Cargo] ?? 9 }, { if ($_.Contacto -eq '0') { 0 } else { 1 } })) {
    if (-not $contactosPorCliente.ContainsKey($c.Cliente)) { $contactosPorCliente[$c.Cliente] = New-Object Collections.Generic.List[object] }
    $contactosPorCliente[$c.Cliente].Add($c)
}

# ---------------------------------------------------------------------------
# 5. Construcción de filas
# ---------------------------------------------------------------------------
function Get-Campo([object]$v) { if ($v -is [DBNull] -or $null -eq $v) { '' } else { [string]$v } }

$listas = [ordered]@{
    Lookalike                 = New-Object Collections.Generic.List[object]
    Exclusion_Compras         = New-Object Collections.Generic.List[object]
    Exclusion_Visitas         = New-Object Collections.Generic.List[object]
    Exclusion_ComprasOVisitas = New-Object Collections.Generic.List[object]
}
$clientesPorLista = @{}; foreach ($k in $listas.Keys) { $clientesPorLista[$k] = 0 }
$revision = New-Object Collections.Generic.List[object]
$stats = @{ personas = 0; empresas = 0; sinIdentificador = 0; conEmail = 0; conMovil = 0; conTelFijoSolo = 0; filasExtra = 0; conCompra = 0; conVisita = 0 }

foreach ($r in $clientes.Rows) {
    $cliente = Get-Campo $r.Cliente
    $nombreFicha = Get-Campo $r.Nombre
    $nif = Get-Campo $r.Nif
    $pais = (Get-Campo $r.Pais).Trim().ToUpperInvariant(); if (-not $pais) { $pais = 'ES' }
    $ciudad = ConvertTo-Titulo ((Get-Campo $r.Poblacion) -replace '\s*\(.*\)\s*$', '')
    $cp = (Get-Campo $r.CodPostal) -replace '[^\dA-Za-z\- ]', ''
    $base = [decimal]$r.Base
    $visitas = [int]$r.Visitas
    $haComprado = ([int]$r.Lineas -gt 0)   # por líneas facturadas, no por base > 0: un cliente con devoluciones netas también ha comprado
    if ($haComprado) { $stats.conCompra++ }
    if ($visitas -gt 0) { $stats.conVisita++ }
    $contactosCli = $contactosPorCliente[$cliente]
    if ($null -eq $contactosCli) { $contactosCli = @() }

    # Nombre y apellidos
    $esEmpresa = Test-EsEmpresa $nif $nombreFicha
    $first = ''; $last = ''; $metodo = ''; $margen = ''
    if (-not $esEmpresa) {
        $stats.personas++
        $sp = Split-NombrePersona $nombreFicha
        if ($sp) { $first = ConvertTo-Titulo $sp.Nombre; $last = ConvertTo-Titulo $sp.Apellidos; $metodo = "persona-$($sp.Orden)"; $margen = '{0:N1}' -f $sp.Margen }
    }
    else {
        $stats.empresas++
        $metodo = 'empresa-sin-contacto'
        $contactoNombre = $contactosCli | Where-Object { Test-NombreContactoValido (Get-Campo $_.Nombre) } | Select-Object -First 1
        if ($contactoNombre) {
            $sp = Split-NombrePersona (Get-Campo $contactoNombre.Nombre)
            if ($sp) { $first = ConvertTo-Titulo $sp.Nombre; $last = ConvertTo-Titulo $sp.Apellidos; $metodo = 'empresa-contacto'; $margen = '{0:N1}' -f $sp.Margen }
        }
    }

    # Teléfonos: ficha + contactos, móviles primero
    $tels = New-Object Collections.Generic.List[string]
    foreach ($t in (Get-Telefonos (Get-Campo $r.Telefono) $pais)) { if (-not $tels.Contains($t)) { $tels.Add($t) } }
    foreach ($c in $contactosCli) { foreach ($t in (Get-Telefonos (Get-Campo $c.Telefono) $pais)) { if (-not $tels.Contains($t)) { $tels.Add($t) } } }
    $moviles = @($tels | Where-Object { $_ -match '^\+34[67]' })
    $telPrincipal = if ($moviles.Count -gt 0) { $moviles[0] } elseif ($tels.Count -gt 0) { $tels[0] } else { '' }
    # El campo Pais de la ficha no es fiable (extranjeros dados de alta como ES): si el teléfono lleva
    # prefijo internacional distinto de +34, manda el prefijo.
    if ($pais -eq 'ES' -and $telPrincipal -and $telPrincipal -notmatch '^\+34') { $pais = Get-PaisPorPrefijo $telPrincipal }
    if ($pais -eq 'ES') { if ($cp -match '^\d{4}$') { $cp = '0' + $cp }; if ($cp -notmatch '^\d{5}$') { $cp = '' } }
    elseif ($cp -match '^EXT') { $cp = '' }

    # Correos: por prioridad de cargo, sin repetir
    $emails = New-Object Collections.Generic.List[object]
    foreach ($c in $contactosCli) {
        $e = Get-Email (Get-Campo $c.Correo)
        if ($e -and -not ($emails | Where-Object { $_.Email -eq $e })) { $emails.Add(@{ Email = $e; Contacto = $c }) }
    }

    if ($emails.Count -eq 0 -and -not $telPrincipal) { $stats.sinIdentificador++; continue }
    if ($emails.Count -gt 0) { $stats.conEmail++ }
    if ($moviles.Count -gt 0) { $stats.conMovil++ } elseif ($telPrincipal) { $stats.conTelFijoSolo++ }

    $filas = New-Object Collections.Generic.List[object]
    $emailPrincipal = if ($emails.Count -gt 0) { $emails[0].Email } else { '' }
    $filas.Add([pscustomobject]@{ email = $emailPrincipal; phone = $telPrincipal; first_name = $first; last_name = $last; city = $ciudad; postal_code = $cp; country = $pais })
    # Filas adicionales: otros correos del cliente, cada uno con el teléfono de su propia persona de contacto (si lo tiene).
    for ($i = 1; $i -lt $emails.Count; $i++) {
        $c = $emails[$i].Contacto
        $telC = @(Get-Telefonos (Get-Campo $c.Telefono) $pais)
        $f2 = $first; $l2 = $last
        if (Test-NombreContactoValido (Get-Campo $c.Nombre)) {
            $sp = Split-NombrePersona (Get-Campo $c.Nombre)
            if ($sp) { $f2 = ConvertTo-Titulo $sp.Nombre; $l2 = ConvertTo-Titulo $sp.Apellidos }
        }
        $filas.Add([pscustomobject]@{ email = $emails[$i].Email; phone = ($(if ($telC.Count -gt 0) { $telC[0] } else { '' })); first_name = $f2; last_name = $l2; city = $ciudad; postal_code = $cp; country = $pais })
        $stats.filasExtra++
    }

    $pertenece = @{
        Lookalike                 = ($base -gt $UmbralLookalike)
        Exclusion_Compras         = $haComprado
        Exclusion_Visitas         = ($visitas -gt 0)
        Exclusion_ComprasOVisitas = ($haComprado -or $visitas -gt 0)
    }
    foreach ($k in $listas.Keys) {
        if ($pertenece[$k]) { $clientesPorLista[$k]++; foreach ($f in $filas) { $listas[$k].Add($f) } }
    }

    $revision.Add([pscustomobject]@{
            Cliente = $cliente; Base = ('{0:N2}' -f $base); Visitas = $visitas; UltimaVisita = ($(if ($r.UltimaVisita -is [DateTime]) { $r.UltimaVisita.ToString('yyyy-MM-dd') } else { '' }))
            Lookalike = $pertenece.Lookalike; Tipo = ($(if ($esEmpresa) { 'empresa' } else { 'persona' }))
            NIF = $nif; NombreFicha = $nombreFicha; first_name = $first; last_name = $last; Metodo = $metodo; Margen = $margen
            email = $emailPrincipal; phone = $telPrincipal; Correos = $emails.Count; Telefonos = $tels.Count
        })
}

# ---------------------------------------------------------------------------
# 6. Salida
# ---------------------------------------------------------------------------
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$fecha = Get-Date -Format 'yyyyMMdd'
$utf8SinBom = New-Object Text.UTF8Encoding($false)

function Write-CsvMeta([Collections.Generic.List[object]]$filas, [string]$ruta) {
    $sb = New-Object Text.StringBuilder
    [void]$sb.AppendLine('email,phone,first_name,last_name,city,postal_code,country')
    foreach ($f in $filas) {
        $campos = foreach ($v in @($f.email, $f.phone, $f.first_name, $f.last_name, $f.city, $f.postal_code, $f.country)) {
            $s = [string]$v
            if ($s -match '[,"\r\n]') { '"' + ($s -replace '"', '""') + '"' } else { $s }
        }
        [void]$sb.AppendLine($campos -join ',')
    }
    [IO.File]::WriteAllText($ruta, $sb.ToString(), $utf8SinBom)
}

$rutaRevision = Join-Path $OutDir "MetaAds_Revision_Nombres_$fecha.csv"
$revision | Export-Csv -Path $rutaRevision -Encoding utf8BOM -NoTypeInformation -Delimiter ';'   # BOM para que Excel lea bien las tildes

Write-Host ""
Write-Host ("Universo ({0} días): {1} clientes = {2} con compra + {3} con seguimiento (solapan); personas físicas {4}, empresas {5}; sin email ni teléfono: {6}" -f `
        $Dias, $clientes.Rows.Count, $stats.conCompra, $stats.conVisita, $stats.personas, $stats.empresas, $stats.sinIdentificador)
Write-Host ("  con email: {0}; con móvil: {1}; solo fijo: {2}; filas extra por correos adicionales: {3}" -f $stats.conEmail, $stats.conMovil, $stats.conTelFijoSolo, $stats.filasExtra)
foreach ($k in $listas.Keys) {
    $ruta = Join-Path $OutDir ("MetaAds_{0}_{1}.csv" -f $k, $fecha)
    Write-CsvMeta $listas[$k] $ruta
    Write-Host ("{0,-26}: {1,5} filas ({2,5} clientes) -> {3}" -f $k, $listas[$k].Count, $clientesPorLista[$k], $ruta)
}
Write-Host ("REVISION                  : {0}" -f $rutaRevision)
