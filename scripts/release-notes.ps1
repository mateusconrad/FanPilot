# Prints the CHANGELOG.md section of a version: the notes of its GitHub release (.github/workflows/release.yml).
# -AllowUnreleased: with no section for that version yet, takes "Não lançado" (the workflow's dry run).
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutFile,
    [switch]$AllowUnreleased
)
$ErrorActionPreference = 'Stop'
$lines = [IO.File]::ReadAllLines((Join-Path (Split-Path $PSScriptRoot) 'CHANGELOG.md'), [Text.Encoding]::UTF8)

# The lines between "## [<title>]..." and the next "## [" heading; $null if there is no such section.
function Section([string]$title) {
    $found = $false
    $inside = $false
    $body = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line -match '^## \[(.+?)\]') {
            if ($inside) { break }
            $inside = $Matches[1] -eq $title
            if ($inside) { $found = $true }
            continue
        }
        if ($inside) { $body.Add($line) }
    }
    if ($found) { return ($body -join "`n").Trim() }
    return $null
}

$notes = Section $Version
if ($null -eq $notes -and $AllowUnreleased) { $notes = Section 'Não lançado' }
if ([string]::IsNullOrWhiteSpace($notes)) {
    throw "O CHANGELOG.md não tem a seção [$Version]: renomeie 'Não lançado' para '[$Version] - <data>' antes de criar a tag."
}
if ($OutFile) {
    $path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
    [IO.File]::WriteAllText($path, $notes + "`n", (New-Object Text.UTF8Encoding $false))
} else {
    $notes
}
