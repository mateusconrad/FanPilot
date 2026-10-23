# Starts FanPilot elevated at logon (scheduled task with highest privileges, so no UAC prompt each time).
# Run in an elevated PowerShell. Use -Remove to delete the task.
param([switch]$Remove)
$task = 'FanPilot'
if ($Remove) { Unregister-ScheduledTask -TaskName $task -Confirm:$false; Write-Host 'Tarefa removida.'; return }
$exe = Join-Path (Split-Path $PSScriptRoot) 'bin\FanPilot.exe'
if (-not (Test-Path $exe)) { throw "Compile primeiro (build.ps1): $exe não existe" }
$action = New-ScheduledTaskAction -Execute $exe -Argument '--no-browser' -WorkingDirectory (Split-Path $exe)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Host "Tarefa '$task' registrada: FanPilot inicia elevado no logon."
