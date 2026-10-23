# Builds bin\FanPilot.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = Join-Path $root 'bin'

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

$gcc = Find-GccLib
if (-not (Test-Path $gcc)) { throw "Gigabyte Control Center não encontrado (procurado em $gcc)" }
Write-Host "GCC: $gcc"
New-Item -ItemType Directory -Force $bin | Out-Null

$refs = @(
    "$gcc\Gigabyte.Engine.dll",
    "$gcc\Gigabyte.Engine.EnvironmentControl.dll",
    "$gcc\Gigabyte.EnvironmentControl.Common.dll",
    'System.Web.Extensions.dll'
) | ForEach-Object { "/r:$_" }

& $csc /nologo /target:exe /platform:x64 /optimize+ /warn:4 `
    "/win32manifest:$root\app.manifest" "/out:$bin\FanPilot.exe" @refs (Get-ChildItem "$root\src\*.cs").FullName
if ($LASTEXITCODE -ne 0) { throw "csc falhou ($LASTEXITCODE)" }

# Gigabyte DLLs are NOT copied: they are resolved at runtime from the GCC install (see Program.cs).
Remove-Item -Recurse -Force "$bin\wwwroot" -ErrorAction SilentlyContinue
Copy-Item -Recurse "$root\wwwroot" "$bin\wwwroot"
Write-Host "OK -> $bin\FanPilot.exe"
