# Builds a release zip in dist\: FanPilot.exe + DLLs + wwwroot + scripts + licenses, without any data\.
# Any machine will do: Gigabyte Control Center is loaded at runtime, so the zip serves Gigabyte and other boards.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd'
$rev = (& git -C $root rev-parse --short HEAD 2>$null)
$name = 'FanPilot-' + $stamp + $(if ($rev) { '-' + $rev } else { '' })
$work = Join-Path ([IO.Path]::GetTempPath()) $name
$dist = Join-Path $root 'dist'

Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
& (Join-Path $root 'build.ps1') -OutDir $work
if (Test-Path (Join-Path $work 'data')) { Remove-Item -Recurse -Force (Join-Path $work 'data') }
New-Item -ItemType Directory -Force (Join-Path $work 'scripts') | Out-Null
Copy-Item (Join-Path $root 'scripts\firewall.ps1'), (Join-Path $root 'scripts\autostart.ps1') (Join-Path $work 'scripts')
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md'), (Join-Path $root 'README.md') $work

New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "$name.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $work '*') -DestinationPath $zip
Remove-Item -Recurse -Force $work
Write-Host "OK -> $zip"
