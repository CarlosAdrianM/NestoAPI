@echo off
echo ============================================
echo Fix IIS Express - HTTP.sys Configuration
echo ============================================
echo.
echo Este script requiere privilegios de Administrador
echo.

REM Verificar si se ejecuta como administrador
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo ERROR: Este script debe ejecutarse como Administrador
    echo.
    echo Haz click derecho en el archivo y selecciona "Ejecutar como administrador"
    echo.
    pause
    exit /b 1
)

echo [1/5] Deteniendo IIS Express...
taskkill /F /IM iisexpress.exe 2>nul
taskkill /F /IM iisexpresstray.exe 2>nul
timeout /t 2 >nul

echo [2/5] Limpiando reservas de URL para puerto 53364...
netsh http delete urlacl url=http://localhost:53364/ 2>nul
netsh http delete urlacl url=http://+:53364/ 2>nul
netsh http delete urlacl url=http://*:53364/ 2>nul

echo [3/5] Agregando nueva reserva de URL...
netsh http add urlacl url=http://localhost:53364/ user=everyone

echo [4/5] Limpiando cache de configuración de IIS Express...
set VS_CONFIG_PATH=%USERPROFILE%\Documents\IISExpress\config
if exist "%VS_CONFIG_PATH%\applicationhost.config" (
    echo Encontrado: %VS_CONFIG_PATH%\applicationhost.config
    del /F /Q "%VS_CONFIG_PATH%\applicationhost.config.bak" 2>nul
    copy "%VS_CONFIG_PATH%\applicationhost.config" "%VS_CONFIG_PATH%\applicationhost.config.bak" >nul
    echo Backup creado
)

set VS_HIDDEN_CONFIG=%~dp0NestoAPI\.vs\config
if exist "%VS_HIDDEN_CONFIG%" (
    echo Limpiando: %VS_HIDDEN_CONFIG%
    rd /S /Q "%VS_HIDDEN_CONFIG%" 2>nul
)

echo [5/5] Limpiando archivos temporales de Visual Studio...
set VS_TEMP=%~dp0NestoAPI\.vs
if exist "%VS_TEMP%\NestoAPI\v17" (
    rd /S /Q "%VS_TEMP%\NestoAPI\v17" 2>nul
)

echo.
echo ============================================
echo COMPLETADO
echo ============================================
echo.
echo Ahora:
echo 1. Cierra Visual Studio completamente
echo 2. Abre Visual Studio de nuevo
echo 3. Abre el proyecto NestoAPI
echo 4. Presiona F5 para ejecutar
echo.
pause
