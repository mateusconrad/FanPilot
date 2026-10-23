# Builds bin\FanPilot.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
# LibreHardwareMonitor and its dependencies are downloaded from nuget.org at pinned versions, checked against the
# SHA-256 below and cached in lib\ (not committed). Gigabyte's DLLs are only referenced if GCC is installed.
# -OutDir builds somewhere else (e.g. while bin\FanPilot.exe is running); data\ is never touched.
param([string]$OutDir)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = if ($OutDir) { $OutDir } else { Join-Path $root 'bin' }
$lib = Join-Path $root 'lib'

# id, version, SHA-256 of the .nupkg, DLL inside the package. Bump together and re-run the build.
$packages = @(
    @('LibreHardwareMonitorLib', '0.9.6', '60DB5E0D9135F4658B385CAE139D3B9EAEAC0A8E867D322D20B5DBE4C4039197', 'runtimes/win-x64/lib/net472/LibreHardwareMonitorLib.dll'),
    @('HidSharp', '2.6.4', 'CF55D4B4D7B42DC1D8496B7B592BA00D1ECD48A70BC07BD8DABD3E9976D108E7', 'lib/net35/HidSharp.dll'),
    @('DiskInfoToolkit', '1.1.2', 'A997F0B3701E20B2AB9296E07EE4F74F877E75015277FB6BD314ADE1E2C4DB13', 'lib/net472/DiskInfoToolkit.dll'),
    @('RAMSPDToolkit-NDD', '1.4.2', '57C893824A68C71538055F242586616D4205A671C300897C21B2A6F88270CE64', 'lib/net472/RAMSPDToolkit-NDD.dll'),
    @('BlackSharp.Core', '1.0.7', '4F713FE18EE7C85A7C49F28309ADAA815714333326AA81FD4D4A81619ECAA0AB', 'lib/net472/BlackSharp.Core.dll'),
    @('System.Memory', '4.6.3', '26078AEB758C9AE985E8BF851F973026061DA6A5EB4837204D0C2D2204C72955', 'lib/net462/System.Memory.dll'),
    @('System.Buffers', '4.6.1', 'B00451E91D016FBEC091AD1E361F3A7015E1D91D4047F7E48A74455B2A673D79', 'lib/net462/System.Buffers.dll'),
    @('System.Numerics.Vectors', '4.6.1', '2BC500A86DCB02F2032D6D877F9E2D6E9E4A79080E57239B4198679D4031F2C7', 'lib/net462/System.Numerics.Vectors.dll'),
    @('System.Runtime.CompilerServices.Unsafe', '6.1.2', '5F6A7F53AF3465F92BEB6DA873EBE0E496206C313313B98BADEE4355A6B25937', 'lib/net462/System.Runtime.CompilerServices.Unsafe.dll'),
    @('System.Threading.AccessControl', '10.0.3', '9F4F28238E13C6CC47B789A4A1D5647997747594DFCFD45C1526F9B671CDE073', 'lib/net462/System.Threading.AccessControl.dll'),
    @('System.Security.AccessControl', '6.0.0', 'A8EC961016CDAF7123C92F9EB451BCFF331BB8F8C0F8EF9D8BBD7B24FF42C728', 'lib/net461/System.Security.AccessControl.dll'),
    @('System.Security.Principal.Windows', '5.0.0', '081390C25F6F78592B28ADA853C24514488A221FE9F9A24EFAAF5373643FF3D6', 'lib/net461/System.Security.Principal.Windows.dll')
)

function Get-Package($id, $version, $sha, $inner) {
    $dll = Join-Path $lib (Split-Path $inner -Leaf)
    if (Test-Path $dll) { return $dll }
    New-Item -ItemType Directory -Force $lib | Out-Null
    $pkg = Join-Path $lib "$id.$version.nupkg"
    $lower = $id.ToLowerInvariant()
    Write-Host "Baixando $id $version"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing "https://api.nuget.org/v3-flatcontainer/$lower/$version/$lower.$version.nupkg" -OutFile $pkg
    $actual = (Get-FileHash -Algorithm SHA256 $pkg).Hash
    if ($actual -ne $sha) { Remove-Item $pkg; throw "SHA-256 de $id $version não confere (esperado $sha, veio $actual)" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($pkg)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq $inner }
        if (-not $entry) { throw "$inner não existe em $id $version" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dll, $true)
    } finally { $zip.Dispose() }
    Remove-Item $pkg
    return $dll
}

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

$dlls = $packages | ForEach-Object { Get-Package $_[0] $_[1] $_[2] $_[3] }
New-Item -ItemType Directory -Force $bin | Out-Null

$sources = (Get-ChildItem "$root\src\*.cs").FullName
$refs = @('System.Web.Extensions.dll', (Join-Path $lib 'LibreHardwareMonitorLib.dll'))
$defines = @()
$gcc = Find-GccLib
if (Test-Path $gcc) {
    Write-Host "GCC: $gcc"
    $refs += "$gcc\Gigabyte.Engine.dll", "$gcc\Gigabyte.Engine.EnvironmentControl.dll", "$gcc\Gigabyte.EnvironmentControl.Common.dll"
} else {
    # Still a working FanPilot (LibreHardwareMonitor only); release builds are made where GCC is installed.
    Write-Warning "Gigabyte Control Center não encontrado: compilando sem o suporte a ele (só LibreHardwareMonitor)."
    $sources = $sources | Where-Object { (Split-Path $_ -Leaf) -ne 'GigabyteBridge.cs' }
    $defines += '/define:NO_GIGABYTE'
}

& $csc /nologo /target:exe /platform:x64 /optimize+ /warn:4 @defines `
    "/win32manifest:$root\app.manifest" "/win32icon:$root\assets\brand\fanpilot.ico" "/out:$bin\FanPilot.exe" `
    ($refs | ForEach-Object { "/r:$_" }) $sources
if ($LASTEXITCODE -ne 0) { throw "csc falhou ($LASTEXITCODE)" }

# LibreHardwareMonitor's dependencies were built against slightly different versions of each other; redirect every
# shipped assembly to the copy next to the exe, as NuGet would in a project build.
$redirects = foreach ($d in $dlls) {
    Copy-Item $d $bin -Force
    $n = [Reflection.AssemblyName]::GetAssemblyName($d)
    $token = ($n.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') }) -join ''
    if (-not $token) { continue }
    "      <dependentAssembly><assemblyIdentity name=`"$($n.Name)`" publicKeyToken=`"$token`" culture=`"neutral`"/>" +
    "<bindingRedirect oldVersion=`"0.0.0.0-$($n.Version)`" newVersion=`"$($n.Version)`"/></dependentAssembly>"
}
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.7.2"/></startup>
  <runtime>
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
$($redirects -join "`r`n")
    </assemblyBinding>
  </runtime>
</configuration>
"@ | Set-Content -Encoding UTF8 "$bin\FanPilot.exe.config"

# Gigabyte DLLs are NOT copied: they are resolved at runtime from the GCC install (see Program.cs).
Remove-Item -Recurse -Force "$bin\wwwroot" -ErrorAction SilentlyContinue
Copy-Item -Recurse "$root\wwwroot" "$bin\wwwroot"
Write-Host "OK -> $bin\FanPilot.exe"
