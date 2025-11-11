@echo off
echo Iniciando IIS Express con logging detallado...
echo.

set IISEXPRESS_PATH="%ProgramFiles%\IIS Express\iisexpress.exe"
if not exist %IISEXPRESS_PATH% set IISEXPRESS_PATH="%ProgramFiles(x86)%\IIS Express\iisexpress.exe"

set PROJECT_PATH=%~dp0NestoAPI
set CONFIG_PATH=%PROJECT_PATH%\.vs\config\applicationhost.config

echo Project Path: %PROJECT_PATH%
echo Config Path: %CONFIG_PATH%
echo.

%IISEXPRESS_PATH% /path:"%PROJECT_PATH%" /port:53364 /trace:error

pause
