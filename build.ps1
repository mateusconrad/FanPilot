# Builds bin\FanPilot.exe with the .NET SDK 10 (global.json). The Windows FanPilot still runs on the .NET Framework 4.7.2
# that ships with Windows: the SDK is only needed to compile. NuGet packages are pinned by src\packages.lock.json. Only
# the Windows build (net472): the Linux one (net10.0, item 28) is published on its own.
# Gigabyte Control Center is not needed to build: its DLLs are loaded at runtime from the GCC install (GigabyteApi).
# -OutDir builds somewhere else (e.g. while bin\FanPilot.exe is running); data\ is never touched.
param([string]$OutDir)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$bin = if ($OutDir) { $OutDir } else { Join-Path $root 'bin' }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Compilar o FanPilot exige o .NET SDK 10: https://dotnet.microsoft.com/download/dotnet/10.0'
}

# Files deleted from wwwroot\ must not linger in the output.
Remove-Item -Recurse -Force (Join-Path $bin 'wwwroot') -ErrorAction SilentlyContinue
& dotnet build (Join-Path $root 'src\FanPilot.csproj') -c Release -f net472 -o $bin --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build falhou ($LASTEXITCODE)" }
# The tray icon (item 14b), next to the exe.
& dotnet build (Join-Path $root 'tray\FanPilotTray.csproj') -c Release -o $bin --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build da bandeja falhou ($LASTEXITCODE)" }
Write-Host "OK -> $bin\FanPilot.exe, $bin\FanPilotTray.exe"
