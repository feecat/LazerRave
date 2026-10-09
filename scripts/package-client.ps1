[CmdletBinding()]
param(
    [string]$DesktopExecutable = '',
    [Parameter(Mandatory)][string]$EnginePackage,
    [Parameter(Mandatory)][string]$RuntimeSource,
    [Parameter(Mandatory)][string]$Destination,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$source = (Resolve-Path -LiteralPath $RuntimeSource).Path.TrimEnd('\')
$destinationPath = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
if ($destinationPath.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The desktop package must be outside its resource source directory.'
}
$required = @((Join-Path $EnginePackage 'OpenLR2_x64.exe'), (Join-Path $EnginePackage 'fmod.dll'), (Join-Path $source 'LR2files/Config/config.xml'))
if (!$PrepareOnly) {
    if (!$DesktopExecutable) { throw 'DesktopExecutable is required when copying a published frontend.' }
    $required += $DesktopExecutable
}
foreach ($file in $required) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required package input is missing: $file" }
}
if (!(Test-Path -LiteralPath (Join-Path $source 'LR2files/Theme') -PathType Container)) {
    throw 'The resource source must include LR2files/Theme.'
}
$active = @(Get-Process -Name 'LazerRave', 'OpenLR2*' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and ((Split-Path $_.Path -Parent) -eq $destinationPath -or
        ($_.ProcessName -like 'OpenLR2*' -and (Split-Path $_.Path -Parent) -eq $source))
})
if ($active.Count) { throw 'Close the game before copying its runtime, or choose an unused package destination.' }
if ($source -ne $destinationPath) {
    $pending = @(Get-ChildItem -LiteralPath (Join-Path $source 'LR2files/Database') -File -Recurse -ErrorAction SilentlyContinue | Where-Object {
        $_.Length -gt 0 -and ($_.Name -like '*.db-journal' -or $_.Name -like '*.db-wal')
    })
    if ($pending.Count) { throw 'The resource database has pending SQLite journal data. Close the game and finish database recovery before copying it.' }
}
New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null
if ($source -ne $destinationPath) {
    foreach ($folder in @('LR2files', 'BMS')) {
        $inputFolder = Join-Path $source $folder
        if ($folder -eq 'BMS' -and !(Test-Path -LiteralPath $inputFolder)) { $inputFolder = Join-Path $repository 'res/library/BMS' }
        if (!(Test-Path -LiteralPath $inputFolder -PathType Container)) { continue }
        $outputFolder = Join-Path $destinationPath $folder
        # Seed missing resources; existing player data and customized skins take precedence.
        & robocopy.exe $inputFolder $outputFolder /E /XJ /R:1 /W:1 /XC /XN /XO /NFL /NDL /NJH /NJS /NP /XF '*.db-journal' '*.db-wal' '*.db-shm' '*.log'
        if ($LASTEXITCODE -ge 8) { throw "Resource copy failed: $folder ($LASTEXITCODE)" }
    }
}
if (!$PrepareOnly) { Copy-Item -LiteralPath $DesktopExecutable -Destination (Join-Path $destinationPath 'LazerRave.exe') -Force }
Copy-Item -LiteralPath (Join-Path $EnginePackage 'OpenLR2_x64.exe') -Destination $destinationPath -Force
Get-ChildItem -LiteralPath $EnginePackage -Filter '*.dll' -File |
    Where-Object { $_.Name -notlike 'ExampleIR*.dll' } |
    Copy-Item -Destination $destinationPath -Force
Copy-Item -LiteralPath (Join-Path $repository 'scripts/set-window.ps1') -Destination $destinationPath -Force
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination $destinationPath -Force
if ($PrepareOnly) { Write-Host "Prepared runtime resources: $destinationPath" }
else { Write-Host "Complete desktop package: $destinationPath" }
exit 0
