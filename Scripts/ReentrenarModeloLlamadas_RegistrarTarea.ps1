<#
    NestoAPI#619: registra en el Programador de tareas de ESTA máquina la tarea «NestoAPI - Reentrenar modelo de
    llamadas», que lanza ReentrenarModeloLlamadas.ps1 el primer domingo de cada mes a las 02:30 con el usuario actual.

    La tarea necesita las credenciales de Windows del usuario para pedir el windows-token del aviso en la campana
    (la API está en otra máquina), así que por defecto se guarda con contraseña («ejecutar tanto si el usuario ha
    iniciado sesión como si no»): el script la pide con una ventana de credenciales y no la guarda en ningún sitio más.
    Si cambia la contraseña de Windows, hay que volver a ejecutar este script.
    Con -SoloConSesionIniciada no pide contraseña, pero solo se ejecuta si el usuario tiene la sesión iniciada.

    Si a las 02:30 la máquina está apagada, NO se lanza al encenderla (sería en horario y carga la BD de producción):
    se espera al mes siguiente o se lanza a mano fuera de horario.

    Uso (en una consola de PowerShell normal; no necesita administrador para una tarea del propio usuario):
      .\ReentrenarModeloLlamadas_RegistrarTarea.ps1
      .\ReentrenarModeloLlamadas_RegistrarTarea.ps1 -SoloConSesionIniciada
    Comprobar / lanzar ya / quitar:
      Get-ScheduledTask -TaskName "NestoAPI - Reentrenar modelo de llamadas" | Get-ScheduledTaskInfo
      Start-ScheduledTask -TaskName "NestoAPI - Reentrenar modelo de llamadas"     # solo fuera de horario
      Unregister-ScheduledTask -TaskName "NestoAPI - Reentrenar modelo de llamadas"
#>
param(
    [string]$Nombre = "NestoAPI - Reentrenar modelo de llamadas",
    [string]$Hora = "02:30",
    [switch]$SoloConSesionIniciada
)

$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "ReentrenarModeloLlamadas.ps1"
if (-not (Test-Path -LiteralPath $script)) { throw "No se encuentra $script" }
$powershell = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if (-not $powershell) { $powershell = (Get-Command powershell).Source }
$usuario = "$env:USERDOMAIN\$env:USERNAME"
$inicio = (Get-Date).ToString("yyyy-MM-dd") + "T" + $Hora + ":00"
$tipoInicio = if ($SoloConSesionIniciada) { "InteractiveToken" } else { "Password" }
$meses = (1..12 | ForEach-Object { "<" + [Globalization.CultureInfo]::InvariantCulture.DateTimeFormat.GetMonthName($_) + " />" }) -join ""
$esc = [System.Security.SecurityElement]

# El trigger «primer domingo de cada mes» no se puede crear con New-ScheduledTaskTrigger: va en XML.
$xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>$($esc::Escape($usuario))</Author>
    <Description>NestoAPI#619: reentrena el modelo de llamadas de Rapports (ModeloLlamadaPedido --completo --promover). Si pasa la puerta de calidad, commit local del zip en NestoAPI (sin push). Log en C:\Users\Carlos\ModelosML\logs y aviso en la campana de Nesto.</Description>
  </RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>$inicio</StartBoundary>
      <Enabled>true</Enabled>
      <ScheduleByMonthDayOfWeek>
        <Weeks><Week>1</Week></Weeks>
        <DaysOfWeek><Sunday /></DaysOfWeek>
        <Months>$meses</Months>
      </ScheduleByMonthDayOfWeek>
    </CalendarTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$($esc::Escape($usuario))</UserId>
      <LogonType>$tipoInicio</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
    <ExecutionTimeLimit>PT4H</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$($esc::Escape($powershell))</Command>
      <Arguments>-NoProfile -ExecutionPolicy Bypass -File "$($esc::Escape($script))"</Arguments>
      <WorkingDirectory>$($esc::Escape($PSScriptRoot))</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"@

if ($SoloConSesionIniciada) {
    Register-ScheduledTask -TaskName $Nombre -Xml $xml -Force | Out-Null
} else {
    $credencial = Get-Credential -UserName $usuario -Message "Contraseña de Windows de $usuario para la tarea «$Nombre»"
    if (-not $credencial) { throw "Cancelado: sin credenciales no se registra la tarea." }
    Register-ScheduledTask -TaskName $Nombre -Xml $xml -User $credencial.UserName `
        -Password $credencial.GetNetworkCredential().Password -Force | Out-Null
}

$info = Get-ScheduledTask -TaskName $Nombre | Get-ScheduledTaskInfo
Write-Host "Tarea «$Nombre» registrada para $usuario ($tipoInicio)." -ForegroundColor Green
Write-Host "  Acción: $powershell -File `"$script`""
Write-Host "  Próxima ejecución: $($info.NextRunTime)"
