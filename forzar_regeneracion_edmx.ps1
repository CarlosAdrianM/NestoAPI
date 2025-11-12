# Script para forzar regeneracion del EDMX
# Toca el archivo .tt para que Visual Studio regenere los archivos

$ttPath = "C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Models\NestoEntities.tt"
$ttContextPath = "C:\Users\Carlos\source\repos\NestoAPI\NestoAPI\Models\NestoEntities.Context.tt"

Write-Host "Forzando regeneracion del EDMX..." -ForegroundColor Cyan
Write-Host ""

# Tocar los archivos .tt (actualizar su timestamp)
(Get-Item $ttPath).LastWriteTime = Get-Date
Write-Host "[OK] Actualizado: NestoEntities.tt" -ForegroundColor Green

(Get-Item $ttContextPath).LastWriteTime = Get-Date
Write-Host "[OK] Actualizado: NestoEntities.Context.tt" -ForegroundColor Green

Write-Host ""
Write-Host "Templates actualizados." -ForegroundColor Green
Write-Host ""
Write-Host "PROXIMOS PASOS:" -ForegroundColor Yellow
Write-Host "1. Abrir Visual Studio" -ForegroundColor White
Write-Host "2. Abrir NestoEntities.edmx" -ForegroundColor White
Write-Host "3. Clic derecho en NestoEntities.tt -> Run Custom Tool" -ForegroundColor White
Write-Host "4. Clic derecho en NestoEntities.Context.tt -> Run Custom Tool" -ForegroundColor White
Write-Host "5. Rebuild Solution" -ForegroundColor White
Write-Host ""
