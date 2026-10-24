# Allows phones on the local network to reach FanPilot. Run in an elevated PowerShell.
# Only the "Private" profile is opened: on public networks (café, hotel) the port stays closed.
param([int]$Port = 8787)
$name = "FanPilot (TCP $Port)"
Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Private | Out-Null
Write-Host "Regra '$name' criada (perfil Privado)."
Write-Host "Perfis de rede atuais:"
Get-NetConnectionProfile | Format-Table Name, InterfaceAlias, NetworkCategory -AutoSize
