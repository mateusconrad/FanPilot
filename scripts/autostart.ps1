# Starts FanPilot with the PC, without any window (item 30): registers it as a Windows service (LocalSystem, before
# logon, restarted if it dies) with its data in %ProgramData%\FanPilot, brings the portable data\ folder there the
# first time, and puts "FanPilot" in the Start menu, which opens its terminal screen (FanPilot.exe --tui) without UAC.
# The tray icon (FanPilotTray.exe, item 14b) starts at every logon, for every user, and opens that screen on a click.
# Replaces the scheduled task the older version of this script created. Run in an elevated PowerShell, from the
# repository (bin\FanPilot.exe) or the release zip (FanPilot.exe next to scripts\); the service runs the exe from there,
# so moving the folder means running this again. -Remove undoes it: the service, the tray icon, the shortcut and the old
# task go, the data stays.
param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$name = 'FanPilot'
$data = Join-Path $env:ProgramData 'FanPilot'
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'FanPilot.lnk'
$run = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$trayName = 'FanPilotTray'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Rode num PowerShell como Administrador.' }

function Remove-FanPilotService {
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        Stop-Service -Name $name -Force -ErrorAction SilentlyContinue
        sc.exe delete $name | Out-Null
        # The Service Control Manager deletes it once nothing holds it open.
        for ($i = 0; $i -lt 50 -and (Get-Service -Name $name -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 200 }
    }
}

# The task of the older autostart.ps1: it opened FanPilot.exe in a window at every logon.
if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $name -Confirm:$false
    Write-Host 'Tarefa agendada antiga removida (ela abria a janela do FanPilot a cada logon).'
}

if ($Remove) {
    Remove-FanPilotService
    Remove-ItemProperty -Path $run -Name $trayName -ErrorAction SilentlyContinue
    Stop-Process -Name $trayName -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $shortcut -ErrorAction SilentlyContinue
    Write-Host "FanPilot removido da partida automática. Os dados ficam em $data."
    return
}

# bin\FanPilot.exe in the repository, FanPilot.exe next to scripts\ in a release zip.
$parent = Split-Path $PSScriptRoot
$exe = @((Join-Path $parent 'bin\FanPilot.exe'), (Join-Path $parent 'FanPilot.exe')) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) { throw 'FanPilot.exe não encontrado (compile com build.ps1 ou extraia o zip da release).' }
$exe = (Resolve-Path $exe).Path
$tray = Join-Path (Split-Path $exe) 'FanPilotTray.exe'
if (-not (Test-Path $tray)) { throw "FanPilotTray.exe não encontrado ao lado de $exe (compile com build.ps1)." }

# An update: the service goes (handing the fans back) and comes back with this exe.
Remove-FanPilotService

# The portable folder's curves, pairings and names, once: only when the service's folder has nothing yet.
$portable = Join-Path (Split-Path $exe) 'data'
if ((Test-Path (Join-Path $portable 'machine.json')) -and -not (Test-Path (Join-Path $data 'machine.json'))) {
    & $exe --import-data $portable --data $data
    if ($LASTEXITCODE -ne 0) { throw "A importação de $portable falhou ($LASTEXITCODE): feche o FanPilot aberto e rode de novo." }
    Write-Host "Dados de $portable copiados para $data."
}

New-Service -Name $name -DisplayName 'FanPilot' -StartupType Automatic `
    -Description 'Temperaturas e fans da placa-mãe pelo celular (painel em http://localhost:8787).' `
    -BinaryPathName ('"' + $exe + '" --service --data "' + $data + '"') | Out-Null
sc.exe failure $name reset= 86400 actions= restart/1000/restart/5000/restart/60000 | Out-Null
Start-Service -Name $name

# The port of the service's config.json (8787 when it has none): the shortcut passes it, as a standard user can't
# read that folder.
$port = 8787
$config = Join-Path $data 'config.json'
if (Test-Path $config) {
    try { $p = (Get-Content -LiteralPath $config -Raw | ConvertFrom-Json).port; if ($p) { $port = [int]$p } } catch { }
}

# The terminal screen without UAC: RunAsInvoker keeps the exe's manifest from asking for Administrator, which --tui
# doesn't need. cmd runs it in the default terminal and closes with it.
$link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
$link.TargetPath = $env:ComSpec
$link.Arguments = '/c "set __COMPAT_LAYER=RunAsInvoker&& "' + $exe + '" --tui --port ' + $port + '"'
$link.WorkingDirectory = Split-Path $exe
$link.IconLocation = "$exe,0"
$link.Description = 'Temperaturas e coolers do FanPilot no terminal'
$link.Save()

# The tray icon at every logon, for every user, with the port for the same reason as the shortcut. Its manifest asks
# for no elevation.
Set-ItemProperty -Path $run -Name $trayName -Value ('"' + $tray + '" --port ' + $port)

# And now, for whoever runs this: started from here it would inherit Administrator, so a one-off task starts it as the
# signed-in person (without Administrator), and goes once the icon is up. A tray already open stays (one per session).
$trayStarted = [bool](Get-Process -Name $trayName -ErrorAction SilentlyContinue)
if (-not $trayStarted) {
    $once = 'FanPilotTray-agora'
    try {
        $who = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        Register-ScheduledTask -TaskName $once -Force `
            -Action (New-ScheduledTaskAction -Execute $tray -Argument "--port $port") `
            -Principal (New-ScheduledTaskPrincipal -UserId $who -LogonType Interactive -RunLevel Limited) `
            -Settings (New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries) | Out-Null
        Start-ScheduledTask -TaskName $once
        for ($i = 0; $i -lt 25 -and -not $trayStarted; $i++) { Start-Sleep -Milliseconds 200; $trayStarted = [bool](Get-Process -Name $trayName -ErrorAction SilentlyContinue) }
    } catch { }
    Unregister-ScheduledTask -TaskName $once -Confirm:$false -ErrorAction SilentlyContinue
}

Write-Host "FanPilot instalado como serviço: sobe com o PC, sem janela, e volta sozinho se fechar."
Write-Host "Painel: http://localhost:$port  ·  No menu Iniciar, 'FanPilot' abre a tela do terminal."
if ($trayStarted) { Write-Host "Ícone na bandeja: um clique abre a tela do terminal; ele volta a cada logon." }
else { Write-Host "Ícone na bandeja: aparece no próximo logon (um clique abre a tela do terminal)." }
