# Builds bin\FanPilot.exe with the .NET SDK (8 or newer). FanPilot still runs on the .NET Framework 4.7.2 that ships
# with Windows: the SDK is only needed to compile. NuGet packages are pinned by src\packages.lock.json.
# Gigabyte's DLLs are only referenced if GCC is installed (they are resolved at runtime from the GCC folder).
# -OutDir builds somewhere else (e.g. while bin\FanPilot.exe is running); data\ is never touched.
param([string]$OutDir)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$bin = if ($OutDir) { $OutDir } else { Join-Path $root 'bin' }

# Same lookup as Program.FindGccLibDir: the installers' uninstall entries locate the GCC client DLLs.
function Find-GccLib {
    $uninstall = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
    $candidates = @(
        @{ Key = 'MBEasyTune'; Sub = '' },
        @{ Key = 'GIGABYTE Control Center'; Sub = 'Lib\MBEasyTune' }
    )
    foreach ($hive in "HKLM:\$uninstall", "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall") {
        foreach ($c in $candidates) {
            $entry = Get-ItemProperty -Path (Join-Path $hive $c.Key) -ErrorAction SilentlyContinue
            if (-not $entry -or -not $entry.UninstallString) { continue }
            $dir = Join-Path (Split-Path $entry.UninstallString.Trim('"')) $c.Sub
            if (Test-Path (Join-Path $dir 'Gigabyte.Engine.EnvironmentControl.dll')) { return $dir }
        }
    }
    return 'C:\Program Files\GIGABYTE\Control Center\Lib\MBEasyTune'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Compilar o FanPilot exige o .NET SDK 8 ou mais novo: https://dotnet.microsoft.com/download'
}

# No trailing backslash: it would escape the closing quote of the argument.
$gcc = (Find-GccLib).TrimEnd('\')
if (Test-Path (Join-Path $gcc 'Gigabyte.Engine.EnvironmentControl.dll')) {
    Write-Host "GCC: $gcc"
} else {
    # Still a working FanPilot (LibreHardwareMonitor only); release builds are made where GCC is installed.
    Write-Warning "Gigabyte Control Center não encontrado: compilando sem o suporte a ele (só LibreHardwareMonitor)."
}

# Files deleted from wwwroot\ must not linger in the output.
Remove-Item -Recurse -Force (Join-Path $bin 'wwwroot') -ErrorAction SilentlyContinue
& dotnet build (Join-Path $root 'src\FanPilot.csproj') -c Release -o $bin --nologo -v:minimal "-p:GccLibDir=$gcc"
if ($LASTEXITCODE -ne 0) { throw "dotnet build falhou ($LASTEXITCODE)" }
Write-Host "OK -> $bin\FanPilot.exe"
