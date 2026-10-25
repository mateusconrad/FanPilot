# Builds bin\FanPilot.exe with the .NET SDK (8 or newer). FanPilot still runs on the .NET Framework 4.7.2 that ships
# with Windows: the SDK is only needed to compile. NuGet packages are pinned by src\packages.lock.json.
# Gigabyte Control Center is not needed to build: its DLLs are loaded at runtime from the GCC install (GigabyteApi).
# -OutDir builds somewhere else (e.g. while bin\FanPilot.exe is running); data\ is never touched.
param([string]$OutDir)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$bin = if ($OutDir) { $OutDir } else { Join-Path $root 'bin' }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Compilar o FanPilot exige o .NET SDK 8 ou mais novo: https://dotnet.microsoft.com/download'
}

# Files deleted from wwwroot\ must not linger in the output.
Remove-Item -Recurse -Force (Join-Path $bin 'wwwroot') -ErrorAction SilentlyContinue
& dotnet build (Join-Path $root 'src\FanPilot.csproj') -c Release -o $bin --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build falhou ($LASTEXITCODE)" }
Write-Host "OK -> $bin\FanPilot.exe"
