# ============================================
# Script para LIMPIAR NotasEntrega del EDMX
# ============================================
# Este script elimina TODAS las referencias a NotasEntrega/NotaEntrega del EDMX
# para poder agregarla de nuevo desde cero
# ============================================

$edmxPath = "C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Models\NestoEntities.edmx"
$backupPath = "C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Models\NestoEntities.edmx.backup_" + (Get-Date -Format "yyyyMMdd_HHmmss")

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "LIMPIEZA DE NOTASENTREGA DEL EDMX" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# 1. Crear backup
Write-Host "[1/4] Creando backup del EDMX..." -ForegroundColor Yellow
Copy-Item $edmxPath $backupPath
Write-Host "      Backup creado: $backupPath" -ForegroundColor Green
Write-Host ""

# 2. Leer el archivo
Write-Host "[2/4] Leyendo EDMX..." -ForegroundColor Yellow
[xml]$edmx = Get-Content $edmxPath
Write-Host "      EDMX cargado correctamente" -ForegroundColor Green
Write-Host ""

# 3. Definir namespaces
$ns = @{
    edmx = "http://schemas.microsoft.com/ado/2009/11/edmx"
    ssdl = "http://schemas.microsoft.com/ado/2009/11/edm/ssdl"
    edm = "http://schemas.microsoft.com/ado/2009/11/edm"
    msl = "http://schemas.microsoft.com/ado/2009/11/mapping/cs"
}

Write-Host "[3/4] Eliminando referencias a NotasEntrega..." -ForegroundColor Yellow

$eliminados = 0

# A. Eliminar EntityType "NotasEntrega" del Storage Model (SSDL)
$storageModel = $edmx.SelectSingleNode("//ssdl:Schema", $ns)
if ($storageModel) {
    $entityType = $storageModel.SelectSingleNode("ssdl:EntityType[@Name='NotasEntrega']", $ns)
    if ($entityType) {
        $storageModel.RemoveChild($entityType) | Out-Null
        Write-Host "      ✓ Eliminado EntityType 'NotasEntrega' del Storage Model" -ForegroundColor Green
        $eliminados++
    }
}

# B. Eliminar EntitySet "NotasEntrega" del Storage EntityContainer
$storageContainer = $edmx.SelectSingleNode("//ssdl:EntityContainer", $ns)
if ($storageContainer) {
    $entitySet = $storageContainer.SelectSingleNode("ssdl:EntitySet[@Name='NotasEntrega']", $ns)
    if ($entitySet) {
        $storageContainer.RemoveChild($entitySet) | Out-Null
        Write-Host "      ✓ Eliminado EntitySet 'NotasEntrega' del Storage EntityContainer" -ForegroundColor Green
        $eliminados++
    }
}

# C. Eliminar EntityType del Conceptual Model (CSDL)
$conceptualSchema = $edmx.SelectSingleNode("//edm:Schema", $ns)
if ($conceptualSchema) {
    # Buscar tanto NotaEntrega como NotasEntrega
    $entityType1 = $conceptualSchema.SelectSingleNode("edm:EntityType[@Name='NotaEntrega']", $ns)
    $entityType2 = $conceptualSchema.SelectSingleNode("edm:EntityType[@Name='NotasEntrega']", $ns)

    if ($entityType1) {
        $conceptualSchema.RemoveChild($entityType1) | Out-Null
        Write-Host "      ✓ Eliminado EntityType 'NotaEntrega' del Conceptual Model" -ForegroundColor Green
        $eliminados++
    }
    if ($entityType2) {
        $conceptualSchema.RemoveChild($entityType2) | Out-Null
        Write-Host "      ✓ Eliminado EntityType 'NotasEntrega' del Conceptual Model" -ForegroundColor Green
        $eliminados++
    }
}

# D. Eliminar EntitySet del Conceptual EntityContainer
$conceptualContainer = $edmx.SelectSingleNode("//edm:EntityContainer", $ns)
if ($conceptualContainer) {
    $entitySet = $conceptualContainer.SelectSingleNode("edm:EntitySet[@Name='NotasEntregas']", $ns)
    if ($entitySet) {
        $conceptualContainer.RemoveChild($entitySet) | Out-Null
        Write-Host "      ✓ Eliminado EntitySet 'NotasEntregas' del Conceptual EntityContainer" -ForegroundColor Green
        $eliminados++
    }
}

# E. Eliminar EntitySetMapping
$mappings = $edmx.SelectSingleNode("//msl:EntityContainerMapping", $ns)
if ($mappings) {
    $entitySetMapping = $mappings.SelectSingleNode("msl:EntitySetMapping[@Name='NotasEntregas']", $ns)
    if ($entitySetMapping) {
        $mappings.RemoveChild($entitySetMapping) | Out-Null
        Write-Host "      ✓ Eliminado EntitySetMapping 'NotasEntregas'" -ForegroundColor Green
        $eliminados++
    }
}

Write-Host ""
Write-Host "      Total elementos eliminados: $eliminados" -ForegroundColor Cyan
Write-Host ""

# 4. Guardar cambios
Write-Host "[4/4] Guardando EDMX limpio..." -ForegroundColor Yellow
$edmx.Save($edmxPath)
Write-Host "      EDMX guardado correctamente" -ForegroundColor Green
Write-Host ""

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "LIMPIEZA COMPLETADA" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "PROXIMOS PASOS:" -ForegroundColor Yellow
Write-Host "1. Abrir Visual Studio" -ForegroundColor White
Write-Host "2. Abrir NestoEntities.edmx" -ForegroundColor White
Write-Host "3. Update Model from Database" -ForegroundColor White
Write-Host "4. Agregar tabla NotasEntrega" -ForegroundColor White
Write-Host "5. Renombrar entidad a NotaEntrega (singular)" -ForegroundColor White
Write-Host ""
Write-Host "Backup creado en:" -ForegroundColor Yellow
Write-Host $backupPath -ForegroundColor Cyan
Write-Host ""
