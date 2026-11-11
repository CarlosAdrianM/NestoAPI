<#
    Eventos: candidatos a invitar en modo sombra (NestoAPI#850, corte 1, 11/11/26). Carga dbo.CentrosCodigosPostales (Scripts/EventosCandidatosSombra.sql)
    con el centro de cada código postal de España, para la distancia en línea recta del cliente a la tienda del evento.

    FUENTE Y LICENCIA: GeoNames postal codes, fichero ES (https://download.geonames.org/export/zip/ES.zip), licencia Creative Commons
    Attribution 4.0 (CC-BY 4.0, https://creativecommons.org/licenses/by/4.0/). Hay que citar a GeoNames (https://www.geonames.org)
    donde se use el dato. Un CP puede tener varias poblaciones: se guarda la media de sus coordenadas y los nombres juntos.

    NO descarga nada ni toca la BD: genera un .sql que lanza Carlos.
    Procedimiento:
      1. Descargar ES.zip de la URL de arriba y descomprimirlo en una carpeta vacía (queda ES.txt, separado por tabuladores).
      2. powershell -File Scripts\CentrosCodigosPostales_GeoNames.ps1 -Fichero C:\ruta\ES.txt -Salida C:\ruta\CentrosCodigosPostales.sql
      3. Lanzar el .sql generado como sa en NV (después de Scripts/EventosCandidatosSombra.sql). Es idempotente (MERGE por CP):
         se puede relanzar con un fichero más nuevo.
    Columnas de ES.txt: país, CP, población, comunidad, cód. comunidad, provincia, cód. provincia, municipio, cód. municipio,
    latitud, longitud, precisión.
#>
param(
    [Parameter(Mandatory = $true)][string]$Fichero,
    [Parameter(Mandatory = $true)][string]$Salida
)

$ErrorActionPreference = 'Stop'
$invariante = [System.Globalization.CultureInfo]::InvariantCulture
$porCp = @{}
foreach ($linea in [System.IO.File]::ReadLines($Fichero, [System.Text.Encoding]::UTF8)) {
    $campos = $linea.Split("`t")
    if ($campos.Length -lt 11 -or $campos[0] -ne 'ES') { continue }
    $cp = $campos[1].Trim()
    if ($cp -notmatch '^\d{5}$') { continue }
    $lat = 0.0; $lon = 0.0
    if (-not [double]::TryParse($campos[9], [System.Globalization.NumberStyles]::Float, $invariante, [ref]$lat)) { continue }
    if (-not [double]::TryParse($campos[10], [System.Globalization.NumberStyles]::Float, $invariante, [ref]$lon)) { continue }
    if (-not $porCp.ContainsKey($cp)) { $porCp[$cp] = @{ Lat = 0.0; Lon = 0.0; N = 0; Nombres = New-Object System.Collections.Generic.List[string] } }
    $d = $porCp[$cp]
    $d.Lat += $lat; $d.Lon += $lon; $d.N += 1
    $nombre = $campos[2].Trim()
    if ($nombre -and -not $d.Nombres.Contains($nombre)) { $d.Nombres.Add($nombre) }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('-- Generado por Scripts/CentrosCodigosPostales_GeoNames.ps1. Datos: GeoNames (https://www.geonames.org), CC-BY 4.0.')
[void]$sb.AppendLine('SET NOCOUNT ON;')
[void]$sb.AppendLine('USE NV;')
[void]$sb.AppendLine('GO')
$filas = @($porCp.Keys | Sort-Object)
for ($i = 0; $i -lt $filas.Count; $i += 500) {
    $lote = $filas[$i..([Math]::Min($i + 499, $filas.Count - 1))]
    $valores = foreach ($cp in $lote) {
        $d = $porCp[$cp]
        $poblacion = ([string]::Join(', ', $d.Nombres)).Replace("'", "''")
        if ($poblacion.Length -gt 180) { $poblacion = $poblacion.Substring(0, 180) }
        "('{0}', {1}, {2}, N'{3}')" -f $cp, ($d.Lat / $d.N).ToString('0.000000', $invariante), ($d.Lon / $d.N).ToString('0.000000', $invariante), $poblacion
    }
    [void]$sb.AppendLine('MERGE dbo.CentrosCodigosPostales AS t')
    [void]$sb.AppendLine('USING (VALUES')
    [void]$sb.AppendLine(([string]::Join(",`r`n", $valores)))
    [void]$sb.AppendLine(') AS s (CodigoPostal, Latitud, Longitud, Poblacion) ON t.CodigoPostal = s.CodigoPostal')
    [void]$sb.AppendLine("WHEN MATCHED THEN UPDATE SET Latitud = s.Latitud, Longitud = s.Longitud, Poblacion = s.Poblacion, Fuente = 'GeoNames', Fecha = GETDATE()")
    [void]$sb.AppendLine("WHEN NOT MATCHED THEN INSERT (CodigoPostal, Latitud, Longitud, Poblacion, Fuente) VALUES (s.CodigoPostal, s.Latitud, s.Longitud, s.Poblacion, 'GeoNames');")
    [void]$sb.AppendLine('GO')
}
[void]$sb.AppendLine('SELECT COUNT(*) AS CodigosPostales FROM dbo.CentrosCodigosPostales;')
[void]$sb.AppendLine('GO')
# UTF-8 con BOM (sqlcmd -f 65001), sin Set-Content
[System.IO.File]::WriteAllText($Salida, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Host ("{0} códigos postales escritos en {1}" -f $filas.Count, $Salida)
