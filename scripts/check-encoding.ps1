# Fails when a .ps1 with any non-ASCII character has no UTF-8 BOM: Windows PowerShell 5.1 reads such a file as ANSI,
# which garbles every accented message and can break the script itself. The CI runs it on every push.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$files = & git -C $root ls-files --cached --others --exclude-standard -- '*.ps1'
if ($LASTEXITCODE -ne 0) { throw "git ls-files falhou ($LASTEXITCODE)" }

$bad = @()
foreach ($rel in $files) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $root $rel))
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    if (-not $bom -and [Array]::Exists($bytes, [Predicate[byte]] { param($b) $b -gt 0x7F })) { $bad += $rel }
}
if ($bad) {
    foreach ($rel in $bad) { Write-Host "Sem BOM e com caractere não-ASCII: $rel" }
    Write-Host 'Grave esses arquivos como UTF-8 com BOM: o Windows PowerShell 5.1 os leria como ANSI.'
    exit 1
}
Write-Host ("OK: " + @($files).Count + " scripts .ps1; os que têm caractere não-ASCII estão com BOM.")
