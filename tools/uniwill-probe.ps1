# Sonda do EC de notebooks Uniwill/Tongfang (Avell G1513 e parentes), pela mesma interface WMI que o app da Avell
# (MyAPP / MyFanBoost 1.4.4.0) usa: root\WMI, AcpiTest_MULong.GetSetULong.
#   leitura: Data = 2^40 + endereço         -> Return, valor no byte baixo
#   escrita: Data = (valor << 16) + endereço
# Sem -TestWrite a sonda só LÊ. Com -TestWrite ela escreve no registrador 0x751 (controle do fan) apenas os valores
# que o próprio app da Avell escreve (0x40 boost, 0x81..0x85 níveis) e devolve o valor original no fim, mesmo com
# Ctrl+C. Rode num PowerShell como Administrador; o relatório sai em um .txt ao lado do script.
param(
    [int]$Samples = 20,          # quantas leituras da faixa 0x400-0x4FF (uma a cada $IntervalSec)
    [int]$IntervalSec = 3,
    [switch]$TestWrite,
    [string]$Out = (Join-Path $PSScriptRoot ("uniwill-probe-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ".txt"))
)
$ErrorActionPreference = 'Stop'
$lines = New-Object System.Collections.Generic.List[string]
function Log([string]$s) { Write-Host $s; $lines.Add($s) }
function Save { [IO.File]::WriteAllLines($Out, $lines, (New-Object Text.UTF8Encoding $true)) }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
Log "# uniwill-probe $(Get-Date -Format s)  admin=$admin  testWrite=$TestWrite"
$cs = Get-CimInstance Win32_ComputerSystem; $bb = Get-CimInstance Win32_BaseBoard; $bios = Get-CimInstance Win32_BIOS
Log "Sistema: $($cs.Manufacturer) | $($cs.Model)"
Log "Placa:   $($bb.Manufacturer) | $($bb.Product) | $($bb.Version)"
Log "BIOS:    $($bios.Manufacturer) | $($bios.SMBIOSBIOSVersion) | $($bios.ReleaseDate)"
Log "CPU:     $((Get-CimInstance Win32_Processor | Select-Object -First 1).Name)"

Log ""
Log "## Classes WMI do fabricante em root\WMI"
$classes = Get-CimClass -Namespace root\WMI -ErrorAction SilentlyContinue |
    Where-Object { $_.CimClassName -match 'AcpiTest|Uniwill|MULong|ULong' }
foreach ($c in $classes) { Log ("  " + $c.CimClassName + "  métodos: " + (($c.CimClassMethods | ForEach-Object Name) -join ', ')) }
$inst = @(Get-CimInstance -Namespace root\WMI -ClassName AcpiTest_MULong -ErrorAction SilentlyContinue)
Log ("Instâncias de AcpiTest_MULong: " + (($inst | ForEach-Object InstanceName) -join ', '))
if ($inst.Count -eq 0) {
    Log "AcpiTest_MULong não existe: este notebook não expõe a interface do app da Avell. Traga este relatório."
    Save; exit 2
}
$ec = $inst | Where-Object { $_.InstanceName -eq 'ACPI\PNP0C14\1_1' } | Select-Object -First 1
if (-not $ec) { $ec = $inst[0] }
Log "Usando: $($ec.InstanceName)"

function Read-Ec([int]$addr) {
    $r = Invoke-CimMethod -InputObject $ec -MethodName GetSetULong -Arguments @{ Data = [uint64]([uint64]1 -shl 40) + [uint64]$addr }
    return [int]([uint64]$r.Return -band 0xFF)
}
function Write-Ec([int]$addr, [int]$value) {
    $r = Invoke-CimMethod -InputObject $ec -MethodName GetSetULong -Arguments @{ Data = ([uint64]$value -shl 16) + [uint64]$addr }
    return $r.Return
}
function Dump([int]$from, [int]$to) {
    $vals = for ($a = $from; $a -le $to; $a++) { Read-Ec $a }
    for ($row = 0; $row -lt $vals.Count; $row += 16) {
        $hex = ($vals[$row..([Math]::Min($row + 15, $vals.Count - 1))] | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
        Log ('  {0:X4}: {1}' -f ($from + $row), $hex)
    }
    return $vals
}
function ThermalZones {
    try {
        (Get-CimInstance -Namespace root\WMI -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop |
            ForEach-Object { '{0:N1}' -f ($_.CurrentTemperature / 10 - 273.15) }) -join ' / '
    } catch { 'n/d' }
}

# Registradores que o app da Avell usa, mais candidatos de RPM/temperatura/duty de outros Uniwill (a confirmar).
$named = [ordered]@{
    0x0751 = 'controle do fan (0 auto, 0x40 boost, 0x81-0x85 nível)'; 0x0765 = 'support byte 1'; 0x0766 = 'support byte 2'
    0x073C = 'support byte 3 (teclado)'; 0x0767 = 'trigger byte'; 0x0768 = 'status byte'; 0x075D = 'trigger byte 2'
    0x0476 = 'BIOSFuncReg'; 0x0490 = 'fonte de energia'; 0x04A2 = 'temp. bateria'; 0x04AB = 'carga bateria %'
    0x043E = 'candidato: temp. CPU'; 0x044F = 'candidato: temp. GPU'
    0x0464 = 'candidato: fan1 RPM hi'; 0x0465 = 'candidato: fan1 RPM lo'
    0x046C = 'candidato: fan2 RPM hi'; 0x046D = 'candidato: fan2 RPM lo'
    0x1804 = 'candidato: duty fan1'; 0x1809 = 'candidato: duty fan2'
}
# GetEnumerator: num [ordered], $named[<int>] seria a POSIÇÃO, não a chave.
function Named { foreach ($e in $named.GetEnumerator()) { Log ('  0x{0:X4} = 0x{1:X2} ({1,3})  {2}' -f $e.Key, (Read-Ec $e.Key), $e.Value) } }

try {
    $t = [Diagnostics.Stopwatch]::StartNew(); [void](Read-Ec 0x0751); $t.Stop()
    Log "Uma leitura leva $($t.ElapsedMilliseconds) ms."
    Log ""; Log "## Registradores conhecidos"; Named
    Log ""; Log "## Dump 0x0700-0x07FF"; [void](Dump 0x0700 0x07FF)
    Log ""; Log "## Dump 0x1800-0x181F"; [void](Dump 0x1800 0x181F)
    Save

    Log ""; Log "## Série: 0x0400-0x04FF a cada $IntervalSec s ($Samples leituras)."
    Log "   Deixe ocioso no começo e depois ponha carga (um jogo, ou o stress do Cinebench) para os fans subirem."
    $first = $null; $changed = @{}
    for ($i = 0; $i -lt $Samples; $i++) {
        Log ""; Log ("### leitura {0}  {1:HH:mm:ss}  zonas térmicas ACPI: {2}" -f ($i + 1), (Get-Date), (ThermalZones))
        $v = Dump 0x0400 0x04FF
        if ($first) { for ($k = 0; $k -lt $v.Count; $k++) { if ($v[$k] -ne $first[$k]) { $changed[0x400 + $k] = $true } } } else { $first = $v }
        Save
        Start-Sleep -Seconds $IntervalSec
    }
    Log ""; Log ("Endereços de 0x400-0x4FF que mudaram: " + (($changed.Keys | Sort-Object | ForEach-Object { '0x{0:X4}' -f $_ }) -join ' '))

    if ($TestWrite) {
        Log ""; Log "## Teste de escrita em 0x0751 (os valores do app da Avell)"
        $orig = Read-Ec 0x0751
        Log ('Valor original: 0x{0:X2}' -f $orig)
        try {
            foreach ($val in 0x40, 0x81, 0x83, 0x85, 0x00) {
                $ret = Write-Ec 0x0751 $val
                Log ('-> escrevi 0x{0:X2} (Return {1}); aguardando 15 s' -f $val, $ret)
                for ($s = 0; $s -lt 3; $s++) {
                    Start-Sleep -Seconds 5
                    Log ('   lido 0x0751=0x{0:X2}' -f (Read-Ec 0x0751))
                    Named
                }
                Save
            }
        } finally {
            [void](Write-Ec 0x0751 $orig)
            Log ('Devolvido 0x0751 = 0x{0:X2}; lido agora: 0x{1:X2}' -f $orig, (Read-Ec 0x0751))
            Save
        }
    }
} finally {
    Save
    Write-Host ""; Write-Host "Relatório: $Out"
}
