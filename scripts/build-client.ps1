[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [Alias("Check")][switch]$CompileOnly,
    [switch]$BuildEngine,
    [ValidateRange(1, 64)][int]$EngineJobs = 4,
    [string]$RuntimeSource = '',
    [string]$Destination = ''
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $workspace '.tools/dotnet/dotnet.exe'
$sdkVersion = '10.0.401'
$project = Join-Path $workspace 'src/LazerRave/lazer/LazerRave.Lazer.csproj'
$savedRoot = $env:DOTNET_ROOT
$savedTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
function Remove-RetiredRulesetArtifacts([string]$Directory) {
    foreach ($ruleset in @('Osu', 'Taiko', 'Catch')) {
        foreach ($extension in @('dll', 'pdb', 'xml', 'deps.json')) {
            $artifact = Join-Path $Directory "osu.Game.Rulesets.$ruleset.$extension"
            if (Test-Path -LiteralPath $artifact -PathType Leaf) { Remove-Item -LiteralPath $artifact -Force }
        }
    }
}
function Remove-PreviousPackageFiles([string]$Directory, [string]$Manifest) {
    if (!(Test-Path -LiteralPath $Manifest -PathType Leaf)) { throw "Package cleanup manifest is missing: $Manifest" }
    $root = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $parents = @()
    $relativePaths = @(Get-Content -LiteralPath $Manifest) + @('ExampleIR.x64.dll', 'ExampleIR.dll')
    foreach ($relativePath in ($relativePaths | Sort-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) { continue }
        $file = [IO.Path]::GetFullPath((Join-Path $root $relativePath))
        if (!$file.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($file) -eq 'LazerRave.exe' -or
            $relativePath -match '^(Resources|Localization|LR2files|BMS|userdata|cache)[\\/]') {
            throw "Unsafe package cleanup entry: $relativePath"
        }
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            if ([IO.Path]::GetExtension($file) -eq '.dll' -and
                [IO.Path]::GetFileName($file) -notin @('ExampleIR.x64.dll', 'ExampleIR.dll')) {
                try { [void][Reflection.AssemblyName]::GetAssemblyName($file) }
                catch [BadImageFormatException] { continue }
            }
            Remove-Item -LiteralPath $file -Force
        }
        $parents += Split-Path $file -Parent
    }
    foreach ($parent in ($parents | Sort-Object -Unique)) {
        if ($parent -ne $root.TrimEnd('\') -and (Test-Path -LiteralPath $parent -PathType Container) -and
            !(Get-ChildItem -LiteralPath $parent -Force | Select-Object -First 1)) {
            Remove-Item -LiteralPath $parent -Force
        }
    }
}
Push-Location $workspace
try {
    if (!(Test-Path -LiteralPath $dotnet)) {
        New-Item -ItemType Directory -Force -Path '.tools' | Out-Null
        $installer = Join-Path $workspace '.tools/dotnet-install.ps1'
        Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
        & $installer -Version $sdkVersion -Architecture x64 -InstallDir (Join-Path $workspace '.tools/dotnet') -NoPath
        if (!(Test-Path -LiteralPath $dotnet)) { throw '.NET SDK installation failed.' }
    }
    $env:DOTNET_ROOT = Split-Path $dotnet -Parent
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    if (!((& $dotnet --list-sdks) | Where-Object { $_ -like "$sdkVersion *" })) { throw "Local SDK $sdkVersion is required." }
    if ($env:DOTNET_CLI_UI_LANGUAGE -eq 'en-US') {
        # NuGet persists translated warning messages and replays them even after changing the UI language.
        $restoreCaches = @('out/build/client/obj/project.assets.json',
            'out/build/upstream/osu.Game/obj/project.assets.json',
            'out/build/upstream/osu.Game.Rulesets.Mania/obj/project.assets.json')
        foreach ($cache in $restoreCaches) {
            if (!(Test-Path -LiteralPath $cache -PathType Leaf)) { continue }
            $cached = Get-Content -LiteralPath $cache -Raw -Encoding UTF8 | ConvertFrom-Json
            if (!@($cached.logs | Where-Object { $_.message -match '^\p{IsCJKUnifiedIdeographs}' }).Count) { continue }
            Write-Host 'Refreshing cached NuGet diagnostics in English...'
            & $dotnet restore $project -r win-x64 --force --force-evaluate -p:SelfContained=true
            if ($LASTEXITCODE -ne 0) { throw 'NuGet diagnostic cache refresh failed.' }
            break
        }
    }
    $enginePackage = Join-Path $workspace 'out/build/engine/windows-vs-x64/RelWithDebInfo'
    $engine = Join-Path $enginePackage 'OpenLR2_x64.exe'
    $stale = !(Test-Path -LiteralPath $engine)
    if (!$stale) {
        $engineTime = (Get-Item -LiteralPath $engine).LastWriteTimeUtc
        $inputs = @(Get-ChildItem -LiteralPath 'src/OpenLR2/LR2' -Recurse -File)
        $inputs += @(Get-ChildItem -LiteralPath 'src/OpenLR2/dep', 'src/OpenLR2/ExampleIR' -Recurse -File)
        $inputs += @(Get-Item -LiteralPath 'src/OpenLR2/CMakeLists.txt', 'src/OpenLR2/CMakePresets.json', 'src/OpenLR2/OpenLR2.manifest', 'src/OpenLR2/vcpkg.json', 'src/OpenLR2/vcpkg-configuration.json', 'scripts/build-engine.ps1')
        $stale = @($inputs | Where-Object { $_.LastWriteTimeUtc -gt $engineTime }).Count -gt 0
    }
    if ($stale -or $BuildEngine) {
        & (Join-Path $PSScriptRoot 'build-engine.ps1') -Architecture x64 -Configuration RelWithDebInfo -Jobs $EngineJobs
        if ($LASTEXITCODE -ne 0) { throw 'OpenLR2 build failed.' }
    }
    $binaryDirectory = Join-Path $workspace "out/build/client/bin/$Configuration/net10.0-windows/win-x64"
    if ($CompileOnly) {
        & $dotnet build $project -c $Configuration -r win-x64 --self-contained true -p:RunAnalyzers=false
        if ($LASTEXITCODE -ne 0) { throw 'LazerRave lazer build failed.' }
        Remove-RetiredRulesetArtifacts $binaryDirectory
        Write-Host 'LazerRave frontend and OpenLR2 compilation passed. No application or tests were launched.'
        return
    }
    if (!$RuntimeSource) {
        $RuntimeSource = @('res/runtime') |
            ForEach-Object { Join-Path $workspace $_ } |
            Where-Object { Test-Path -LiteralPath (Join-Path $_ 'LR2files/Config/config.xml') } | Select-Object -First 1
    }
    if (!$RuntimeSource) { throw 'Pass -RuntimeSource with a complete LR2 runtime folder.' }
    if (!$Destination) { $Destination = Join-Path $workspace 'out/app' }
    $Destination = [IO.Path]::GetFullPath($Destination)
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.ProcessName -like 'LazerRave*' -or $_.ProcessName -like 'OpenLR2*') })
    if (@($running | Where-Object { (Split-Path $_.Path -Parent) -eq $Destination }).Count) {
        throw 'The destination is running. Close it before updating the application.'
    }
    & (Join-Path $PSScriptRoot 'package-client.ps1') -EnginePackage $enginePackage -RuntimeSource $RuntimeSource -Destination $Destination -PrepareOnly
    if ($LASTEXITCODE -ne 0) { throw 'Runtime preparation failed.' }
    & $dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:RunAnalyzers=false -o $Destination
    if ($LASTEXITCODE -ne 0) { throw 'LazerRave lazer publish failed.' }
    $cleanupManifest = Join-Path $workspace "out/build/client/obj/$Configuration/net10.0-windows/win-x64/package-cleanup.txt"
    Remove-PreviousPackageFiles $Destination $cleanupManifest
    $nativeManifest = Join-Path (Split-Path $cleanupManifest -Parent) 'native-dependencies.txt'
    if (!(Test-Path -LiteralPath $nativeManifest -PathType Leaf)) { throw 'Native dependency manifest is missing.' }
    $nativeDependencies = @(Get-Content -LiteralPath $nativeManifest | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
    if (!$nativeDependencies.Count) { throw 'Native dependency manifest is empty.' }
    $packageRoot = $Destination.TrimEnd('\') + '\'
    foreach ($relativePath in $nativeDependencies) {
        $dependency = [IO.Path]::GetFullPath((Join-Path $packageRoot $relativePath))
        if (!$dependency.StartsWith($packageRoot, [StringComparison]::OrdinalIgnoreCase) -or
            !(Test-Path -LiteralPath $dependency -PathType Leaf)) {
            throw "Required native dependency is missing from the package: $relativePath"
        }
    }
    Remove-RetiredRulesetArtifacts $binaryDirectory
    Remove-RetiredRulesetArtifacts $Destination
    $licenses = Join-Path $Destination 'licenses'
    New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    Copy-Item -LiteralPath 'src/osu-lazer/LICENCE' -Destination (Join-Path $licenses 'osu-lazer-MIT.txt') -Force
    Copy-Item -LiteralPath 'res/licenses/osu-framework-LICENSE.txt' -Destination $licenses -Force
    Copy-Item -LiteralPath 'res/licenses/osu-resources-LICENCE.txt' -Destination $licenses -Force
    Get-ChildItem -LiteralPath 'res/licenses' -Filter 'osu-font-*' -File | Copy-Item -Destination $licenses -Force
    Write-Host 'Build and packaging completed. No application or tests were launched.'
    Write-Host "LazerRave executable: $Destination\LazerRave.exe"
    $global:LASTEXITCODE = 0
} finally {
    Pop-Location
    $env:DOTNET_ROOT = $savedRoot
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $savedTelemetry
}
