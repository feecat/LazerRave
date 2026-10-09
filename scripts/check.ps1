[CmdletBinding()]
param(
    [switch]$Build,
    [switch]$Tests
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Push-Location $workspace
try {
    & python.exe -B scripts/check_docs.py
    if ($LASTEXITCODE -ne 0) { throw 'Documentation validation failed.' }
    if ($Tests) {
        & python.exe -B -m unittest discover -s scripts/tests -v
        if ($LASTEXITCODE -ne 0) { throw 'Script tests failed.' }
    }
    if ($Build) {
        & (Join-Path $workspace 'build.cmd') -CompileOnly
        if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
    }
    Write-Host 'All requested checks passed.'
    $global:LASTEXITCODE = 0
} finally { Pop-Location }
