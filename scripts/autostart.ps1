# Starts FanPilot elevated at logon (scheduled task with highest privileges, so no UAC prompt each time).
# Run in an elevated PowerShell. Use -Remove to delete the task.
param([switch]$Remove)
$task = 'FanPilot'
if ($Remove) { Unregister-ScheduledTask -TaskName $task -Confirm:$false; Write-Host 'Tarefa removida.'; return }
# bin\FanPilot.exe in the repository, FanPilot.exe next to scripts\ in a release zip.
$parent = Split-Path $PSScriptRoot
$exe = @((Join-Path $parent 'bin\FanPilot.exe'), (Join-Path $parent 'FanPilot.exe')) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) { throw "FanPilot.exe não encontrado (compile com build.ps1 ou extraia o zip da release)" }
$action = New-ScheduledTaskAction -Execute $exe -Argument '--no-browser' -WorkingDirectory (Split-Path $exe)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Host "Tarefa '$task' registrada: FanPilot inicia elevado no logon."
