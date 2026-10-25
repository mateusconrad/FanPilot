# Builds a release zip in dist\: FanPilot.exe + DLLs + wwwroot + scripts + licenses, without any data\.
# Any machine will do: Gigabyte Control Center is loaded at runtime, so the zip serves Gigabyte and other boards.
# The zip is named after the version in Directory.Build.props (dist\FanPilot-0.1.0-win-x64.zip); the exe inside also
# carries the commit it was built from (FanPilot.exe --version).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) 'FanPilot-package'
$dist = Join-Path $root 'dist'

Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
& (Join-Path $root 'build.ps1') -OutDir $work
if (Test-Path (Join-Path $work 'data')) { Remove-Item -Recurse -Force (Join-Path $work 'data') }
New-Item -ItemType Directory -Force (Join-Path $work 'scripts') | Out-Null
Copy-Item (Join-Path $root 'scripts\firewall.ps1'), (Join-Path $root 'scripts\autostart.ps1') (Join-Path $work 'scripts')
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md'), (Join-Path $root 'README.md'), (Join-Path $root 'README.pt-BR.md'), (Join-Path $root 'CHANGELOG.md') $work

# ProductVersion is "0.1.0+<commit>": the zip takes the version alone.
$version = ((Get-Item (Join-Path $work 'FanPilot.exe')).VersionInfo.ProductVersion -split '\+')[0]
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "FanPilot-$version-win-x64.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
# Not Compress-Archive: Windows PowerShell 5.1's writes "wwwroot\app.js", and the zip format wants forward slashes.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$base = (Get-Item $work).FullName.TrimEnd('\')
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($file in Get-ChildItem $base -Recurse -File) {
        $entry = $file.FullName.Substring($base.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, 'Optimal')
    }
} finally { $archive.Dispose() }
Remove-Item -Recurse -Force $work
Write-Host "OK -> $zip"
