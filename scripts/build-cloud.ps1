[CmdletBinding()]
param(
    [ValidateSet('Release','Debug')][string]$Configuration = 'Release',
    [switch]$CompileOnly
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $workspace '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
    $version = & $dotnet --version
    if ($version -notlike '10.0.*') { throw 'Install .NET SDK 10.0.401 or build the desktop client once to prepare the local SDK.' }
}
$npm = (Get-Command npm.cmd -ErrorAction Stop).Source
$savedRoot = $env:DOTNET_ROOT
try {
    $env:DOTNET_ROOT = Split-Path $dotnet -Parent
    Push-Location $workspace
    & $npm ci --prefix src/Cloud/web --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { throw 'Website dependency restore failed.' }
    & $npm run build --prefix src/Cloud/web
    if ($LASTEXITCODE -ne 0) { throw 'Website build failed.' }
    $project = Join-Path $workspace 'src/Cloud/server/Cloud.csproj'
    if ($CompileOnly) {
        & $dotnet build $project -c $Configuration -p:RestoreLockedMode=true
    } else {
        $destination = Join-Path $workspace 'out/cloud/server'
        & $dotnet publish $project -c $Configuration --self-contained false -p:RestoreLockedMode=true -o $destination
        if ($LASTEXITCODE -ne 0) { throw 'Cloud server publish failed.' }
        $webRoot = Join-Path $destination 'wwwroot'
        [IO.Directory]::CreateDirectory($webRoot) | Out-Null
        $assets = Join-Path $webRoot 'assets'
        if (Test-Path -LiteralPath $assets) {
            $resolvedAssets = [IO.Path]::GetFullPath($assets)
            $expectedRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'out/cloud/server/wwwroot')).TrimEnd('\') + '\'
            if (!$resolvedAssets.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe website cleanup path.' }
            Remove-Item -LiteralPath $resolvedAssets -Recurse -Force
        }
        Copy-Item -Path (Join-Path $workspace 'out/build/cloud/web/dist/*') -Destination $webRoot -Recurse -Force
        foreach ($name in @('Dockerfile','compose.yaml','Caddyfile','.env.example','.dockerignore')) {
            Copy-Item -LiteralPath (Join-Path $workspace "deploy/cloud/$name") -Destination (Join-Path $workspace "out/cloud/$name") -Force
        }
        Write-Host 'Cloud deployment bundle: out/cloud'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Cloud server build failed.' }
    Write-Host 'Cloud build passed. No server or tests were started.'
    $global:LASTEXITCODE = 0
} finally {
    Pop-Location
    $env:DOTNET_ROOT = $savedRoot
}
